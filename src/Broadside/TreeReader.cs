using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// The walker shared by name trees and number trees: Limits-guided lookup, an in-order walk that checks the tree as it goes, and
/// a flattened index for trees whose Limits cannot be trusted.
/// </summary>
/// <typeparam name="TKey">The key type: <see cref="CosString"/> for name trees, <see cref="int"/> for number trees.</typeparam>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.9.6, Table 36, and §7.9.7, Table 37. A lookup descends from the root, at each level binary-searching the kids
/// by their <c>Limits</c> for the first one whose greatest key is not less than the key, then binary-searching the leaf. It loads
/// one node per level and reports nothing. When anything on that path is not as the clause requires (a node without usable
/// <c>Limits</c>, a malformed key, a level deeper than the cap) or the key is not found, the lookup falls back to the flattened
/// index: every key in the tree, de-duplicated, sorted. The index is built once by the checking walk and kept until a node it was
/// built from changes (each container's internal version), so stale <c>Limits</c> cost one full walk, not one per lookup.
/// </para>
/// <para>
/// The walk visits nodes in tree order (a root's own pairs, then its kids, depth first), with one visited set for the whole walk
/// and a depth cap, iteratively, so a cycle or a very deep chain ends. It reports each deviation once per node. A key seen
/// earlier in the walk is a duplicate and is skipped, so the first occurrence in tree order is the one every API returns; the first
/// deviation a walk meets switches every later lookup to the index, so lookups agree with enumeration from then on.
/// </para>
/// <para>
/// Values are returned resolved one level: a value that is an indirect reference comes back as the object it refers to.
/// Thread-safe for concurrent reads (the index is built under a lock and published whole).
/// </para>
/// </remarks>
internal abstract class TreeReader<TKey>
    where TKey : notnull
{
    /// <summary>The deepest tree the reader descends into: deeper nodes are skipped with a diagnostic.</summary>
    public const int DefaultMaxDepth = 64;

    private readonly Lock _gate = new();
    private volatile TreeIndex? _index;
    private volatile bool _limitsUntrusted;

    /// <summary>Initializes a new instance of the <see cref="TreeReader{TKey}"/> class.</summary>
    /// <param name="root">The tree's root node dictionary.</param>
    /// <param name="rootReference">The root's reference, for diagnostics, or <see langword="null"/> when it is direct.</param>
    /// <param name="resolve">Resolves references through the document.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="maxDepth">The deepest level the walk descends to; the root is level 0.</param>
    private protected TreeReader(CosDictionary root, CosReference? rootReference, Func<CosObject?, CosObject> resolve, DiagnosticSink diagnostics, int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        Root = root;
        RootReference = rootReference;
        Resolve = resolve;
        Diagnostics = diagnostics;
        MaxDepth = maxDepth;
    }

    /// <summary>Gets the root node.</summary>
    public CosDictionary Root { get; }

    /// <summary>Gets the root's reference, or <see langword="null"/> when the root is a direct dictionary.</summary>
    public CosReference? RootReference { get; }

    /// <summary>Gets the deepest level the walk descends to.</summary>
    public int MaxDepth { get; }

    private protected Func<CosObject?, CosObject> Resolve { get; }

    private protected DiagnosticSink Diagnostics { get; }

    /// <summary>Gets the key of the array holding the pairs: <c>Names</c> or <c>Nums</c>.</summary>
    private protected abstract CosName EntriesKey { get; }

    /// <summary>Gets the diagnostic codes for this kind of tree.</summary>
    private protected abstract TreeCodes Codes { get; }

    /// <summary>Gets the name of the tree kind, for messages: "name tree" or "number tree".</summary>
    private protected abstract string Kind { get; }

    /// <summary>Gets the comparer that orders keys as the clause sorts them.</summary>
    private protected abstract IComparer<TKey> KeyComparer { get; }

    /// <summary>Gets the comparer that decides key equality.</summary>
    private protected abstract IEqualityComparer<TKey> KeyEquality { get; }

    /// <summary>Looks <paramref name="key"/> up.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, resolved one level, when found.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out CosObject value)
    {
        if (TryGetRawValue(key, out CosObject? raw))
        {
            value = Resolve(raw);
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Looks <paramref name="key"/> up and returns its value as stored: an indirect reference stays a reference.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value as stored in the <c>Names</c> or <c>Nums</c> array, when found.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    public bool TryGetRawValue(TKey key, [MaybeNullWhen(false)] out CosObject value)
    {
        if (!_limitsUntrusted && _index is null && TryDescend(key, out value))
        {
            return true;
        }

        TreeIndex index = GetIndex();
        int position = index.Find(key, KeyComparer);
        if (position < 0)
        {
            value = null;
            return false;
        }

        value = index.Values[position];
        return true;
    }

    /// <summary>Finds the greatest key not greater than <paramref name="key"/>, through the flattened index.</summary>
    /// <param name="key">The key.</param>
    /// <param name="floorKey">The greatest key in the tree that is not greater than <paramref name="key"/>.</param>
    /// <param name="value">Its value, resolved one level.</param>
    /// <returns><see langword="true"/> when the tree holds a key not greater than <paramref name="key"/>.</returns>
    public bool TryGetFloor(TKey key, [MaybeNullWhen(false)] out TKey floorKey, [MaybeNullWhen(false)] out CosObject value)
    {
        TreeIndex index = GetIndex();
        int position = index.Find(key, KeyComparer);
        if (position < 0)
        {
            position = ~position - 1;
        }

        if (position < 0)
        {
            floorKey = default;
            value = null;
            return false;
        }

        floorKey = index.Keys[position];
        value = Resolve(index.Values[position]);
        return true;
    }

    /// <summary>Walks the tree in tree order, checking it, and yields each key once with its value resolved one level.</summary>
    /// <returns>The pairs. Each enumeration walks the tree again; it is a live view.</returns>
    public IEnumerable<(TKey Key, CosObject Value)> Enumerate()
    {
        foreach ((TKey key, CosObject value) in Walk(stamps: null))
        {
            yield return (key, Resolve(value));
        }
    }

    /// <summary>As <see cref="Enumerate"/>, with each value as stored: an indirect reference stays a reference.</summary>
    /// <returns>The pairs, in tree order.</returns>
    public IEnumerable<(TKey Key, CosObject Value)> EnumerateRaw() => Walk(stamps: null);

    /// <summary>Reads a key from the <c>Names</c> or <c>Nums</c> array.</summary>
    /// <param name="key">The array element, resolved.</param>
    /// <param name="value">The key.</param>
    /// <param name="message">When the key is repaired or rejected, why.</param>
    /// <returns>
    /// <see cref="KeyState.Valid"/>, <see cref="KeyState.Repaired"/> (usable, with a diagnostic) or <see cref="KeyState.Invalid"/>
    /// (the pair is skipped with a diagnostic).
    /// </returns>
    private protected abstract KeyState ReadKey(CosObject key, [MaybeNullWhen(false)] out TKey value, out string? message);

    /// <summary>Gets the index, building it when there is none or a node it was built from has changed.</summary>
    private TreeIndex GetIndex()
    {
        TreeIndex? index = _index;
        if (index is not null && index.IsCurrent())
        {
            return index;
        }

        lock (_gate)
        {
            index = _index;
            if (index is not null && index.IsCurrent())
            {
                return index;
            }

            var stamps = new List<(CosObject Container, int Version)>();
            var keys = new List<TKey>();
            var values = new List<CosObject>();
            foreach ((TKey key, CosObject value) in Walk(stamps))
            {
                keys.Add(key);
                values.Add(value);
            }

            TKey[] keyArray = [.. keys];
            CosObject[] valueArray = [.. values];

            // Keys are unique after the walk, so the sort order is total.
            Array.Sort(keyArray, valueArray, KeyComparer);
            index = new TreeIndex(keyArray, valueArray, [.. stamps]);
            _index = index;
            return index;
        }
    }

    /// <summary>The Limits-guided descent. Fails, reporting nothing, on anything the clause does not allow.</summary>
    private bool TryDescend(TKey key, [MaybeNullWhen(false)] out CosObject value)
    {
        value = null;
        CosDictionary node = Root;
        for (int depth = 0; depth <= MaxDepth; depth++)
        {
            if (node.TryGetValue(EntriesKey, out CosObject? entriesEntry))
            {
                return Resolve(entriesEntry) is CosArray entries && TryFindInLeaf(entries, key, out value);
            }

            if (!node.TryGetValue(KnownNames.Kids, out CosObject? kidsEntry) || Resolve(kidsEntry) is not CosArray kids || kids.Count == 0)
            {
                return false;
            }

            // The first kid whose greatest key is not less than the key: with duplicates on a shared boundary that is the
            // earliest kid in tree order.
            int low = 0;
            int high = kids.Count - 1;
            CosDictionary? candidate = null;
            TKey? candidateLeast = default;
            while (low <= high)
            {
                int middle = low + ((high - low) / 2);
                if (Resolve(kids[middle]) is not CosDictionary kid || !TryReadLimits(kid, out TKey? least, out TKey? greatest))
                {
                    return false;
                }

                if (KeyComparer.Compare(greatest, key) >= 0)
                {
                    candidate = kid;
                    candidateLeast = least;
                    high = middle - 1;
                }
                else
                {
                    low = middle + 1;
                }
            }

            if (candidate is null || KeyComparer.Compare(candidateLeast!, key) > 0)
            {
                return false;
            }

            node = candidate;
        }

        return false;
    }

    /// <summary>Binary-searches a leaf's pairs for the first occurrence of <paramref name="key"/>.</summary>
    private bool TryFindInLeaf(CosArray entries, TKey key, [MaybeNullWhen(false)] out CosObject value)
    {
        value = null;
        int low = 0;
        int high = (entries.Count / 2) - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (ReadKey(Resolve(entries[2 * middle]), out TKey? probe, out _) != KeyState.Valid)
            {
                return false;
            }

            int comparison = KeyComparer.Compare(probe, key);
            if (comparison >= 0)
            {
                found = comparison == 0 ? middle : found;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        if (found < 0)
        {
            return false;
        }

        value = entries[(2 * found) + 1];
        return true;
    }

    /// <summary>Reads a node's <c>Limits</c>: an array of two valid keys, least first.</summary>
    private bool TryReadLimits(CosDictionary node, [MaybeNullWhen(false)] out TKey least, [MaybeNullWhen(false)] out TKey greatest)
    {
        least = default;
        greatest = default;
        return node.TryGetValue(NavigationNames.Limits, out CosObject? entry)
            && Resolve(entry) is CosArray { Count: 2 } limits
            && ReadKey(Resolve(limits[0]), out least, out _) == KeyState.Valid
            && ReadKey(Resolve(limits[1]), out greatest, out _) == KeyState.Valid
            && KeyComparer.Compare(least, greatest) <= 0;
    }

    /// <summary>
    /// The checking walk, in tree order. Yields each key once (the first occurrence) with its raw value. Records the version of every
    /// container it reads the structure from when <paramref name="stamps"/> is given.
    /// </summary>
    private IEnumerable<(TKey Key, CosObject Value)> Walk(List<(CosObject Container, int Version)>? stamps)
    {
        var visitedReferences = new HashSet<CosReference>();
        var visitedNodes = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var seen = new HashSet<TKey>(KeyEquality);
        bool hasPrevious = false;
        TKey previous = default!;

        if (RootReference is not null)
        {
            visitedReferences.Add(RootReference);
        }

        visitedNodes.Add(Root);
        var stack = new Stack<Frame>();
        stack.Push(new Frame(Root, RootReference, null, 0, IsRoot: true));
        while (stack.TryPop(out Frame frame))
        {
            CosDictionary node = frame.Node;
            stamps?.Add((node, node.Version));
            Bounds? bounds = CheckLimits(frame);
            bool hasEntries = node.TryGetValue(EntriesKey, out CosObject? entriesEntry);
            bool hasKids = node.TryGetValue(KnownNames.Kids, out CosObject? kidsEntry);
            if (hasEntries == hasKids)
            {
                _limitsUntrusted = true;
                Report(
                    Codes.NodeInvalid,
                    DiagnosticSeverity.Warning,
                    hasEntries
                        ? $"A {Kind} node has both Kids and {EntriesKey.Value}; its own pairs are read first, then its kids."
                        : $"A {Kind} node has neither Kids nor {EntriesKey.Value}; it holds no keys.",
                    frame.Reference);
            }

            if (hasEntries)
            {
                if (Resolve(entriesEntry) is not CosArray entries)
                {
                    _limitsUntrusted = true;
                    Report(Codes.NodeInvalid, DiagnosticSeverity.Warning, $"A {Kind} node's {EntriesKey.Value} is not an array; it holds no keys.", frame.Reference);
                }
                else
                {
                    stamps?.Add((entries, entries.Version));
                    if (entries.Count % 2 != 0)
                    {
                        _limitsUntrusted = true;
                        Report(
                            Codes.NodeInvalid,
                            DiagnosticSeverity.Warning,
                            $"A {Kind} node's {EntriesKey.Value} array has an odd number of elements; the last key has no value and is ignored.",
                            frame.Reference);
                    }

                    for (int index = 0; index + 1 < entries.Count; index += 2)
                    {
                        KeyState state = ReadKey(Resolve(entries[index]), out TKey? key, out string? message);
                        if (state != KeyState.Valid)
                        {
                            _limitsUntrusted = true;
                            Report(Codes.KeyInvalid, DiagnosticSeverity.Warning, message!, frame.Reference);
                            if (state == KeyState.Invalid)
                            {
                                continue;
                            }
                        }

                        if (!IsWithin(key!, frame.Ancestors, bounds, frame.Reference))
                        {
                            _limitsUntrusted = true;
                        }

                        if (!seen.Add(key!))
                        {
                            _limitsUntrusted = true;
                            Report(
                                Codes.DuplicateKey,
                                DiagnosticSeverity.Warning,
                                $"A {Kind} key appears more than once; the first occurrence in tree order is used.",
                                frame.Reference);
                            continue;
                        }

                        if (hasPrevious && KeyComparer.Compare(key!, previous) < 0)
                        {
                            _limitsUntrusted = true;
                            Report(
                                Codes.KeysUnsorted,
                                DiagnosticSeverity.Warning,
                                $"The keys of a {Kind} are not in ascending order; lookups fall back to reading the whole tree.",
                                frame.Reference);
                        }

                        hasPrevious = true;
                        previous = key!;
                        yield return (key!, entries[index + 1]);
                    }
                }
            }

            if (hasKids)
            {
                foreach (Frame kid in ReadKids(frame, kidsEntry, bounds, visitedReferences, visitedNodes, stamps))
                {
                    stack.Push(kid);
                }
            }
        }
    }

    /// <summary>Reads a node's kids for the walk; returns them in reverse order, ready to push.</summary>
    private List<Frame> ReadKids(
        Frame frame,
        CosObject? kidsEntry,
        Bounds? bounds,
        HashSet<CosReference> visitedReferences,
        HashSet<CosDictionary> visitedNodes,
        List<(CosObject Container, int Version)>? stamps)
    {
        var kids = new List<Frame>();
        if (Resolve(kidsEntry) is not CosArray array)
        {
            _limitsUntrusted = true;
            Report(Codes.NodeInvalid, DiagnosticSeverity.Warning, $"A {Kind} node's Kids is not an array; it has no kids.", frame.Reference);
            return kids;
        }

        stamps?.Add((array, array.Version));
        if (frame.Depth >= MaxDepth)
        {
            _limitsUntrusted = true;
            Report(
                Codes.TooDeep,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The {Kind} is deeper than {MaxDepth} levels; the deeper nodes are skipped."),
                frame.Reference);
            return kids;
        }

        var ancestors = bounds is null ? frame.Ancestors : new BoundsChain(bounds.Value, frame.Reference, frame.Ancestors);
        foreach (CosObject kid in array)
        {
            var reference = kid as CosReference;
            if (Resolve(kid) is not CosDictionary child)
            {
                _limitsUntrusted = true;
                Report(Codes.NodeInvalid, DiagnosticSeverity.Warning, $"A {Kind} Kids entry is not a node dictionary; it is skipped.", reference ?? frame.Reference);
                continue;
            }

            if (reference is null)
            {
                _limitsUntrusted = true;
                Report(Codes.NodeInvalid, DiagnosticSeverity.Warning, $"A {Kind} Kids entry shall be an indirect reference; it is a direct dictionary, used as is.", frame.Reference);
            }

            if ((reference is not null && !visitedReferences.Add(reference)) || !visitedNodes.Add(child))
            {
                _limitsUntrusted = true;
                Report(
                    Codes.Cycle,
                    DiagnosticSeverity.Error,
                    $"A {Kind} node is reached a second time (a cycle or a shared node); the repeat is skipped.",
                    reference ?? frame.Reference);
                continue;
            }

            kids.Add(new Frame(child, reference ?? frame.Reference, ancestors, frame.Depth + 1, IsRoot: false));
        }

        kids.Reverse();
        return kids;
    }

    /// <summary>Checks a node's <c>Limits</c> entry; returns the bounds when they are usable.</summary>
    private Bounds? CheckLimits(Frame frame)
    {
        bool present = frame.Node.TryGetValue(NavigationNames.Limits, out CosObject? entry);
        if (frame.IsRoot)
        {
            if (present)
            {
                _limitsUntrusted = true;
                Report(Codes.LimitsInvalid, DiagnosticSeverity.Warning, $"The root of a {Kind} shall not have Limits; they are ignored.", frame.Reference);
            }

            return null;
        }

        if (present
            && Resolve(entry) is CosArray { Count: 2 } limits
            && ReadKey(Resolve(limits[0]), out TKey? least, out _) == KeyState.Valid
            && ReadKey(Resolve(limits[1]), out TKey? greatest, out _) == KeyState.Valid
            && KeyComparer.Compare(least, greatest) <= 0)
        {
            return new Bounds(least, greatest);
        }

        _limitsUntrusted = true;
        Report(
            Codes.LimitsInvalid,
            DiagnosticSeverity.Warning,
            present
                ? $"A {Kind} node's Limits is not an array of its least and greatest keys; lookups fall back to reading the whole tree."
                : $"A {Kind} node below the root has no Limits; lookups fall back to reading the whole tree.",
            frame.Reference);
        return null;
    }

    /// <summary>Checks a key against the Limits of its leaf and of every ancestor that has usable ones; reports each node that misses it.</summary>
    private bool IsWithin(TKey key, BoundsChain? ancestors, Bounds? own, CosReference? ownReference)
    {
        bool within = true;
        if (own is { } bounds && !bounds.Contains(key, KeyComparer))
        {
            within = false;
            ReportStale(ownReference);
        }

        for (BoundsChain? chain = ancestors; chain is not null; chain = chain.Next)
        {
            if (!chain.Bounds.Contains(key, KeyComparer))
            {
                within = false;
                ReportStale(chain.Reference);
            }
        }

        return within;
    }

    private void ReportStale(CosReference? reference) =>
        Report(
            Codes.LimitsInvalid,
            DiagnosticSeverity.Warning,
            $"A {Kind} key lies outside the Limits of a node that holds it; lookups fall back to reading the whole tree.",
            reference);

    private void Report(string code, DiagnosticSeverity severity, string message, CosReference? reference) =>
        Diagnostics.Report(code, severity, message, objectReference: reference);

    /// <summary>How usable a key read from a tree is.</summary>
    private protected enum KeyState
    {
        /// <summary>The key is of the type the clause requires.</summary>
        Valid,

        /// <summary>The key is not of the required type but is used, with a diagnostic.</summary>
        Repaired,

        /// <summary>The key cannot be used; the pair is skipped with a diagnostic.</summary>
        Invalid,
    }

    /// <summary>The diagnostic codes of one kind of tree.</summary>
    private protected sealed record TreeCodes(string NodeInvalid, string LimitsInvalid, string KeysUnsorted, string DuplicateKey, string KeyInvalid, string Cycle, string TooDeep);

    /// <summary>A node waiting to be visited.</summary>
    /// <param name="Node">The node dictionary.</param>
    /// <param name="Reference">The node's reference, or the nearest indirect ancestor's when the node is direct.</param>
    /// <param name="Ancestors">The usable Limits of the nodes above it.</param>
    /// <param name="Depth">0 for the root.</param>
    /// <param name="IsRoot">Whether this is the root.</param>
    private readonly record struct Frame(CosDictionary Node, CosReference? Reference, BoundsChain? Ancestors, int Depth, bool IsRoot);

    /// <summary>A node's least and greatest keys.</summary>
    private readonly record struct Bounds(TKey Least, TKey Greatest)
    {
        public bool Contains(TKey key, IComparer<TKey> comparer) => comparer.Compare(key, Least) >= 0 && comparer.Compare(key, Greatest) <= 0;
    }

    /// <summary>The usable Limits of the nodes on the path to a node, nearest first.</summary>
    private sealed record BoundsChain(Bounds Bounds, CosReference? Reference, BoundsChain? Next);

    /// <summary>Every key of the tree, sorted, with raw values; current while no container it was read from has changed.</summary>
    private sealed class TreeIndex(TKey[] keys, CosObject[] values, (CosObject Container, int Version)[] stamps)
    {
        public TKey[] Keys => keys;

        public CosObject[] Values => values;

        public int Find(TKey key, IComparer<TKey> comparer) => Array.BinarySearch(keys, key, comparer);

        public bool IsCurrent()
        {
            foreach ((CosObject container, int version) in stamps)
            {
                int current = container switch
                {
                    CosDictionary dictionary => dictionary.Version,
                    CosArray array => array.Version,
                    _ => version,
                };
                if (current != version)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
