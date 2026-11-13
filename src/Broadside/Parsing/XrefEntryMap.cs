using System.Diagnostics.CodeAnalysis;

namespace Broadside.Parsing;

/// <summary>
/// The entries of a cross-reference section, or of the merged lookup, by object number: a compact, read-only map whose entries are
/// kept in ascending object number, one array slot of 16 bytes each, with one run record per stretch of consecutive numbers.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4: a table is "one or more cross-reference subsections" of consecutive object numbers, and §7.5.8.2's
/// <c>Index</c> array names the same runs for a cross-reference stream, so a file of a million objects is usually one run. A lookup
/// within a run is an array index, and between runs a binary search over the runs, never a hash. A hash map of the same entries
/// spends 40 or more bytes on each and about twice that while it grows (issue #237, spec story 7: a five-million-object file).
/// </para>
/// <para>
/// Entries are added through a <see cref="Builder"/>, first write wins, in any order; numbers added in ascending order (what a
/// well-formed file holds) are stored as they come, and anything else is sorted once when the map is built. Enumeration is in
/// ascending object number.
/// </para>
/// </remarks>
internal sealed class XrefEntryMap : IReadOnlyDictionary<int, XrefEntry>
{
    private readonly XrefEntry[] _entries;
    private readonly int[] _runNumbers;
    private readonly int[] _runStarts;
    private readonly int _runCount;
    private readonly int _count;

    private XrefEntryMap(XrefEntry[] entries, int count, int[] runNumbers, int[] runStarts, int runCount)
    {
        _entries = entries;
        _count = count;
        _runNumbers = runNumbers;
        _runStarts = runStarts;
        _runCount = runCount;
    }

    /// <summary>Gets a map with no entries.</summary>
    public static XrefEntryMap Empty { get; } = new([], 0, [], [], 0);

    /// <inheritdoc/>
    public int Count => _count;

    /// <summary>Gets the object numbers, ascending.</summary>
    public IEnumerable<int> Keys
    {
        get
        {
            foreach (KeyValuePair<int, XrefEntry> pair in this)
            {
                yield return pair.Key;
            }
        }
    }

    /// <summary>Gets the entries, in ascending object number.</summary>
    public IEnumerable<XrefEntry> Values
    {
        get
        {
            for (int index = 0; index < _count; index++)
            {
                yield return _entries[index];
            }
        }
    }

    /// <summary>Gets the largest object number the map lists, or -1 when it is empty.</summary>
    public int MaxNumber => _runCount == 0 ? -1 : _runNumbers[_runCount - 1] + (_count - 1 - _runStarts[_runCount - 1]);

    /// <inheritdoc/>
    public XrefEntry this[int key] => TryGetValue(key, out XrefEntry entry) ? entry : throw new KeyNotFoundException($"Object {key} is not listed.");

    /// <summary>Builds a map from entries in any order; for a number given twice, the first wins.</summary>
    /// <param name="entries">The entries.</param>
    /// <returns>The map.</returns>
    public static XrefEntryMap From(IEnumerable<KeyValuePair<int, XrefEntry>> entries)
    {
        var builder = new Builder(entries.TryGetNonEnumeratedCount(out int count) ? count : 0);
        foreach ((int number, XrefEntry entry) in entries)
        {
            builder.Add(number, entry);
        }

        return builder.Build();
    }

