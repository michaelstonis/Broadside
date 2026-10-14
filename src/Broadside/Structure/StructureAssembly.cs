using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Writing;

namespace Broadside.Structure;

/// <summary>
/// Carries the structure of appended pages into the target's structure tree (issue #161): the part of
/// <see cref="PdfDocument.AppendPages(PdfDocument, Range)"/>, and so of merging and splitting, that keeps tagged pages tagged.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.2 (Tables 354 and 355), §14.7.3 (role maps), §14.7.4 (namespaces, Table 356), §14.7.5 (content items, the
/// parent tree, Tables 357-359) and §14.7.6.2 (class maps). Runs inside the <see cref="PdfObjectCopier"/> session that copies the
/// pages. <see cref="Plan"/> runs before any page is copied: it decides which elements are kept (those with a content item on a
/// copied page, and their ancestors; every element when every page is copied), gives each kept element an empty target object the
/// session names whenever it reaches the element (a go-to action's structure destination, say), and makes the elements not kept copy
/// as null. <see cref="Finish"/> runs once everything else is copied: it fills the kept elements, pruning the content items on pages
/// not copied, appends the top-level ones to the target's root, and rebuilds the parent tree for the copies from the elements' <c>K</c>
/// entries (as the reader decides ownership, §14.7.5.4) with keys the target hands out.
/// </para>
/// <para>
/// Names the target already uses differently are renamed in the copies, as destination and field names are (issue #160), and each
/// rename is recorded as an <see cref="DiagnosticSeverity.Information"/> diagnostic: a structure type its role map (or a namespace's
/// <c>RoleMapNS</c>) maps elsewhere (<c>StructureRoleConflict</c>), an attribute class its class map defines otherwise
/// (<c>StructureClassConflict</c>), and an element ID it has (<c>StructureIdConflict</c>; a table cell's <c>Headers</c> naming the
/// ID follow). The target's own objects are never renamed.
/// </para>
/// </remarks>
internal sealed class StructureAssembly
{
    /// <summary>Element entries <see cref="Fill"/> writes itself rather than copying as they are.</summary>
    private static readonly HashSet<CosName> OwnEntries =
        [StructureNames.P, StructureNames.K, StructureNames.Pg, StructureNames.S, StructureNames.C, StructureNames.ID, StructureNames.Ref];

    private static readonly CosName Headers = new("Headers");

    private readonly PdfDocument _source;
    private readonly PdfDocument _target;
    private readonly PdfObjectCopier _copier;
    private readonly int _first;
    private readonly int _count;
    private readonly bool _whole;
    private readonly CosReference[] _pageCopies;
    private readonly Dictionary<CosDictionary, int> _pages = new(ReferenceEqualityComparer.Instance);
    private readonly List<Node> _nodes = [];
    private readonly Dictionary<CosDictionary, Node> _kept = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CosReference, CosReference> _elementCopies = [];
    private readonly List<(PdfStructureNamespace Source, PdfStructureNamespace Target)> _namespaces = [];
    private readonly Dictionary<CosDictionary, SortedDictionary<int, CosReference>> _contentOwners = new(ReferenceEqualityComparer.Instance);
    private readonly List<CosDictionary> _contentOrder = [];
    private readonly List<(CosDictionary Owner, CosReference Element)> _objectOwners = [];
    private readonly List<Conflict> _conflicts = [];
    private readonly Dictionary<(string Code, string Renamed), int> _unplaced = [];
    private PdfStructureTreeRoot? _sourceRoot;
    private PdfStructureTreeRoot? _targetRoot;

    public StructureAssembly(PdfDocument source, PdfDocument target, PdfObjectCopier copier, int first, int count, bool whole, CosReference[] pageCopies)
    {
        _source = source;
        _target = target;
        _copier = copier;
        _first = first;
        _count = count;
        _whole = whole;
        _pageCopies = pageCopies;
    }

