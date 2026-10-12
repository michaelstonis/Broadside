using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>What <see cref="CountingContentProcessor"/> counts; the names are the snapshot keys of the content corpus gate.</summary>
public enum ContentCounter
{
    /// <summary>Top-level runs: page contents and annotation appearances.</summary>
    Runs,

    /// <summary>Every operator executed, in every stream.</summary>
    Operators,

    /// <summary>Operators whose keyword ISO 32000-2 does not define.</summary>
    UnknownOperators,

    /// <summary>Saves of the graphics state, explicit and implicit.</summary>
    StateSaves,

    /// <summary>Path construction verbs of painted or ended paths.</summary>
    PathVerbs,

    /// <summary>Paths stroked.</summary>
    PathsStroked,

    /// <summary>Paths filled.</summary>
    PathsFilled,

    /// <summary>Paths filled and stroked.</summary>
    PathsFilledAndStroked,

    /// <summary>Paths ended without painting (<c>n</c>).</summary>
    PathsEnded,

    /// <summary>Clips by a path.</summary>
    ClipsPath,

    /// <summary>Clips by a rectangle: form, cell and appearance bounding boxes.</summary>
    ClipsRectangle,

    /// <summary>Clips by text.</summary>
    ClipsText,

    /// <summary>Text objects.</summary>
    TextObjects,

    /// <summary>Glyphs of Type 1 fonts.</summary>
    GlyphsType1,

    /// <summary>Glyphs of multiple master fonts.</summary>
    GlyphsMMType1,

    /// <summary>Glyphs of TrueType fonts.</summary>
    GlyphsTrueType,

    /// <summary>Glyphs of Type 3 fonts.</summary>
    GlyphsType3,

    /// <summary>Glyphs of composite fonts.</summary>
    GlyphsType0,

    /// <summary>Type 3 glyph descriptions run, once per font and glyph per page.</summary>
    Type3GlyphsRun,

    /// <summary>Image XObjects painted.</summary>
    ImagesXObject,

    /// <summary>Inline images painted.</summary>
    ImagesInline,

    /// <summary>Images painted that are stencil masks.</summary>
    ImagesStencil,

    /// <summary>Images whose dictionary the image layer could not read (no <see cref="ImageEvent.Image"/>).</summary>
    ImagesUnresolved,

    /// <summary>Shadings painted by <c>sh</c> with a function-based model.</summary>
    ShadingsFunction,

    /// <summary>Shadings painted by <c>sh</c> with an axial or radial model.</summary>
    ShadingsAxialRadial,

    /// <summary>Shadings painted by <c>sh</c> with a mesh model (types 4 to 7).</summary>
    ShadingsMesh,

    /// <summary>Shadings painted by <c>sh</c> with no usable model.</summary>
    ShadingsUnresolved,

    /// <summary>Distinct mesh shadings whose geometry was decoded.</summary>
    MeshesDecoded,

    /// <summary>Paints with a tiling pattern colour.</summary>
    PatternFillsTiling,

    /// <summary>Paints with a shading pattern colour.</summary>
    PatternFillsShading,

    /// <summary>Tiling pattern cells run, once per pattern per page.</summary>
    PatternCellsRun,

    /// <summary>Form XObjects entered.</summary>
    FormsEntered,

    /// <summary>Forms entered that are transparency groups.</summary>
    TransparencyGroups,

    /// <summary>Soft-mask groups run, once per mask per page.</summary>
    SoftMaskGroupsRun,

    /// <summary>Marked-content sequences begun.</summary>
    MarkedContentSequences,

    /// <summary>Marked-content sequences that are optional content.</summary>
    OptionalContentSequences,

    /// <summary>Marked-content points.</summary>
    MarkedContentPoints,

    /// <summary>Deepest nesting of streams seen (a maximum, not a sum).</summary>
    MaxDepth,
}

/// <summary>A set of <see cref="ContentCounter"/> totals; sums add, <see cref="ContentCounter.MaxDepth"/> takes the maximum.</summary>
public sealed class ContentCounts
{
    private static readonly ContentCounter[] All = Enum.GetValues<ContentCounter>();

    private readonly long[] _values = new long[All.Length];

    /// <summary>Gets the value of a counter.</summary>
    /// <param name="counter">The counter.</param>
    public long this[ContentCounter counter] => _values[(int)counter];

    /// <summary>Adds one to a counter.</summary>
    /// <param name="counter">The counter.</param>
    /// <param name="amount">How much.</param>
    public void Add(ContentCounter counter, long amount = 1) => _values[(int)counter] += amount;

    /// <summary>Raises <see cref="ContentCounter.MaxDepth"/> to <paramref name="depth"/>.</summary>
    /// <param name="depth">A nesting depth.</param>
    public void SeeDepth(int depth) => _values[(int)ContentCounter.MaxDepth] = Math.Max(_values[(int)ContentCounter.MaxDepth], depth);

