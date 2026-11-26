using Broadside.Graphics;

namespace Broadside;

/// <content>Stamps, watermarks and headers and footers on existing pages (issue #627).</content>
public sealed partial class PdfDocument
{
    /// <summary>Draws <paramref name="stamp"/> on each page of <paramref name="pages"/> (every page by default), marked as an artifact.</summary>
    /// <param name="stamp">The stamp: text, an image, a form or a page, with its placement.</param>
    /// <param name="pages">The pages, by index; <see langword="null"/> for all. An empty range does nothing.</param>
    /// <exception cref="ArgumentException">
    /// The stamp's font, image, form, colour space or optional content group belongs to another document, or its font cannot show a
    /// character of a page's text (never drawn as <c>.notdef</c>); no page changes.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is outside the document's pages.</exception>
    /// <exception cref="NotSupportedException">A page stamp's page cannot be made into a form (<see cref="CreateFormXObject(PdfPage, PdfPageFormOptions)"/>); no page changes.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.8.2.2 (Table 363): the stamp is wrapped in <c>/Artifact</c> marked content with the stamp's type, subtype and its
    /// bounding box, so text extraction leaves it out by default; with an optional content group, also in <c>/OC</c> (§8.11.3.2,
    /// PDF 1.5). A foreground stamp is drawn after the page's content (<see cref="PdfPage.CreateCanvas"/>); a background stamp is a
    /// new content stream put first in <c>Contents</c>, the existing content wrapped in <c>q</c> and <c>Q</c>. Images and forms are
    /// drawn with one <c>cm</c>; one image or form XObject and one font resource serve every page. Tokens expand per page (§12.4.2).
    /// </para>
    /// <para>
    /// Changes go into each page's <c>Contents</c> and <c>Resources</c> directly: pages that share an inherited resource dictionary
    /// all gain the stamp's resource names, as with <see cref="PdfPage.CreateCanvas"/>. Read repairs of the pages are kept. A mutation.
    /// </para>
    /// </remarks>
    public void Stamp(PdfStamp stamp, Range? pages = null)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        StampWriter.Stamp(this, stamp, pages);
    }

    /// <summary>Adds the texts of <paramref name="headerFooter"/> to each page of <paramref name="pages"/> (every page by default).</summary>
    /// <param name="headerFooter">The texts, font and margin.</param>
    /// <param name="pages">The pages, by index; <see langword="null"/> for all. An empty range does nothing.</param>
    /// <exception cref="ArgumentException">The font belongs to another document or cannot show a character of a text; no page changes.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is outside the document's pages.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.2.2, Table 363: each text is a <c>Pagination</c> artifact attached to the <c>Top</c> or <c>Bottom</c> edge with
    /// its bounding box, subtype <c>Header</c> or <c>Footer</c>; a text with <c>{page}</c> or <c>{label}</c> has the PDF 2.0
    /// <c>PageNum</c> subtype when the document's version is 2.0 or later. Texts are placed in the crop box as the page is viewed,
    /// <see cref="PdfHeaderFooter.Margin"/> from its edges, in the page's foreground. A mutation.
    /// </remarks>
    public void AddHeaderFooter(PdfHeaderFooter headerFooter, Range? pages = null)
    {
        ArgumentNullException.ThrowIfNull(headerFooter);
        StampWriter.AddHeaderFooter(this, headerFooter, pages);
    }
}
