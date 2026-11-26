using Broadside.Fonts;

namespace Broadside;

/// <summary>
/// Header and footer texts to add to existing pages with <see cref="PdfDocument.AddHeaderFooter(PdfHeaderFooter, Range?)"/>: up to
/// three slots at the top and three at the bottom of each page's crop box, as the page is viewed, marked as pagination artifacts.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.2 (Table 363: <c>Pagination</c> artifacts with the <c>Header</c>, <c>Footer</c> or, in a PDF 2.0 document, the
/// <c>PageNum</c> subtype, attached to the <c>Top</c> or <c>Bottom</c> edge) and §12.4.2 (page labels). A slot may hold the tokens of
/// <see cref="PdfStamp.FromText"/>: <c>{page}</c>, <c>{pages}</c> and <c>{label}</c>.
/// </remarks>
public sealed class PdfHeaderFooter
{
    /// <summary>Initializes a new instance of the <see cref="PdfHeaderFooter"/> class.</summary>
    /// <param name="font">A font of the document the texts are added to.</param>
    /// <param name="fontSize">The font size, in points.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §9.2 to §9.4.</remarks>
    public PdfHeaderFooter(PdfCanvasFont font, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (!double.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "The font size shall be a positive finite number.");
        }

        Font = font;
        FontSize = fontSize;
    }

    /// <summary>Gets the font the texts are shown in.</summary>
    public PdfCanvasFont Font { get; }

    /// <summary>Gets the font size, in points.</summary>
    public double FontSize { get; }

    /// <summary>Gets the text at the top left, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public string? HeaderLeft { get; init; }

    /// <summary>Gets the text at the top center, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public string? HeaderCenter { get; init; }

    /// <summary>Gets the text at the top right, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public string? HeaderRight { get; init; }

    /// <summary>Gets the text at the bottom left, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public string? FooterLeft { get; init; }

    /// <summary>Gets the text at the bottom center, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public string? FooterCenter { get; init; }

    /// <summary>Gets the text at the bottom right, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    public string? FooterRight { get; init; }

    /// <summary>Gets the distance, in points, from the edges of the crop box to the texts; 36 by default.</summary>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public double Margin { get; init; } = 36;

    /// <summary>Gets the number the first page of the range has for <c>{page}</c>; 1 by default.</summary>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    public int FirstPageNumber { get; init; } = 1;
}
