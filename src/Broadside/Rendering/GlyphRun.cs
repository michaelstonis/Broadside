using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// One glyph of a <see cref="GlyphRun"/>: the glyph id it selects in the run's font program, where it is shown and how much it is
/// stretched horizontally.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.4 (the text rendering matrix), §9.6.5 and §9.7.4.2 (selecting the glyph of a character code), §9.2.4 (glyph
/// space). <see cref="GlyphRun.GetGlyphTransform"/> composes the members into the matrix from glyph space to user space.
/// </remarks>
public readonly record struct DisplayGlyph
{
    internal DisplayGlyph(int glyphId, in Matrix textMatrix, double horizontalScale, int outline)
    {
        GlyphId = glyphId;
        TextMatrix = textMatrix;
        HorizontalScale = horizontalScale;
        Outline = outline;
    }

    /// <summary>Gets the glyph id in <see cref="GlyphRun.Program"/>: 0, the missing glyph (<c>.notdef</c>), when the code selects none.</summary>
    /// <remarks>ISO 32000-2 §9.6.5 (simple fonts), §9.7.4.2 (CIDFonts) and §9.7.6.3 (the CID 0 glyph for an unmapped code).</remarks>
    public int GlyphId { get; }

    /// <summary>
    /// Gets the matrix from text space to user space for this glyph: <c>[T_fs·T_h 0 0 T_fs 0 T_rise] × T_m</c>, with the glyph's
    /// origin at text space's origin (for vertical writing the position vector is already subtracted, §9.7.4.3).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.4.4. The CTM is the transform set before the run (<see cref="IRenderingBackend.SetTransform"/>).</remarks>
    public Matrix TextMatrix { get; }

    /// <summary>
    /// Gets the horizontal scale applied in text space so that a glyph drawn with a substitute fills the width the PDF gives it: the
    /// font's width for the code divided by the substitute's width for the glyph. 1 for a glyph drawn with the font's own program.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §9.2.4 and §9.6.2.2: the glyph is positioned by the font's widths whatever program draws it; ADR 0009 (font
    /// substitution). Only horizontal writing is scaled.
    /// </remarks>
    public double HorizontalScale { get; }

    /// <summary>Gets the index of the glyph's interned outline among the list's paths.</summary>
    internal int Outline { get; }
}

