using System.Buffers;
using System.Runtime.InteropServices;

namespace Broadside.Rendering;

/// <summary>
/// One run of equal coverage of a cached glyph, relative to the glyph's mask: what the scan converter's sweep hands a paint, kept so
/// that painting the glyph again replays the same runs.
/// </summary>
/// <param name="Row">The mask row.</param>
/// <param name="X">The first mask column.</param>
/// <param name="Count">The pixels in the run.</param>
/// <param name="Coverage">The 8-bit coverage, above zero.</param>
/// <remarks>ISO 32000-2 §10.7.4.</remarks>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal readonly record struct CoverageSpan(short Row, short X, short Count, byte Coverage);

/// <summary>
/// What a cached glyph mask is for: the font program and glyph id whose outline it covers, the linear part of the glyph-space-to-device
/// matrix, the origin's offset inside its pixel (exact, or rounded to a quarter pixel) and the coverage rule, compared bit for bit.
/// </summary>
/// <remarks>ISO 32000-2 §9.2.4 (glyph space) and §10.7.4.</remarks>
internal readonly struct GlyphKey : IEquatable<GlyphKey>
{
    private readonly object _program;
    private readonly int _glyphId;
    private readonly int _rule;
    private readonly long _a;
    private readonly long _b;
    private readonly long _c;
    private readonly long _d;
    private readonly long _x;
    private readonly long _y;

    public GlyphKey(object program, int glyphId, in Graphics.Matrix linear, double x, double y, CoverageRule rule)
    {
        _program = program;
        _glyphId = glyphId;
        _rule = (int)rule;
        _a = BitConverter.DoubleToInt64Bits(linear.A);
        _b = BitConverter.DoubleToInt64Bits(linear.B);
        _c = BitConverter.DoubleToInt64Bits(linear.C);
        _d = BitConverter.DoubleToInt64Bits(linear.D);
        _x = BitConverter.DoubleToInt64Bits(x);
        _y = BitConverter.DoubleToInt64Bits(y);
    }

    public bool Equals(GlyphKey other) =>
        ReferenceEquals(_program, other._program) && _glyphId == other._glyphId && _rule == other._rule && _a == other._a && _b == other._b
        && _c == other._c && _d == other._d && _x == other._x && _y == other._y;

    public override bool Equals(object? obj) => obj is GlyphKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_program));
        hash.Add(_glyphId);
        hash.Add(_rule);
        hash.Add(_a);
        hash.Add(_b);
        hash.Add(_c);
        hash.Add(_d);
        hash.Add(_x);
        hash.Add(_y);
        return hash.ToHashCode();
    }
}

/// <summary>
/// The software rasterizer's glyph cache (issue #370): the coverage runs of each glyph scan converted in its own small mask, kept by
/// <see cref="GlyphKey"/> within a byte budget and evicted least recently used, so that a glyph shown again at the same size, rotation
/// and sub-pixel offset is painted from its runs instead of being flattened, scan converted and sorted again.
/// </summary>
/// <remarks>
/// <para>
/// Not thread-safe: each rasterizer owns one (a viewer's tile backends each have their own rasterizer, so nothing is shared across
/// threads). Runs live in one pooled arena; a lookup that hits allocates nothing, and once the arena and the table have grown, a miss
/// allocates nothing either: eviction drops entries to three quarters of the budget, and the arena is compacted into a pooled
/// buffer of the same size when its end is reached.
/// </para>
/// <para>ISO 32000-2 §10.7.4.</para>
/// </remarks>
internal sealed class GlyphCache : IDisposable
{
    /// <summary>The bytes an entry is charged beyond its runs: the key, the table slot and the entry record.</summary>
    internal const int EntryOverhead = 96;

    private const int SpanBytes = 8;
    private const int InitialArena = 4096;

    // Made at the first glyph, so that a rasterizer (or a nested backend) that draws no text allocates nothing for it.
    private Dictionary<GlyphKey, int>? _index;
    private Entry[] _entries = [];
    private int _entryCount;
    private int _freeEntry = -1;
    private int _head = -1;
    private int _tail = -1;

    private CoverageSpan[] _arena = [];
    private int _arenaEnd;
    private int _liveSpans;