    /// <summary>Adds every counter of <paramref name="other"/> to these.</summary>
    /// <param name="other">More counts.</param>
    public void Add(ContentCounts other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (ContentCounter counter in All)
        {
            if (counter == ContentCounter.MaxDepth)
            {
                SeeDepth((int)other[counter]);
            }
            else
            {
                Add(counter, other[counter]);
            }
        }
    }

    /// <summary>Gets every counter with its value, in declaration order.</summary>
    /// <returns>The pairs.</returns>
    public IEnumerable<KeyValuePair<ContentCounter, long>> Values() => All.Select(counter => KeyValuePair.Create(counter, this[counter]));

    /// <summary>Returns whether every counter equals the other's.</summary>
    /// <param name="other">The other counts.</param>
    /// <returns>Whether they are equal.</returns>
    public bool SameAs(ContentCounts other) => other is not null && _values.AsSpan().SequenceEqual(other._values);

    /// <inheritdoc/>
    public override string ToString() => string.Join(", ", Values().Where(pair => pair.Value != 0).Select(pair => $"{pair.Key}={pair.Value}"));
}

/// <summary>
/// A <see cref="ContentProcessor"/> that subscribes to every event and counts it, entering everything a renderer would run: form
/// XObjects (entered by default), each tiling pattern cell once per pattern per page, each Type 3 glyph description once per font and
/// glyph per page, each soft-mask group once per mask per page, every mesh shading's geometry once per shading, and every image's
/// dictionary (geometry, colour space, Decode, masks; not its samples) once per image per page. It goes through the
/// public content seam only (ISO 32000-2 §8, §9; issue #80). Use one instance per thread; it is reset by every top-level
/// <see cref="BeginRun"/>, and its <see cref="Counts"/> accumulate across runs.
/// </summary>
public sealed class CountingContentProcessor : ContentProcessor
{
    private readonly HashSet<CosObject> _patternsRun = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CosObject> _softMasksRun = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(CosDictionary Font, uint Code)> _type3GlyphsRun = [];
    private readonly HashSet<CosObject> _meshesDecoded = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CosStream> _imagesResolved = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the counts so far.</summary>
    public ContentCounts Counts { get; } = new();

    /// <inheritdoc/>
    public override ContentEvents Events => ContentEvents.All;

