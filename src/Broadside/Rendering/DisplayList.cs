using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The device-independent, fully resolved sequence of drawing operations a page's content produces, which every backend consumes
/// through <see cref="Replay(IRenderingBackend)"/>. Build one with <see cref="PdfPage.CreateDisplayList()"/>.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006. A struct-based operation buffer: operations are records in one array, paths are stored once per distinct path (verbs
/// and points in shared arrays) and colours once per run of equal colours, so building costs a fixed number of arrays per page and
/// replaying allocates nothing. Fonts, images and colour spaces are resolved before an operation enters the list; the list never
/// reads the file again (a replay touches only the list), so it can be cached and replayed many times, for example per tile and
/// zoom, where <see cref="Replay(IRenderingBackend, PdfRectangle)"/> skips the paints a tile does not reach. Converting its colours goes through <see cref="Document"/>, which must still be open for that.
/// </para>
/// <para>
/// Today the list carries the graphics state stack (ISO 32000-2 §8.4.2), transforms (§8.3.2), path fills with their colour
/// (§8.5.3, §8.6), strokes with their colour and line parameters (§8.4.3, §8.5.3.2), path clips (§8.5.4, including form
/// bounding boxes, §8.10.1), images with their masks (§8.9, §11.6.5, <see cref="DisplayImage"/>, decoded once per image XObject),
/// stencil masks (§8.9.6.2, <see cref="DisplayImageMask"/>), text as glyph runs (§9.4.3, §9.3.6, <see cref="GlyphRun"/>: font
/// program, glyph ids, text matrices, outlines interned once per program and glyph) and shadings, from <c>sh</c> and as shading
/// patterns on fills and strokes (§8.7.4, <see cref="DisplayShading"/>), tiling patterns on fills and strokes, each cell a display
/// list of its own (§8.7.3, <see cref="DisplayTilingPattern"/>), constant alpha and blend modes
/// (<see cref="IRenderingBackend.SetCompositing"/>, §11.6.3, §11.6.4.4), transparency groups (§11.4, <see cref="DisplayGroup"/>:
/// group XObjects, the page group and the implicit group of a combined fill and stroke) and annotations' appearances (§12.5.5,
/// <see cref="DisplayAnnotation"/>; a list holds the page's content, annotation and form-field layers as <see cref="DisplayLayers"/>
/// asks). Type 3 glyphs (§9.6.4) are in it as what their descriptions paint, under each glyph's transform and, for a <c>d1</c>
/// glyph, in the colours of the text that shows it; each description is interpreted once per font, code and inherited graphics
/// state in a list and replayed at every show. Text clips (§9.3.6) reach backends as path clips of the glyph outlines. Soft masks
/// join it in a later Phase 3 ticket, as new <see cref="IRenderingBackend"/> calls.
/// </para>
/// <para>
/// Immutable once built: any number of threads may replay the same list at once, each into its own backend.
/// </para>
/// </remarks>
public sealed partial class DisplayList
{
    private readonly DisplayOperation[] _operations;
    private readonly PathVerb[] _verbs;
    private readonly PathPoint[] _points;
    private readonly DisplayPath[] _paths;
    private readonly DisplayColorEntry[] _colors;
    private readonly float[] _components;
    private readonly Matrix[] _transforms;

    internal DisplayList(
        PdfDocument document,
        DisplayOperation[] operations,
        PathVerb[] verbs,
        PathPoint[] points,
        DisplayPath[] paths,
        DisplayColorEntry[] colors,
        float[] components,
        Matrix[] transforms)
    {
        Document = document;
        _operations = operations;
        _verbs = verbs;
        _points = points;
        _paths = paths;
        _colors = colors;
        _components = components;
        _transforms = transforms;
    }

    /// <summary>Gets the strokes' colours and line parameters; set by the builder.</summary>
    internal DisplayStroke[] Strokes { get; init; } = [];

    /// <summary>Gets the dash arrays the strokes slice; set by the builder.</summary>
    internal double[] Dashes { get; init; } = [];