    /// <summary>
    /// Merges sections, highest priority first: an object number takes its entry from the first section that lists it, except that a
    /// free entry in a table whose <c>XRefStm</c> stream is the next section gives way to that stream's entry (§7.5.8.4: "A PDF
    /// reader shall look in the cross-reference stream first ... and shall ignore the free entry").
    /// </summary>
    /// <param name="sections">The sections, newest first.</param>
    /// <returns>The merged map; a single section's own map.</returns>
    public static XrefEntryMap Merge(IReadOnlyList<XrefSection> sections)
    {
        if (sections.Count == 1)
        {
            return sections[0].Entries;
        }

        // A k-way merge of the ascending sections: the queue orders cursors by (number, section), so the first cursor to reach a
        // number is the newest section's. A cursor keeps going without the queue while it stays below the queue's head.
        int largest = 0;
        var queue = new PriorityQueue<int, long>(sections.Count);
        var cursors = new Enumerator[sections.Count];
        for (int index = 0; index < sections.Count; index++)
        {
            largest = Math.Max(largest, sections[index].Entries.Count);
            cursors[index] = sections[index].Entries.GetEnumerator();
            if (cursors[index].MoveNext())
            {
                queue.Enqueue(index, Priority(cursors[index].Current.Key, index));
            }
        }

        var builder = new Builder(largest);
        while (queue.TryDequeue(out int section, out _))
        {
            ref Enumerator cursor = ref cursors[section];
            while (true)
            {
                (int number, XrefEntry entry) = cursor.Current;
                if (number > builder.Last)
                {
                    builder.Add(number, Effective(sections, section, number, entry));
                }

                if (!cursor.MoveNext())
                {
                    break;
                }

                long next = Priority(cursor.Current.Key, section);
                if (queue.TryPeek(out _, out long head) && head < next)
                {
                    queue.Enqueue(section, next);
                    break;
                }
            }
        }

        return builder.Build();

        static long Priority(int number, int section) => ((long)number << 32) | (uint)section;
    }

    /// <inheritdoc/>
    public bool ContainsKey(int key) => TryGetValue(key, out _);

