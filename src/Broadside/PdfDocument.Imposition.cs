using Broadside.Graphics;

namespace Broadside;

/// <content>N-up sheets, booklets and tiles: imposition into a new document (issue #647).</content>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.10.1 and §14.11.2. Each source page is drawn as a form XObject of the new document
/// (<see cref="CreateFormXObject(PdfPage, PdfPageFormOptions)"/>) with its <see cref="PdfImpositionOptions.Boundary"/>, its rotation
/// applied (§7.7.3.3) and, with <see cref="PdfImpositionOptions.IncludeAnnotations"/>, its printable annotations' appearances drawn in.
/// </para>
/// <para>
/// The new document is a PDF 2.0 document opened with this document's options. What it does not carry: links and form fields (only
/// their appearances, when drawn in), the outline, named destinations, page labels and the structure tree; it is untagged. This
/// document is only read, so readers may share it while the new one is made; its read repairs stay its own, and no diagnostic is added.
/// </para>
/// </remarks>
public sealed partial class PdfDocument
{
    /// <summary>Creates a new document with the pages of <paramref name="pages"/> laid out <paramref name="columns"/> by <paramref name="rows"/> on each sheet.</summary>
    /// <param name="columns">The cells across a sheet; at least 1.</param>
    /// <param name="rows">The cells down a sheet; at least 1.</param>
    /// <param name="options">The sheet, margin, gutter, order and what of each page is drawn; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based page indices; <see langword="null"/> for every page. An empty range makes an empty document.</param>
    /// <returns>The new document, not yet saved; dispose it when done.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> or <paramref name="rows"/> is less than 1, or <paramref name="pages"/> is outside the document's pages.</exception>
    /// <exception cref="ArgumentException">The margin and gutters leave no room for a cell.</exception>
    /// <exception cref="NotSupportedException">A page's content uses a filter this engine does not decode, or is encrypted with a key the document was opened without.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.10.1 and §14.11.2. Pages are taken in order and fill the cells in <see cref="PdfImpositionOptions.Order"/>; the
    /// last sheet may be partly empty. Each page is uniformly scaled to fit its cell and centered in it, turned 90° counterclockwise
    /// when the cell's orientation (portrait or landscape) differs from the page's as displayed. See the class remarks for what the
    /// new document does not carry.
    /// </remarks>
    public PdfDocument CreateNUp(int columns, int rows, PdfImpositionOptions? options = null, Range? pages = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        return Impose(options, pages, (target, first, count, chosen) => PageImposition.NUp(target, this, first, count, columns, rows, chosen));
    }

    /// <summary>Creates a new document with the pages of <paramref name="pages"/> laid out 2-up in saddle-stitch order, for folding into a booklet.</summary>
    /// <param name="options">The sheet, margin, gutter (the spine) and what of each page is drawn; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based page indices; <see langword="null"/> for every page. An empty range makes an empty document.</param>
    /// <returns>The new document, not yet saved; dispose it when done. Its pages alternate front and back of each sheet.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pages"/> is outside the document's pages.</exception>
    /// <exception cref="ArgumentException">The margin and gutter leave no room for a cell.</exception>
    /// <exception cref="NotSupportedException">A page's content uses a filter this engine does not decode, or is encrypted with a key the document was opened without.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.10.1 and §12.2 (<c>Direction</c>, Table 147). The <c>n</c> pages are padded with blank pages to a multiple of 4;
    /// the front of sheet 1 holds pages (n, 1), its back (2, n − 1), and so on inward. Right to left, the pair is swapped, when the
    /// viewer preferences' <c>Direction</c> is <c>R2L</c> or the order is <see cref="PdfImpositionOrder.RightToLeftThenDown"/>; other
    /// orders are ignored. Pages are placed as <see cref="CreateNUp"/> places them.
    /// </remarks>
    public PdfDocument CreateBooklet(PdfImpositionOptions? options = null, Range? pages = null) =>
        Impose(options, pages, (target, first, count, chosen) => PageImposition.Booklet(target, this, first, count, chosen));

    /// <summary>Creates a new document with each page of <paramref name="pages"/> split into <paramref name="columns"/> by <paramref name="rows"/> tiles, each enlarged onto a sheet of its own.</summary>
    /// <param name="columns">The tiles across a page; at least 1.</param>
    /// <param name="rows">The tiles down a page; at least 1.</param>
    /// <param name="options">The sheet, margin, overlap, order and what of each page is drawn; <see langword="null"/> for the defaults.</param>
    /// <param name="pages">The 0-based page indices; <see langword="null"/> for every page. An empty range makes an empty document.</param>
    /// <returns>The new document, not yet saved; dispose it when done.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="columns"/> or <paramref name="rows"/> is less than 1, or <paramref name="pages"/> is outside the document's pages.</exception>
    /// <exception cref="ArgumentException">The margin leaves no room, or the overlap is not smaller than a page it splits.</exception>
    /// <exception cref="NotSupportedException">A page's content uses a filter this engine does not decode, or is encrypted with a key the document was opened without.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.10.1 and §8.5.4 (clipping). Neighboring tiles share <see cref="PdfImpositionOptions.Overlap"/> points of the
    /// page, so the sheets can be pasted together. Each tile is the page's form clipped to the tile, scaled uniformly to fit the sheet
    /// inside its margin, centered, and turned 90° counterclockwise when its orientation differs from the sheet's. Tiles are output
    /// page by page, each page's in <see cref="PdfImpositionOptions.Order"/>. The sheet defaults to the page's size, as displayed.
    /// </remarks>
    public PdfDocument CreateTiles(int columns, int rows, PdfImpositionOptions? options = null, Range? pages = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        return Impose(options, pages, (target, first, count, chosen) => PageImposition.Tiles(target, this, first, count, columns, rows, chosen));
    }

    private PdfDocument Impose(PdfImpositionOptions? options, Range? pages, Action<PdfDocument, int, int, PdfImpositionOptions> layout)
    {
        (int first, int count) = (pages ?? Range.All).GetOffsetAndLength(Pages.Count);
        PdfDocument target = CreateEmpty(Configuration);
        try
        {
            if (count > 0)
            {
                layout(target, first, count, options ?? new PdfImpositionOptions());
            }

            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }
}