    /// <summary>Gets the images the list paints, each decoded once (indexed by <see cref="DisplayOperationKind.DrawImage"/> records).</summary>
    internal DisplayImage[] Images { get; init; } = [];

    /// <summary>Gets what each image paint depends on beyond its image.</summary>
    internal DisplayImagePaint[] ImagePaints { get; init; } = [];

    /// <summary>Gets the glyph runs the list paints (indexed by <see cref="DisplayOperationKind.DrawGlyphs"/> records).</summary>
    internal DisplayGlyphRun[] GlyphRuns { get; init; } = [];

    /// <summary>Gets the glyphs the runs slice.</summary>
    internal DisplayGlyph[] Glyphs { get; init; } = [];

    /// <summary>Gets the shading paints (indexed by the shading records).</summary>
    internal DisplayShadingPaint[] ShadingPaints { get; init; } = [];

    /// <summary>Gets the stencil masks the list paints, each decoded once (indexed by <see cref="DisplayOperationKind.FillImageMask"/> records).</summary>
    internal DisplayImageMask[] ImageMasks { get; init; } = [];

    /// <summary>Gets the alpha constants and blend modes the list sets (indexed by <see cref="DisplayOperationKind.SetCompositing"/> records).</summary>
    internal DisplayCompositing[] Compositings { get; init; } = [];

    /// <summary>Gets the transparency groups the list paints (indexed by <see cref="DisplayOperationKind.DrawGroup"/> records).</summary>
    internal DisplayGroupEntry[] Groups { get; init; } = [];
    /// <summary>Gets the tiling-pattern paints (indexed by the tiling-pattern records).</summary>
    internal DisplayTilingPaint[] TilingPaints { get; init; } = [];

    /// <summary>Gets the annotations the list draws (indexed by <see cref="DisplayOperationKind.BeginAnnotation"/> records).</summary>
    internal DisplayAnnotation[] Annotations { get; init; } = [];

    /// <summary>Gets the document the list was built from, whose colour converters a backend uses (see <see cref="DisplayColor"/>).</summary>
    public PdfDocument Document { get; }

    /// <summary>Calls <paramref name="backend"/> once for every operation of the list, in order.</summary>
    /// <param name="backend">The backend that draws.</param>
    /// <remarks>
    /// ADR 0006. Reads only the list (never the document or its file) and allocates nothing; safe to call on several threads at once
    /// with a backend per thread. See <see cref="IRenderingBackend"/> for the state a replay starts from.
    /// </remarks>
    public void Replay(IRenderingBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ReplayRange(backend, 0, _operations.Length);
    }

    /// <summary>Replays the operations from <paramref name="start"/> to <paramref name="end"/> (exclusive); a group's content goes to the backend through its <see cref="DisplayGroup"/>.</summary>
    internal void ReplayRange(IRenderingBackend backend, int start, int end)
    {
        ReadOnlySpan<DisplayOperation> operations = _operations.AsSpan(start, end - start);
        for (int index = 0; index < operations.Length; index++)
        {
            ref readonly DisplayOperation operation = ref operations[index];
            Execute(backend, in operation);
            if (operation.Kind == DisplayOperationKind.DrawGroup)
            {
                // The backend replayed the group's content through its DisplayGroup (or chose not to).
                index = Groups[operation.First].End - start - 1;
            }
        }
    }