/// <summary>
/// A run of glyphs that share a font program, a text rendering mode and the colours and line parameters they are painted with, as a
/// display list hands it to <see cref="IRenderingBackend.DrawGlyphs"/>. Valid only during that call.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.3 (text-showing operators), §9.3.6 (text rendering mode), §9.2.4 (glyph space and the font matrix). Every glyph
/// carries its glyph id and text matrix, so a backend that draws glyphs natively from <see cref="Program"/> can position each one;
/// a backend that draws outlines (the software rasterizer, or any backend when outlines are forced) fills
/// <see cref="GetOutline"/> through <see cref="GetGlyphTransform"/> and the current transform.
/// </para>
/// <para>
/// Painting (§9.3.6, Table 104): when <see cref="Fills"/>, each glyph's outline is filled with <see cref="FillColor"/> by the
/// non-zero winding rule (§9.2.4: glyph outlines are filled that way); when <see cref="Strokes"/>, its outline (mapped to user
/// space) is stroked with <see cref="StrokeStyle"/> and <see cref="StrokeColor"/>; a glyph that is both is filled, then stroked,
/// before the next glyph. Neither is set for mode 3 (<see cref="TextRenderingMode.Invisible"/>), which the list records only so
/// that hit-testing sees the text, nor for a colour that is a pattern (not painted yet). Modes 4 to 7 paint like 0 to 3; adding
/// their glyphs to the clip comes in a later ticket.
/// </para>
/// </remarks>
public readonly ref struct GlyphRun
{
    private readonly ReadOnlySpan<PathVerb> _verbs;
    private readonly ReadOnlySpan<PathPoint> _points;
    private readonly ReadOnlySpan<DisplayPath> _paths;

    internal GlyphRun(
        FontProgram? program,
        in Matrix fontMatrix,
        TextRenderingMode renderingMode,
        ReadOnlySpan<DisplayGlyph> glyphs,
        ReadOnlySpan<PathVerb> verbs,
        ReadOnlySpan<PathPoint> points,
        ReadOnlySpan<DisplayPath> paths,
        bool fills,
        DisplayColor fillColor,
        bool strokes,
        StrokeStyle strokeStyle,
        DisplayColor strokeColor)
    {
        Program = program;
        FontMatrix = fontMatrix;
        RenderingMode = renderingMode;
        Glyphs = glyphs;
        _verbs = verbs;
        _points = points;
        _paths = paths;
        Fills = fills;
        FillColor = fillColor;
        Strokes = strokes;
        StrokeStyle = strokeStyle;
        StrokeColor = strokeColor;
    }

    /// <summary>
    /// Gets the font program the glyph ids select in: the font's embedded program, or the substitute the engine's font resolvers
    /// found for it; <see langword="null"/> when the font has neither (the glyphs then have empty outlines).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.9 (embedded font programs), §9.6.2.2 and ADR 0009 (substitutes).</remarks>
    public FontProgram? Program { get; }

    /// <summary>Gets the matrix from the program's glyph space to text space (<see cref="FontProgram.FontMatrix"/>; identity without a program).</summary>
    /// <remarks>ISO 32000-2 §9.2.4.</remarks>
    public Matrix FontMatrix { get; }

    /// <summary>Gets the text rendering mode the glyphs were shown in.</summary>
    /// <remarks>ISO 32000-2 §9.3.6, Table 104.</remarks>
    public TextRenderingMode RenderingMode { get; }

    /// <summary>Gets the glyphs, in the order they were shown.</summary>
    public ReadOnlySpan<DisplayGlyph> Glyphs { get; }

    /// <summary>Gets a value indicating whether the glyphs are filled with <see cref="FillColor"/>.</summary>
    /// <remarks>ISO 32000-2 §9.3.6: modes 0, 2, 4 and 6, with a colour that is not a pattern.</remarks>
    public bool Fills { get; }

    /// <summary>Gets the nonstroking colour, unconverted; meaningful only when <see cref="Fills"/>.</summary>
    public DisplayColor FillColor { get; }

    /// <summary>Gets a value indicating whether the glyphs' outlines are stroked with <see cref="StrokeStyle"/> and <see cref="StrokeColor"/>.</summary>
    /// <remarks>ISO 32000-2 §9.3.6: modes 1, 2, 5 and 6, with a colour that is not a pattern.</remarks>
    public bool Strokes { get; }

    /// <summary>Gets the line parameters of the stroke, in user space; meaningful only when <see cref="Strokes"/>.</summary>
    /// <remarks>ISO 32000-2 §9.3.6 and §8.4.3: text is stroked with the graphics state's line parameters, in user space.</remarks>
    public StrokeStyle StrokeStyle { get; }

    /// <summary>Gets the stroking colour, unconverted; meaningful only when <see cref="Strokes"/>.</summary>
    public DisplayColor StrokeColor { get; }

    /// <summary>Gets the outline of a glyph in glyph space, interned once per program and glyph id in the list.</summary>
    /// <param name="index">The glyph's index in <see cref="Glyphs"/>.</param>
    /// <returns>The outline: closed contours filled by the non-zero winding rule; empty for a glyph that draws nothing.</returns>
    /// <remarks>ISO 32000-2 §9.2.4 and §9.9; see <see cref="GlyphOutline"/>.</remarks>
    public PathView GetOutline(int index)
    {
        DisplayPath path = _paths[Glyphs[index].Outline];
        return new PathView(_verbs.Slice(path.VerbStart, path.VerbCount), _points.Slice(path.PointStart, path.PointCount));
    }

    /// <summary>Gets the matrix from a glyph's glyph space to user space: the font matrix, the horizontal scale, then the text matrix.</summary>
    /// <param name="index">The glyph's index in <see cref="Glyphs"/>.</param>
    /// <returns><c>FontMatrix × [HorizontalScale 0 0 1 0 0] × TextMatrix</c>.</returns>
    /// <remarks>ISO 32000-2 §9.2.4 and §9.4.4 (the text rendering matrix).</remarks>
    public Matrix GetGlyphTransform(int index)
    {
        ref readonly DisplayGlyph glyph = ref Glyphs[index];
        Matrix font = FontMatrix;
        if (glyph.HorizontalScale != 1)
        {
            font *= Matrix.CreateScale(glyph.HorizontalScale, 1);
        }

        return font * glyph.TextMatrix;
    }
}
