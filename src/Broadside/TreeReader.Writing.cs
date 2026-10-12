using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <content>
/// Writing (issue #146): building a balanced tree from keys, and inserting, replacing and removing keys in place with every
/// <c>Limits</c> entry kept correct.
/// </content>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.9.6, Table 36, and §7.9.7, Table 37. A built tree is balanced: every leaf is at the same depth and holds at most
/// <see cref="NodeCapacity"/> pairs, every intermediate node at most <see cref="NodeCapacity"/> kids, and the nodes of one level differ
/// in size by at most one. A tree of up to <see cref="NodeCapacity"/> keys is a root holding the pairs itself; the root never has
/// <c>Limits</c>, every other node has them, and every kid is a new indirect object.
/// </para>
/// <para>
/// An edit descends from the root as a lookup does, checking each node on the way (one of <c>Kids</c> and the pairs array, usable
/// sorted <c>Limits</c> on every kid, kids that are indirect references, kids' keys within their parent's <c>Limits</c>, a sorted
/// leaf), changes the leaf, and walks back up: a node grown past <see cref="NodeCapacity"/> splits in two (the root splits into two
/// new kids, so every leaf stays at the same depth), a node left empty is removed from its parent, and each node's <c>Limits</c> is
/// rewritten when it no longer matches its least and greatest keys. Only the nodes on that path change, so an incremental save
/// writes only them.
/// </para>
/// <para>
/// The first edit of a tree walks the whole tree once with the checking walk. A tree that walk or a later descent finds damaged
/// (anything the reader would report) is rebuilt balanced in place from what the reader reads from it, keeping the root dictionary,
/// and the edit is applied to the rebuilt tree; the old nodes are no longer referenced from it. Edits are mutations: single-threaded.
/// </para>
/// </remarks>
internal abstract partial class TreeReader<TKey>
{
    /// <summary>The most pairs a leaf and the most kids an intermediate node holds in a tree this library writes.</summary>
    public const int NodeCapacity = 64;

    private bool _checkedForEditing;

    /// <summary>Writes a key as the object the tree stores: a string for name trees, an integer for number trees.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The key object.</returns>
    private protected abstract CosObject WriteKey(TKey key);

    /// <summary>Replaces whatever the root holds with a balanced tree of <paramref name="entries"/>.</summary>
    /// <param name="entries">The keys and values, in any order.</param>
    /// <param name="add">Makes an object indirect in the document and returns its reference.</param>
    /// <exception cref="ArgumentException">A key appears more than once.</exception>
    public void Build(IEnumerable<KeyValuePair<TKey, CosObject>> entries, Func<CosObject, CosReference> add)
    {
        ArgumentNullException.ThrowIfNull(entries);
        KeyValuePair<TKey, CosObject>[] sorted = [.. entries];
        TKey[] keys = new TKey[sorted.Length];
        CosObject[] values = new CosObject[sorted.Length];
        for (int index = 0; index < sorted.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(sorted[index].Key, nameof(entries));
            ArgumentNullException.ThrowIfNull(sorted[index].Value, nameof(entries));
            keys[index] = sorted[index].Key;
            values[index] = sorted[index].Value;
        }

        Array.Sort(keys, values, KeyComparer);
        for (int index = 1; index < keys.Length; index++)
        {
            if (KeyComparer.Compare(keys[index - 1], keys[index]) == 0)
            {
                throw new ArgumentException($"The key {keys[index]} appears more than once; a {Kind} holds each key once.", nameof(entries));
            }
        }

        BuildSorted(keys, values, add);
    }

    /// <summary>Inserts <paramref name="key"/> with <paramref name="value"/>, or replaces the value of the key already in the tree.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, stored as given (an indirect reference stays one).</param>
    /// <param name="add">Makes an object indirect in the document and returns its reference.</param>
    public void Set(TKey key, CosObject value, Func<CosObject, CosReference> add)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (IsDamaged())
        {
            Rebuild(add);
        }

        List<EditStep> path = FindEditPath(key, add);
        CosArray entries = path[^1].Entries!;
        int position = FindPair(entries, key, out bool found);
        if (found)
        {
            if (!ReferenceEquals(entries[(2 * position) + 1], value))
            {
                entries[(2 * position) + 1] = value;
            }

            Edited();
            return;
        }

