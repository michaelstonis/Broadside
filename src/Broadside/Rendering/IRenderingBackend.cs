using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The backend contract: what a backend implements to draw a <see cref="DisplayList"/> on one technology (the software rasterizer,
/// SkiaSharp, CoreGraphics, Direct2D, Android Canvas). <see cref="DisplayList.Replay(IRenderingBackend)"/> calls these members in the list's order.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006: backends consume display lists and never see content-stream operators. Everything a call hands over is resolved:
/// paths in user space with the transform set before them, colours as colour space plus components (ISO 32000-2 §8.6.1, colour
/// specification). Colour rendering is the backend's (§8.6.1, §10): it converts a <see cref="DisplayColor"/> to its device model
/// through <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/> on
/// <see cref="DisplayList.Document"/>, which goes through the engine's colour-management extension point
/// (<see cref="IColorManagement"/>).
/// </para>
/// <para>
/// State: a replay starts with the identity transform, so user space is the page's default user space (§8.3.2.3); mapping that to
/// the device (scale, rotation, crop box, user unit) is the backend's or its renderer's. <see cref="SetTransform"/> replaces the
/// current transform; <see cref="SaveState"/> and <see cref="RestoreState"/> are balanced in every list and save and restore the
/// transform and, as later operations join the list, everything else the backend holds per state.
/// </para>
/// <para>
/// Spans and views handed to a call are valid only during it; copy what you keep. A list replays on any number of threads at once,
/// each with its own backend; one backend instance receives the calls of one replay from one thread.
/// </para>
/// </remarks>
public interface IRenderingBackend
{
    /// <summary>Saves the current state: the transform and whatever else the backend keeps per state.</summary>
    /// <remarks>ISO 32000-2 §8.4.2 (<c>q</c>, and the state saved around a form XObject's content, §8.10.1).</remarks>
    void SaveState();

    /// <summary>Restores the state saved by the matching <see cref="SaveState"/>.</summary>
    /// <remarks>ISO 32000-2 §8.4.2 (<c>Q</c>).</remarks>
    void RestoreState();

    /// <summary>Sets the transform from user space to the page's default user space for the paints that follow.</summary>
    /// <param name="transform">The current transformation matrix; it replaces the current transform, it is not concatenated.</param>
    /// <remarks>ISO 32000-2 §8.3.2 and §8.4.4 (<c>cm</c>): the CTM in effect at the paints that follow.</remarks>
    void SetTransform(in Matrix transform);

    /// <summary>Fills a path with a colour.</summary>
    /// <param name="path">
    /// The path in user space, exactly as constructed (see <see cref="PathView"/>): the backend closes open subpaths for the fill and
    /// disregards a trailing single-point subpath (ISO 32000-2 §8.5.3.3.1).
    /// </param>
    /// <param name="rule">The rule that decides the inside: non-zero winding (§8.5.3.3.2) or even-odd (§8.5.3.3.3).</param>
    /// <param name="color">The nonstroking colour, unconverted.</param>
    /// <remarks>ISO 32000-2 §8.5.3.1 and §8.5.3.3 (<c>f</c>, <c>F</c>, <c>f*</c>, and the fill of <c>B</c>, <c>B*</c>, <c>b</c>, <c>b*</c>).</remarks>
    void FillPath(PathView path, FillRule rule, in DisplayColor color);

    /// <summary>Strokes a path with a colour and line parameters.</summary>
    /// <param name="path">
    /// The path in user space, exactly as constructed (see <see cref="PathView"/>): the backend strokes each subpath on its own, joins
    /// the ends of a closed subpath, paints a degenerate subpath only as a round-cap dot and ignores a trailing lone moveto
    /// (ISO 32000-2 §8.5.3.2).
    /// </param>
    /// <param name="style">The line width, caps, joins, miter limit, dash pattern and stroke adjustment, in user space (§8.4.3, §10.7.5).</param>
    /// <param name="color">The stroking colour, unconverted.</param>
    /// <remarks>ISO 32000-2 §8.5.3.2 (<c>S</c>, <c>s</c>, and the stroke of <c>B</c>, <c>B*</c>, <c>b</c>, <c>b*</c>, after its fill).</remarks>
    void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color);

    /// <summary>Intersects the current clip with the inside of a path, for the paints that follow until the state is restored.</summary>
    /// <param name="path">
    /// The path in user space under the current transform, exactly as constructed: its inside is the area <see cref="FillPath"/>
    /// would fill with <paramref name="rule"/> (open subpaths closed, a trailing single-point subpath disregarded, ISO 32000-2
    /// §8.5.3.3.1).
    /// </param>
    /// <param name="rule">The rule that decides the inside: non-zero winding (<c>W</c>) or even-odd (<c>W*</c>).</param>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §8.5.4 (<c>W</c>, <c>W*</c>): the clip only shrinks, by intersection, and <see cref="RestoreState"/> brings back
    /// the clip of the matching <see cref="SaveState"/>. The call comes after the paint of the path object that set it, so that
    /// paint is not clipped by its own path. A form XObject's bounding box (§8.10.1) arrives the same way, as a rectangle under the
    /// form's transform, inside the state saved around the form.
    /// </para>
    /// <para>
    /// A replay starts with the initial clipping path (§8.5.4: the whole page): the area the renderer maps the page to, which is the
    /// crop box (§14.11.2, never larger than the media box). Clipping is anti-aliased like filling where the backend anti-aliases.
    /// Text clips (§9.3.6, rendering modes 4 to 7) are not in the list yet.
    /// </para>
    /// </remarks>
    void IntersectClip(PathView path, FillRule rule);

    /// <summary>Draws an image into the unit square of user space.</summary>
    /// <param name="image">
    /// The decoded image, the same instance for every paint of one image XObject in a list. Image space maps to the unit square
    /// through <c>[1/Width 0 0 −1/Height 0 1]</c> (its first row at the top), then through the current transform.
    /// </param>
    /// <param name="defaultColorSpaces">The default colour spaces current where the image was painted (§8.6.5.6).</param>
    /// <param name="conversion">
    /// The conversion at the paint, with <see cref="ColorConversion.Target"/> RGB: the graphics state's, its intent replaced by the
    /// image's Intent entry when it has one (§8.6.5.8).
    /// </param>
    /// <remarks>
    /// ISO 32000-2 §8.9.4 (the unit square), §8.9.5 (image XObjects, Decode, Interpolate) and §8.9.7 (inline images). Map the samples
    /// through <see cref="DisplayImage.DecodeMap"/>, convert them through
    /// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, and resample them to the
    /// device as the backend sees fit; honour <see cref="DisplayImage.Interpolate"/> when enlarging (§8.9.5.3). Masks travel with the
    /// image (§8.9.6, §11.6.5.2, §7.4.9): <see cref="DisplayImage.Mask"/>, <see cref="DisplayImage.ColorKey"/> and
    /// <see cref="DisplayImage.SoftMask"/> say where and how opaquely it is painted, each mask resampled from its own resolution;
    /// <see cref="DisplayImage.Matte"/> is removed from the colours before conversion.
    /// </remarks>
    void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion);

    /// <summary>Draws a run of glyphs: fills and strokes their outlines as the run's rendering mode says.</summary>
    /// <param name="run">
    /// The glyphs, their font program and outlines, and the colours and line parameters they are painted with. Each glyph maps from
    /// glyph space to user space through <see cref="GlyphRun.GetGlyphTransform"/>, then through the current transform.
    /// </param>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.4.3 (<c>Tj</c>, <c>TJ</c>, <c>'</c>, <c>"</c>), §9.3.6 (text rendering mode, Table 104) and §9.2.4 (glyph
    /// outlines, filled by the non-zero winding rule). Glyphs whose run neither fills nor strokes (mode 3, invisible text) paint
    /// nothing; they reach the backend so that a backend collecting positions (hit-testing) sees them. Text in clipping modes 4 to 7
    /// paints like modes 0 to 3; its contribution to the clip is not in the list yet. Type 3 glyphs (§9.6.4) are not in the list yet.
    /// </para>
    /// <para>
    /// A backend may draw the glyphs natively from <see cref="GlyphRun.Program"/> and the glyph ids, or from the interned outlines
    /// (<see cref="GlyphRun.GetOutline"/>); a render option to force outlines makes every backend's output comparable. A glyph that is
    /// both filled and stroked is filled, then stroked, before the next glyph is painted.
    /// </para>
    /// </remarks>
    void DrawGlyphs(in GlyphRun run);

    /// <summary>Paints a shading over the whole current clip (the <c>sh</c> operator).</summary>
    /// <param name="shading">
    /// The shading, its geometry mapped to default user space by <see cref="DisplayShading.Matrix"/> (the CTM at <c>sh</c>), with
    /// <see cref="DisplayShading.PaintsBackground"/> false.
    /// </param>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.2, Table 76: the shading fills the current clipping path, further clipped by its BBox when it has one
    /// (§8.7.4.3); points the shading does not define (beyond an end that is not extended, §8.7.4.5.3 and §8.7.4.5.4) are left
    /// unpainted; the Background is ignored. The current colour plays no part.
    /// </remarks>
    void DrawShading(in DisplayShading shading);

    /// <summary>Fills a path with a shading pattern.</summary>
    /// <param name="path">The path in user space under the current transform, exactly as for <see cref="FillPath"/>.</param>
    /// <param name="rule">The rule that decides the inside (§8.5.3.3.2, §8.5.3.3.3).</param>
    /// <param name="shading">The pattern's shading, in pattern space (<see cref="DisplayShading.Matrix"/>), with its Background.</param>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.1, Table 75, and §8.7.4.3, Table 77: inside the path (and the clip), the Background, when present, fills
    /// the points outside the shading's bounds, then the shading paints the points it defines within its BBox.
    /// </remarks>
    void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading);

    /// <summary>Strokes a path with a shading pattern.</summary>
    /// <param name="path">The path in user space under the current transform, exactly as for <see cref="StrokePath"/>.</param>
    /// <param name="style">The line parameters, as for <see cref="StrokePath"/>.</param>
    /// <param name="shading">The pattern's shading, in pattern space (<see cref="DisplayShading.Matrix"/>), with its Background.</param>
    /// <remarks>ISO 32000-2 §8.7.4.1 and §8.5.3.2: the area the stroke covers is painted as <see cref="FillPathWithShading"/> paints a fill's.</remarks>
    void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading);

    /// <summary>Fills a path with a tiling pattern.</summary>
    /// <param name="path">The path in user space under the current transform, exactly as for <see cref="FillPath"/>.</param>
    /// <param name="rule">The rule that decides the inside (§8.5.3.3.2, §8.5.3.3.3).</param>
    /// <param name="pattern">The pattern, its cell and its pattern space (<see cref="DisplayTilingPattern.Matrix"/>).</param>
    /// <remarks>
    /// ISO 32000-2 §8.7.3.1: inside the path (and the clip), the cell is painted as many times as needed, at multiples of XStep and
    /// YStep in pattern space, each copy clipped to the bounding box (the cell list does that itself). The tiles, painted onto a
    /// clear surface, are then composited over the backdrop as one object with the alpha and blend mode in force (§11.6.7).
    /// </remarks>
    void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern);

    /// <summary>Strokes a path with a tiling pattern.</summary>
    /// <param name="path">The path in user space under the current transform, exactly as for <see cref="StrokePath"/>.</param>
    /// <param name="style">The line parameters, as for <see cref="StrokePath"/>.</param>
    /// <param name="pattern">The pattern, its cell and its pattern space (<see cref="DisplayTilingPattern.Matrix"/>).</param>
    /// <remarks>ISO 32000-2 §8.7.3.1 and §8.5.3.2: the area the stroke covers is tiled as <see cref="FillPathWithTilingPattern"/> tiles a fill's.</remarks>
    void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern);

    /// <summary>Paints a colour through a stencil mask in the unit square of user space: where the mask paints, the colour is painted.</summary>
    /// <param name="mask">
    /// The image mask, the same instance for every paint of one mask XObject in a list; it maps to the unit square like an image
    /// (§8.9.4).
    /// </param>
    /// <param name="color">The nonstroking colour, unconverted.</param>
    /// <remarks>
    /// ISO 32000-2 §8.9.6.2 (stencil masking, <c>ImageMask true</c>): the mask's samples say where the current fill colour is painted
    /// and where the backdrop shows through; §8.9.5.3 (Interpolate) applies when it is enlarged. A stencil mask in a Pattern colour
    /// is not in the list yet.
    /// </remarks>
    void FillImageMask(DisplayImageMask mask, in DisplayColor color);

    /// <summary>Sets the alpha constants and the blend mode the paints that follow composite with, until the state is restored.</summary>
    /// <param name="strokeAlpha">
    /// The stroking alpha constant (<c>CA</c>), 0 to 1: the opacity of <see cref="StrokePath"/>, <see cref="StrokePathWithShading"/>,
    /// <see cref="StrokePathWithTilingPattern"/> and the stroke of <see cref="DrawGlyphs"/>.
    /// </param>
    /// <param name="fillAlpha">The nonstroking alpha constant (<c>ca</c>), 0 to 1: the opacity of every other paint.</param>
    /// <param name="blendMode">The blend function that combines the source colour with the backdrop.</param>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §11.6.4.4 (constant shape and opacity), §11.6.3 (blend mode) and §11.3 (the compositing formulas, §11.3.3 and
    /// §11.3.6; the blend functions of §11.3.5, Tables 134 and 135). Each paint's source alpha is its shape (coverage, and an image's
    /// own masks) times the alpha constant that applies to it, and it composites over the backdrop, the result of earlier painting,
    /// as αr × Cr = (1 − αs) × αb × Cb + (1 − αb) × αs × Cs + αb × αs × B(Cb, Cs), with αr = αs + αb − αs × αb. A replay starts with
    /// both alphas at 1 and the Normal blend mode; the call arrives before the first paint it applies to, only when one of the values
    /// changes, and <see cref="SaveState"/> and <see cref="RestoreState"/> save and restore it.
    /// </para>
    /// <para>
    /// A shading pattern's paint arrives with its pattern's ExtGState applied (§11.6.7): its <c>ca</c> already multiplied into the
    /// alpha of the paint, its blend mode, when not Normal, in place of the state's. The page's blending colour space is the
    /// backend's (§11.3.4, §11.4.7). Transparency groups, soft masks and the knockout of a combined fill and stroke (§11.7.4.4) are
    /// not in the list yet: a fill and stroke of one path composite as two objects.
    /// </para>
    /// </remarks>
    void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode);

    /// <summary>
    /// Begins an annotation's appearance: saves the state as <see cref="SaveState"/> does, and the paints until the matching
    /// <see cref="EndAnnotation"/> are the appearance, composited as one group.
    /// </summary>
    /// <param name="annotation">Where the annotation sits, whether it keeps its size and orientation on screen, and how it is composited.</param>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.5: the paints that follow are already placed on the annotation rectangle (their transforms include the
    /// matrix <c>AA</c>) and clipped to the appearance's bounding box. A backend that honours <see cref="DisplayAnnotation.NoZoom"/>
    /// or <see cref="DisplayAnnotation.NoRotate"/> (§12.5.3) maps default user space to the device differently until
    /// <see cref="EndAnnotation"/>: about the rectangle's upper-left corner, without the device transform's scale or rotation. The
    /// appearance is a transparency group composited with <see cref="DisplayAnnotation.Opacity"/> and
    /// <see cref="DisplayAnnotation.BlendMode"/> (§12.5.5, §11.6.6), through the same compositing formulas as
    /// <see cref="SetCompositing"/>; inside it, paints composite with the alpha constants and blend mode that
    /// <see cref="SetCompositing"/> sets, which <see cref="EndAnnotation"/> restores with the rest of the state.
    /// </para>
    /// <para>Annotations never nest, and a list that holds one starts it with the identity transform (default user space).</para>
    /// </remarks>
    void BeginAnnotation(in DisplayAnnotation annotation);

    /// <summary>Ends the annotation <see cref="BeginAnnotation"/> began: composites its appearance and restores the state saved there.</summary>
    /// <remarks>ISO 32000-2 §12.5.5.</remarks>
    void EndAnnotation();
}