    /// <summary>
    /// Decides which elements are kept and makes the copy session name their copies (and null for the others); creates the target's
    /// structure tree when an element is kept and it has none. Runs before any page is copied.
    /// </summary>
    public void Plan()
    {
        if (_source.StructureTree is not { } root)
        {
            return;
        }

        _sourceRoot = root;
        for (int index = 0; index < _count; index++)
        {
            _pages[_source.Pages[_first + index].Dictionary] = index;
        }

        // Children after their parents in the walk: from the end, an element is kept when a content item or a child is.
        IReadOnlyList<PdfStructureElement> elements = root.Elements;
        var items = new IReadOnlyList<PdfStructureItem>[elements.Count];
        var keep = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        for (int index = elements.Count - 1; index >= 0; index--)
        {
            items[index] = elements[index].Children;
            if (_whole || items[index].Any(item => item is PdfStructureElement child ? keep.Contains(child.Dictionary) : Keeps(item)))
            {
                keep.Add(elements[index].Dictionary);
            }
        }

        if (root.Reference is { } rootReference)
        {
            _copier.Exclude(rootReference);
        }

        for (int index = 0; index < elements.Count; index++)
        {
            PdfStructureElement element = elements[index];
            if (!keep.Contains(element.Dictionary))
            {
                if (element.Reference is { } excluded)
                {
                    _copier.Exclude(excluded);
                }

                continue;
            }

            if (_kept.ContainsKey(element.Dictionary))
            {
                continue;
            }

            var copy = new CosDictionary();
            CosReference reference = _target.AddObject(copy);
            if (element.Reference is { } sourceReference)
            {
                _copier.Assign(sourceReference, reference);
                _elementCopies[sourceReference] = reference;
            }

            var node = new Node(element, items[index], copy, reference);
            _nodes.Add(node);
            _kept.Add(element.Dictionary, node);
        }

        if (_nodes.Count == 0)
        {
            return;
        }

        _targetRoot = _target.StructureTree ?? CreateTargetTree();
        MapNamespaces(root);
    }

    /// <summary>The target copy of the source structure element <paramref name="value"/> refers to, or null when it is not kept.</summary>
    /// <remarks>ISO 32000-2 Table 151 (<c>SE</c>): an outline item's structure element.</remarks>
    public CosReference? MapElement(CosObject value) =>
        value is CosReference reference && _elementCopies.TryGetValue(reference, out CosReference? copy) ? copy : null;

    /// <summary>
    /// Fills the kept elements, attaches them to the target's root, merges the role, class and namespace maps, rebuilds the parent
    /// tree for the copies and adds their IDs. Runs once everything else is copied.
    /// </summary>
    public void Finish()
    {
        StripStructParents();
        if (_targetRoot is null || _sourceRoot is null)
        {
            return;
        }

        StructureContext context = _targetRoot.Context;
        CosReference rootReference = StructureWriter.RootReference(context);
        Dictionary<string, CosName> roles = MergeRoleMap();
        var namespaceRoles = new Dictionary<CosDictionary, Dictionary<string, CosName>>(ReferenceEqualityComparer.Instance);
        MergeNamespaces(namespaceRoles);
        Dictionary<string, CosName> classes = MergeClassMap();
        Dictionary<string, CosString> ids = PlanIds();

        foreach (Node node in _nodes)
        {
            Fill(node, roles, namespaceRoles, classes, ids);
        }

        foreach (Node node in _nodes)
        {
            node.Copy[StructureNames.P] = node.Parent?.Reference ?? rootReference;
            if (node.Parent is null)
            {
                StructureWriter.AppendTo(context, _targetRoot.Dictionary, StructureNames.K, node.Reference);
            }
        }

        WriteParentTree(context);
        WriteIds();
        if (_source.MarkInfo.UserProperties)
        {
            _target.MarkInfo.UserProperties = true;
        }

        if (_source.MarkInfo.Suspects)
        {
            _target.MarkInfo.Suspects = true;
        }

        foreach (Conflict conflict in _conflicts)
        {
            _target.DiagnosticSink.Report(conflict.Code, DiagnosticSeverity.Information, conflict.Message, objectReference: conflict.Where ?? rootReference);
        }
    }

    /// <summary>Whether a content item is on a copied page (a form's or an object's whose page is unknown only when every page is copied).</summary>
    private bool Keeps(PdfStructureItem item) => item switch
    {
        PdfMarkedContentReference content => content.PageObject is { } page ? _pages.ContainsKey(page) : _whole && content.ContentStream is not null,
        PdfObjectReference reference => reference.Page is { } page ? _pages.ContainsKey(page.Dictionary) : _whole,
        _ => false,
    };