    /// <inheritdoc/>
    public bool TryGetValue(int key, [MaybeNullWhen(false)] out XrefEntry value)
    {
        int run = FindRun(key);
        if (run >= 0)
        {
            int index = _runStarts[run] + (key - _runNumbers[run]);
            int end = run + 1 < _runCount ? _runStarts[run + 1] : _count;
            if (index < end)
            {
                value = _entries[index];
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>Returns an enumerator over the entries, in ascending object number, that allocates nothing.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <inheritdoc/>
    IEnumerator<KeyValuePair<int, XrefEntry>> IEnumerable<KeyValuePair<int, XrefEntry>>.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The entry a merge takes for <paramref name="number"/> from <paramref name="section"/> (§7.5.8.4 for a hybrid table).</summary>
    private static XrefEntry Effective(IReadOnlyList<XrefSection> sections, int section, int number, XrefEntry entry) =>
        entry.Kind == XrefEntryKind.Free
        && sections[section].XRefStreamOffset is { } streamOffset
        && section + 1 < sections.Count
        && sections[section + 1].Offset == streamOffset
        && sections[section + 1].Entries.TryGetValue(number, out XrefEntry fromStream)
            ? fromStream
            : entry;

    /// <summary>The run whose first number is the largest at or below <paramref name="number"/>, or -1.</summary>
    private int FindRun(int number)
    {
        if (_runCount == 1)
        {
            return number >= _runNumbers[0] ? 0 : -1;
        }

        int found = _runNumbers.AsSpan(0, _runCount).BinarySearch(number);
        return found >= 0 ? found : ~found - 1;
    }

    /// <summary>Enumerates a map's entries in ascending object number.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<int, XrefEntry>>
    {
        private readonly XrefEntryMap _map;
        private int _index;
        private int _run;

        internal Enumerator(XrefEntryMap map)
        {
            _map = map;
            _index = -1;
            _run = 0;
        }

        /// <inheritdoc/>
        public readonly KeyValuePair<int, XrefEntry> Current =>
            new(_map._runNumbers[_run] + (_index - _map._runStarts[_run]), _map._entries[_index]);

        /// <inheritdoc/>
        readonly object System.Collections.IEnumerator.Current => Current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_index + 1 >= _map._count)
            {
                _index = _map._count;
                return false;
            }

            _index++;
            while (_run + 1 < _map._runCount && _map._runStarts[_run + 1] <= _index)
            {
                _run++;
            }

            return true;
        }

        /// <inheritdoc/>
        public void Reset()
        {
            _index = -1;
            _run = 0;
        }

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }

    /// <summary>Collects entries for a map, first write wins.</summary>
    internal sealed class Builder
    {
        private const int MinimumCapacity = 16;

        private XrefEntry[] _entries;
        private int _count;
        private int[] _runNumbers = new int[4];
        private int[] _runStarts = new int[4];
        private int _runCount;

        /// <summary>The number of each entry, kept only once an entry came out of ascending order.</summary>
        private int[]? _numbers;

        /// <summary>Initializes a new instance of the <see cref="Builder"/> class.</summary>
        /// <param name="capacity">How many entries to make room for at once; more grow the storage.</param>
        public Builder(int capacity = 0) => _entries = capacity > 0 ? new XrefEntry[capacity] : [];

        /// <summary>Gets the largest number added while the numbers came in ascending order; -1 before the first.</summary>
        public int Last { get; private set; } = -1;

        /// <summary>Makes room for <paramref name="additional"/> more entries at once, so that a known count is stored without growing.</summary>
        /// <param name="additional">The entries about to be added; the caller bounds it by what the file can hold.</param>
        public void EnsureCapacity(long additional)
        {
            long needed = _count + Math.Max(0, additional);
            if (needed > _entries.Length)
            {
                Array.Resize(ref _entries, (int)Math.Min(needed, Array.MaxLength));
                if (_numbers is not null)
                {
                    Array.Resize(ref _numbers, _entries.Length);
                }
            }
        }

        /// <summary>Adds an entry, unless one for the same number was added before.</summary>
        /// <param name="number">The object number, not negative.</param>
        /// <param name="entry">The entry.</param>
        public void Add(int number, XrefEntry entry)
        {
            if (_numbers is null)
            {
                if (number == Last)
                {
                    return;
                }

                if (number > Last)
                {
                    Grow();
                    if (_count == 0 || number != Last + 1)
                    {
                        AddRun(number, _count);
                    }

                    _entries[_count++] = entry;
                    Last = number;
                    return;
                }

                _numbers = new int[_entries.Length];
                Numbers(_numbers);
            }

            Grow();
            _numbers[_count] = number;
            _entries[_count++] = entry;
        }

        /// <summary>Builds the map; the builder is not used afterwards.</summary>
        /// <returns>The map.</returns>
        public XrefEntryMap Build()
        {
            if (_numbers is null)
            {
                return _count == 0 ? Empty : new XrefEntryMap(_entries, _count, _runNumbers, _runStarts, _runCount);
            }

            // Sort by (number, order added): the first entry added for each number comes first, and wins.
            long[] order = new long[_count];
            for (int index = 0; index < _count; index++)
            {
                order[index] = ((long)_numbers[index] << 32) | (uint)index;
            }

            Array.Sort(order);
            var sorted = new Builder(_count);
            foreach (long key in order)
            {
                sorted.Add((int)(key >> 32), _entries[(int)key]);
            }

            return sorted.Build();
        }

        private void Grow()
        {
            if (_count < _entries.Length)
            {
                return;
            }

            int capacity = (int)Math.Min(Array.MaxLength, Math.Max(MinimumCapacity, 2L * _entries.Length));
            if (capacity <= _count)
            {
                throw new InvalidOperationException("A cross-reference section cannot hold more entries.");
            }

            Array.Resize(ref _entries, capacity);
            if (_numbers is not null)
            {
                Array.Resize(ref _numbers, capacity);
            }
        }

        private void AddRun(int number, int start)
        {
            if (_runCount == _runNumbers.Length)
            {
                Array.Resize(ref _runNumbers, _runCount * 2);
                Array.Resize(ref _runStarts, _runCount * 2);
            }

            _runNumbers[_runCount] = number;
            _runStarts[_runCount++] = start;
        }

        /// <summary>Writes the number of every entry added so far, from the runs, into <paramref name="numbers"/>.</summary>
        private void Numbers(int[] numbers)
        {
            for (int run = 0; run < _runCount; run++)
            {
                int end = run + 1 < _runCount ? _runStarts[run + 1] : _count;
                for (int index = _runStarts[run]; index < end; index++)
                {
                    numbers[index] = _runNumbers[run] + (index - _runStarts[run]);
                }
            }
        }
    }
}
