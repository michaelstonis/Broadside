namespace Broadside.Rendering;

/// <summary>A filter a render applies to the colors it shows, for reading on screen; <see cref="None"/> by default.</summary>
/// <remarks>
/// <para>
/// Spec #33 viewer capability 9 (smart dark mode). A filter is for display only, never for printing or export: it changes the pixels of
/// the render, not the page, and the display list is the same with or without it, so a viewer turns it on and off without building
/// any list again.
/// </para>
/// <para>
/// ISO 32000-2 §10.7.1 (the page is rendered as usual, §8.6 and §11.4.7 included) and §8.9 (images): the filter runs on the
/// rendered pixels, after compositing, and keeps the pixels images painted as they are.
/// </para>
/// </remarks>
public enum ColorFilter
{
    /// <summary>The page's own colors.</summary>
    None = 0,

    /// <summary>
    /// Smart dark mode: the lightness of text and vector colors is inverted while their hue is kept (black text on white paper becomes
    /// white text on black paper, dark blue becomes light blue, a saturated red stays red), and images are left as they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each rendered pixel is mapped in RGB by c' = c + 1 − max(r, g, b) − min(r, g, b) for each channel c: the HSL lightness
    /// L = (max + min) / 2 becomes 1 − L while the hue and the HSL saturation stay, and applying the mapping twice gives back the
    /// pixel exactly. On premultiplied 8-bit pixels it is c' = c + α − max − min, exact integer arithmetic, so it gives the same bytes
    /// on every platform. White paper becomes black.
    /// </para>
    /// <para>
    /// The pixels an image covers keep their colors as rendered without the filter: the image itself, what shows through its
    /// transparent parts (white paper behind a soft-masked image stays white) and anything drawn over it, so text over a photograph
    /// keeps its contrast with it. An image's anti-aliased edge pixels mix the two by the share of the pixel the image covers.
    /// Stencil masks (§8.9.6.2), which paint a color, are filtered like vector content, and so are shadings and tiling patterns;
    /// an image inside a tiling pattern's cell or a soft mask's group counts as the pattern or the mask, not as an image.
    /// </para>
    /// </remarks>
    DarkMode = 1,
}
