using System.Diagnostics.CodeAnalysis;

namespace Broadside.Caching;

/// <summary>A value a <see cref="ByteBudgetCache{TKey, TEntry}"/> holds: what it costs, and whether it still describes the document.</summary>
internal interface IBudgetedEntry
{
    /// <summary>Gets the bytes the entry is charged against the budget: computed from the sizes of what it holds, so deterministic.</summary>
    long Bytes { get; }

    /// <summary>Gets a value indicating whether the entry still describes the document; a stale entry is dropped at its next lookup.</summary>
    bool IsCurrent { get; }
}

/// <summary>
/// A cache bounded by a byte budget: entries are charged their <see cref="IBudgetedEntry.Bytes"/>, the least recently used are evicted
/// when an addition would exceed the budget, an entry larger than the whole budget is never stored, and a budget of zero stores
/// nothing (spec #33 viewer capability 13, issue #103).
/// </summary>
/// <remarks>
/// Safe for any number of threads (the document's thread-safety contract): every operation holds one lock for a dictionary lookup
/// and a few pointer moves, and a hit allocates nothing. Evicted values are only dropped, never disposed: a display list may still
/// hold them, and the garbage collector frees them with the last holder.
/// </remarks>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TEntry">The entry.</typeparam>
internal sealed class ByteBudgetCache<TKey, TEntry>
    where TKey : notnull
    where TEntry : class, IBudgetedEntry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, Node> _nodes;

    // Most recently used first.
    private Node? _head;
    private Node? _tail;
    private long _bytes;
    private long _peakBytes;
    private long _hits;
    private long _misses;
    private long _evictions;

    /// <summary>Initializes a new instance of the <see cref="ByteBudgetCache{TKey, TEntry}"/> class.</summary>
    /// <param name="budget">The most bytes held; zero turns the cache off.</param>
    /// <param name="comparer">How keys compare, or <see langword="null"/> for the default.</param>
    public ByteBudgetCache(long budget, IEqualityComparer<TKey>? comparer = null)
    {
        Budget = budget;
        _nodes = new Dictionary<TKey, Node>(comparer);
    }

    /// <summary>Gets the most bytes the cache holds.</summary>
    public long Budget { get; }

    /// <summary>Returns the entry of <paramref name="key"/> when it is held and current, making it the most recently used.</summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>Whether it was found; a stale entry is removed and counts as a miss.</returns>
    public bool TryGet(TKey key, [NotNullWhen(true)] out TEntry? entry)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(key, out Node? node))
            {
                if (node.Entry.IsCurrent)
                {
                    MoveToFront(node);
                    _hits++;
                    entry = node.Entry;
                    return true;
                }

                Remove(node);
            }

            _misses++;
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Stores <paramref name="entry"/> under <paramref name="key"/> as the most recently used, replacing any entry the key had and
    /// evicting the least recently used until it fits; an entry larger than the budget is not stored.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="entry">The entry.</param>
    public void Add(TKey key, TEntry entry)
    {
        long bytes = entry.Bytes;
        if (bytes > Budget)
        {
            return;
        }

        lock (_gate)
        {
            if (_nodes.TryGetValue(key, out Node? existing))
            {
                Remove(existing);
            }

            while (_tail is not null && _bytes + bytes > Budget)
            {
                Remove(_tail);
                _evictions++;
            }

            var node = new Node(key, entry, bytes);
            _nodes.Add(key, node);
            node.Next = _head;
            _head?.Previous = node;
            _head = node;
            _tail ??= node;
            _bytes += bytes;
            _peakBytes = Math.Max(_peakBytes, _bytes);
        }
    }

    /// <summary>Drops every entry; the counters keep counting.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _nodes.Clear();
            _head = null;
            _tail = null;
            _bytes = 0;
        }
    }

    /// <summary>Returns the counters as they are now.</summary>
    /// <returns>The counters.</returns>
    public PdfCacheStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new PdfCacheStatistics(Budget, _bytes, _peakBytes, _nodes.Count, _hits, _misses, _evictions);
        }
    }

    private void MoveToFront(Node node)
    {
        if (node == _head)
        {
            return;
        }

        Unlink(node);
        node.Next = _head;
        _head!.Previous = node;
        _head = node;
    }

    private void Remove(Node node)
    {
        Unlink(node);
        _nodes.Remove(node.Key);
        _bytes -= node.Bytes;
    }

    private void Unlink(Node node)
    {
        if (node.Previous is not null)
        {
            node.Previous.Next = node.Next;
        }
        else
        {
            _head = node.Next;
        }

        if (node.Next is not null)
        {
            node.Next.Previous = node.Previous;
        }
        else
        {
            _tail = node.Previous;
        }

        node.Previous = null;
        node.Next = null;
    }

    private sealed class Node(TKey key, TEntry entry, long bytes)
    {
        public TKey Key { get; } = key;

        public TEntry Entry { get; } = entry;

        public long Bytes { get; } = bytes;

        public Node? Previous { get; set; }

        public Node? Next { get; set; }
    }
}
