using Broadside.Objects;

namespace Broadside;

/// <summary>
/// Edits the links of the document outline in place: inserting, unlinking and freeing items, and keeping every <c>Count</c> on the
/// way to the outline dictionary correct (ISO 32000-2 §12.3.3, Tables 150 and 151).
/// </summary>
/// <remarks>
/// <para>
/// Every edit reads the level it changes from its <c>First</c>/<c>Next</c> chain, as the outline walk does (the chain wins over
/// <c>Last</c>, <c>Prev</c> and <c>Parent</c>), stopping where the chain repeats an item or meets something that is not a dictionary,
/// and rewrites the links of the items next to the change from that chain, so an edit also repairs those links. An item the chain
/// holds directly (the clause requires indirect items) is made an indirect object when an edit walks past it.
/// </para>
/// <para>
/// After an edit, the <c>Count</c> of the changed node is recomputed from its children (each counts 1, plus its own <c>Count</c> when
/// that is positive) keeping the node's sign, and so on up the <c>Parent</c> links to the outline dictionary, stopping as soon as a
/// <c>Count</c> stays the same or a closed item hides the change from its parent.
/// </para>
/// </remarks>
internal static class OutlineEditor
{
    /// <summary>The most items one level is read to; past it the chain is treated as ending.</summary>
    private const int MaxLevelLength = 1 << 20;

    /// <summary>Returns the outline dictionary the catalog's <c>Outlines</c> names, or <see langword="null"/>.</summary>
    public static CosDictionary? Root(PdfDocument document) =>
        document.Catalog.TryGetValue(NavigationNames.Outlines, out CosObject? entry) ? document.Resolve(entry) as CosDictionary : null;

    /// <summary>Reads the children of <paramref name="node"/> from its <c>First</c>/<c>Next</c> chain, making direct items indirect.</summary>
    public static List<Link> Children(PdfDocument document, CosDictionary node)
    {
        var children = new List<Link>();
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { node };
        CosDictionary owner = node;
        CosName key = NavigationNames.First;
        while (children.Count < MaxLevelLength && owner.TryGetValue(key, out CosObject? entry))
        {
            if (document.Resolve(entry) is not CosDictionary item || !seen.Add(item))
            {
                break;
            }

            if (entry is not CosReference reference)
            {
                reference = document.AddObject(item);
                owner[key] = reference;
            }

            children.Add(new Link(item, reference));
            owner = item;
            key = NavigationNames.Next;
        }

        return children;
    }

    /// <summary>Returns the index of <paramref name="item"/> among <paramref name="children"/>, or -1.</summary>
    public static int IndexOf(List<Link> children, CosDictionary item) =>
        children.FindIndex(child => ReferenceEquals(child.Item, item));

