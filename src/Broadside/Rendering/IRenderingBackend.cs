using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The backend contract: what a backend implements to draw a <see cref="DisplayList"/> on one technology (the software rasterizer,
/// SkiaSharp, CoreGraphics, Direct2D, Android Canvas). <see cref="DisplayList.Replay"/> calls these members in the list's order.
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
    /// device as the backend sees fit; honour <see cref="DisplayImage.Interpolate"/> when enlarging (§8.9.5.3).
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
}