    private CoverageSpan[] _scratch = [];
    private int _scratchCount;

    private long _budget;
    private long _bytes;
    private long _peakBytes;
    private long _hits;
    private long _misses;
    private long _evictions;

    /// <summary>Initializes a new instance of the <see cref="GlyphCache"/> class.</summary>
    /// <param name="budget">The most bytes held; zero stores nothing.</param>
    public GlyphCache(long budget) => _budget = budget;

    /// <summary>Gets or sets the most bytes the cache holds; setting it to a new value empties the cache.</summary>
    public long Budget
    {
        get => _budget;
        set
        {
            if (value != _budget)
            {
                _budget = value;
                Clear();
            }
        }
    }

    /// <summary>Gets the counters.</summary>
    public PdfCacheStatistics Statistics => new(_budget, _bytes, _peakBytes, _index?.Count ?? 0, _hits, _misses, _evictions);

    /// <summary>
    /// Returns the entry of <paramref name="key"/> when held, making it the most recently used. Lookups are not counted here: the
    /// caller counts each glyph it paints (<see cref="CountLookup"/>), so that glyphs a tile culls do not count.
    /// </summary>
    public bool TryGet(in GlyphKey key, out GlyphMask mask)
    {
        if (_index is not null && _index.TryGetValue(key, out int slot))
        {
            MoveToFront(slot);
            ref Entry entry = ref _entries[slot];
            mask = new GlyphMask(entry.Left, entry.Top, entry.Width, entry.Height, entry.Offset, entry.Length);
            return true;
        }

        mask = default;
        return false;
    }

    /// <summary>Counts a glyph painted from a mask that was held (a hit) or computed (a miss).</summary>
    public void CountLookup(bool hit)
    {
        if (hit)
        {
            _hits++;
        }
        else
        {
            _misses++;
        }
    }

    /// <summary>Gets the runs of a mask <see cref="TryGet"/> or <see cref="Add"/> returned; valid until the next <see cref="Add"/>.</summary>
    public ReadOnlySpan<CoverageSpan> Spans(in GlyphMask mask) =>
        mask.Offset < 0 ? _scratch.AsSpan(0, mask.Length) : _arena.AsSpan(mask.Offset, mask.Length);

    /// <summary>Starts collecting the runs of a new mask.</summary>
    public void BeginMask() => _scratchCount = 0;

    /// <summary>Appends one run of the mask being collected.</summary>
    public void Append(int row, int x, int count, int coverage)
    {
        if (_scratchCount == _scratch.Length)
        {
            CoverageSpan[] grown = ArrayPool<CoverageSpan>.Shared.Rent(Math.Max(256, _scratch.Length * 2));
            _scratch.AsSpan(0, _scratchCount).CopyTo(grown);
            if (_scratch.Length > 0)
            {
                ArrayPool<CoverageSpan>.Shared.Return(_scratch);
            }

            _scratch = grown;
        }

        _scratch[_scratchCount++] = new CoverageSpan((short)row, (short)x, (short)count, (byte)coverage);
    }

    /// <summary>
    /// Stores the mask collected since <see cref="BeginMask"/> under <paramref name="key"/> when it fits the budget, and returns it either
    /// way (from the collection buffer when it is not stored).
    /// </summary>
    public GlyphMask Add(in GlyphKey key, int left, int top, int width, int height)
    {
        int length = _scratchCount;
        long bytes = Bytes(length);
        if (bytes > _budget)
        {
            return new GlyphMask(left, top, width, height, -1, length);
        }

        if (_bytes + bytes > _budget)
        {
            // Down to three quarters of the budget, so that the next misses fit without evicting again.
            long target = Math.Max(0, Math.Min(_budget - bytes, _budget * 3 / 4));
            while (_tail >= 0 && _bytes > target)
            {
                Evict(_tail);
                _evictions++;
            }
        }

        if (_arenaEnd + length > _arena.Length)
        {
            Reserve(length);
        }

        int offset = _arenaEnd;
        _scratch.AsSpan(0, length).CopyTo(_arena.AsSpan(offset));
        _arenaEnd += length;
        _liveSpans += length;

        int slot = _freeEntry;
        if (slot >= 0)
        {
            _freeEntry = _entries[slot].Next;
        }
        else
        {
            if (_entryCount == _entries.Length)
            {
                Array.Resize(ref _entries, Math.Max(16, _entries.Length * 2));
            }

            slot = _entryCount++;
        }

        _entries[slot] = new Entry { Key = key, Left = left, Top = top, Width = width, Height = height, Offset = offset, Length = length, Previous = -1, Next = _head };
        if (_head >= 0)
        {
            _entries[_head].Previous = slot;
        }

        _head = slot;
        if (_tail < 0)
        {
            _tail = slot;
        }

        _index ??= new Dictionary<GlyphKey, int>();
        _index.Add(key, slot);
        _bytes += bytes;
        _peakBytes = Math.Max(_peakBytes, _bytes);
        return new GlyphMask(left, top, width, height, offset, length);
    }

