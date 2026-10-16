using Broadside.Fonts;
using Broadside.Images;

namespace Broadside.Signatures;

/// <summary>
/// What a visible signature shows and where: the page and rectangle of its widget, the text (by default the signer's name, the
/// date, the reason and the location) and an optional image, such as a scanned handwritten signature.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.5.5 (a signature field's widget gives its position with <c>Rect</c>; signing updates the widget's <c>AP</c>),
/// §12.5.6.19 (widget annotations) and §12.5.5 (the appearance stream, a form XObject whose bounding box maps onto the rectangle).
/// The appearance is written before the byte range is computed, so the signature covers it.
/// </para>
/// <para>
/// With an image and text, the image takes the left two fifths of a rectangle wider than it is tall (the top half of a taller one)
/// and the text the rest; the image keeps its aspect ratio, centred in its part. Text is shown one line per line of
/// <see cref="Text"/>, at <see cref="FontSize"/> or, by default, at the largest size at which every line fits.
/// </para>
/// </remarks>
public sealed class PdfSignatureAppearance
{
    private int _pageIndex;
    private double? _fontSize;

    /// <summary>Initializes a new instance of the <see cref="PdfSignatureAppearance"/> class.</summary>
    /// <param name="pageIndex">The zero-based index of the page the new signature field's widget goes on.</param>
    /// <param name="rectangle">The widget's rectangle in the page's default user space; it must have a width and a height.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is negative.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.5 and §12.5.2 (Table 166, <c>Rect</c> and <c>P</c>).</remarks>
    public PdfSignatureAppearance(int pageIndex, PdfRectangle rectangle)
    {
        PageIndex = pageIndex;
        Rectangle = rectangle;
    }

    /// <summary>
    /// Gets or sets the zero-based index of the page a new signature field's widget goes on. Signing a field that already exists
    /// keeps its widget's page and rectangle and draws the appearance there.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166 (<c>P</c>).</remarks>
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A page index is not negative.");
    }

    /// <summary>Gets or sets the rectangle of a new signature field's widget, in the page's default user space.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5 and §12.5.2, Table 166 (<c>Rect</c>).</remarks>
    public PdfRectangle Rectangle { get; set; }

    /// <summary>
    /// Gets or sets the text shown, one line per line; <see langword="null"/> (the default) shows <c>Digitally signed by</c> and the
    /// signer's name (<see cref="PdfSignatureOptions.SignerName"/>, else the certificate's subject name), <c>Date:</c> and the signing
    /// time, and <c>Reason:</c> and <c>Location:</c> when the options give them. An empty string shows no text.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255 (<c>Name</c>, <c>M</c>, <c>Reason</c>, <c>Location</c>).</remarks>
    public string? Text { get; set; }

    /// <summary>Gets or sets the image shown beside the text, created by the document being signed, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 (image XObjects) painted in the appearance stream (§12.5.5).</remarks>
    public PdfImage? Image { get; set; }

    /// <summary>
    /// Gets or sets the font of the text, a font of the document being signed; <see langword="null"/> (the default) uses Helvetica,
    /// which shows WinAnsiEncoding text only. Use an embedded font for other text and for PDF/A.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.6.2.2 (Standard 14 fonts) and §9.7 (embedded composite fonts).</remarks>
    public PdfCanvasFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the font size in points, or <see langword="null"/> (the default) to size the text automatically: the largest size
    /// at which every line fits the text's part of the rectangle.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §9.3.1 (<c>Tf</c>), the same rule as auto-sized variable text (§12.7.4.3).</remarks>
    public double? FontSize
    {
        get => _fontSize;
        set => _fontSize = value is null || (double.IsFinite(value.Value) && value.Value > 0)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A font size is a positive finite number.");
    }

    /// <summary>Sets <see cref="PageIndex"/>.</summary>
    /// <param name="pageIndex">The zero-based page index.</param>
    /// <returns>This appearance.</returns>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public PdfSignatureAppearance WithPageIndex(int pageIndex)
    {
        PageIndex = pageIndex;
        return this;
    }

    /// <summary>Sets <see cref="Rectangle"/>.</summary>
    /// <param name="rectangle">The widget's rectangle.</param>
    /// <returns>This appearance.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public PdfSignatureAppearance WithRectangle(PdfRectangle rectangle)
    {
        Rectangle = rectangle;
        return this;
    }

    /// <summary>Sets <see cref="Text"/>.</summary>
    /// <param name="text">The lines to show, or <see langword="null"/> for the signature's properties.</param>
    /// <returns>This appearance.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfSignatureAppearance WithText(string? text)
    {
        Text = text;
        return this;
    }

    /// <summary>Sets <see cref="Image"/>.</summary>
    /// <param name="image">The image, or <see langword="null"/>.</param>
    /// <returns>This appearance.</returns>
    /// <remarks>ISO 32000-2 §8.9.5.</remarks>
    public PdfSignatureAppearance WithImage(PdfImage? image)
    {
        Image = image;
        return this;
    }

    /// <summary>Sets <see cref="Font"/>.</summary>
    /// <param name="font">The font, or <see langword="null"/> for Helvetica.</param>
    /// <returns>This appearance.</returns>
    /// <remarks>ISO 32000-2 §9.6.2.2, §9.7.</remarks>
    public PdfSignatureAppearance WithFont(PdfCanvasFont? font)
    {
        Font = font;
        return this;
    }

    /// <summary>Sets <see cref="FontSize"/>.</summary>
    /// <param name="fontSize">The size in points, or <see langword="null"/> to size automatically.</param>
    /// <returns>This appearance.</returns>
    /// <remarks>ISO 32000-2 §9.3.1.</remarks>
    public PdfSignatureAppearance WithFontSize(double? fontSize)
    {
        FontSize = fontSize;
        return this;
    }
}