    /// <summary>The copy of the source page <paramref name="page"/>, or null when it is not copied.</summary>
    private CosReference? PageCopy(CosDictionary? page) => page is not null && _pages.TryGetValue(page, out int index) ? _pageCopies[index] : null;

    /// <summary>
    /// A new structure tree for the target whose parent-tree keys start past every <c>StructParents</c> and <c>StructParent</c> its
    /// pages and their annotations hold, so content the target had marked before it had a tree is not taken for the copies'.
    /// </summary>
    private PdfStructureTreeRoot CreateTargetTree()
    {
        long next = 0;
        foreach (PdfPage page in _target.Pages)
        {
            next = Math.Max(next, KeyAfter(page.Dictionary, StructureNames.StructParents));
            if (_target.Resolve(page.Dictionary.GetValueOrDefault(Annotations.AnnotationNames.Annots)) is CosArray annots)
            {
                foreach (CosObject annotation in annots)
                {
                    if (_target.Resolve(annotation) is CosDictionary dictionary)
                    {
                        next = Math.Max(next, KeyAfter(dictionary, StructureNames.StructParent));
                    }
                }
            }
        }

        PdfStructureTreeRoot root = _target.CreateStructureTree();
        if (next > 0)
        {
            root.Dictionary[StructureNames.ParentTreeNextKey] = new CosInteger(next);
        }

        return root;

        long KeyAfter(CosDictionary owner, CosName key) => ViewReading.Int32(_target, owner, key) is { } value and >= 0 ? value + 1L : 0;
    }

