using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>
/// The state one document's structure tree views share: the document (to resolve objects and find pages), its diagnostics, and the
/// derived indexes, each built once per generation through <see cref="OnceCache{TKey, TValue}"/> and then read without locks.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7. The indexes belong to a <see cref="StructureIndexes"/> generation that records the version of every container
/// they were built from; once one of those changes (an edit through the structure API or directly on the COS objects), the next
/// lookup starts a new generation (issue #156). Replacing the catalog's <c>StructTreeRoot</c> creates a new context
/// (<see cref="PdfDocument.StructureTree"/>). Reading never changes a COS object; every repair lives in the views.
/// </remarks>
internal sealed class StructureContext
{
    /// <summary>How deep the structure hierarchy may nest before the walk stops descending (a cap on untrusted input).</summary>
    public const int MaxDepth = 256;

    /// <summary>How many role-map steps resolving one type may take; maps may form cycles, which also end the chain.</summary>
    public const int MaxRoleSteps = 32;

    /// <summary>The longest parent-tree array read for one content stream; longer arrays are truncated (MCIDs are small in practice).</summary>
    public const int MaxParentTreeArray = 1_000_000;

    private readonly OnceCache<CosDictionary, PdfStructureNamespace> _namespaces = new(ReferenceEqualityComparer.Instance);
    private StructureIndexes _indexes;

    public StructureContext(PdfDocument document, CosDictionary root, CosReference? rootReference)
    {
        Document = document;
        Root = root;
        RootReference = rootReference;
        _indexes = new StructureIndexes(CosMutation.Epoch, document.ObjectsVersion);
    }

    public PdfDocument Document { get; }

    public CosDictionary Root { get; }

    /// <summary>Gets or sets the root's indirect reference; set when an edit makes a direct root indirect.</summary>
    public CosReference? RootReference { get; set; }

    public DiagnosticSink Diagnostics => Document.DiagnosticSink;

    /// <summary>Gets a value indicating whether the document claims to be a tagged PDF (<c>MarkInfo</c> <c>Marked</c> true, §14.8.1).</summary>
    public bool IsTagged => Document.MarkInfo.Marked;

    /// <summary>Gets the document-order walk of the hierarchy from the root's <c>K</c>, built once per generation of the indexes.</summary>
    public StructureWalk Walk
    {
        get
        {
            StructureIndexes indexes = Indexes;
            return indexes.Walk.GetOrCreate(
                0,
                (Context: this, Indexes: indexes),
                static (_, state) => new Created<StructureWalk>(state.Context.Build(state.Indexes, static (context, stamps) => StructureWalk.Build(context, stamps))),
                static (_, _) => StructureWalk.Empty);
        }
    }

    /// <summary>
    /// Gets the current generation of the derived indexes: the one in use while nothing it was built from has changed, else a new,
    /// empty one (ISO 32000-2 §14.7; edits through the structure API or directly on the COS objects are both seen).
    /// </summary>
    private StructureIndexes Indexes
    {
        get
        {
            StructureIndexes current = Volatile.Read(ref _indexes);
            long epoch = CosMutation.Epoch;
            int objectsVersion = Document.ObjectsVersion;
            if (current.IsCurrent(epoch, objectsVersion))
            {
                return current;
            }

            var fresh = new StructureIndexes(epoch, objectsVersion);
            StructureIndexes previous = Interlocked.CompareExchange(ref _indexes, fresh, current);
            return ReferenceEquals(previous, current) ? fresh : previous;
        }
    }

    /// <summary>Gets the reader of the root's <c>ParentTree</c> number tree, or <see langword="null"/> when there is none (§14.7.5.4).</summary>
    public NumberTreeReader? ParentTree => Root.TryGetValue(StructureNames.ParentTree, out CosObject? value) ? Document.GetNumberTreeReader(value) : null;

    /// <summary>Gets the reader of the root's <c>IDTree</c> name tree, or <see langword="null"/> when there is none (Table 354).</summary>
    public NameTreeReader? IdTree => Root.TryGetValue(StructureNames.IDTree, out CosObject? value) ? Document.GetNameTreeReader(value) : null;

    public CosObject Resolve(CosObject? value) => Document.Resolve(value);

    /// <summary>
    /// Reports <c>ParentTreeMissing</c> when the root has no <c>ParentTree</c> although an element has content items (Table 354:
    /// "Required if any structure element contains content items"). Lookups then fall back to the <c>K</c> walk.
    /// </summary>
    public void CheckParentTree(StructureWalk walk)
    {
        if (walk.HasContentItems && !Root.ContainsKey(StructureNames.ParentTree))
        {
            Report(DiagnosticCodes.ParentTreeMissing, "The structure tree root has no ParentTree although elements have content items (Table 354); content is mapped to elements through K.", RootReference);
        }
    }

