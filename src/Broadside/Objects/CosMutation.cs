namespace Broadside.Objects;

/// <summary>
/// A process-wide count of changes to COS containers: every change to a <see cref="CosDictionary"/> or <see cref="CosArray"/>, and
/// every replacement of a <see cref="CosStream"/>'s data, through the public API advances it, and so does every indirect object
/// added to or removed from a document (<see cref="PdfDocument.AddObject"/>, <see cref="PdfDocument.RemoveObject"/>); reading and
/// loading never do.
/// </summary>
/// <remarks>
/// <para>
/// A cache built from many containers (the structure tree's indexes, ISO 32000-2 §14.7) records the epoch it was last known current
/// at, with each container's <c>Version</c>. While the epoch has not moved nothing can have changed, so the cache is used without
/// looking at its containers; once it has moved, the cache compares its containers' versions once and, when they all still match,
/// records the new epoch. A change to an unrelated document only costs that one comparison.
/// </para>
/// <para>
/// Mutation is single-threaded per document, but documents are independent, so the counter is advanced atomically.
/// </para>
/// </remarks>
internal static class CosMutation
{
    private static long _epoch;

    /// <summary>Gets the current epoch.</summary>
    public static long Epoch => Volatile.Read(ref _epoch);

    [ThreadStatic]
    private static int _quiet;

    /// <summary>Records one change, unless this thread is inside a <see cref="Quiet"/> scope.</summary>
    public static void Advance()
    {
        if (_quiet == 0)
        {
            Interlocked.Increment(ref _epoch);
        }
    }

    /// <summary>
    /// Starts a scope in which this thread's changes do not advance the epoch: for building objects that belong to no document and no
    /// cache depends on, such as a viewer's transient appearance built while a display list is built (which would otherwise make the
    /// list, and every other cache, stale at once). Nothing a document holds may be changed inside it.
    /// </summary>
    /// <returns>The scope; dispose it to end it.</returns>
    public static QuietScope Quiet()
    {
        _quiet++;
        return default;
    }

    /// <summary>A <see cref="Quiet"/> scope.</summary>
    public readonly struct QuietScope : IDisposable
    {
        /// <summary>Ends the scope.</summary>
        public void Dispose() => _quiet--;
    }
}
