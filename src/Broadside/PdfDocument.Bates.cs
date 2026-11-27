namespace Broadside;

/// <content>Bates numbering of existing pages (issue #646).</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Stamps each page of <paramref name="pages"/> (every page by default) with the next Bates number of <paramref name="numbering"/>,
    /// in page order, and advances its <see cref="PdfBatesNumbering.NextNumber"/> past them.
    /// </summary>
    /// <param name="numbering">The numbering: format, placement and the counter, which carries to the next document.</param>
    /// <param name="pages">The pages, by index; <see langword="null"/> for all. An empty range numbers nothing and leaves the counter.</param>
    /// <returns>The numbers used; for an empty range, <see cref="PdfBatesRange.LastNumber"/> is <see cref="PdfBatesRange.FirstNumber"/> minus 1.</returns>
    /// <exception cref="InvalidOperationException">The last number needs more than <see cref="PdfBatesNumbering.Digits"/> digits; no page changes and the counter stays.</exception>
    /// <exception cref="ArgumentException">
    /// The font cannot show the prefix, the suffix or the digits, the font is embedded in another document, or the text color's space
    /// belongs to another document; no page changes and the counter stays.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is outside the document's pages.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.2.2.2 (Table 363): each number is drawn as a <see cref="PdfStamp"/> text that follows the page rotation, wrapped
    /// in a <c>Pagination</c> artifact with its bounding box, attached to the top or bottom edge it is aligned to. The subtype is the
    /// PDF 2.0 <c>Bates</c> when <see cref="Version"/> is 2.0 or later and <c>Footer</c> otherwise, so numbering never raises
    /// the version. One font resource serves every page. A mutation of this document; <paramref name="numbering"/> is not thread-safe.
    /// </remarks>
    public PdfBatesRange AddBatesNumbers(PdfBatesNumbering numbering, Range? pages = null)
    {
        ArgumentNullException.ThrowIfNull(numbering);
        (int first, int count) = (pages ?? Range.All).GetOffsetAndLength(Pages.Count);
        return numbering.Apply(this, first, count, nameof(numbering));
    }
}