    /// <summary>
    /// Finds, as a last-resort repair, the page of MCID <paramref name="mcid"/> of <paramref name="element"/>: the page whose
    /// <c>StructParents</c> parent-tree array lists the element at that MCID.
    /// </summary>
    public CosDictionary? PageFromParentTree(CosDictionary element, int mcid)
    {
        StructureIndexes indexes = Indexes;
        return indexes.ParentTreePages.GetOrCreate(
            0,
            (Context: this, Indexes: indexes),
            static (_, state) => new Created<Dictionary<(CosDictionary, int), CosDictionary>>(state.Context.Build(state.Indexes, static (context, stamps) => context.IndexParentTreePages(stamps))),
            static (_, _) => []).TryGetValue((element, mcid), out CosDictionary? page)
            ? page
            : null;
    }

    /// <summary>(element, MCID) to page, from every page's <c>StructParents</c> parent-tree array: read once, for the missing-<c>Pg</c> repair.</summary>
    private Dictionary<(CosDictionary, int), CosDictionary> IndexParentTreePages(StructureStamps stamps)
    {
        var pages = new Dictionary<(CosDictionary, int), CosDictionary>(ElementMcidComparer.Instance);
        stamps.Add(Root);
        if (ParentTree is not { } tree)
        {
            return pages;
        }

        StampParentTree(stamps);
        var pageByKey = new Dictionary<int, CosDictionary>();
        foreach (PdfPage page in Document.Pages)
        {
            stamps.Add(page.Dictionary);
            if (ViewReading.Int32(Document, page.Dictionary, StructureNames.StructParents) is { } key)
            {
                pageByKey.TryAdd(key, page.Dictionary);
            }
        }

        foreach ((int key, CosObject value) in tree.Enumerate())
        {
            stamps.Add(value);
            if (value is not CosArray array || !pageByKey.TryGetValue(key, out CosDictionary? pageObject))
            {
                continue;
            }

            int count = Math.Min(array.Count, MaxParentTreeArray);
            for (int mcid = 0; mcid < count; mcid++)
            {
                if (Resolve(array[mcid]) is CosDictionary listed)
                {
                    pages.TryAdd((listed, mcid), pageObject);
                }
            }
        }

        return pages;
    }

    public void Report(string code, string message, CosReference? reference) =>
        Diagnostics.Report(code, DiagnosticSeverity.Warning, message, offset: null, reference);

    /// <summary>Finds the page whose page object <paramref name="value"/> resolves to, or <see langword="null"/>.</summary>
    public PdfPage? FindPage(CosObject? value) =>
        Resolve(value) is CosDictionary pageObject && Document.Pages.TryGetPage(pageObject, out PdfPage? page) ? page : null;

    /// <summary>Gets the view of the namespace dictionary <paramref name="dictionary"/>, one per dictionary.</summary>
    public PdfStructureNamespace Namespace(CosDictionary dictionary, CosReference? reference) => _namespaces.GetOrCreate(
        dictionary,
        (Context: this, Reference: reference),
        static (key, state) => new Created<PdfStructureNamespace>(new PdfStructureNamespace(state.Context, key, state.Reference)),
        static (key, state) => new PdfStructureNamespace(state.Context, key, state.Reference));

    /// <summary>Whether the root's <c>Namespaces</c> array lists <paramref name="dictionary"/> (§14.8.6.2: an explicit namespace shall be listed).</summary>
    public bool IsDeclared(CosDictionary dictionary)
    {
        StructureIndexes indexes = Indexes;
        return indexes.DeclaredNamespaces.GetOrCreate(
            0,
            (Context: this, Indexes: indexes),
            static (_, state) => new Created<HashSet<CosDictionary>>(state.Context.Build(state.Indexes, static (context, stamps) => context.ReadDeclaredNamespaces(stamps))),
            static (_, _) => []).Contains(dictionary);
    }

    /// <summary>Resolves the standard type of (<paramref name="type"/>, <paramref name="ns"/>) through the role maps, once per pair and generation.</summary>
    public RoleResolution ResolveRole(CosName type, PdfStructureNamespace ns)
    {
        StructureIndexes indexes = Indexes;
        return indexes.Roles.GetOrCreate(
            new RoleKey(type, ns),
            (Context: this, Indexes: indexes),
            static (key, state) => new Created<RoleResolution>(state.Context.Build(state.Indexes, (context, stamps) => RoleResolver.Resolve(context, key.Type, key.Namespace, stamps))),
            static (key, state) => RoleResolver.Resolve(state.Context, key.Type, key.Namespace, new StructureStamps()));
    }

