using Broadside.Caching;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>
/// The state one document's structure tree views share: the document (to resolve objects and find pages), its diagnostics, and the
/// derived indexes, each built once on first use through <see cref="OnceCache{TKey, TValue}"/> and then read without locks.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7. The indexes are snapshots of the tree as it was when first needed: they are rebuilt when the catalog's
/// <c>StructTreeRoot</c> is replaced (<see cref="PdfDocument.StructureTree"/> creates a new context), not when an element inside it
/// is edited. Reading never changes a COS object; every repair lives in the views.
/// </remarks>
internal sealed class StructureContext
{
    /// <summary>How deep the structure hierarchy may nest before the walk stops descending (a cap on untrusted input).</summary>
    public const int MaxDepth = 256;

    /// <summary>How many role-map steps resolving one type may take; maps may form cycles, which also end the chain.</summary>
    public const int MaxRoleSteps = 32;

    /// <summary>The longest parent-tree array read for one content stream; longer arrays are truncated (MCIDs are small in practice).</summary>
    public const int MaxParentTreeArray = 1_000_000;

    private readonly OnceCache<int, StructureWalk> _walk = new();
    private readonly OnceCache<RoleKey, RoleResolution> _roles = new(RoleKeyComparer.Instance);
    private readonly OnceCache<CosDictionary, PdfStructureNamespace> _namespaces = new(ReferenceEqualityComparer.Instance);
    private readonly OnceCache<int, HashSet<CosDictionary>> _declaredNamespaces = new();
    private readonly OnceCache<CosObject, MarkedContentIndex> _markedContent = new(ReferenceEqualityComparer.Instance);
    private readonly OnceCache<int, Dictionary<(CosDictionary, int), CosDictionary>> _parentTreePages = new();

    public StructureContext(PdfDocument document, CosDictionary root, CosReference? rootReference)
    {
        Document = document;
        Root = root;
        RootReference = rootReference;
    }

    public PdfDocument Document { get; }

    public CosDictionary Root { get; }

    public CosReference? RootReference { get; }

    public DiagnosticSink Diagnostics => Document.DiagnosticSink;

    /// <summary>Gets a value indicating whether the document claims to be a tagged PDF (<c>MarkInfo</c> <c>Marked</c> true, §14.8.1).</summary>
    public bool IsTagged => Document.MarkInfo.Marked;

    /// <summary>Gets the document-order walk of the hierarchy from the root's <c>K</c>, built once.</summary>
    public StructureWalk Walk => _walk.GetOrCreate(
        0,
        this,
        static (_, context) => new Created<StructureWalk>(StructureWalk.Build(context)),
        static (_, _) => StructureWalk.Empty);

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
    public CosDictionary? PageFromParentTree(CosDictionary element, int mcid) =>
        _parentTreePages.GetOrCreate(
            0,
            this,
            static (_, context) => new Created<Dictionary<(CosDictionary, int), CosDictionary>>(context.IndexParentTreePages()),
            static (_, _) => []).TryGetValue((element, mcid), out CosDictionary? page)
            ? page
            : null;

    /// <summary>(element, MCID) to page, from every page's <c>StructParents</c> parent-tree array: read once, for the missing-<c>Pg</c> repair.</summary>
    private Dictionary<(CosDictionary, int), CosDictionary> IndexParentTreePages()
    {
        var pages = new Dictionary<(CosDictionary, int), CosDictionary>(ElementMcidComparer.Instance);
        if (ParentTree is not { } tree)
        {
            return pages;
        }

        var pageByKey = new Dictionary<int, CosDictionary>();
        foreach (PdfPage page in Document.Pages)
        {
            if (ViewReading.Int32(Document, page.Dictionary, StructureNames.StructParents) is { } key)
            {
                pageByKey.TryAdd(key, page.Dictionary);
            }
        }

        foreach ((int key, CosObject value) in tree.Enumerate())
        {
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
    public bool IsDeclared(CosDictionary dictionary) => _declaredNamespaces.GetOrCreate(
        0,
        this,
        static (_, context) => new Created<HashSet<CosDictionary>>(context.ReadDeclaredNamespaces()),
        static (_, _) => []).Contains(dictionary);

    /// <summary>Resolves the standard type of (<paramref name="type"/>, <paramref name="ns"/>) through the role maps, once per pair.</summary>
    public RoleResolution ResolveRole(CosName type, PdfStructureNamespace ns) => _roles.GetOrCreate(
        new RoleKey(type, ns),
        this,
        static (key, context) => new Created<RoleResolution>(RoleResolver.Resolve(context, key.Type, key.Namespace)),
        static (key, context) => RoleResolver.Resolve(context, key.Type, key.Namespace));

    /// <summary>Gets the MCID index of the content stream <paramref name="owner"/> (a page object or a form XObject), built once.</summary>
    public MarkedContentIndex MarkedContent(CosObject owner) => _markedContent.GetOrCreate(
        owner,
        this,
        static (key, context) => new Created<MarkedContentIndex>(MarkedContentIndex.Build(context, key)),
        static (_, _) => MarkedContentIndex.Empty);

    private HashSet<CosDictionary> ReadDeclaredNamespaces()
    {
        var declared = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        if (Root.TryGetValue(StructureNames.Namespaces, out CosObject? value) && Resolve(value) is CosArray array)
        {
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

    private sealed class RoleKeyComparer : IEqualityComparer<RoleKey>
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