        entries.Insert(2 * position, WriteKey(key));
        entries.Insert((2 * position) + 1, value);
        for (int level = path.Count - 1; level >= 0; level--)
        {
            EditStep step = path[level];
            if (Size(step) > NodeCapacity)
            {
                if (level == 0)
                {
                    SplitRoot(step, add);
                }
                else
                {
                    Split(step, path[level - 1].Kids!, add);
                }
            }

            if (level > 0)
            {
                UpdateLimits(step);
            }
        }

        Edited();
    }

    /// <summary>Removes <paramref name="key"/> and its value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="add">Makes an object indirect in the document and returns its reference.</param>
    /// <returns><see langword="true"/> when the tree held the key.</returns>
    public bool Remove(TKey key, Func<CosObject, CosReference> add)
    {
        if (IsDamaged())
        {
            // The index the check built is current: a key it does not hold leaves the damaged tree as it is.
            if (!TryGetRawValue(key, out _))
            {
                return false;
            }

            Rebuild(add);
        }

        List<EditStep> path = FindEditPath(key, add);
        CosArray entries = path[^1].Entries!;
        int position = FindPair(entries, key, out bool found);
        if (!found)
        {
            return false;
        }

        entries.RemoveAt((2 * position) + 1);
        entries.RemoveAt(2 * position);
        for (int level = path.Count - 1; level >= 0; level--)
        {
            EditStep step = path[level];
            if (Size(step) == 0)
            {
                if (level > 0)
                {
                    // An empty node below the root is dropped from its parent (§7.9.6: a leaf holds the keys in its range).
                    path[level - 1].Kids!.RemoveAt(step.IndexInParent);
                    continue;
                }

                if (step.Kids is not null)
                {
                    // Every kid is gone: the root becomes an empty leaf.
                    step.Node.Remove(KnownNames.Kids);
                    step.Node[EntriesKey] = new CosArray();
                }
            }
            else if (level > 0)
            {
                UpdateLimits(step);
            }
        }

        Edited();
        return true;
    }

    private static int Size(EditStep step) => step.Entries is { } entries ? entries.Count / 2 : step.Kids!.Count;

    /// <summary>Splits node sizes as evenly as possible into parts of at most <see cref="NodeCapacity"/>.</summary>
    private static IEnumerable<(int Start, int Length)> Parts(int count)
    {
        int parts = (count + NodeCapacity - 1) / NodeCapacity;
        int size = count / parts;
        int larger = count % parts;
        int start = 0;
        for (int part = 0; part < parts; part++)
        {
            int length = size + (part < larger ? 1 : 0);
            yield return (start, length);
            start += length;
        }
    }

    /// <summary>Moves the elements of <paramref name="array"/> from <paramref name="start"/> on into a new array.</summary>
    private static CosArray TakeTail(CosArray array, int start)
    {
        var tail = new CosArray();
        for (int index = start; index < array.Count; index++)
        {
            tail.Add(array[index]);
        }

        for (int index = array.Count - 1; index >= start; index--)
        {
            array.RemoveAt(index);
        }

        return tail;
    }

    /// <summary>Whether the whole tree, checked once per reader before its first edit, is damaged.</summary>
    private bool IsDamaged()
    {
        if (!_checkedForEditing)
        {
            _ = GetIndex();
            _checkedForEditing = true;
        }

        return _limitsUntrusted;
    }

    /// <summary>After an edit of a well-formed tree: lookups descend again instead of using an index of the old tree.</summary>
    private void Edited() => _index = null;

    /// <summary>Rebuilds the tree, balanced, from what the checking walk reads from it.</summary>
    private void Rebuild(Func<CosObject, CosReference> add)
    {
        TreeIndex index = GetIndex();
        BuildSorted(index.Keys, index.Values, add);
    }

    /// <summary>Builds a balanced tree of sorted, unique keys into the root.</summary>
    private void BuildSorted(TKey[] keys, CosObject[] values, Func<CosObject, CosReference> add)
    {
        Root.Remove(NavigationNames.Limits);
        Root.Remove(KnownNames.Kids);
        Root.Remove(EntriesKey);
        if (keys.Length <= NodeCapacity)
        {
            Root[EntriesKey] = Pairs(keys, values, 0, keys.Length);
        }
        else
        {
            var level = new List<(CosReference Reference, TKey Least, TKey Greatest)>();
            foreach ((int start, int length) in Parts(keys.Length))
            {
                TKey least = keys[start];
                TKey greatest = keys[start + length - 1];
                var leaf = new CosDictionary
                {
                    [NavigationNames.Limits] = new CosArray { WriteKey(least), WriteKey(greatest) },
                    [EntriesKey] = Pairs(keys, values, start, length),
                };
                level.Add((add(leaf), least, greatest));
            }

            while (level.Count > NodeCapacity)
            {
                var parents = new List<(CosReference Reference, TKey Least, TKey Greatest)>();
                foreach ((int start, int length) in Parts(level.Count))
                {
                    TKey least = level[start].Least;
                    TKey greatest = level[start + length - 1].Greatest;
                    var kids = new CosArray();
                    for (int index = start; index < start + length; index++)
                    {
                        kids.Add(level[index].Reference);
                    }

                    var node = new CosDictionary
                    {
                        [NavigationNames.Limits] = new CosArray { WriteKey(least), WriteKey(greatest) },
                        [KnownNames.Kids] = kids,
                    };
                    parents.Add((add(node), least, greatest));
                }

                level = parents;
            }

            Root[KnownNames.Kids] = new CosArray(level.Select(node => (CosObject)node.Reference));
        }

        _limitsUntrusted = false;
        _index = null;
        _checkedForEditing = true;
    }

    private CosArray Pairs(TKey[] keys, CosObject[] values, int start, int length)
    {
        var pairs = new CosArray();
        for (int index = start; index < start + length; index++)
        {
            pairs.Add(WriteKey(keys[index]));
            pairs.Add(values[index]);
        }

        return pairs;
    }

    /// <summary>
    /// Finds the path from the root to the leaf that holds, or should hold, <paramref name="key"/>; rebuilds the tree first when a node
    /// on the way is not as the clause requires.
    /// </summary>
    private List<EditStep> FindEditPath(TKey key, Func<CosObject, CosReference> add)
    {
        if (TryFindEditPath(key, out List<EditStep>? path))
        {
            return path;
        }

        Rebuild(add);
        return TryFindEditPath(key, out path) ? path : throw new InvalidOperationException($"A rebuilt {Kind} has no leaf for the key.");
    }

    /// <summary>The checked descent: fails on anything an edit could make worse.</summary>
    private bool TryFindEditPath(TKey key, [NotNullWhen(true)] out List<EditStep>? path)
    {
        path = [];
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        CosDictionary node = Root;
        int indexInParent = -1;
        bool hasBounds = false;
        TKey least = default!;
        TKey greatest = default!;
        for (int depth = 0; depth <= MaxDepth; depth++)
        {
            bool hasEntries = node.TryGetValue(EntriesKey, out CosObject? entriesEntry);
            bool hasKids = node.TryGetValue(KnownNames.Kids, out CosObject? kidsEntry);
            if (!visited.Add(node) || hasEntries == hasKids || (depth == 0 && node.ContainsKey(NavigationNames.Limits)))
            {
                return false;
            }

            if (hasEntries)
            {
                if (Resolve(entriesEntry) is not CosArray entries || entries.Count % 2 != 0 || (depth > 0 && entries.Count == 0))
                {
                    return false;
                }

                TKey previous = default!;
                for (int index = 0; index < entries.Count; index += 2)
                {
                    if (ReadKey(Resolve(entries[index]), out TKey? current, out _) != KeyState.Valid
                        || (index > 0 && KeyComparer.Compare(previous, current) >= 0)
                        || (hasBounds && (KeyComparer.Compare(current, least) < 0 || KeyComparer.Compare(current, greatest) > 0)))
                    {
                        return false;
                    }

                    previous = current;
                }

                path.Add(new EditStep(node, entries, null, indexInParent));
                return true;
            }

            if (Resolve(kidsEntry) is not CosArray kids || kids.Count == 0)
            {
                return false;
            }

            int chosen = -1;
            CosDictionary? chosenNode = null;
            TKey chosenLeast = default!;
            TKey chosenGreatest = default!;
            bool hasPrevious = false;
            TKey previousGreatest = default!;
            for (int index = 0; index < kids.Count; index++)
            {
                if (kids[index] is not CosReference
                    || Resolve(kids[index]) is not CosDictionary kid
                    || !TryReadLimits(kid, out TKey? kidLeast, out TKey? kidGreatest)
                    || (hasPrevious && KeyComparer.Compare(previousGreatest, kidLeast) >= 0)
                    || (hasBounds && (KeyComparer.Compare(kidLeast, least) < 0 || KeyComparer.Compare(kidGreatest, greatest) > 0)))
                {
                    return false;
                }

                // The first kid whose greatest key is not less than the key; past every kid, the last one.
                if (chosen < 0 && (KeyComparer.Compare(kidGreatest, key) >= 0 || index == kids.Count - 1))
                {
                    chosen = index;
                    chosenNode = kid;
                    chosenLeast = kidLeast;
                    chosenGreatest = kidGreatest;
                }

                hasPrevious = true;
                previousGreatest = kidGreatest;
            }

            path.Add(new EditStep(node, null, kids, indexInParent));
            node = chosenNode!;
            indexInParent = chosen;
            hasBounds = true;
            least = chosenLeast;
            greatest = chosenGreatest;
        }

        return false;
    }

    /// <summary>Binary-searches a checked leaf for the pair index of <paramref name="key"/>, or where it would be inserted.</summary>
    private int FindPair(CosArray entries, TKey key, out bool found)
    {
        int low = 0;
        int high = (entries.Count / 2) - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            _ = ReadKey(Resolve(entries[2 * middle]), out TKey? probe, out _);
            int comparison = KeyComparer.Compare(probe!, key);
            if (comparison == 0)
            {
                found = true;
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        found = false;
        return low;
    }

    /// <summary>Splits a node below the root that holds too much: its upper half moves to a new node after it in its parent.</summary>
    private void Split(EditStep step, CosArray parentKids, Func<CosObject, CosReference> add)
    {
        var sibling = new CosDictionary();
        if (step.Entries is { } entries)
        {
            sibling[EntriesKey] = TakeTail(entries, 2 * ((Size(step) + 1) / 2));
        }
        else
        {
            sibling[KnownNames.Kids] = TakeTail(step.Kids!, (Size(step) + 1) / 2);
        }

        SetLimits(sibling);
        parentKids.Insert(step.IndexInParent + 1, add(sibling));
    }

    /// <summary>Splits a root that holds too much into two new kids, so every leaf stays at the same depth.</summary>
    private void SplitRoot(EditStep step, Func<CosObject, CosReference> add)
    {
        CosName key = step.Entries is null ? KnownNames.Kids : EntriesKey;
        CosArray content = step.Entries ?? step.Kids!;
        int half = step.Entries is null ? (Size(step) + 1) / 2 : 2 * ((Size(step) + 1) / 2);
        var upper = new CosDictionary { [key] = TakeTail(content, half) };
        var lower = new CosDictionary { [key] = content };
        SetLimits(lower);
        SetLimits(upper);
        step.Node.Remove(EntriesKey);
        step.Node[KnownNames.Kids] = new CosArray { add(lower), add(upper) };
    }

    /// <summary>Rewrites a node's <c>Limits</c> when they are not its least and greatest keys.</summary>
    private void UpdateLimits(EditStep step) => SetLimits(step.Node);

    private void SetLimits(CosDictionary node)
    {
        CosObject least;
        CosObject greatest;
        if (node.TryGetValue(EntriesKey, out CosObject? entriesEntry))
        {
            var entries = (CosArray)Resolve(entriesEntry);
            least = entries[0];
            greatest = entries[^2];
        }
        else
        {
            var kids = (CosArray)Resolve(node[KnownNames.Kids]);
            least = LimitsOf(kids[0])[0];
            greatest = LimitsOf(kids[^1])[1];
        }

        least = Resolve(least);
        greatest = Resolve(greatest);
        if (node.TryGetValue(NavigationNames.Limits, out CosObject? limitsEntry)
            && limitsEntry is CosArray { Count: 2 } limits
            && limits[0].Equals(least)
            && limits[1].Equals(greatest))
        {
            return;
        }

        node[NavigationNames.Limits] = new CosArray { least, greatest };
    }

    private CosArray LimitsOf(CosObject kid) => (CosArray)Resolve(((CosDictionary)Resolve(kid))[NavigationNames.Limits]);

    /// <summary>One node on an edit's path.</summary>
    /// <param name="Node">The node.</param>
    /// <param name="Entries">A leaf's pairs array, resolved; <see langword="null"/> for an intermediate node.</param>
    /// <param name="Kids">An intermediate node's kids array, resolved; <see langword="null"/> for a leaf.</param>
    /// <param name="IndexInParent">The node's position in its parent's kids; -1 for the root.</param>
    private readonly record struct EditStep(CosDictionary Node, CosArray? Entries, CosArray? Kids, int IndexInParent);
}
