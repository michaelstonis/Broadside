namespace Broadside;

/// <summary>The counters of a document's render caches, read one cache at a time (<see cref="PdfDocument.CacheStatistics"/>).</summary>
/// <param name="Content">Decoded content streams of pages, form XObjects, patterns and Type 3 glyphs (<see cref="PdfOptions.ContentCacheBudget"/>).</param>
/// <param name="Images">Decoded image XObjects with their masks, and stencil masks (<see cref="PdfOptions.ImageCacheBudget"/>).</param>
/// <param name="Glyphs">Glyph outlines read from font programs (<see cref="PdfOptions.GlyphCacheBudget"/>).</param>
/// <param name="DisplayLists">Pages' display lists (<see cref="PdfOptions.DisplayListCacheBudget"/>).</param>
/// <remarks>
/// Spec #33 viewer capability 13 and story 8. Each cache is read under its own lock, so while other threads render the four may come
/// from slightly different moments.
/// </remarks>
public sealed record PdfDocumentCacheStatistics(
    PdfCacheStatistics Content,
    PdfCacheStatistics Images,
    PdfCacheStatistics Glyphs,
    PdfCacheStatistics DisplayLists);
