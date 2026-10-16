using Broadside.Caching;

namespace Broadside;

/// <summary>Render caches with byte budgets (issue #103, spec #33 viewer capability 13 and story 8).</summary>
public sealed partial class PdfDocument
{
    /// <summary>Gets the document's render caches, bounded by the engine's budgets.</summary>
    internal DocumentCaches Caches { get; private init; } = null!;

    /// <summary>
    /// Gets the counters of the document's render caches as they are now: decoded content, decoded images and masks, glyph outlines
    /// and display lists, each with its budget, the bytes and entries it holds, the most bytes it has held, and its hits, misses
    /// and evictions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec #33 viewer capability 13 and story 8. Each cache holds what rendering derives from the document, up to the budget set
    /// with <see cref="PdfOptions.WithContentCacheBudget"/>, <see cref="PdfOptions.WithImageCacheBudget"/>,
    /// <see cref="PdfOptions.WithGlyphCacheBudget"/> and <see cref="PdfOptions.WithDisplayListCacheBudget"/>, dropping the least
    /// recently used entries to stay within it; a budget of zero turns that cache off without changing any output.
    /// </para>
    /// <para>
    /// The caches are safe for any number of concurrent renders while nobody changes the document (the thread-safety contract on
    /// <see cref="PdfDocument"/>). An entry derived from many objects (an image with its masks and colour space, a display list) is
    /// current only while no COS object has changed through the public API since it was made, so after any edit, of this
    /// document or another, it is rebuilt at its next use; decoded content follows its own stream's changes only, and a glyph
    /// outline belongs to a parsed program that never changes.
    /// </para>
    /// </remarks>
    public PdfDocumentCacheStatistics CacheStatistics => Caches.GetStatistics();

    /// <summary>Gets the bytes the document's render caches hold right now, all four together, without allocating.</summary>
    /// <remarks>Spec #33 viewer capability 13: a viewer budgets these bytes with its tiles (<see cref="TrimCaches"/>).</remarks>
    public long CachedBytes => Caches.Bytes;

    /// <summary>
    /// Drops the least recently used entries of the render caches until they hold at most <paramref name="maxBytes"/> together:
    /// decoded content first (only builds read it), then images, glyph outlines and, last, display lists. Nothing is disposed; a
    /// dropped entry is rebuilt at its next use and renders the same.
    /// </summary>
    /// <param name="maxBytes">The most bytes the caches may keep; zero empties them.</param>
    /// <returns>The bytes the caches hold afterwards.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is negative.</exception>
    /// <remarks>
    /// Spec #33 viewer capability 13: one memory budget covers a viewer's tiles, its display lists and the core caches, so the
    /// viewer trims the caches to what its tiles leave. Safe to call while other threads render.
    /// </remarks>
    public long TrimCaches(long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        return Caches.Trim(maxBytes);
    }
}