    /// <summary>Gets the MCID index of the content stream <paramref name="owner"/> (a page object or a form XObject), built once per generation.</summary>
    public MarkedContentIndex MarkedContent(CosObject owner)
    {
        StructureIndexes indexes = Indexes;
        return indexes.MarkedContent.GetOrCreate(
            owner,
            (Context: this, Indexes: indexes),
            static (key, state) => new Created<MarkedContentIndex>(state.Context.Build(state.Indexes, (context, stamps) => MarkedContentIndex.Build(context, key, stamps))),
            static (_, _) => MarkedContentIndex.Empty);
    }

    /// <summary>
    /// Records, once per generation, the nodes of the parent tree (each node dictionary, its <c>Kids</c> and <c>Nums</c> arrays):
    /// every MCID index depends on them. Silent: the number-tree reader reports damage when it reads the tree.
    /// </summary>
    public void StampParentTree(StructureStamps stamps)
    {
        StructureIndexes indexes = Indexes;
        _ = indexes.ParentTreeStamped.GetOrCreate(
            0,
            (Context: this, Indexes: indexes),
            static (_, state) => new Created<bool>(state.Context.Build(state.Indexes, static (context, own) => context.StampTreeNodes(own))),
            static (_, _) => false);
        stamps.Add(Root);
    }

    /// <summary>Runs an index builder, then records the containers it read in <paramref name="indexes"/>.</summary>
    private T Build<T>(StructureIndexes indexes, Func<StructureContext, StructureStamps, T> build)
    {
        var stamps = new StructureStamps();
        T value = build(this, stamps);
        indexes.Record(stamps);
        return value;
    }

    private bool StampTreeNodes(StructureStamps stamps)
    {
        stamps.Add(Root);
        if (!Root.TryGetValue(StructureNames.ParentTree, out CosObject? value))
        {
            return true;
        }

        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CosObject Node, int Depth)>();
        pending.Push((value, 0));
        while (pending.TryPop(out (CosObject Node, int Depth) next))
        {
            if (Resolve(next.Node) is not CosDictionary node || next.Depth > TreeReader<int>.DefaultMaxDepth || !visited.Add(node))
            {
                continue;
            }

            stamps.Add(node);
            if (node.TryGetValue(StructureNames.Nums, out CosObject? nums))
            {
                stamps.Add(Resolve(nums));
            }

            if (node.TryGetValue(KnownNames.Kids, out CosObject? kids) && Resolve(kids) is CosArray kidArray)
            {
                stamps.Add(kidArray);
                foreach (CosObject kid in kidArray)
                {
                    pending.Push((kid, next.Depth + 1));
                }
            }
        }

        return true;
    }

    private HashSet<CosDictionary> ReadDeclaredNamespaces(StructureStamps stamps)
    {
        var declared = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        stamps.Add(Root);
        if (Root.TryGetValue(StructureNames.Namespaces, out CosObject? value) && Resolve(value) is CosArray array)
        {
            stamps.Add(array);
            foreach (CosObject item in array)
            {
                if (Resolve(item) is CosDictionary dictionary)
                {
                    declared.Add(dictionary);
                }
            }
        }

        return declared;
    }

    /// <summary>A role-resolution cache key: a type name and the namespace it is in, the namespace compared by its dictionary.</summary>
    internal readonly record struct RoleKey(CosName Type, PdfStructureNamespace Namespace);

    internal sealed class RoleKeyComparer : IEqualityComparer<RoleKey>
    {
        public static readonly RoleKeyComparer Instance = new();

        public bool Equals(RoleKey x, RoleKey y) =>
            x.Type.Equals(y.Type) && (x.Namespace.Dictionary is null
                ? y.Namespace.Dictionary is null && x.Namespace.Kind == y.Namespace.Kind
                : ReferenceEquals(x.Namespace.Dictionary, y.Namespace.Dictionary));

        public int GetHashCode(RoleKey obj) => HashCode.Combine(
            obj.Type,
            obj.Namespace.Dictionary is { } dictionary ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(dictionary) : (int)obj.Namespace.Kind);
    }

    /// <summary>Compares (element, MCID) pairs by element identity and MCID.</summary>
    private sealed class ElementMcidComparer : IEqualityComparer<(CosDictionary Element, int Mcid)>
    {
        public static readonly ElementMcidComparer Instance = new();

        public bool Equals((CosDictionary Element, int Mcid) x, (CosDictionary Element, int Mcid) y) => ReferenceEquals(x.Element, y.Element) && x.Mcid == y.Mcid;

        public int GetHashCode((CosDictionary Element, int Mcid) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Element), obj.Mcid);
    }
}
