namespace Broadside;

/// <summary>The counters of one of a document's render caches at one moment (<see cref="PdfDocument.CacheStatistics"/>).</summary>
/// <param name="Budget">The most bytes the cache holds, from <see cref="PdfOptions"/>; zero when the cache is off.</param>
/// <param name="Bytes">The bytes the cache holds now.</param>
/// <param name="PeakBytes">The most bytes the cache has held at once since the document was opened; never more than <paramref name="Budget"/>.</param>
/// <param name="Count">The entries the cache holds now.</param>
/// <param name="Hits">The lookups that found a current entry.</param>
/// <param name="Misses">The lookups that found none, or one made stale by a change to the document, and so built the value afresh.</param>
/// <param name="Evictions">The entries dropped, least recently used first, to keep an addition within the budget.</param>
/// <remarks>
/// Spec #33 viewer capability 13 and story 8. Bytes are charged from the sizes of what an entry holds (sample buffers, path points,
/// decoded content, operation tables), so they are the same on every run and every machine. An entry larger than the whole budget
/// is used but never stored.
/// </remarks>
public readonly record struct PdfCacheStatistics(long Budget, long Bytes, long PeakBytes, int Count, long Hits, long Misses, long Evictions);
