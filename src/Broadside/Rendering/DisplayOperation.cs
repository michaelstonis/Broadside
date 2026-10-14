using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>What one record of a display list does.</summary>
internal enum DisplayOperationKind : byte
{
    /// <summary><see cref="IRenderingBackend.SaveState"/>.</summary>
    SaveState,

    /// <summary><see cref="IRenderingBackend.RestoreState"/>.</summary>
    RestoreState,

    /// <summary><see cref="IRenderingBackend.SetTransform"/>; <see cref="DisplayOperation.First"/> indexes the transforms.</summary>
    SetTransform,

    /// <summary><see cref="IRenderingBackend.FillPath"/>; <see cref="DisplayOperation.First"/> indexes the paths, <see cref="DisplayOperation.Second"/> the colours.</summary>
    FillPath,

    /// <summary><see cref="IRenderingBackend.StrokePath"/>; <see cref="DisplayOperation.First"/> indexes the paths, <see cref="DisplayOperation.Second"/> the strokes.</summary>
    StrokePath,

    /// <summary><see cref="IRenderingBackend.IntersectClip"/>; <see cref="DisplayOperation.First"/> indexes the paths.</summary>
    IntersectClip,

    /// <summary><see cref="IRenderingBackend.DrawImage"/>; <see cref="DisplayOperation.First"/> indexes the images, <see cref="DisplayOperation.Second"/> the image paints.</summary>
    DrawImage,

    /// <summary><see cref="IRenderingBackend.DrawGlyphs"/>; <see cref="DisplayOperation.First"/> indexes the glyph runs.</summary>
    DrawGlyphs,

    /// <summary><see cref="IRenderingBackend.DrawShading"/>; <see cref="DisplayOperation.First"/> indexes the shading paints.</summary>
    DrawShading,

    /// <summary><see cref="IRenderingBackend.FillPathWithShading"/>; <see cref="DisplayOperation.First"/> indexes the paths, <see cref="DisplayOperation.Second"/> the shading paints.</summary>
    FillPathWithShading,

    /// <summary>
    /// <see cref="IRenderingBackend.StrokePathWithShading"/>; <see cref="DisplayOperation.First"/> indexes the paths,
    /// <see cref="DisplayOperation.Second"/> the strokes, whose <see cref="DisplayStroke.ColorIndex"/> then indexes the shading paints.
    /// </summary>
    StrokePathWithShading,

    /// <summary><see cref="IRenderingBackend.FillImageMask"/>; <see cref="DisplayOperation.First"/> indexes the image masks, <see cref="DisplayOperation.Second"/> the colours.</summary>
    FillImageMask,

    /// <summary><see cref="IRenderingBackend.SetCompositing"/>; <see cref="DisplayOperation.First"/> indexes the compositing table.</summary>
    SetCompositing,

    /// <summary><see cref="IRenderingBackend.FillPathWithTilingPattern"/>; <see cref="DisplayOperation.First"/> indexes the paths, <see cref="DisplayOperation.Second"/> the tiling paints.</summary>
    FillPathWithTilingPattern,

    /// <summary>
    /// <see cref="IRenderingBackend.StrokePathWithTilingPattern"/>; <see cref="DisplayOperation.First"/> indexes the paths,
    /// <see cref="DisplayOperation.Second"/> the strokes, whose <see cref="DisplayStroke.ColorIndex"/> then indexes the tiling paints.
    /// </summary>
    StrokePathWithTilingPattern,
    /// <summary><see cref="IRenderingBackend.BeginAnnotation"/>; <see cref="DisplayOperation.First"/> indexes the annotations.</summary>
    BeginAnnotation,

    /// <summary><see cref="IRenderingBackend.EndAnnotation"/>.</summary>
    EndAnnotation,
}

/// <summary>One record of a display list: a kind and up to two indexes into the list's tables, 16 bytes.</summary>
internal readonly record struct DisplayOperation(DisplayOperationKind Kind, FillRule FillRule, int First, int Second);

/// <summary>A path interned in a display list: a slice of its verbs and a slice of its points.</summary>
internal readonly record struct DisplayPath(int VerbStart, int VerbCount, int PointStart, int PointCount);

/// <summary>A colour in a display list, its components a slice of the list's component array.</summary>
internal readonly record struct DisplayColorEntry(
    PdfColorSpace ColorSpace,
    int ComponentStart,
    int ComponentCount,
    PdfDefaultColorSpaces DefaultColorSpaces,
    ColorConversion Conversion);

/// <summary>The colour and line parameters of a stroke in a display list, its dash array a slice of the list's dash array.</summary>
internal readonly record struct DisplayStroke(
    int ColorIndex,
    double LineWidth,
    LineCap LineCap,
    LineJoin LineJoin,
    double MiterLimit,
    int DashStart,
    int DashCount,
    double DashPhase,
    bool StrokeAdjustment);

/// <summary>What one paint of an interned image depends on beyond the image: the default colour spaces and the conversion at the paint.</summary>
internal readonly record struct DisplayImagePaint(PdfDefaultColorSpaces DefaultColorSpaces, ColorConversion Conversion);

/// <summary>
/// A run of glyphs in a display list: the program and font matrix, the rendering mode, a slice of the list's glyphs, and the fill
/// colour (−1 when the run is not filled) and stroke record (−1 when it is not stroked) it is painted with.
/// </summary>
internal readonly record struct DisplayGlyphRun(
    Fonts.FontProgram? Program,
    Matrix FontMatrix,
    TextRenderingMode Mode,
    int GlyphStart,
    int GlyphCount,
    int FillColor,
    int Stroke);

/// <summary>A shading paint in a display list: the shading, its target space, whether its Background is painted, and its conversion.</summary>
internal readonly record struct DisplayShadingPaint(
    PdfShading Shading,
    Matrix Matrix,
    bool PaintsBackground,
    PdfDefaultColorSpaces DefaultColorSpaces,
    ColorConversion Conversion);

/// <summary>The alpha constants and blend mode a display list sets with <see cref="DisplayOperationKind.SetCompositing"/>.</summary>
internal readonly record struct DisplayCompositing(double StrokeAlpha, double FillAlpha, BlendMode BlendMode)
{
    /// <summary>The compositing a replay starts with: both alphas 1, Normal (ISO 32000-2 §8.4.1, Table 52).</summary>
    public static DisplayCompositing Initial => new(1, 1, BlendMode.Normal);
}

/// <summary>A tiling-pattern paint in a display list: the pattern, its pattern space relative to the list, and its cell's own list.</summary>
internal readonly record struct DisplayTilingPaint(PdfTilingPattern Pattern, Matrix Matrix, DisplayList Cell);