    /// <summary>Makes the session name, for each namespace of the source, the target's namespace of the same name (added when it has none).</summary>
    private void MapNamespaces(PdfStructureTreeRoot root)
    {
        var mapped = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfStructureNamespace ns in root.Namespaces.Concat(_nodes.Select(node => node.Source.Namespace)))
        {
            if (ns.Dictionary is not { } dictionary || ns.Reference is not { } reference || ns.Name.Length == 0 || !mapped.Add(dictionary))
            {
                continue;
            }

            PdfStructureNamespace target = _targetRoot!.GetOrAddNamespace(ns.Name);
            _copier.Assign(reference, target.Reference!);
            _namespaces.Add((ns, target));
        }
    }

    // ----- Role, namespace and class maps (§14.7.3, §14.7.4.2, §14.7.6.2) -----

    /// <summary>Adds the source's role map entries to the target's; returns the source types renamed because the target maps them otherwise.</summary>
    private Dictionary<string, CosName> MergeRoleMap()
    {
        var renames = new Dictionary<string, CosName>(StringComparer.Ordinal);
        if (_sourceRoot!.RoleMap is { } source)
        {
            if (_targetRoot!.RoleMap is not { } target)
            {
                target = new CosDictionary();
                _targetRoot.Dictionary[StructureNames.RoleMap] = target;
            }

            MergeRoles(source, target, renames, "the role map");
        }

        return renames;
    }

    /// <summary>Carries each source namespace's role map and other entries into the target namespace of the same name.</summary>
    private void MergeNamespaces(Dictionary<CosDictionary, Dictionary<string, CosName>> renames)
    {
        foreach ((PdfStructureNamespace source, PdfStructureNamespace target) in _namespaces)
        {
            CosDictionary into = target.Dictionary!;
            foreach ((CosName key, CosObject value) in source.Dictionary!)
            {
                if (key.Equals(KnownNames.Type) || key.Equals(StructureNames.NS))
                {
                    continue;
                }

                if (key.Equals(StructureNames.RoleMapNS))
                {
                    if (_source.Resolve(value) is not CosDictionary map)
                    {
                        continue;
                    }

                    if (_target.Resolve(into.GetValueOrDefault(key)) is not CosDictionary targetMap)
                    {
                        targetMap = new CosDictionary();
                        into[key] = targetMap;
                    }

                    if (!renames.TryGetValue(into, out Dictionary<string, CosName>? named))
                    {
                        named = new Dictionary<string, CosName>(StringComparer.Ordinal);
                        renames.Add(into, named);
                    }

                    MergeRoles(map, targetMap, named, $"the role map of the namespace {source.Name}");
                }
                else if (!into.ContainsKey(key) && _copier.Copy(value) is { } copied and not CosNull)
                {
                    into[key] = copied;
                }
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="source"/>'s role mappings to <paramref name="target"/>. A type the target maps to something else is renamed
    /// (<c>_1</c>, <c>_2</c>, …) in <paramref name="renames"/>, and so is a type mapped to a renamed one that the target maps too.
    /// </summary>
    private void MergeRoles(CosDictionary source, CosDictionary target, Dictionary<string, CosName> renames, string where)
    {
        var copies = new List<(CosName Key, CosObject Value)>();
        foreach ((CosName key, CosObject value) in source)
        {
            if (_copier.Copy(value) is { } copied and not CosNull)
            {
                copies.Add((key, _target.Resolve(copied)));
            }
        }

        var conflicting = new HashSet<string>(StringComparer.Ordinal);
        foreach ((CosName key, CosObject value) in copies)
        {
            if (target.TryGetValue(key, out CosObject? existing) && !Same(_target.Resolve(existing), value))
            {
                conflicting.Add(key.Value);
            }
        }

        // A type mapped to a renamed type would map to the renamed one: when the target maps the type too, that is a conflict as well.
        for (bool changed = conflicting.Count > 0; changed;)
        {
            changed = false;
            foreach ((CosName key, CosObject value) in copies)
            {
                if (value is CosName mapped && conflicting.Contains(mapped.Value) && target.ContainsKey(key) && conflicting.Add(key.Value))
                {
                    changed = true;
                }
            }
        }

        var taken = new HashSet<string>(target.Keys.Select(key => key.Value), StringComparer.Ordinal);
        taken.UnionWith(copies.Select(entry => entry.Key.Value));
        foreach ((CosName key, CosObject value) in copies)
        {
            if (conflicting.Contains(key.Value))
            {
                CosName renamed = new(FreeName(key.Value, taken));
                renames[key.Value] = renamed;
                AddConflict(new Conflict(
                    DiagnosticCodes.StructureRoleConflict,
                    $"The structure type /{key.Value} is mapped to {Describe(_target.Resolve(target[key]))} in {where} of the document and to {Describe(value)} in that of the appended pages (§14.7.3); the appended elements use /{renamed.Value} instead.",
                    renamed.Value));
            }
        }

        foreach ((CosName key, CosObject value) in copies)
        {
            CosName name = renames.GetValueOrDefault(key.Value) ?? key;
            if (!target.ContainsKey(name))
            {
                target[name] = value is CosName mapped && renames.TryGetValue(mapped.Value, out CosName? renamed) ? renamed : value;
            }
        }
    }

    /// <summary>Adds the source's attribute classes to the target's class map; returns the classes renamed because the target defines them otherwise.</summary>
    private Dictionary<string, CosName> MergeClassMap()
    {
        var renames = new Dictionary<string, CosName>(StringComparer.Ordinal);
        if (_sourceRoot!.ClassMap is not { } source)
        {
            return renames;
        }

        if (_targetRoot!.ClassMap is not { } target)
        {
            target = new CosDictionary();
            _targetRoot.Dictionary[StructureNames.ClassMap] = target;
        }

        var taken = new HashSet<string>(target.Keys.Select(key => key.Value), StringComparer.Ordinal);
        taken.UnionWith(source.Keys.Select(key => key.Value));
        foreach ((CosName key, CosObject value) in source)
        {
            if (!target.TryGetValue(key, out CosObject? existing))
            {
                if (_copier.Copy(value) is { } copied and not CosNull)
                {
                    target[key] = copied;
                }

                continue;
            }

            if (Equivalent(_source.Resolve(value), _target.Resolve(existing), depth: 0))
            {
                continue;
            }

            CosName renamed = new(FreeName(key.Value, taken));
            renames[key.Value] = renamed;
            target[renamed] = _copier.Copy(value);
            AddConflict(new Conflict(
                DiagnosticCodes.StructureClassConflict,
                $"The attribute class /{key.Value} has other attributes in the class map of the document than in that of the appended pages (§14.7.6.2); the appended elements use /{renamed.Value} instead.",
                renamed.Value));
        }

        return renames;
    }

    /// <summary>The source IDs of kept elements that the target already has, each with a free name (Table 355: IDs are unique).</summary>
    private Dictionary<string, CosString> PlanIds()
    {
        var renames = new Dictionary<string, CosString>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (_targetRoot!.IdTree is { } tree)
        {
            foreach (KeyValuePair<CosString, CosObject> entry in tree)
            {
                used.Add(Key(entry.Key.Bytes));
            }
        }

        foreach (PdfStructureElement element in _targetRoot.Elements)
        {
            if (element.Id is { } id)
            {
                used.Add(Key(id.Bytes));
            }
        }

        var taken = new HashSet<string>(used, StringComparer.Ordinal);
        var sourceIds = new List<CosString>();
        foreach (Node node in _nodes)
        {
            if (_source.Resolve(node.Source.Dictionary.GetValueOrDefault(StructureNames.ID)) is CosString id)
            {
                sourceIds.Add(id);
                taken.Add(Key(id.Bytes));
            }
        }

        foreach (CosString id in sourceIds)
        {
            string key = Key(id.Bytes);
            if (used.Contains(key) && !renames.ContainsKey(key))
            {
                byte[] renamed = FreeBytes(id.Bytes, taken);
                renames[key] = new CosString(renamed);
                AddConflict(new Conflict(
                    DiagnosticCodes.StructureIdConflict,
                    $"The document already has a structure element with the ID ({key}) (Table 355: IDs are unique); the appended element's is ({Key(renamed)}).",
                    Key(renamed)));
            }
        }

        return renames;
    }

    // ----- Elements (§14.7.2, §14.7.5) -----

    /// <summary>Writes a kept element's copy: its entries, with renames applied, and its kept kids.</summary>
    private void Fill(
        Node node,
        Dictionary<string, CosName> roles,
        Dictionary<CosDictionary, Dictionary<string, CosName>> namespaceRoles,
        Dictionary<string, CosName> classes,
        Dictionary<string, CosString> ids)
    {
        CosDictionary source = node.Source.Dictionary;
        CosDictionary copy = node.Copy;
        foreach ((CosName key, CosObject value) in source)
        {
            if (!OwnEntries.Contains(key) && _copier.Copy(value) is { } copied and not CosNull)
            {
                copy[key] = copied;
            }
        }

        CarriedFeatures.InDictionary(_source, source, isStream: false, _target.RequireFeature);

        // S, renamed when its namespace's (or the default) role map in the target maps it otherwise.
        if (_source.Resolve(source.GetValueOrDefault(StructureNames.S)) is CosName type)
        {
            Dictionary<string, CosName>? renames = _target.Resolve(copy.GetValueOrDefault(StructureNames.NS)) is CosDictionary ns
                ? namespaceRoles.GetValueOrDefault(ns)
                : roles;
            CosName name = renames?.GetValueOrDefault(type.Value) ?? type;
            copy[StructureNames.S] = name;
            if (!ReferenceEquals(name, type))
            {
                Where(DiagnosticCodes.StructureRoleConflict, name.Value, node.Reference);
            }
        }

        if (PageCopy(_source.Resolve(source.GetValueOrDefault(StructureNames.Pg)) as CosDictionary) is { } page)
        {
            copy[StructureNames.Pg] = page;
        }

        if (source.TryGetValue(StructureNames.C, out CosObject? classNames) && _copier.Copy(classNames) is { } copiedClasses and not CosNull)
        {
            copy[StructureNames.C] = RenameClasses(copiedClasses, classes, node.Reference);
        }

        if (_source.Resolve(source.GetValueOrDefault(StructureNames.ID)) is CosString id)
        {
            copy[StructureNames.ID] = ids.GetValueOrDefault(Key(id.Bytes)) ?? id;
            Where(DiagnosticCodes.StructureIdConflict, Key(((CosString)copy[StructureNames.ID]).Bytes), node.Reference);
        }

        if (source.TryGetValue(StructureNames.Ref, out CosObject? references) && _copier.Copy(references) is { } copiedReferences)
        {
            if (_target.Resolve(copiedReferences) is CosArray array)
            {
                for (int index = array.Count - 1; index >= 0; index--)
                {
                    if (_target.Resolve(array[index]) is CosNull)
                    {
                        array.RemoveAt(index);
                    }
                }

                if (array.Count > 0)
                {
                    copy[StructureNames.Ref] = copiedReferences;
                }
            }
        }

        if (ids.Count > 0 && copy.TryGetValue(StructureNames.A, out CosObject? attributes))
        {
            RenameHeaders(attributes, ids);
        }

        var kids = new CosArray();
        bool single = _source.Resolve(source.GetValueOrDefault(StructureNames.K)) is not CosArray;
        foreach (PdfStructureItem item in node.Items)
        {
            switch (item)
            {
                case PdfStructureElement child when _kept.TryGetValue(child.Dictionary, out Node? kid):
                    kid.Parent ??= node;
                    kids.Add(kid.Reference);
                    break;
                case PdfMarkedContentReference content when Keeps(content):
                    if (MarkedContent(node, content) is { } entry)
                    {
                        kids.Add(entry);
                    }

                    break;
                case PdfObjectReference reference when Keeps(reference):
                    if (ObjectReference(node, reference) is { } objr)
                    {
                        kids.Add(objr);
                    }

                    break;
            }
        }

        if (kids.Count == 1 && single)
        {
            copy[StructureNames.K] = kids[0];
        }
        else if (kids.Count > 0)
        {
            copy[StructureNames.K] = kids;
        }
    }

    /// <summary>The copy of a marked-content item (an MCID, or an MCR when the item was one or its page is not the element's), recorded for the parent tree.</summary>
    private CosObject? MarkedContent(Node node, PdfMarkedContentReference content)
    {
        CosReference? page = PageCopy(content.PageObject);
        if (content.ContentStream is null)
        {
            if (page is null || _target.Resolve(page) is not CosDictionary pageObject)
            {
                return null;
            }

            Record(pageObject, content.Mcid, node.Reference);
            if (content.Dictionary is null && page.Equals(node.Copy.GetValueOrDefault(StructureNames.Pg)))
            {
                return new CosInteger(content.Mcid);
            }

            return new CosDictionary
            {
                [KnownNames.Type] = StructureNames.MCR,
                [StructureNames.Pg] = page,
                [StructureNames.MCID] = new CosInteger(content.Mcid),
            };
        }

        // A form XObject's content: Stm shall be an indirect reference (Table 357).
        if (content.Dictionary!.GetValueOrDefault(StructureNames.Stm) is not CosReference stream
            || _copier.Copy(stream) is not CosReference streamCopy
            || _target.Resolve(streamCopy) is not CosStream form)
        {
            return null;
        }

        Record(form.Dictionary, content.Mcid, node.Reference);
        var mcr = new CosDictionary
        {
            [KnownNames.Type] = StructureNames.MCR,
            [StructureNames.MCID] = new CosInteger(content.Mcid),
            [StructureNames.Stm] = streamCopy,
        };
        if (page is not null)
        {
            mcr[StructureNames.Pg] = page;
        }

        if (content.Dictionary!.TryGetValue(StructureNames.StmOwn, out CosObject? owner) && _copier.Copy(owner) is { } ownerCopy and not CosNull)
        {
            mcr[StructureNames.StmOwn] = ownerCopy;
        }

        return mcr;
    }

    /// <summary>The copy of an object content item (an annotation or XObject the session copied), recorded for the parent tree.</summary>
    private CosDictionary? ObjectReference(Node node, PdfObjectReference reference)
    {
        if (reference.ObjectReference is not { } target)
        {
            return null;
        }

        CosReference? copied = reference.ReferencedObject is CosDictionary dictionary && ViewReading.HasType(_source, dictionary, KnownNames.Page)
            ? PageCopy(dictionary)
            : _copier.Copy(target) as CosReference;
        CosDictionary? owner = _target.Resolve(copied) switch
        {
            CosDictionary value => value,
            CosStream stream => stream.Dictionary,
            _ => null,
        };
        if (copied is null || owner is null)
        {
            return null;
        }

        _objectOwners.Add((owner, node.Reference));
        var objr = new CosDictionary
        {
            [KnownNames.Type] = StructureNames.OBJR,
            [StructureNames.Obj] = copied,
        };
        if (PageCopy(reference.Page?.Dictionary) is { } page)
        {
            objr[StructureNames.Pg] = page;
        }

        return objr;
    }

    private void Record(CosDictionary owner, int mcid, CosReference element)
    {
        if (!_contentOwners.TryGetValue(owner, out SortedDictionary<int, CosReference>? items))
        {
            items = [];
            _contentOwners.Add(owner, items);
            _contentOrder.Add(owner);
        }

        items.TryAdd(mcid, element);
    }

    // ----- Parent tree and ID tree (§14.7.5.4, Table 354) -----

    /// <summary>
    /// Removes the <c>StructParents</c> and <c>StructParent</c> entries of everything the session copied: their keys name the source's
    /// parent tree, not the target's. <see cref="WriteParentTree"/> gives the copies that are content of kept elements new keys.
    /// </summary>
    private void StripStructParents()
    {
        foreach (CosReference reference in _copier.Copies)
        {
            Strip(_target.Resolve(reference));
        }

        foreach (CosReference page in _pageCopies)
        {
            if (_target.Resolve(page) is CosDictionary pageObject && _target.Resolve(pageObject.GetValueOrDefault(Annotations.AnnotationNames.Annots)) is CosArray annots)
            {
                foreach (CosObject annotation in annots)
                {
                    Strip(annotation);
                }
            }
        }

        static void Strip(CosObject value)
        {
            CosDictionary? dictionary = value as CosDictionary ?? (value as CosStream)?.Dictionary;
            if (dictionary is not null)
            {
                dictionary.Remove(StructureNames.StructParents);
                dictionary.Remove(StructureNames.StructParent);
            }
        }
    }

    /// <summary>Gives each copied content stream and object content item a new key in the target's parent tree, listing its elements.</summary>
    private void WriteParentTree(StructureContext context)
    {
        if (_contentOrder.Count == 0 && _objectOwners.Count == 0)
        {
            return;
        }

        PdfNumberTree tree = StructureWriter.ParentTree(context);
        foreach (CosDictionary owner in _contentOrder)
        {
            SortedDictionary<int, CosReference> items = _contentOwners[owner];
            int key = StructureWriter.NextKey(context, tree);
            owner[StructureNames.StructParents] = new CosInteger(key);
            var parents = new CosArray();
            foreach ((int mcid, CosReference element) in items)
            {
                while (parents.Count < mcid)
                {
                    parents.Add(CosNull.Instance);
                }

                parents.Add(element);
            }

            tree.Set(key, _target.AddObject(parents));
        }

        foreach ((CosDictionary owner, CosReference element) in _objectOwners)
        {
            if (owner.ContainsKey(StructureNames.StructParent))
            {
                continue;
            }

            int key = StructureWriter.NextKey(context, tree);
            owner[StructureNames.StructParent] = new CosInteger(key);
            tree.Set(key, element);
        }
    }

    /// <summary>Adds the copies' IDs to the target's ID tree, created when it has none.</summary>
    private void WriteIds()
    {
        var entries = new List<KeyValuePair<CosString, CosObject>>();
        foreach (Node node in _nodes)
        {
            if (node.Copy.GetValueOrDefault(StructureNames.ID) is CosString id)
            {
                entries.Add(KeyValuePair.Create(id, (CosObject)node.Reference));
            }
        }

        if (entries.Count == 0)
        {
            return;
        }

        if (_targetRoot!.IdTree is { } tree)
        {
            foreach (KeyValuePair<CosString, CosObject> entry in entries)
            {
                tree.Set(entry.Key, entry.Value);
            }
        }
        else
        {
            var unique = new Dictionary<string, KeyValuePair<CosString, CosObject>>(StringComparer.Ordinal);
            foreach (KeyValuePair<CosString, CosObject> entry in entries)
            {
                unique.TryAdd(Key(entry.Key.Bytes), entry);
            }

            _targetRoot.Dictionary[StructureNames.IDTree] = _target.CreateNameTree(unique.Values).RootReference!;
        }
    }

    // ----- Renames -----

    /// <summary>The class names of a copied <c>C</c> entry (a name or an array of names and revision numbers) with renamed classes replaced.</summary>
    private CosObject RenameClasses(CosObject copied, Dictionary<string, CosName> renames, CosReference element)
    {
        if (renames.Count == 0)
        {
            return copied;
        }

        switch (_target.Resolve(copied))
        {
            case CosName name when renames.TryGetValue(name.Value, out CosName? renamed):
                Where(DiagnosticCodes.StructureClassConflict, renamed.Value, element);
                return renamed;
            case CosArray array:
                for (int index = 0; index < array.Count; index++)
                {
                    if (_target.Resolve(array[index]) is CosName entry && renames.TryGetValue(entry.Value, out CosName? renamedEntry))
                    {
                        array[index] = renamedEntry;
                        Where(DiagnosticCodes.StructureClassConflict, renamedEntry.Value, element);
                    }
                }

                break;
        }

        return copied;
    }

    /// <summary>Renames the IDs a copied table attribute object's <c>Headers</c> names (Table 384).</summary>
    private void RenameHeaders(CosObject attributes, Dictionary<string, CosString> renames)
    {
        CosObject resolved = _target.Resolve(attributes);
        IEnumerable<CosObject> objects = resolved is CosArray array ? array : [resolved];
        foreach (CosObject item in objects)
        {
            if (_target.Resolve(item) is not CosDictionary attribute
                || !StructureNames.Table.Equals(_target.Resolve(attribute.GetValueOrDefault(StructureNames.O)))
                || _target.Resolve(attribute.GetValueOrDefault(Headers)) is not CosArray headers)
            {
                continue;
            }

            for (int index = 0; index < headers.Count; index++)
            {
                if (_target.Resolve(headers[index]) is CosString id && renames.TryGetValue(Key(id.Bytes), out CosString? renamed))
                {
                    headers[index] = renamed;
                }
            }
        }
    }

    /// <summary>Records the first copied element that uses a renamed name, for the rename's diagnostic.</summary>
    private void Where(string code, string renamed, CosReference element)
    {
        if (_unplaced.Remove((code, renamed), out int index))
        {
            _conflicts[index] = _conflicts[index] with { Where = element };
        }
    }

    private void AddConflict(Conflict conflict)
    {
        _unplaced[(conflict.Code, conflict.Renamed)] = _conflicts.Count;
        _conflicts.Add(conflict);
    }

    private static string FreeName(string name, HashSet<string> taken)
    {
        for (int number = 1; ; number++)
        {
            string candidate = name + "_" + number.ToString(CultureInfo.InvariantCulture);
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static byte[] FreeBytes(ReadOnlySpan<byte> name, HashSet<string> taken)
    {
        for (int number = 1; ; number++)
        {
            byte[] candidate = [.. name, .. Encoding.ASCII.GetBytes("_" + number.ToString(CultureInfo.InvariantCulture))];
            if (taken.Add(Key(candidate)))
            {
                return candidate;
            }
        }
    }

    /// <summary>A byte string as a dictionary key (one character per byte).</summary>
    private static string Key(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Whether two role map values of the target are the same: equal names, the same references, arrays of such.</summary>
    private static bool Same(CosObject left, CosObject right) => (left, right) switch
    {
        (CosArray a, CosArray b) => a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second)),
        _ => left.Equals(right),
    };

    /// <summary>Whether a source value and a target value hold the same data, resolving references in each document (bounded depth).</summary>
    private bool Equivalent(CosObject source, CosObject target, int depth)
    {
        if (depth > 16)
        {
            return false;
        }

        source = _source.Resolve(source);
        target = _target.Resolve(target);
        switch (source, target)
        {
            case (CosArray a, CosArray b):
                if (a.Count != b.Count)
                {
                    return false;
                }

                for (int index = 0; index < a.Count; index++)
                {
                    if (!Equivalent(a[index], b[index], depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            case (CosDictionary a, CosDictionary b):
                if (a.Count != b.Count)
                {
                    return false;
                }

                foreach ((CosName key, CosObject value) in a)
                {
                    if (!b.TryGetValue(key, out CosObject? other) || !Equivalent(value, other, depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            case (CosStream, _) or (_, CosStream):
                return false;
            default:
                return source.Equals(target);
        }
    }

    private static string Describe(CosObject value) => value switch
    {
        CosName name => "/" + name.Value,
        CosArray { Count: > 0 } array when array[0] is CosName name => "/" + name.Value + " of another namespace",
        _ => "another value",
    };

    /// <summary>A kept element: its source view, its kids as read, and its copy.</summary>
    private sealed class Node(PdfStructureElement source, IReadOnlyList<PdfStructureItem> items, CosDictionary copy, CosReference reference)
    {
        public PdfStructureElement Source { get; } = source;

        public IReadOnlyList<PdfStructureItem> Items { get; } = items;

        public CosDictionary Copy { get; } = copy;

        public CosReference Reference { get; } = reference;

        /// <summary>Gets or sets the kept element whose copy lists this one's first; null for a top-level copy.</summary>
        public Node? Parent { get; set; }
    }

    /// <summary>A rename to report once the copies are written: where is the first copied element using the new name.</summary>
    private sealed record Conflict(string Code, string Message, string Renamed)
    {
        public CosReference? Where { get; init; }
    }
}