    /// <summary>Hands one record of the list to the backend.</summary>
    private void Execute(IRenderingBackend backend, in DisplayOperation operation)
    {
        switch (operation.Kind)
        {
            case DisplayOperationKind.SaveState:
                backend.SaveState();
                break;
            case DisplayOperationKind.RestoreState:
                backend.RestoreState();
                break;
            case DisplayOperationKind.SetTransform:
                backend.SetTransform(in _transforms[operation.First]);
                break;
            case DisplayOperationKind.FillPath:
                ref readonly DisplayPath path = ref _paths[operation.First];
                ref readonly DisplayColorEntry entry = ref _colors[operation.Second];
                var color = new DisplayColor(entry.ColorSpace, _components.AsSpan(entry.ComponentStart, entry.ComponentCount), entry.DefaultColorSpaces, entry.Conversion);
                backend.FillPath(
                    new PathView(_verbs.AsSpan(path.VerbStart, path.VerbCount), _points.AsSpan(path.PointStart, path.PointCount)),
                    operation.FillRule,
                    in color);
                break;
            case DisplayOperationKind.StrokePath:
                Stroke(backend, operation);
                break;
            case DisplayOperationKind.IntersectClip:
                ref readonly DisplayPath clip = ref _paths[operation.First];
                backend.IntersectClip(
                    new PathView(_verbs.AsSpan(clip.VerbStart, clip.VerbCount), _points.AsSpan(clip.PointStart, clip.PointCount)),
                    operation.FillRule);
                break;
            case DisplayOperationKind.DrawImage:
                ref readonly DisplayImagePaint paint = ref ImagePaints[operation.Second];
                backend.DrawImage(Images[operation.First], paint.DefaultColorSpaces, paint.Conversion);
                break;
            case DisplayOperationKind.DrawGlyphs:
                DrawGlyphs(backend, operation);
                break;
            case DisplayOperationKind.DrawShading:
                DisplayShading drawn = Shading(operation.First);
                backend.DrawShading(in drawn);
                break;
            case DisplayOperationKind.FillPathWithShading:
                ref readonly DisplayPath shaded = ref _paths[operation.First];
                DisplayShading filled = Shading(operation.Second);
                backend.FillPathWithShading(
                    new PathView(_verbs.AsSpan(shaded.VerbStart, shaded.VerbCount), _points.AsSpan(shaded.PointStart, shaded.PointCount)),
                    operation.FillRule,
                    in filled);
                break;
            case DisplayOperationKind.StrokePathWithShading:
                Stroke(backend, operation);
                break;
            case DisplayOperationKind.FillImageMask:
                ref readonly DisplayColorEntry maskColor = ref _colors[operation.Second];
                var maskPaint = new DisplayColor(maskColor.ColorSpace, _components.AsSpan(maskColor.ComponentStart, maskColor.ComponentCount), maskColor.DefaultColorSpaces, maskColor.Conversion);
                backend.FillImageMask(ImageMasks[operation.First], in maskPaint);
                break;
            case DisplayOperationKind.SetCompositing:
                ref readonly DisplayCompositing compositing = ref Compositings[operation.First];
                backend.SetCompositing(compositing.StrokeAlpha, compositing.FillAlpha, compositing.BlendMode);
                break;
            case DisplayOperationKind.DrawGroup:
                backend.DrawGroup(new DisplayGroup(this, Groups[operation.First]));
                break;
            case DisplayOperationKind.FillPathWithTilingPattern:
                ref readonly DisplayPath tiled = ref _paths[operation.First];
                DisplayTilingPattern tiling = Tiling(operation.Second);
                backend.FillPathWithTilingPattern(
                    new PathView(_verbs.AsSpan(tiled.VerbStart, tiled.VerbCount), _points.AsSpan(tiled.PointStart, tiled.PointCount)),
                    operation.FillRule,
                    in tiling);
                break;
            case DisplayOperationKind.StrokePathWithTilingPattern:
                Stroke(backend, operation);
                break;
            case DisplayOperationKind.BeginAnnotation:
                backend.BeginAnnotation(in Annotations[operation.First]);
                break;
            case DisplayOperationKind.EndAnnotation:
                backend.EndAnnotation();
                break;
        }
    }

