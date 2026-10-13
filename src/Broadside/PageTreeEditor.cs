using Broadside.Objects;

namespace Broadside;

/// <summary>Rebuilds a document's page tree, balanced, around an edited list of pages (issue #158).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3: "PDF processors shall not be required to preserve the existing structure of the page tree" (§7.7.3.2), so
/// every edit rebuilds it. Before any node changes, each page's inherited attributes (§7.7.3.4: <c>Resources</c>, <c>MediaBox</c>,
/// <c>CropBox</c>, <c>Rotate</c>) are made its own: an inherited reference is copied, an inherited direct dictionary becomes one
/// new indirect object the pages share (as they shared it through the node), an inherited direct array is copied per page. The tree
/// is then rebuilt with at most <see cref="MaxKids"/> kids per node and every page at the same depth (§7.7.3.1, note: a balanced
/// tree), each node's <c>Count</c> its number of pages and each kid's <c>Parent</c> its node (Table 30).
/// </para>
/// <para>
/// The root keeps its object (the catalog's <c>Pages</c>) and its other entries; its inheritable entries, now redundant, are removed.
/// Intermediate nodes the previous tree used are reused, in order, before new ones are added, and the ones left over are removed from
/// the document, so an edit changes only the nodes whose kids changed. An entry is only written when its value changes, so an
/// incremental update (§7.5.6) holds the pages and nodes the edit touched.
/// </para>
/// </remarks>
internal static class PageTreeEditor
{
    /// <summary>The most kids a rebuilt node has.</summary>
    public const int MaxKids = 32;

    private static readonly CosName[] InheritableKeys = [KnownNames.Resources, KnownNames.MediaBox, KnownNames.CropBox, KnownNames.Rotate];

    /// <summary>Rebuilds the page tree so its pages are <paramref name="pages"/>, in that order.</summary>
    /// <param name="document">The document.</param>
    /// <param name="previous">The pages before the edit, whose ancestors are the current tree.</param>
    /// <param name="pages">The pages after the edit; pages not in <paramref name="previous"/> may have no ancestors.</param>
    /// <param name="nodes">The intermediate nodes the current tree uses, from the last rebuild; <see langword="null"/> to find them.</param>
    /// <returns>The intermediate nodes the rebuilt tree uses, for the next rebuild.</returns>
    public static List<CosReference> Rebuild(PdfDocument document, IReadOnlyList<PdfPage> previous, IReadOnlyList<PdfPage> pages, IReadOnlyList<CosReference>? nodes)
    {
        (CosDictionary root, CosReference rootReference) = Root(document, previous);
        var reusable = new Queue<CosReference>(nodes ?? NodesBelow(root, previous));

        // Inherited attributes are read through the current tree, so they are made explicit before any node changes.
        var promoted = new Dictionary<CosDictionary, CosReference>(ReferenceEqualityComparer.Instance);
        foreach (PdfPage page in pages)
        {
            MakeInheritedExplicit(document, page, promoted);
        }

        var items = new List<Node>(pages.Count);
        foreach (PdfPage page in pages)
        {
            items.Add(new Node(page.Dictionary, page.Reference ?? document.AddObject(page.Dictionary), 1));
        }

        Node[] leaves = [.. items];
        var used = new List<CosReference>();
        while (items.Count > MaxKids)
        {
            int groups = (items.Count + MaxKids - 1) / MaxKids;
            int size = items.Count / groups;
            int larger = items.Count % groups;
            var level = new List<Node>(groups);
            int start = 0;
            for (int group = 0; group < groups; group++)
            {
                int count = size + (group < larger ? 1 : 0);
                (CosDictionary dictionary, CosReference reference) = NextNode(document, reusable);
                used.Add(reference);
                level.Add(Fill(dictionary, reference, items.GetRange(start, count)));
                start += count;
            }

            items = level;
        }

        Node top = Fill(root, rootReference, items);
        root.Remove(KnownNames.Parent);
        while (reusable.TryDequeue(out CosReference? leftover))
        {
            document.RemoveObject(leftover);
        }

        var chains = new Dictionary<Node, PageTreeAncestor>(ReferenceEqualityComparer.Instance) { [top] = new PageTreeAncestor(root, null, rootReference) };
        for (int index = 0; index < pages.Count; index++)
        {
            pages[index].Relink(leaves[index].Reference, Chain(leaves[index].Parent!, chains));
        }

        return used;
    }

