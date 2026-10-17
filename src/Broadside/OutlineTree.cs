using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// One walk of the document outline: every item's view, parent, level and children, kept while nothing the walk read has changed
/// (ISO 32000-2 §12.3.3, Tables 150 and 151). The outline views read their shape from it, so an edit through any view or straight to
/// the COS objects is seen by every view at the next read (ADR 0004).
/// </summary>
/// <remarks>
/// <para>
/// The walk follows each level's <c>First</c>/<c>Next</c> chain, iteratively, with one visited set for the whole outline and a
/// depth cap of <see cref="PdfOutline.MaxDepth"/> levels, and reports the deviations <see cref="PdfOutline"/> lists, once per walk.
/// It records the <c>Version</c> of the outline dictionary and of every item dictionary it reached: those hold every link and entry
/// the walk read (a <c>Title</c>, <c>C</c> or <c>Dest</c> edit also walks again, which keeps the walk's diagnostics true), so the
/// tree is current while none of them has changed. An item view reached again by a later walk is reused, so a
/// view stays the same object while its item stays in the outline.
/// </para>
/// <para>
/// Built by one thread under the document's outline lock, then published and only read: safe for concurrent readers under the
/// document's thread-safety contract.
/// </para>
/// </remarks>
internal sealed class OutlineTree
{
    /// <summary>The shape of an item the outline does not reach: no parent, level 0, no children.</summary>
    public static readonly Node Outside = new(null, null, 0, [], 0);

    private readonly Dictionary<CosDictionary, Node> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly int _rootVersion;
    private readonly int _objectsVersion;
    private long _epoch;

    private OutlineTree(CosDictionary root, CosReference? rootReference, int objectsVersion)
    {
        _epoch = CosMutation.Epoch;
        _objectsVersion = objectsVersion;
        Root = root;
        RootReference = rootReference;
        _rootVersion = root.Version;
    }

    /// <summary>Gets the outline dictionary walked.</summary>
    public CosDictionary Root { get; }

    /// <summary>Gets the outline dictionary's reference as the walk had it, or <see langword="null"/> when it was direct.</summary>
    public CosReference? RootReference { get; }