    /// <summary>Forgets the pattern cells, glyph descriptions and soft masks already run, for the next page.</summary>
    public void ResetPage()
    {
        _patternsRun.Clear();
        _softMasksRun.Clear();
        _type3GlyphsRun.Clear();
        _imagesResolved.Clear();
    }

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add(ContentCounter.Runs);
    }

    /// <inheritdoc/>
    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add(ContentCounter.Operators);
        if (op.Code == ContentOperatorCode.Unknown)
        {
            Counts.Add(ContentCounter.UnknownOperators);
        }

        Counts.SeeDepth(context.Depth);
    }

    /// <inheritdoc/>
    public override void SaveState(ContentContext context) => Counts.Add(ContentCounter.StateSaves);

    /// <inheritdoc/>
    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add(ContentCounter.PathVerbs, path.Path.Verbs.Length);
        Counts.Add(path.Paint switch
        {
            PathPaint.Stroke => ContentCounter.PathsStroked,
            PathPaint.Fill => ContentCounter.PathsFilled,
            PathPaint.FillAndStroke => ContentCounter.PathsFilledAndStroked,
            _ => ContentCounter.PathsEnded,
        });
        if (path.Paint is PathPaint.Fill or PathPaint.FillAndStroke)
        {
            UsePaint(context.State.FillColor, context);
        }

        if (path.Paint is PathPaint.Stroke or PathPaint.FillAndStroke)
        {
            UsePaint(context.State.StrokeColor, context);
        }

        UseSoftMask(context);
    }

    /// <inheritdoc/>
    public override void IntersectClip(in ClipEvent clip, ContentContext context) => Counts.Add(clip.Kind switch
    {
        ClipKind.Rectangle => ContentCounter.ClipsRectangle,
        ClipKind.Text => ContentCounter.ClipsText,
        _ => ContentCounter.ClipsPath,
    });

    /// <inheritdoc/>
    public override void BeginText(ContentContext context) => Counts.Add(ContentCounter.TextObjects);

    /// <inheritdoc/>
    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add((glyph.Font?.FontType ?? PdfFontType.Type1) switch
        {
            PdfFontType.MMType1 => ContentCounter.GlyphsMMType1,
            PdfFontType.TrueType => ContentCounter.GlyphsTrueType,
            PdfFontType.Type3 => ContentCounter.GlyphsType3,
            PdfFontType.Type0 => ContentCounter.GlyphsType0,
            _ => ContentCounter.GlyphsType1,
        });
        if (glyph.RenderingMode is TextRenderingMode.Fill or TextRenderingMode.FillStroke or TextRenderingMode.FillClip or TextRenderingMode.FillStrokeClip)
        {
            UsePaint(context.State.FillColor, context);
        }
    }

    /// <inheritdoc/>
    public override ContentVisit BeginType3Glyph(in GlyphEvent glyph, ContentContext context)
    {
        if (glyph.FontDictionary is not { } font || !_type3GlyphsRun.Add((font, glyph.CharacterCode)))
        {
            return ContentVisit.Skip;
        }

        Counts.Add(ContentCounter.Type3GlyphsRun);
        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add(image.IsInline ? ContentCounter.ImagesInline : ContentCounter.ImagesXObject);
        if (image.IsStencil)
        {
            Counts.Add(ContentCounter.ImagesStencil);
            UsePaint(context.State.FillColor, context);
        }

        if (image.Image is not { } model)
        {
            Counts.Add(ContentCounter.ImagesUnresolved);
        }
        else if (model.IsInline || model.Stream is not { } stream || _imagesResolved.Add(stream))
        {
            // Resolve the image dictionary (§8.9.5, §8.9.6) as a renderer would before decoding: geometry, colour space, Decode
            // and masks. Samples are not decoded; the codec benchmarks and the image sweeps own that.
            _ = (model.Width, model.Height, model.BitsPerComponent, model.IsStencil, model.ColorSpace, model.DecodeArray, model.MaskKind, model.Intent);
            if (model.Mask is { } mask)
            {
                _ = (mask.Width, mask.Height, mask.IsStencil, mask.DecodeArray);
            }
        }

        UseSoftMask(context);
    }

    /// <inheritdoc/>
    public override void PaintShading(in ShadingEvent shading, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add(shading.Model switch
        {
            PdfFunctionShading => ContentCounter.ShadingsFunction,
            PdfAxialShading or PdfRadialShading => ContentCounter.ShadingsAxialRadial,
            PdfMeshShading => ContentCounter.ShadingsMesh,
            _ => ContentCounter.ShadingsUnresolved,
        });
        DecodeMesh(shading.Model);
        UseSoftMask(context);
    }

    /// <inheritdoc/>
    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Counts.Add(ContentCounter.FormsEntered);
        if (form.IsTransparencyGroup)
        {
            Counts.Add(ContentCounter.TransparencyGroups);
        }

        Counts.SeeDepth(form.Depth);
        UseSoftMask(context);
        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        Counts.Add(ContentCounter.MarkedContentSequences);
        if (mc.IsOptionalContent)
        {
            Counts.Add(ContentCounter.OptionalContentSequences);
        }
    }

    /// <inheritdoc/>
    public override void MarkedContentPoint(in MarkedContentEvent mc, ContentContext context) => Counts.Add(ContentCounter.MarkedContentPoints);

    /// <summary>A paint with <paramref name="color"/>: a tiling pattern's cell runs once per page, a shading pattern's mesh is decoded.</summary>
    private void UsePaint(in PdfColor color, ContentContext context)
    {
        if (color.Pattern is null)
        {
            return;
        }

        switch (context.GetPattern(color))
        {
            case PdfTilingPattern tiling:
                Counts.Add(ContentCounter.PatternFillsTiling);
                if (_patternsRun.Add(color.Pattern) && context.RunPatternCell(color, this))
                {
                    Counts.Add(ContentCounter.PatternCellsRun);
                }

                _ = tiling;
                break;
            case PdfShadingPattern shading:
                Counts.Add(ContentCounter.PatternFillsShading);
                DecodeMesh(shading.Shading);
                break;
        }
    }

    /// <summary>Runs the current soft mask's group once per page.</summary>
    private void UseSoftMask(ContentContext context)
    {
        if (context.State.SoftMask is { } mask && mask.Group is not null && _softMasksRun.Add(mask.Dictionary))
        {
            Counts.Add(ContentCounter.SoftMaskGroupsRun);
            context.RunSoftMaskGroup(this);
        }
    }

    /// <summary>Decodes a mesh shading's geometry once per shading object.</summary>
    private void DecodeMesh(PdfShading? shading)
    {
        if (shading is not PdfMeshShading mesh || !_meshesDecoded.Add(mesh.CosObject))
        {
            return;
        }

        Counts.Add(ContentCounter.MeshesDecoded);
        switch (mesh)
        {
            case PdfTriangleMeshShading triangles:
                _ = triangles.Triangles.Length + triangles.Vertices.Length + triangles.VertexColors.Length;
                break;
            case PdfPatchMeshShading patches:
                _ = patches.ControlPoints.Length + patches.CornerColors.Length;
                break;
        }
    }
}
