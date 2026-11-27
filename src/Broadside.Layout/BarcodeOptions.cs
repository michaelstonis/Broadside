using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// How <see cref="BarcodeCanvasExtensions.DrawBarcode"/> paints a barcode: the colors, whether a linear symbol has its text under it, the
/// font of that text, and whether the quiet zone is kept inside the box. Immutable: each <c>With*</c> returns a new instance.
/// </summary>
/// <remarks>ISO 32000-2 §8.5.3 (filling, in the <see cref="Foreground"/> color) and §9.4 (the human-readable text).</remarks>
public sealed class BarcodeOptions
{
    private static readonly PdfColor Black = PdfColor.FromGray(0);

    private BarcodeOptions(PdfColor foreground, PdfColor? background, bool showText, bool includeQuietZone, PdfCanvasFont? font, double fontSize)
    {
        Foreground = foreground;
        Background = background;
        ShowText = showText;
        IncludeQuietZone = includeQuietZone;
        Font = font;
        FontSize = fontSize;
    }

    /// <summary>Creates the defaults: black modules on no background, text shown when a font is set, quiet zone included.</summary>
    public BarcodeOptions()
        : this(Black, null, showText: true, includeQuietZone: true, null, 0)
    {
    }

    /// <summary>Gets the color of the dark modules and of the text; black by default.</summary>
    public PdfColor Foreground { get; }

    /// <summary>Gets the color painted behind the symbol and its quiet zone, or null (the default) to paint nothing there.</summary>
    public PdfColor? Background { get; }

    /// <summary>
    /// Gets a value indicating whether a linear symbol has its <see cref="Barcode.HumanReadableText"/> under the bars; true by default.
    /// The text needs a <see cref="Font"/>: on a canvas, set one with <see cref="WithFont"/>; the document builder uses the style's font.
    /// </summary>
    public bool ShowText { get; }

    /// <summary>Gets a value indicating whether the quiet zone (<see cref="Barcode.QuietZone"/>) is kept light inside the box; true by default.</summary>
    public bool IncludeQuietZone { get; }

    /// <summary>Gets the font of the human-readable text, or null (the default) for none.</summary>
    public PdfCanvasFont? Font { get; }

    /// <summary>Gets the size of the human-readable text in points, or 0 when <see cref="Font"/> is null.</summary>
    public double FontSize { get; }

    /// <summary>Returns options that paint the dark modules and the text in <paramref name="color"/>.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The new options.</returns>
    public BarcodeOptions WithForeground(PdfColor color) => new(color, Background, ShowText, IncludeQuietZone, Font, FontSize);

    /// <summary>Returns options that paint <paramref name="color"/> behind the symbol and its quiet zone, or nothing for null.</summary>
    /// <param name="color">The color, or null.</param>
    /// <returns>The new options.</returns>
    public BarcodeOptions WithBackground(PdfColor? color) => new(Foreground, color, ShowText, IncludeQuietZone, Font, FontSize);

    /// <summary>Returns options that show or hide a linear symbol's human-readable text.</summary>
    /// <param name="show">Whether to show it.</param>
    /// <returns>The new options.</returns>
    public BarcodeOptions WithText(bool show) => new(Foreground, Background, show, IncludeQuietZone, Font, FontSize);

    /// <summary>Returns options that keep or drop the quiet zone inside the box.</summary>
    /// <param name="include">Whether the box includes the quiet zone.</param>
    /// <returns>The new options.</returns>
    public BarcodeOptions WithQuietZone(bool include) => new(Foreground, Background, ShowText, include, Font, FontSize);

    /// <summary>Returns options that write the human-readable text in <paramref name="font"/> at <paramref name="size"/> points.</summary>
    /// <param name="font">The font, embedded in the document of the canvas drawn on.</param>
    /// <param name="size">The size in points.</param>
    /// <returns>The new options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public BarcodeOptions WithFont(PdfCanvasFont font, double size)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!(size > 0 && double.IsFinite(size)))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The font size shall be positive and finite.");
        }

        return new(Foreground, Background, ShowText, IncludeQuietZone, font, size);
    }
}