    /// <summary>Hands a glyph run to the backend.</summary>
    private void DrawGlyphs(IRenderingBackend backend, in DisplayOperation operation)
    {
        ref readonly DisplayGlyphRun record = ref GlyphRuns[operation.First];
        DisplayColor fill = default;
        if (record.FillColor >= 0)
        {
            fill = Color(record.FillColor);
        }

        DisplayColor strokeColor = default;
        StrokeStyle style = default;
        if (record.Stroke >= 0)
        {
            ref readonly DisplayStroke stroke = ref Strokes[record.Stroke];
            strokeColor = Color(stroke.ColorIndex);
            style = new StrokeStyle(
                stroke.LineWidth,
                stroke.LineCap,
                stroke.LineJoin,
                stroke.MiterLimit,
                Dashes.AsSpan(stroke.DashStart, stroke.DashCount),
                stroke.DashPhase,
                stroke.StrokeAdjustment);
        }

        var run = new GlyphRun(
            record.Program,
            record.FontMatrix,
            record.Mode,
            Glyphs.AsSpan(record.GlyphStart, record.GlyphCount),
            _verbs,
            _points,
            _paths,
            record.FillColor >= 0,
            fill,
            record.Stroke >= 0,
            style,
            strokeColor);
        backend.DrawGlyphs(in run);
    }

    /// <summary>Gets an entry of the colour table as the colour a backend receives.</summary>
    private DisplayColor Color(int index)
    {
        ref readonly DisplayColorEntry entry = ref _colors[index];
        return new DisplayColor(entry.ColorSpace, _components.AsSpan(entry.ComponentStart, entry.ComponentCount), entry.DefaultColorSpaces, entry.Conversion);
    }

    /// <summary>Returns the shading paint at <paramref name="index"/> as the backend receives it.</summary>
    private DisplayShading Shading(int index)
    {
        ref readonly DisplayShadingPaint paint = ref ShadingPaints[index];
        return new DisplayShading(paint.Shading, paint.Matrix, paint.PaintsBackground, paint.DefaultColorSpaces, paint.Conversion);
    }

    /// <summary>Returns the tiling paint at <paramref name="index"/> as the backend receives it.</summary>
    private DisplayTilingPattern Tiling(int index)
    {
        ref readonly DisplayTilingPaint paint = ref TilingPaints[index];
        return new DisplayTilingPattern(paint.Pattern, paint.Matrix, paint.Cell);
    }

    /// <summary>
    /// Hands a stroke record (with a colour or, for <see cref="DisplayOperationKind.StrokePathWithShading"/> and
    /// <see cref="DisplayOperationKind.StrokePathWithTilingPattern"/>, a shading or tiling pattern) to the backend.
    /// </summary>
    private void Stroke(IRenderingBackend backend, in DisplayOperation operation)
    {
        ref readonly DisplayPath path = ref _paths[operation.First];
        ref readonly DisplayStroke stroke = ref Strokes[operation.Second];
        var style = new StrokeStyle(
            stroke.LineWidth,
            stroke.LineCap,
            stroke.LineJoin,
            stroke.MiterLimit,
            Dashes.AsSpan(stroke.DashStart, stroke.DashCount),
            stroke.DashPhase,
            stroke.StrokeAdjustment);
        var view = new PathView(_verbs.AsSpan(path.VerbStart, path.VerbCount), _points.AsSpan(path.PointStart, path.PointCount));
        if (operation.Kind == DisplayOperationKind.StrokePathWithShading)
        {
            DisplayShading shading = Shading(stroke.ColorIndex);
            backend.StrokePathWithShading(view, in style, in shading);
            return;
        }

        if (operation.Kind == DisplayOperationKind.StrokePathWithTilingPattern)
        {
            DisplayTilingPattern tiling = Tiling(stroke.ColorIndex);
            backend.StrokePathWithTilingPattern(view, in style, in tiling);
            return;
        }

        ref readonly DisplayColorEntry entry = ref _colors[stroke.ColorIndex];
        var color = new DisplayColor(entry.ColorSpace, _components.AsSpan(entry.ComponentStart, entry.ComponentCount), entry.DefaultColorSpaces, entry.Conversion);
        backend.StrokePath(view, in style, in color);
    }
}