    /// <summary>
    /// Creates an item titled <paramref name="title"/> and links it at <paramref name="index"/> among the children of
    /// <paramref name="parent"/>, or after the last when <paramref name="index"/> is null.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is greater than the number of children.</exception>
    public static Link Insert(PdfDocument document, CosDictionary parent, CosReference parentReference, int? index, string title)
    {
        List<Link> children = Children(document, parent);
        int position = index ?? children.Count;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, children.Count, nameof(index));
        var item = new CosDictionary { [NavigationNames.Title] = new CosString(TextStringEncoder.Encode(title)) };
        var link = new Link(item, document.AddObject(item));
        LinkAt(document, parent, parentReference, children, position, link);
        return link;
    }

    /// <summary>Links <paramref name="item"/> at <paramref name="index"/> of <paramref name="children"/>, the current children of <paramref name="parent"/>.</summary>
    public static void LinkAt(PdfDocument document, CosDictionary parent, CosReference parentReference, List<Link> children, int index, Link item)
    {
        Link? previous = index > 0 ? children[index - 1] : null;
        Link? next = index < children.Count ? children[index] : null;
        item.Item[KnownNames.Parent] = parentReference;
        SetOrRemove(item.Item, KnownNames.Prev, previous?.Reference);
        SetOrRemove(item.Item, NavigationNames.Next, next?.Reference);
        if (previous is { } before)
        {
            before.Item[NavigationNames.Next] = item.Reference;
        }
        else
        {
            parent[NavigationNames.First] = item.Reference;
        }

        if (next is { } after)
        {
            after.Item[KnownNames.Prev] = item.Reference;
        }
        else
        {
            parent[NavigationNames.Last] = item.Reference;
        }

        // An item that gets its first child opens: Count 1 is the sign UpdateCounts keeps.
        bool open = children.Count == 0 || (PdfOutlineItem.ReadCount(document, parent) ?? 0) > 0;
        UpdateCounts(document, parent, open);
    }

    /// <summary>Unlinks the child at <paramref name="index"/> of <paramref name="children"/>, the current children of <paramref name="parent"/>.</summary>
    public static void UnlinkAt(PdfDocument document, CosDictionary parent, List<Link> children, int index)
    {
        Link item = children[index];
        Link? previous = index > 0 ? children[index - 1] : null;
        Link? next = index < children.Count - 1 ? children[index + 1] : null;
        if (previous is { } before)
        {
            SetOrRemove(before.Item, NavigationNames.Next, next?.Reference);
        }
        else
        {
            SetOrRemove(parent, NavigationNames.First, next?.Reference);
        }

        if (next is { } after)
        {
            SetOrRemove(after.Item, KnownNames.Prev, previous?.Reference);
        }
        else
        {
            SetOrRemove(parent, NavigationNames.Last, previous?.Reference);
        }

        item.Item.Remove(KnownNames.Prev);
        item.Item.Remove(NavigationNames.Next);
        children.RemoveAt(index);
        UpdateCounts(document, parent, open: null);
    }

    /// <summary>
    /// Recomputes the <c>Count</c> of <paramref name="node"/> from its children, opened or closed as <paramref name="open"/> says (its
    /// current state when null), then of its ancestors as far as the change shows.
    /// </summary>
    public static void UpdateCounts(PdfDocument document, CosDictionary node, bool? open)
    {
        CosDictionary? root = Root(document);
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        CosDictionary? current = node;
        while (current is not null && seen.Add(current))
        {
            int visible = 0;
            foreach (Link child in Children(document, current))
            {
                visible += 1 + Math.Max(PdfOutlineItem.ReadCount(document, child.Item) ?? 0, 0);
            }

            bool isRoot = ReferenceEquals(current, root);
            int? before = PdfOutlineItem.ReadCount(document, current);
            bool isOpen = isRoot || (open ?? before > 0);
            open = null;
            int? after = visible == 0 ? null : isOpen ? visible : -visible;
            if (after == before && current.ContainsKey(KnownNames.Count) == after.HasValue)
            {
                return;
            }

            SetOrRemove(current, KnownNames.Count, after is { } count ? new CosInteger(count) : null);

            // A closed item counts 1 in its parent however many it hides; the root has no parent.
            bool shownBefore = before > 0;
            if (isRoot || (!isOpen && !shownBefore))
            {
                return;
            }

            current = current.TryGetValue(KnownNames.Parent, out CosObject? parent) ? document.Resolve(parent) as CosDictionary : null;
        }
    }

    /// <summary>Returns the items of the subtree under <paramref name="item"/>, the item first, by their links.</summary>
    public static List<Link> Subtree(PdfDocument document, Link item)
    {
        var items = new List<Link> { item };
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { item.Item };
        for (int index = 0; index < items.Count; index++)
        {
            foreach (Link child in Children(document, items[index].Item))
            {
                if (seen.Add(child.Item))
                {
                    items.Add(child);
                }
            }
        }

        return items;
    }

    /// <summary>Returns every item the outline dictionary reaches, by their links.</summary>
    public static HashSet<CosDictionary> Reachable(PdfDocument document, CosDictionary root)
    {
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { root };
        var pending = new Stack<CosDictionary>([root]);
        while (pending.TryPop(out CosDictionary? node))
        {
            foreach (Link child in Children(document, node))
            {
                if (seen.Add(child.Item))
                {
                    pending.Push(child.Item);
                }
            }
        }

        return seen;
    }

    private static void SetOrRemove(CosDictionary dictionary, CosName key, CosObject? value)
    {
        if (value is null)
        {
            dictionary.Remove(key);
        }
        else
        {
            dictionary[key] = value;
        }
    }

    /// <summary>An outline item and the reference that links it.</summary>
    /// <param name="Item">The item dictionary.</param>
    /// <param name="Reference">Its indirect reference.</param>
    internal readonly record struct Link(CosDictionary Item, CosReference Reference);
}
