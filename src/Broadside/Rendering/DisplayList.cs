using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The device-independent, fully resolved sequence of drawing operations a page's content produces, which every backend consumes
/// through <see cref="Replay"/>. Build one with <see cref="PdfPage.CreateDisplayList()"/>.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006. A struct-based operation buffer: operations are records in one array, paths are stored once per distinct path (verbs
/// and points in shared arrays) and colours once per run of equal colours, so building costs a fixed number of arrays per page and
/// replaying allocates nothing. Fonts, images and colour spaces are resolved before an operation enters the list; the list never
/// reads the file again (a replay touches only the list), so it can be cached and replayed many times, for example per tile and
/// zoom. Converting its colours goes through <see cref="Document"/>, which must still be open for that.
/// </para>
/// <para>
/// Today the list carries the graphics state stack (ISO 32000-2 §8.4.2), transforms (§8.3.2), path fills with their colour
/// (§8.5.3, §8.6), strokes with their colour and line parameters (§8.4.3, §8.5.3.2), path clips (§8.5.4, including form
/// bounding boxes, §8.10.1), images (§8.9, <see cref="DisplayImage"/>, decoded once per image XObject) and text as glyph runs
/// (§9.4.3, §9.3.6, <see cref="GlyphRun"/>: font program, glyph ids, text matrices, outlines interned once per program and glyph).
/// Type 3 glyphs, text clips, image masks, shadings, patterns and transparency join it in later Phase 3 tickets, each as new
/// <see cref="IRenderingBackend"/> calls.
/// </para>
/// <para>
/// Immutable once built: any number of threads may replay the same list at once, each into its own backend.
/// </para>
/// </remarks>
public sealed class DisplayList
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
        foreach (ref readonly DisplayOperation operation in _operations.AsSpan())
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
            }
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

    /// <summary>Hands a stroke record to the backend.</summary>
    private void Stroke(IRenderingBackend backend, in DisplayOperation operation)
    {
        ref readonly DisplayPath path = ref _paths[operation.First];
        ref readonly DisplayStroke stroke = ref Strokes[operation.Second];
        ref readonly DisplayColorEntry entry = ref _colors[stroke.ColorIndex];
        var color = new DisplayColor(entry.ColorSpace, _components.AsSpan(entry.ComponentStart, entry.ComponentCount), entry.DefaultColorSpaces, entry.Conversion);
        var style = new StrokeStyle(
            stroke.LineWidth,
            stroke.LineCap,
            stroke.LineJoin,
            stroke.MiterLimit,
            Dashes.AsSpan(stroke.DashStart, stroke.DashCount),
            stroke.DashPhase,
            stroke.StrokeAdjustment);
        backend.StrokePath(
            new PathView(_verbs.AsSpan(path.VerbStart, path.VerbCount), _points.AsSpan(path.PointStart, path.PointCount)),
            in style,
            in color);
    }
}