    /// <summary>The catalog's page tree root, made indirect, or a new root when it has none (or it is a page).</summary>
    private static (CosDictionary Root, CosReference Reference) Root(PdfDocument document, IReadOnlyList<PdfPage> previous)
    {
        document.Catalog.TryGetValue(KnownNames.Pages, out CosObject? entry);
        if (document.Resolve(entry) is CosDictionary root && !previous.Any(page => ReferenceEquals(page.Dictionary, root)))
        {
            if (entry is CosReference reference)
            {
                return (root, reference);
            }

            CosReference promoted = document.AddObject(root);
            document.Catalog[KnownNames.Pages] = promoted;
            return (root, promoted);
        }

        var created = new CosDictionary { [KnownNames.Type] = KnownNames.Pages };
        CosReference createdReference = document.AddObject(created);
        document.Catalog[KnownNames.Pages] = createdReference;
        return (created, createdReference);
    }

    /// <summary>The next reusable node that is still a dictionary, else a new one.</summary>
    private static (CosDictionary Dictionary, CosReference Reference) NextNode(PdfDocument document, Queue<CosReference> reusable)
    {
        while (reusable.TryDequeue(out CosReference? reference))
        {
            if (document.Resolve(reference) is CosDictionary existing)
            {
                return (existing, reference);
            }
        }

        var created = new CosDictionary();
        return (created, document.AddObject(created));
    }

    /// <summary>The indirect intermediate nodes the pages were found through, below the root, each once.</summary>
    private static List<CosReference> NodesBelow(CosDictionary root, IReadOnlyList<PdfPage> pages)
    {
        var nodes = new List<CosReference>();
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { root };
        foreach (PdfPage page in pages)
        {
            var path = new Stack<PageTreeAncestor>();
            for (PageTreeAncestor? node = page.Ancestors; node is not null; node = node.Parent)
            {
                path.Push(node);
            }

            foreach (PageTreeAncestor node in path)
            {
                if (seen.Add(node.Dictionary) && node.Reference is { } reference)
                {
                    nodes.Add(reference);
                }
            }
        }

        return nodes;
    }

    /// <summary>Gives the page its own copy of each attribute it inherits (§7.7.3.4).</summary>
    private static void MakeInheritedExplicit(PdfDocument document, PdfPage page, Dictionary<CosDictionary, CosReference> promoted)
    {
        foreach (CosName key in InheritableKeys)
        {
            switch (page.FindInheritedEntry(key))
            {
                case null:
                    break;
                case CosDictionary dictionary:
                    if (!promoted.TryGetValue(dictionary, out CosReference? reference))
                    {
                        reference = document.AddObject(dictionary);
                        promoted.Add(dictionary, reference);
                    }

                    page.Dictionary[key] = reference;
                    break;
                case CosArray array:
                    page.Dictionary[key] = new CosArray(array);
                    break;
                case { } value:
                    page.Dictionary[key] = value;
                    break;
            }
        }
    }

    /// <summary>Makes <paramref name="dictionary"/> the page tree node over <paramref name="kids"/>, writing only what changes.</summary>
    private static Node Fill(CosDictionary dictionary, CosReference reference, List<Node> kids)
    {
        var node = new Node(dictionary, reference, 0);
        var references = new CosObject[kids.Count];
        for (int index = 0; index < kids.Count; index++)
        {
            Node kid = kids[index];
            kid.Parent = node;
            node.Leaves += kid.Leaves;
            references[index] = kid.Reference;
            SetIfChanged(kid.Dictionary, KnownNames.Parent, reference);
        }

        SetIfChanged(dictionary, KnownNames.Type, KnownNames.Pages);
        if (!(dictionary.TryGetValue(KnownNames.Kids, out CosObject? existing) && existing is CosArray array && array.SequenceEqual(references)))
        {
            dictionary[KnownNames.Kids] = new CosArray(references);
        }

        SetIfChanged(dictionary, KnownNames.Count, new CosInteger(node.Leaves));
        foreach (CosName key in InheritableKeys)
        {
            dictionary.Remove(key);
        }

        return node;
    }

    private static void SetIfChanged(CosDictionary dictionary, CosName key, CosObject value)
    {
        if (!(dictionary.TryGetValue(key, out CosObject? existing) && value.Equals(existing)))
        {
            dictionary[key] = value;
        }
    }

    /// <summary>The ancestors of a page whose parent is <paramref name="node"/>, sharing each node's chain.</summary>
    private static PageTreeAncestor Chain(Node node, Dictionary<Node, PageTreeAncestor> chains)
    {
        if (!chains.TryGetValue(node, out PageTreeAncestor? chain))
        {
            chain = new PageTreeAncestor(node.Dictionary, Chain(node.Parent!, chains), node.Reference);
            chains.Add(node, chain);
        }

        return chain;
    }

    /// <summary>A page or node of the tree being built.</summary>
    private sealed class Node(CosDictionary dictionary, CosReference reference, int leaves)
    {
        public CosDictionary Dictionary { get; } = dictionary;

        public CosReference Reference { get; } = reference;

        public int Leaves { get; set; } = leaves;

        public Node? Parent { get; set; }
    }
}
