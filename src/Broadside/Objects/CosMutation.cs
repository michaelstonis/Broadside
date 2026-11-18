namespace Broadside.Objects;

/// <summary>
/// Counts changes to COS containers, per document: every change to a <see cref="CosDictionary"/> or <see cref="CosArray"/>, and every
/// replacement of a <see cref="CosStream"/>'s data, through the public API advances the count of the document the container belongs
/// to (its <see cref="CosOwner"/>), and so does every indirect object added to or removed from a document
/// (<see cref="PdfDocument.AddObject"/>, <see cref="PdfDocument.RemoveObject"/>); reading and loading never do. A change to a container
/// that belongs to more than one document advances a process-wide count that every document's epoch includes, and a change to one
/// that belongs to no document advances nothing.
/// </summary>
/// <remarks>
/// <para>
/// A document's epoch (<see cref="EpochOf"/>) is the sum of its own count and the process-wide one: both only grow, so the sum moves
/// exactly when either does. A cache derived from many objects of one document (its display lists, decoded images, the structure
/// tree's indexes, ISO 32000-2 §14.7) records the epoch before it reads anything, and is current while the epoch has not moved. A
/// change to another document's objects moves only that document's count, so it leaves this document's caches current (issue #578).
/// </para>
/// <para>
/// Which document a container belongs to (<see cref="CosOwner"/>) is decided once: a loaded object belongs to the document that loaded
/// it, an object added as an indirect object (<see cref="PdfDocument.AddObject"/>) or put into a container that belongs to a document
/// belongs to that document, and so does every direct object it contains. A container put into a second document belongs to both
/// (<see cref="CosOwner.Shared"/>) from then on, with everything it contains, so its changes advance every document's epoch. A
/// container that belongs to no document is one no document reaches: every way into a document (loading, adding, putting it into a
/// container of the document) makes it the document's, and putting it there is itself a change of the document. So building new
/// objects, before they are added to a document, makes no cache stale.
/// </para>
/// <para>
/// Mutation is single-threaded per document, but documents are independent, so the counters are advanced atomically.
/// </para>
/// </remarks>
internal static class CosMutation
{
    /// <summary>How deep <see cref="Adopt"/> recurses before it continues with a stack on the heap.</summary>
    private const int MaxRecursion = 64;

    private static long _epoch;

    [ThreadStatic]
    private static int _quiet;

    [ThreadStatic]
    private static Stack<(CosObject Value, CosOwner Owner)>? _pending;

    /// <summary>Gets the process-wide count: changes to containers that belong to more than one document.</summary>
    public static long Epoch => Volatile.Read(ref _epoch);

    /// <summary>Gets the epoch of the document <paramref name="owner"/> stands for: its own changes plus the process-wide ones.</summary>
    /// <param name="owner">The document's owner.</param>
    /// <returns>The epoch; it moves on every change that can affect what the document reads, and only then.</returns>
    public static long EpochOf(CosOwner owner) => Volatile.Read(ref _epoch) + owner.Count;

    /// <summary>
    /// Records one change to a container that belongs to <paramref name="owner"/>, unless this thread is inside a <see cref="Quiet"/>
    /// scope or the container belongs to no document.
    /// </summary>
    /// <param name="owner">The container's owner; <see langword="null"/> for a container that belongs to no document.</param>
    public static void Advance(CosOwner? owner)
    {
        if (_quiet != 0 || owner is null)
        {
            return;
        }

        if (owner.IsShared)
        {
            Interlocked.Increment(ref _epoch);
        }
        else
        {
            owner.Advance();
        }
    }

    /// <summary>
    /// Makes <paramref name="value"/>, and every direct object it contains, belong to <paramref name="owner"/>: an object that belonged
    /// to no document becomes <paramref name="owner"/>'s, and one that belonged to another document becomes
    /// <see cref="CosOwner.Shared"/>. References are not followed: what they name belongs to the document that resolves them.
    /// </summary>
    /// <param name="value">The object put into the document.</param>
    /// <param name="owner">The document's owner, or <see cref="CosOwner.Shared"/>.</param>
    /// <remarks>Allocates nothing unless the objects nest deeper than <see cref="MaxRecursion"/>.</remarks>
    public static void Adopt(CosObject value, CosOwner owner) => AdoptFrom(value, owner, 0);

    /// <summary>
    /// Starts a scope in which this thread's changes do not advance any epoch: for building objects that belong to no document and no
    /// cache depends on, such as a viewer's transient appearance built while a display list is built (which would otherwise make the
    /// list, and every other cache, stale at once). Nothing a document holds may be changed inside it.
    /// </summary>
    /// <returns>The scope; dispose it to end it.</returns>
    public static QuietScope Quiet()
    {
        _quiet++;
        return default;
    }

    /// <summary>The owner <paramref name="current"/> becomes when put into <paramref name="owner"/>'s document; null when unchanged.</summary>
    private static CosOwner? Joined(CosOwner? current, CosOwner owner) =>
        current is null ? owner
        : ReferenceEquals(current, owner) || current.IsShared ? null
        : CosOwner.Shared;

    private static void AdoptFrom(CosObject value, CosOwner owner, int depth)
    {
        CosDictionary? dictionary = value switch
        {
            CosDictionary direct => direct,
            CosStream stream => stream.Dictionary,
            _ => null,
        };

        if (dictionary is not null)
        {
            if (Joined(dictionary.Owner, owner) is not { } joined)
            {
                return;
            }

            dictionary.Owner = joined;
            for (int index = 0; index < dictionary.Count; index++)
            {
                AdoptChild(dictionary.GetAt(index).Value, joined, depth);
            }
        }
        else if (value is CosArray array)
        {
            if (Joined(array.Owner, owner) is not { } joined)
            {
                return;
            }

            array.Owner = joined;
            for (int index = 0; index < array.Count; index++)
            {
                AdoptChild(array[index], joined, depth);
            }
        }

        if (depth == 0 && _pending is { Count: > 0 } pending)
        {
            while (pending.TryPop(out (CosObject Value, CosOwner Owner) next))
            {
                AdoptFrom(next.Value, next.Owner, 1);
            }
        }
    }

    private static void AdoptChild(CosObject child, CosOwner owner, int depth)
    {
        if (child is not (CosDictionary or CosArray or CosStream))
        {
            return;
        }

        if (depth < MaxRecursion)
        {
            AdoptFrom(child, owner, depth + 1);
        }
        else
        {
            (_pending ??= new()).Push((child, owner));
        }
    }

    /// <summary>A <see cref="Quiet"/> scope.</summary>
    public readonly struct QuietScope : IDisposable
    {
        /// <summary>Ends the scope.</summary>
        public void Dispose() => _quiet--;
    }
}

/// <summary>
/// The document a COS container belongs to, for counting changes per document (<see cref="CosMutation"/>): each document has one, and
/// <see cref="Shared"/> stands for every document at once.
/// </summary>
/// <remarks>Read by any thread; advanced by the thread that mutates the document.</remarks>
internal sealed class CosOwner
{
    private long _count;

    /// <summary>Gets the owner of a container that belongs to more than one document: its changes advance every document's epoch.</summary>
    public static CosOwner Shared { get; } = new();

    /// <summary>Gets a value indicating whether this is <see cref="Shared"/>.</summary>
    public bool IsShared => ReferenceEquals(this, Shared);

    /// <summary>Gets how many changes the document's containers have had.</summary>
    public long Count => Volatile.Read(ref _count);

    /// <summary>Records one change.</summary>
    internal void Advance() => Interlocked.Increment(ref _count);
}
