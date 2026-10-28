using System.Runtime.CompilerServices;

namespace Broadside.Rendering;

/// <summary>
/// A converted image a <see cref="SharedImageCache{T}"/> holds: leased while a backend draws it, freed exactly once, when it has left
/// the cache and its last lease is released.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.5 (an image's samples converted for drawing). The fields are the cache's, read and written under its lock. This
/// file is compiled into each backend assembly that shares converted images, so each has its own caches.
/// </remarks>
internal abstract class SharedImage
{
    private int _freed;

    /// <summary>Gets or sets how many draws hold the image now.</summary>
    internal int Leases { get; set; }

    /// <summary>Gets or sets a value indicating whether the cache keeps the image (it is its key's current image).</summary>
    internal bool Kept { get; set; }

    /// <summary>Gets or sets a value indicating whether the image is one of the two kept outside the budget.</summary>
    internal bool OutsideBudget { get; set; }

    /// <summary>Gets or sets the bytes the cache counts for the image: its pixels and whatever was added to them since.</summary>
    internal long CountedBytes { get; set; }

    /// <summary>Gets or sets the slot of the key the image was converted for.</summary>
    internal object? Owner { get; set; }

    /// <summary>Gets or sets the image's node in the cache's least-recently-used list.</summary>
    internal LinkedListNode<SharedImage>? Recent { get; set; }

    /// <summary>Gets a value indicating whether the image has been freed.</summary>
    internal bool IsFreed => Volatile.Read(ref _freed) != 0;

    /// <summary>Frees the image's pixels the first time it is called, never again.</summary>
    internal void FreeOnce()
    {
        if (Interlocked.Exchange(ref _freed, 1) == 0)
        {
            Free();
        }
    }

    /// <summary>Releases the image's pixels (native images and their resamplings); called once.</summary>
    protected abstract void Free();
}