    /// <summary>Gets the top-level items, in chain order.</summary>
    public IReadOnlyList<PdfOutlineItem> Items { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether this walk is of <paramref name="root"/> through <paramref name="reference"/> and nothing it read
    /// has changed since.
    /// </summary>
    /// <param name="root">The outline dictionary the catalog names now.</param>
    /// <param name="reference">Its reference, or <see langword="null"/> when it is direct.</param>
    /// <param name="objectsVersion">The document's <c>ObjectsVersion</c> now.</param>
    /// <remarks>
    /// The one place that decides whether the walk is reused: it compares the <c>Version</c> of the outline dictionary and of every
    /// item dictionary reached with the one the walk recorded, allocating nothing. An object added to or removed from the document
    /// since the walk changes what an unchanged link resolves to (§7.3.10), so the walk is not reused then either (issue #302). The
    /// comparison is skipped while <see cref="CosMutation.Epoch"/> has not moved since the walk was last found current.
    /// </remarks>
    public bool IsCurrent(CosDictionary root, CosReference? reference, int objectsVersion)
    {
        if (!ReferenceEquals(root, Root) || !Equals(reference, RootReference))
        {
            return false;
        }

        long epoch = CosMutation.Epoch;
        if (Volatile.Read(ref _epoch) == epoch)
        {
            return true;
        }

        if (objectsVersion != _objectsVersion || root.Version != _rootVersion)
        {
            return false;
        }

        foreach (KeyValuePair<CosDictionary, Node> node in _nodes)
        {
            if (node.Key.Version != node.Value.Version)
            {
                return false;
            }
        }

        Volatile.Write(ref _epoch, epoch);
        return true;
    }

    /// <summary>Finds the shape of the item <paramref name="item"/> is the dictionary of.</summary>
    /// <returns><see cref="Outside"/> when the walk did not reach it.</returns>
    public Node Find(CosDictionary item) => _nodes.TryGetValue(item, out Node node) ? node : Outside;

    /// <summary>Walks the outline <paramref name="root"/>, reusing the views of <paramref name="previous"/> and <paramref name="created"/>.</summary>
    /// <param name="document">The document.</param>
    /// <param name="root">The outline dictionary.</param>
    /// <param name="rootReference">Its reference, or <see langword="null"/> when it is direct.</param>
    /// <param name="previous">The walk this one replaces, or <see langword="null"/>.</param>
    /// <param name="created">Views the edits since <paramref name="previous"/> returned, or <see langword="null"/>.</param>
    /// <exception cref="DiagnosticException">In strict mode, for the first deviation the walk finds.</exception>
    public static OutlineTree Build(
        PdfDocument document,
        CosDictionary root,
        CosReference? rootReference,
        OutlineTree? previous,
        Dictionary<CosDictionary, PdfOutlineItem>? created)
    {
        var tree = new OutlineTree(root, rootReference, document.ObjectsVersion);
        new Builder(document, tree, previous, created).Run();
        return tree;
    }

    /// <summary>The shape of one item as the walk found it.</summary>
    /// <param name="View">The item's view, or <see langword="null"/> for <see cref="Outside"/>.</param>
    /// <param name="Parent">The item it is a child of, or <see langword="null"/> at the top level.</param>
    /// <param name="Level">Its depth: 0 at the top level.</param>
    /// <param name="Children">Its children, in chain order.</param>
    /// <param name="Version">The item dictionary's <c>Version</c> when the walk read it.</param>
    public readonly record struct Node(PdfOutlineItem? View, PdfOutlineItem? Parent, int Level, IReadOnlyList<PdfOutlineItem> Children, int Version);

    /// <summary>The walk itself (Table 150 and 151 checks included).</summary>
    private readonly struct Builder(PdfDocument document, OutlineTree tree, OutlineTree? previous, Dictionary<CosDictionary, PdfOutlineItem>? created)
    {
        public void Run()
        {
            CosDictionary root = tree.Root;
            CosReference? rootReference = tree.RootReference;
            var top = new List<PdfOutlineItem>();
            var levels = new Stack<Level>();
            levels.Push(new Level(null, root, rootReference, 0, top));
            while (levels.TryPop(out Level level))
            {
                List<PdfOutlineItem> siblings = level.Siblings;
                CosReference? previousReference = null;
                PdfOutlineItem? last = null;
                level.Node.TryGetValue(NavigationNames.First, out CosObject? entry);
                while (entry is not null)
                {
                    var reference = entry as CosReference;
                    CosObject resolved = document.Resolve(entry);
                    if (resolved is not CosDictionary dictionary)
                    {
                        if (resolved is not CosNull)
                        {
                            Report(DiagnosticCodes.OutlineItemInvalid, DiagnosticSeverity.Warning, "An outline item is not a dictionary; its level ends there.", reference ?? level.Reference);
                        }

                        break;
                    }

                    if (ReferenceEquals(dictionary, root) || tree._nodes.ContainsKey(dictionary))
                    {
                        Report(
                            DiagnosticCodes.OutlineCycle,
                            DiagnosticSeverity.Error,
                            "An outline item is reached a second time (a cycle through Next or First); its level ends there.",
                            reference ?? level.Reference);
                        break;
                    }

                    PdfOutlineItem item = ViewOf(dictionary, reference);
                    tree._nodes.Add(dictionary, new Node(item, level.Parent, level.Depth, [], dictionary.Version));
                    CheckItem(item, level.Reference, previousReference);
                    siblings.Add(item);
                    previousReference = reference;
                    last = item;
                    dictionary.TryGetValue(NavigationNames.Next, out entry);
                }

                if (last is not null && !(level.Node.TryGetValue(NavigationNames.Last, out CosObject? lastEntry) && SameObject(lastEntry, last)))
                {
                    Report(
                        DiagnosticCodes.OutlineLinkInconsistent,
                        DiagnosticSeverity.Warning,
                        "The Last entry does not name the item the First/Next chain ends with; the chain is used.",
                        level.Reference);
                }

                for (int index = siblings.Count - 1; index >= 0 && last is not null; index--)
                {
                    PdfOutlineItem item = siblings[index];
                    if (!item.Dictionary.ContainsKey(NavigationNames.First))
                    {
                        continue;
                    }

                    if (level.Depth + 1 >= PdfOutline.MaxDepth)
                    {
                        Report(
                            DiagnosticCodes.OutlineTooDeep,
                            DiagnosticSeverity.Error,
                            string.Create(CultureInfo.InvariantCulture, $"The outline is deeper than {PdfOutline.MaxDepth} levels; the deeper items are skipped."),
                            item.Reference ?? level.Reference);
                        continue;
                    }

                    var children = new List<PdfOutlineItem>();
                    tree._nodes[item.Dictionary] = tree._nodes[item.Dictionary] with { Children = children };
                    levels.Push(new Level(item, item.Dictionary, item.Reference ?? level.Reference, level.Depth + 1, children));
                }
            }

            tree.Items = top;
            CheckCounts(top);
        }

        /// <summary>The view of an item: the one an earlier walk or an edit made for it, else a new one.</summary>
        private PdfOutlineItem ViewOf(CosDictionary dictionary, CosReference? reference)
        {
            if (previous is not null && previous._nodes.TryGetValue(dictionary, out Node known) && Equals(known.View!.Reference, reference))
            {
                return known.View;
            }

            if (created is not null && created.TryGetValue(dictionary, out PdfOutlineItem? made) && Equals(made.Reference, reference))
            {
                return made;
            }

            return new PdfOutlineItem(document, dictionary, reference);
        }

        /// <summary>Checks one item's entries (Table 151) as the walk reaches it.</summary>
        private void CheckItem(PdfOutlineItem item, CosReference? parentReference, CosReference? previousReference)
        {
            CosDictionary dictionary = item.Dictionary;
            CosReference? reference = item.Reference ?? parentReference;
            bool parentMatches = parentReference is null || (dictionary.TryGetValue(KnownNames.Parent, out CosObject? parent) && parentReference.Equals(parent));
            bool hasPrev = dictionary.TryGetValue(KnownNames.Prev, out CosObject? prev);
            bool prevMatches = previousReference is null ? !hasPrev : previousReference.Equals(prev);
            if (!parentMatches || !prevMatches)
            {
                Report(
                    DiagnosticCodes.OutlineLinkInconsistent,
                    DiagnosticSeverity.Warning,
                    "An outline item's Parent or Prev does not name the item the First/Next chain reached it from; the chain is used.",
                    reference);
            }

            if (document.Resolve(dictionary.TryGetValue(NavigationNames.Title, out CosObject? title) ? title : null) is not CosString)
            {
                Report(DiagnosticCodes.OutlineItemInvalid, DiagnosticSeverity.Warning, "An outline item has no Title text string; it reads as empty.", reference);
            }

            _ = item.ReadColor(out bool colorValid);
            _ = item.ReadFlags(out bool flagsValid);
            if (!colorValid || !flagsValid)
            {
                Report(
                    DiagnosticCodes.OutlineItemInvalid,
                    DiagnosticSeverity.Warning,
                    "An outline item's C is not three numbers from 0 to 1, or its F is not an integer; black or no flags are used where they cannot be read.",
                    reference);
            }

            bool hasDest = dictionary.ContainsKey(NavigationNames.Dest);
            if (hasDest && dictionary.ContainsKey(NavigationNames.A))
            {
                Report(
                    DiagnosticCodes.OutlineDestAndAction,
                    DiagnosticSeverity.Warning,
                    "An outline item shall not have both Dest and A; both are read, and the action is the one to perform.",
                    reference);
            }
            else if (hasDest && PdfOutlineItem.IsActionDictionary(document.Resolve(dictionary[NavigationNames.Dest])))
            {
                Report(DiagnosticCodes.DestinationInvalid, DiagnosticSeverity.Warning, "An outline item's Dest holds an action dictionary; it is read as the item's action.", reference);
            }

            // Read the destination and the action so that their own deviations are reported with the walk.
            _ = item.Destination;
            _ = item.Action;
        }

        /// <summary>
        /// Checks the <c>Count</c> entries that decide something (Tables 150 and 151), parents before children: an item with children
        /// needs a non-zero <c>Count</c> to be open or closed, and the outline's <c>Count</c> cannot be negative.
        /// </summary>
        /// <remarks>
        /// The magnitude is not checked against the visible descendants: producers commonly write the number of immediate children
        /// (the Isartor PDF/A suite, PDFium's test files), viewers ignore it, and nothing here uses it, so a mismatch would only make
        /// strict mode reject files every reader shows alike.
        /// </remarks>
        private void CheckCounts(List<PdfOutlineItem> top)
        {
            if (tree.Root.ContainsKey(KnownNames.Count) && PdfOutlineItem.ReadCount(document, tree.Root) is not >= 0)
            {
                Report(
                    DiagnosticCodes.OutlineCountInvalid,
                    DiagnosticSeverity.Warning,
                    "The outline's Count is negative or not an integer; it cannot be.",
                    tree.RootReference);
            }

            var pending = new Stack<PdfOutlineItem>(top.AsEnumerable().Reverse());
            while (pending.TryPop(out PdfOutlineItem? item))
            {
                IReadOnlyList<PdfOutlineItem> children = tree._nodes[item.Dictionary].Children;
                if (children.Count > 0 && item.Count is null or 0)
                {
                    Report(
                        DiagnosticCodes.OutlineCountInvalid,
                        DiagnosticSeverity.Warning,
                        "An outline item with children has no non-zero Count to say whether it is open; it is read as closed.",
                        item.Reference);
                }

                for (int index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }
        }

        private bool SameObject(CosObject entry, PdfOutlineItem item) =>
            item.Reference is { } reference ? reference.Equals(entry) : ReferenceEquals(document.Resolve(entry), item.Dictionary);

        private void Report(string code, DiagnosticSeverity severity, string message, CosReference? reference) =>
            document.DiagnosticSink.Report(code, severity, message, objectReference: reference);
    }

    /// <summary>A level waiting to be walked: the children of <paramref name="Node"/>.</summary>
    /// <param name="Parent">The item whose children these are, or <see langword="null"/> for the top level.</param>
    /// <param name="Node">The outline dictionary or the parent item's dictionary.</param>
    /// <param name="Reference">The node's reference, or its nearest indirect ancestor's.</param>
    /// <param name="Depth">The level of the children: 0 for top-level items.</param>
    /// <param name="Siblings">The list the children are added to.</param>
    private readonly record struct Level(PdfOutlineItem? Parent, CosDictionary Node, CosReference? Reference, int Depth, List<PdfOutlineItem> Siblings);
}