    /// <summary>Drops every entry; the counters stay.</summary>
    public void Clear()
    {
        _index?.Clear();
        Array.Clear(_entries, 0, _entryCount);
        _entryCount = 0;
        _freeEntry = -1;
        _head = _tail = -1;
        _arenaEnd = 0;
        _liveSpans = 0;
        _bytes = 0;
    }

    /// <summary>Returns the pooled buffers.</summary>
    public void Dispose()
    {
        Clear();
        if (_arena.Length > 0)
        {
            ArrayPool<CoverageSpan>.Shared.Return(_arena);
        }

        if (_scratch.Length > 0)
        {
            ArrayPool<CoverageSpan>.Shared.Return(_scratch);
        }

        _arena = [];
        _scratch = [];
    }

    private static long Bytes(int spans) => ((long)spans * SpanBytes) + EntryOverhead;

    /// <summary>Makes room for <paramref name="length"/> more runs at the arena's end: compacts the live runs, growing the arena if they need it.</summary>
    private void Reserve(int length)
    {
        int needed = _liveSpans + length;
        int size = Math.Max(_arena.Length, InitialArena);
        while (size < needed)
        {
            size *= 2;
        }

        CoverageSpan[] next = ArrayPool<CoverageSpan>.Shared.Rent(size);
        int end = 0;
        for (int slot = _head; slot >= 0; slot = _entries[slot].Next)
        {
            ref Entry entry = ref _entries[slot];
            _arena.AsSpan(entry.Offset, entry.Length).CopyTo(next.AsSpan(end));
            entry.Offset = end;
            end += entry.Length;
        }

        if (_arena.Length > 0)
        {
            ArrayPool<CoverageSpan>.Shared.Return(_arena);
        }

        _arena = next;
        _arenaEnd = end;
    }

    private void Evict(int slot)
    {
        Unlink(slot);
        ref Entry entry = ref _entries[slot];
        _index!.Remove(entry.Key);
        _bytes -= Bytes(entry.Length);
        _liveSpans -= entry.Length;
        entry = new Entry { Next = _freeEntry };
        _freeEntry = slot;
    }

    private void MoveToFront(int slot)
    {
        if (slot == _head)
        {
            return;
        }

        Unlink(slot);
        ref Entry entry = ref _entries[slot];
        entry.Previous = -1;
        entry.Next = _head;
        _entries[_head].Previous = slot;
        _head = slot;
        if (_tail < 0)
        {
            _tail = slot;
        }
    }

    private void Unlink(int slot)
    {
        ref Entry entry = ref _entries[slot];
        if (entry.Previous >= 0)
        {
            _entries[entry.Previous].Next = entry.Next;
        }
        else
        {
            _head = entry.Next;
        }

        if (entry.Next >= 0)
        {
            _entries[entry.Next].Previous = entry.Previous;
        }
        else
        {
            _tail = entry.Previous;
        }
    }

    private struct Entry
    {
        public GlyphKey Key;
        public int Left;
        public int Top;
        public int Width;
        public int Height;
        public int Offset;
        public int Length;
        public int Previous;
        public int Next;
    }
}

/// <summary>A glyph's mask: where it sits relative to the glyph origin's pixel, its size, and where its runs are.</summary>
internal readonly record struct GlyphMask(int Left, int Top, int Width, int Height, int Offset, int Length);