/// <summary>
/// The converted images every backend of one kind that draws the same document shares (issue #432): one conversion per image (or
/// mask) and key, whichever backend of a viewer's pool draws it, kept least recently used within the document's image cache budget
/// (<see cref="PdfOptions.ImageCacheBudget"/>), the two used last that are larger than the whole budget outside it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.5. A viewer's tile backends draw the tiles of a page concurrently, one backend per tile (spec #33 story 14, the
/// session's pool); each converting its own copy of an 8-megapixel photograph took gigabytes. Here a key's conversion is made once:
/// a backend that misses takes the key's converting lock (<see cref="Slot.Converting"/>), looks again, and converts only when no other
/// backend did meanwhile (single flight); the others wait for it and draw its result.
/// </para>
/// <para>
/// Thread-safe. A draw leases the image it uses (<see cref="Lease"/>, <see cref="Release"/>); an image evicted, or replaced by
/// another conversion of its key, is freed when its last lease is released, never while a draw holds it. Backends join a document's
/// cache (<see cref="Join"/>) and leave it when disposed (<see cref="Leave"/>); when the last one leaves, every image is freed. Finding
/// a kept image allocates nothing. Keys are weak, as in each backend's own cache.
/// </para>
/// </remarks>
/// <typeparam name="T">The converted image.</typeparam>
internal sealed class SharedImageCache<T>
    where T : SharedImage
{
    private static readonly ConditionalWeakTable<PdfDocument, SharedImageCache<T>> Documents = [];

    private readonly Lock _gate = new();
    private readonly WeakReference<PdfDocument> _document;
    private readonly ConditionalWeakTable<object, Slot> _slots = [];
    private readonly LinkedList<SharedImage> _recent = new();
    private T? _oversize;
    private T? _oversizeOlder;
    private long _bytes;
    private long _budget;
    private int _users;
    private int _converted;

    private SharedImageCache(PdfDocument document) => _document = new WeakReference<PdfDocument>(document);

    /// <summary>Gets the most bytes of images kept within the budget.</summary>
    public long Budget
    {
        get
        {
            lock (_gate)
            {
                return _budget;
            }
        }
    }

    /// <summary>Gets the bytes of images kept within the budget.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>Gets how many backends use the cache.</summary>
    public int Users
    {
        get
        {
            lock (_gate)
            {
                return _users;
            }
        }
    }

    /// <summary>Gets how many images were published (each one conversion).</summary>
    public int Converted => Volatile.Read(ref _converted);

    /// <summary>Joins <paramref name="document"/>'s cache as one more backend, with the document's image cache budget.</summary>
    /// <param name="document">The document drawn.</param>
    /// <param name="budget">Its image cache budget (<see cref="PdfOptions.ImageCacheBudget"/>).</param>
    /// <returns>The cache; the caller calls <see cref="Leave"/> once when it no longer draws the document.</returns>
    public static SharedImageCache<T> Join(PdfDocument document, long budget)
    {
        SharedImageCache<T> cache = Documents.GetValue(document, static document => new SharedImageCache<T>(document));
        lock (cache._gate)
        {
            cache._users++;
            cache._budget = budget;
        }

        return cache;
    }

    /// <summary>Leaves the cache; the last backend to leave frees every image no draw holds (the rest when released).</summary>
    public void Leave()
    {
        List<T>? free = null;
        lock (_gate)
        {
            if (--_users > 0)
            {
                return;
            }

            while (_recent.Last is { } last)
            {
                Remove((T)last.Value, ref free);
            }

            while (_oversize is { } oversize)
            {
                Remove(oversize, ref free);
            }
        }

        FreeAll(free);
    }

    /// <summary>Whether this is <paramref name="document"/>'s cache.</summary>
    public bool IsFor(PdfDocument document) => _document.TryGetTarget(out PdfDocument? mine) && ReferenceEquals(mine, document);

    /// <summary>Returns the slot of <paramref name="key"/> (an interned display-list image or mask).</summary>
    public Slot SlotOf(object key) => _slots.GetValue(key, static _ => new Slot());

    /// <summary>Leases the slot's current image, or returns <see langword="null"/> when none is kept.</summary>
    public T? Lease(Slot slot)
    {
        lock (_gate)
        {
            if (slot.Current is not { } image)
            {
                return null;
            }

            image.Leases++;
            if (!image.OutsideBudget)
            {
                _recent.Remove(image.Recent!);
                _recent.AddFirst(image.Recent!);
            }
            else if (image == _oversizeOlder)
            {
                (_oversize, _oversizeOlder) = (image, _oversize);
            }

            return image;
        }
    }

    /// <summary>Releases a lease; the image is freed when it was the last and the cache no longer keeps it.</summary>
    public void Release(T? image)
    {
        if (image is null)
        {
            return;
        }

        bool free;
        lock (_gate)
        {
            free = --image.Leases == 0 && !image.Kept;
        }

        if (free)
        {
            image.FreeOnce();
        }
    }

    /// <summary>
    /// Makes <paramref name="image"/>, just converted, the slot's current image, leased once for its converter: kept when it fits the
    /// budget (evicting the least recently used), or as one of the two outside it when larger than a non-zero budget; with a budget
    /// of zero it is kept by no one and freed when released. The slot's previous image is freed once no draw holds it.
    /// </summary>
    /// <param name="slot">The key's slot; the caller holds its <see cref="Slot.Converting"/> lock.</param>
    /// <param name="image">The new image.</param>
    /// <param name="bytes">Its pixels' bytes.</param>
    public void Publish(Slot slot, T image, long bytes)
    {
        List<T>? free = null;
        lock (_gate)
        {
            _converted++;
            image.Leases = 1;
            image.Owner = slot;
            image.CountedBytes = bytes;
            if (slot.Current is { } previous)
            {
                Remove(previous, ref free);
            }

            if (bytes > _budget)
            {
                if (_budget > 0)
                {
                    if (_oversizeOlder is { } oldest)
                    {
                        Remove(oldest, ref free);
                    }

                    image.Kept = true;
                    image.OutsideBudget = true;
                    (_oversize, _oversizeOlder) = (image, _oversize);
                    slot.Current = image;
                }
            }
            else
            {
                Evict(bytes, except: null, ref free);
                image.Kept = true;
                image.Recent ??= new LinkedListNode<SharedImage>(image);
                _recent.AddFirst(image.Recent);
                _bytes += bytes;
                slot.Current = image;
            }
        }

        FreeAll(free);
    }

    /// <summary>
    /// Counts <paramref name="bytes"/> more for a kept image (a resampling made from it) when they fit the budget beside it, evicting
    /// others; an image outside the budget always takes them (its resamplings are bounded by their own limit and slots).
    /// </summary>
    /// <returns>Whether the image keeps the addition; when not, the caller frees it after drawing.</returns>
    public bool Grow(T image, long bytes)
    {
        List<T>? free = null;
        bool kept;
        lock (_gate)
        {
            if (!image.Kept)
            {
                return false;
            }

            if (image.OutsideBudget)
            {
                image.CountedBytes += bytes;
                return true;
            }

            if (bytes > _budget - image.CountedBytes)
            {
                return false;
            }

            Evict(bytes, except: image, ref free);
            kept = _bytes + bytes <= _budget;
            if (kept)
            {
                image.CountedBytes += bytes;
                _bytes += bytes;
            }
        }

        FreeAll(free);
        return kept;
    }

    private static void FreeAll(List<T>? free)
    {
        if (free is null)
        {
            return;
        }

        foreach (T image in free)
        {
            image.FreeOnce();
        }
    }

    private void Evict(long bytes, T? except, ref List<T>? free)
    {
        while (_bytes + bytes > _budget && _recent.Last is { } last && last.Value != except)
        {
            Remove((T)last.Value, ref free);
        }
    }

    /// <summary>Takes a kept image out of the cache (under the lock); it is freed now when no draw holds it, else by the last release.</summary>
    private void Remove(T image, ref List<T>? free)
    {
        if (image.OutsideBudget)
        {
            image.OutsideBudget = false;
            if (image == _oversize)
            {
                (_oversize, _oversizeOlder) = (_oversizeOlder, null);
            }
            else
            {
                _oversizeOlder = null;
            }
        }
        else
        {
            _recent.Remove(image.Recent!);
            _bytes -= image.CountedBytes;
        }

        image.Kept = false;
        if (image.Owner is Slot slot && slot.Current == image)
        {
            slot.Current = null;
        }

        if (image.Leases == 0)
        {
            (free ??= []).Add(image);
        }
    }

    /// <summary>A key's current image, and the lock its conversion is made under.</summary>
    public sealed class Slot
    {
        /// <summary>Gets the lock a backend holds while it converts the key's image, so that one conversion is made at a time.</summary>
        public Lock Converting { get; } = new();

        /// <summary>Gets or sets the image kept for the key (under the cache's lock).</summary>
        internal T? Current { get; set; }
    }
}
