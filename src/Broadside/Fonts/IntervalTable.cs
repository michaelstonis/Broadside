namespace Broadside.Fonts;

/// <summary>
/// Sorted, disjoint intervals of 32-bit keys, each with a value: the lookup structure of CMap mappings (code to CID) and of
/// CIDFont metrics (CID to width). Immutable once built and shared across threads; a lookup is a binary search and allocates
/// nothing.
/// </summary>
/// <remarks>
/// A range is never expanded key by key: <c>&lt;0000&gt; &lt;FFFF&gt; 0</c> and a <c>W</c> entry <c>0 2147483647 500</c> are one
/// interval each. With <see cref="Increment"/> the value of a key is the interval's value plus the key's offset from the interval's
/// start (a <c>cidrange</c>); without, it is the interval's value (a <c>notdefrange</c>, a width).
/// </remarks>
internal sealed class IntervalTable
{
    private readonly uint[] _low;
    private readonly uint[] _high;
    private readonly int[] _value;

    private IntervalTable(uint[] low, uint[] high, int[] value, bool increment)
    {
        _low = low;
        _high = high;
        _value = value;
        Increment = increment;
    }

    /// <summary>Gets a table without intervals.</summary>
    public static IntervalTable Empty { get; } = new([], [], [], increment: false);

    /// <summary>Gets a value indicating whether a key's value grows with its offset in the interval.</summary>
    public bool Increment { get; }

    /// <summary>Gets the number of intervals.</summary>
    public int Count => _low.Length;

    /// <summary>Looks up the value of a key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value; 0 when not found.</param>
    /// <returns>Whether an interval holds the key.</returns>
    public bool TryFind(uint key, out int value)
    {
        int index = _low.AsSpan().BinarySearch(key);
        if (index < 0)
        {
            index = ~index - 1;
        }

        if (index >= 0 && key <= _high[index])
        {
            long found = Increment ? _value[index] + (long)(key - _low[index]) : _value[index];
            value = found > int.MaxValue ? int.MaxValue : (int)found;
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Builds a table from entries in declaration order.</summary>
    /// <param name="entries">The entries; each covers <c>Low</c> to <c>High</c> inclusive.</param>
    /// <param name="increment">Whether a key's value grows with its offset in its interval.</param>
    /// <param name="laterWins">
    /// Whether, where entries overlap, the later one is used (CMap mappings, Adobe TN 5014 §5.2) rather than the first one (CIDFont
    /// widths, ISO 32000-2 §9.7.4.3).
    /// </param>
    /// <returns>The table.</returns>
    public static IntervalTable Build(List<Interval> entries, bool increment, bool laterWins)
    {
        if (entries.Count == 0)
        {
            return increment ? new IntervalTable([], [], [], increment: true) : Empty;
        }

        List<Interval> pieces = IsAscendingAndDisjoint(entries) ? entries : Resolve(entries, increment, laterWins);
        var low = new List<uint>(pieces.Count);
        var high = new List<uint>(pieces.Count);
        var value = new List<int>(pieces.Count);
        foreach (Interval piece in pieces)
        {
            int last = low.Count - 1;
            bool adjacent = last >= 0 && high[last] != uint.MaxValue && piece.Low == high[last] + 1;
            long expected = increment && last >= 0 ? value[last] + (long)(high[last] - low[last]) + 1 : last >= 0 ? value[last] : 0;
            if (adjacent && piece.Value == expected)
            {
                high[last] = piece.High;
                continue;
            }

            low.Add(piece.Low);
            high.Add(piece.High);
            value.Add(piece.Value);
        }

        return new IntervalTable([.. low], [.. high], [.. value], increment);
    }

    private static bool IsAscendingAndDisjoint(List<Interval> entries)
    {
        for (int index = 1; index < entries.Count; index++)
        {
            if (entries[index].Low <= entries[index - 1].High)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Splits overlapping entries into disjoint pieces, each taken from the winning entry: a sweep over the entries' boundaries with
    /// the covering entries in a priority queue by declaration order. O(n log n) whatever the ranges' sizes.
    /// </summary>
    private static List<Interval> Resolve(List<Interval> entries, bool increment, bool laterWins)
    {
        var order = new int[entries.Count];
        for (int index = 0; index < order.Length; index++)
        {
            order[index] = index;
        }

        Array.Sort(order, (a, b) => entries[a].Low.CompareTo(entries[b].Low));
        var boundaries = new List<ulong>(entries.Count * 2);
        foreach (Interval entry in entries)
        {
            boundaries.Add(entry.Low);
            boundaries.Add((ulong)entry.High + 1);
        }

        boundaries.Sort();
        var covering = new PriorityQueue<int, int>();
        var pieces = new List<Interval>();
        int next = 0;
        for (int index = 0; index < boundaries.Count - 1; index++)
        {
            ulong start = boundaries[index];
            ulong end = boundaries[index + 1];
            if (start == end)
            {
                continue;
            }

            while (next < order.Length && entries[order[next]].Low <= start)
            {
                int entry = order[next++];
                covering.Enqueue(entry, laterWins ? -entry : entry);
            }

            while (covering.TryPeek(out int top, out _) && entries[top].High < start)
            {
                covering.Dequeue();
            }

            if (covering.TryPeek(out int winner, out _))
            {
                Interval entry = entries[winner];
                long offset = increment ? entry.Value + (long)(start - entry.Low) : entry.Value;
                int value = offset > int.MaxValue ? int.MaxValue : (int)offset;
                pieces.Add(new Interval((uint)start, (uint)(end - 1), value));
            }
        }

        return pieces;
    }

    /// <summary>One entry: keys <see cref="Low"/> to <see cref="High"/> inclusive with <see cref="Value"/> at <see cref="Low"/>.</summary>
    internal readonly record struct Interval(uint Low, uint High, int Value);
}
