using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>
/// The edits behind the structure tree's write API (issue #156): new elements, content items linked through the parent tree,
/// namespaces, role and class maps, attributes and the ID tree. Every new indirect object is made through
/// <see cref="PdfDocument.AddObject"/>; the parent tree and ID tree are edited through <see cref="PdfNumberTree"/> and
/// <see cref="PdfNameTree"/>, so only the nodes on one path change.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7.2 (Tables 354 and 355), §14.7.4 (Table 356), §14.7.5 (Tables 357-359), §14.7.6. Edits are mutations: no read of
/// the document may run concurrently with them. The read indexes see them at the next lookup (<see cref="StructureIndexes"/>).
/// </remarks>
internal static class StructureWriter
{
    /// <summary>The root's indirect reference, making a direct root indirect first (a <c>P</c> entry shall be an indirect reference).</summary>
    public static CosReference RootReference(StructureContext context)
    {
        if (context.RootReference is { } reference)
        {
            return reference;
        }

        CosReference created = context.Document.AddObject(context.Root);
        context.Document.Catalog[StructureNames.StructTreeRoot] = created;
        context.RootReference = created;
        return created;
    }

    /// <summary>The indirect reference to <paramref name="element"/>, making a direct element in its parent's <c>K</c> indirect first.</summary>
    /// <exception cref="InvalidOperationException">The element is a direct object that is not in the tree.</exception>
    public static CosReference ElementReference(StructureContext context, PdfStructureElement element)
    {
        if (element.Reference is { } reference)
        {
            return reference;
        }

        if (context.Walk.TryGet(element.Dictionary, out PdfStructureElement? walked))
        {
            if (walked.Reference is { } found)
            {
                return found;
            }

            CosDictionary owner = walked.Parent?.Dictionary ?? context.Root;
            CosReference created = context.Document.AddObject(element.Dictionary);
            if (ReplaceKid(context, owner, element.Dictionary, created))
            {
                return created;
            }
        }

        throw new InvalidOperationException("The structure element is a direct object that is not reachable from the structure tree root; it cannot be referred to.");
    }

    /// <summary>Creates a structure element of <paramref name="type"/> as the last child of <paramref name="owner"/> (the root or an element).</summary>
    public static PdfStructureElement AddElement(StructureContext context, CosDictionary owner, CosReference ownerReference, PdfStructureElement? parent, PdfStructureType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Name.Length == 0)
        {
            throw new ArgumentException("A structure type is a non-empty name (Table 355, S).", nameof(type));
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = StructureNames.StructElem,
            [StructureNames.S] = new CosName(type.Name),
            [StructureNames.P] = ownerReference,
        };
        if (NamespaceReference(context, type.Namespace) is { } ns)
        {
            dictionary[StructureNames.NS] = ns;
        }

        CosReference reference = context.Document.AddObject(dictionary);
        AppendTo(context, owner, StructureNames.K, reference);
        return new PdfStructureElement(context, dictionary, reference, parent, parentKnown: true, parent is null ? 0 : parent.Depth + 1);
    }

    /// <summary>Sets the element's <c>S</c> and <c>NS</c> to <paramref name="type"/>.</summary>
    public static void SetType(StructureContext context, CosDictionary element, PdfStructureType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Name.Length == 0)
        {
            throw new ArgumentException("A structure type is a non-empty name (Table 355, S).", nameof(type));
        }

        CosReference? ns = NamespaceReference(context, type.Namespace);
        element[StructureNames.S] = new CosName(type.Name);
        if (ns is null)
        {
            element.Remove(StructureNames.NS);
        }
        else
        {
            element[StructureNames.NS] = ns;
        }
    }

    /// <summary>
    /// The <c>NS</c> value for <paramref name="ns"/>: none for the default namespace, else a reference to a namespace dictionary listed
    /// in the root's <c>Namespaces</c> (§14.8.6.2), added when the document has none for that name.
    /// </summary>
    public static CosReference? NamespaceReference(StructureContext context, PdfStructureNamespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (ns.IsDefault)
        {
            return null;
        }

        if (ns.Dictionary is not { } dictionary)
        {
            return GetOrAddNamespace(context, ns.Name).Reference;
        }

        if (!ReferenceEquals(ns.Document, context.Document))
        {
            throw new ArgumentException("The namespace belongs to another document; use GetOrAddNamespace with its name.", nameof(ns));
        }

        CosArray? declared = NamespacesArray(context, create: false);
        if (declared is not null)
        {
            foreach (CosObject item in declared)
            {
                if (item is CosReference listed && ReferenceEquals(context.Resolve(listed), dictionary))
                {
                    return listed;
                }
            }
        }

        if (ns.Reference is not { } reference)
        {
            throw new ArgumentException("The namespace dictionary is a direct object; an element's NS shall be an indirect reference (Table 355).", nameof(ns));
        }

        NamespacesArray(context, create: true)!.Add(reference);
        return reference;
    }

    /// <summary>The namespace named <paramref name="name"/> the root's <c>Namespaces</c> lists, added as a new namespace dictionary when there is none.</summary>
    public static PdfStructureNamespace GetOrAddNamespace(StructureContext context, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (NamespacesArray(context, create: false) is { } declared)
        {
            foreach (CosObject item in declared)
            {
                if (item is CosReference reference
                    && context.Resolve(item) is CosDictionary dictionary
                    && ViewReading.Text(context.Document, dictionary, StructureNames.NS) == name)
                {
                    return context.Namespace(dictionary, reference);
                }
            }
        }

        var created = new CosDictionary
        {
            [KnownNames.Type] = StructureNames.Namespace,
            [StructureNames.NS] = Text(name),
        };
        CosReference added = context.Document.AddObject(created);
        NamespacesArray(context, create: true)!.Add(added);
        return context.Namespace(created, added);
    }

    /// <summary>Makes <paramref name="page"/>'s content (or <paramref name="form"/>'s) sequence <paramref name="mcid"/> a content item of <paramref name="element"/>.</summary>
    public static PdfMarkedContentReference AddMarkedContent(StructureContext context, PdfStructureElement element, PdfPage page, PdfFormXObject? form, int? mcid)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (mcid < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mcid), mcid, "An MCID is a non-negative integer (§14.7.5.2).");
        }

        CosReference pageReference = page.Reference
            ?? throw new ArgumentException("The page is not an indirect object of the document.", nameof(page));
        CosReference? streamReference = null;
        if (form is not null)
        {
            if (!ReferenceEquals(form.Document, context.Document))
            {
                throw new ArgumentException("The form XObject belongs to another document.", nameof(form));
            }

            streamReference = form.Reference
                ?? throw new ArgumentException("The form XObject is not an indirect object; an MCR's Stm shall be an indirect reference (Table 357).", nameof(form));
        }

        CosReference elementReference = ElementReference(context, element);
        CosObject streamKey = (CosObject?)form?.Stream ?? page.Dictionary;
        CosArray parents = ContentArray(context, form?.Dictionary ?? page.Dictionary);
        int id = mcid ?? NextMcid(context, parents, streamKey);
        if (id < parents.Count && context.Resolve(parents[id]) is not CosNull)
        {
            throw new InvalidOperationException($"MCID {id} of this content stream is already a content item of a structure element (§14.7.5.2: MCIDs are unique per content stream).");
        }

        while (parents.Count <= id)
        {
            parents.Add(CosNull.Instance);
        }

        parents[id] = elementReference;

        CosDictionary? mcr = null;
        CosObject? ownPage = element.Dictionary.TryGetValue(StructureNames.Pg, out CosObject? pg) ? pg : null;
        if (form is null && ownPage is null)
        {
            element.Dictionary[StructureNames.Pg] = pageReference;
        }

        if (form is not null || (ownPage is not null && !ReferenceEquals(context.Resolve(ownPage), page.Dictionary)))
        {
            mcr = new CosDictionary
            {
                [KnownNames.Type] = StructureNames.MCR,
                [StructureNames.Pg] = pageReference,
                [StructureNames.MCID] = new CosInteger(id),
            };
            if (streamReference is not null)
            {
                mcr[StructureNames.Stm] = streamReference;
            }
        }

        AppendTo(context, element.Dictionary, StructureNames.K, (CosObject?)mcr ?? new CosInteger(id));
        return new PdfMarkedContentReference(element, mcr, id, page, page.Dictionary, form?.Stream, null);
    }

    /// <summary>Makes the annotation or XObject <paramref name="target"/> an object content item of <paramref name="element"/>.</summary>
    public static PdfObjectReference AddObjectReference(StructureContext context, PdfStructureElement element, CosReference target, PdfPage? page)
    {
        ArgumentNullException.ThrowIfNull(target);
        CosObject resolved = context.Resolve(target);
        CosDictionary dictionary = resolved as CosDictionary ?? (resolved as CosStream)?.Dictionary
            ?? throw new ArgumentException("An object content item is an annotation or an XObject: a dictionary or a stream (§14.7.5.3).", nameof(target));
        if (dictionary.ContainsKey(StructureNames.StructParent))
        {
            throw new InvalidOperationException("The object already has a StructParent: it is a content item of a structure element (§14.7.5.4).");
        }

        CosReference elementReference = ElementReference(context, element);
        PdfNumberTree tree = ParentTree(context);
        int key = NextKey(context, tree);
        dictionary[StructureNames.StructParent] = new CosInteger(key);
        tree.Set(key, elementReference);

        var objr = new CosDictionary
        {
            [KnownNames.Type] = StructureNames.OBJR,
            [StructureNames.Obj] = target,
        };
        if (page is not null)
        {
            objr[StructureNames.Pg] = page.Reference ?? throw new ArgumentException("The page is not an indirect object of the document.", nameof(page));
        }

        AppendTo(context, element.Dictionary, StructureNames.K, objr);
        return new PdfObjectReference(element, objr, resolved, target, page);
    }

    /// <summary>Detaches <paramref name="element"/> from the tree and unlinks it and everything under it from the parent tree and the ID tree.</summary>
    public static void Remove(StructureContext context, PdfStructureElement element)
    {
        if (!context.Walk.TryGet(element.Dictionary, out PdfStructureElement? walked))
        {
            throw new InvalidOperationException("The structure element is not reachable from the structure tree root.");
        }

        var pending = new Stack<PdfStructureElement>();
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        pending.Push(walked);
        PdfNumberTree? tree = context.Root.TryGetValue(StructureNames.ParentTree, out CosObject? treeRoot) ? context.Document.GetNumberTree(treeRoot) : null;
        PdfNameTree? ids = context.Root.TryGetValue(StructureNames.IDTree, out CosObject? idRoot) ? context.Document.GetNameTree(idRoot) : null;
        while (pending.TryPop(out PdfStructureElement? current))
        {
            if (!visited.Add(current.Dictionary))
            {
                continue;
            }

            if (ids is not null && current.Id is { } id && ids.TryGetValue(id.Bytes, out CosObject? listed) && ReferenceEquals(listed, current.Dictionary))
            {
                ids.Remove(id);
            }

            foreach (PdfStructureItem item in current.Children)
            {
                switch (item)
                {
                    case PdfStructureElement child:
                        pending.Push(child);
                        break;
                    case PdfMarkedContentReference content when tree is not null:
                        UnlinkMarkedContent(context, tree, current.Dictionary, content);
                        break;
                    case PdfObjectReference reference when tree is not null:
                        UnlinkObject(context, tree, current.Dictionary, reference);
                        break;
                }
            }
        }

        // A damaged tree can hold the element in several K entries (a DAG read as a tree): detach it until no path reaches it.
        bool detached;
        do
        {
            detached = RemoveKid(context, walked.Parent?.Dictionary ?? context.Root, element.Dictionary);
        }
        while (detached && context.Walk.TryGet(element.Dictionary, out walked));
    }

    /// <summary>The root's parent tree, created empty (with <c>ParentTreeNextKey</c>) when the root has none.</summary>
    public static PdfNumberTree ParentTree(StructureContext context)
    {
        if (context.Root.TryGetValue(StructureNames.ParentTree, out CosObject? value) && context.Document.GetNumberTree(value) is { } tree)
        {
            return tree;
        }

        PdfNumberTree created = context.Document.CreateNumberTree([]);
        context.Root[StructureNames.ParentTree] = created.RootReference!;
        return created;
    }

    /// <summary>Hands out the next parent-tree key: past both <c>ParentTreeNextKey</c> and every key in the tree, then advances <c>ParentTreeNextKey</c>.</summary>
    public static int NextKey(StructureContext context, PdfNumberTree tree)
    {
        int next = Math.Max(0, ViewReading.Int32(context.Document, context.Root, StructureNames.ParentTreeNextKey) ?? 0);
        if (tree.TryGetFloor(int.MaxValue, out int greatest, out _) && greatest >= next)
        {
            next = greatest == int.MaxValue ? throw new InvalidOperationException("The parent tree has used every key.") : greatest + 1;
        }

        context.Root[StructureNames.ParentTreeNextKey] = new CosInteger(checked(next + 1));
        return next;
    }

    /// <summary>Appends <paramref name="item"/> to the entry <paramref name="key"/> of <paramref name="owner"/>, which holds one object or an array of them.</summary>
    public static void AppendTo(StructureContext context, CosDictionary owner, CosName key, CosObject item)
    {
        if (!owner.TryGetValue(key, out CosObject? existing))
        {
            owner[key] = new CosArray { item };
            return;
        }

        switch (context.Resolve(existing))
        {
            case CosArray array:
                array.Add(item);
                break;
            case CosNull:
                owner[key] = new CosArray { item };
                break;
            default:
                owner[key] = new CosArray { existing, item };
                break;
        }
    }

    /// <summary>A text string object (PDFDocEncoding when it fits, else UTF-16BE), or <see langword="null"/>.</summary>
    public static CosString Text(string value) => new(TextStringEncoder.Encode(value));

    /// <summary>Sets or (with <see langword="null"/>) removes a text-string entry.</summary>
    public static void SetText(CosDictionary dictionary, CosName key, string? value)
    {
        if (value is null)
        {
            dictionary.Remove(key);
        }
        else
        {
            dictionary[key] = Text(value);
        }
    }

    /// <summary>The parent-tree array of the content stream whose dictionary is <paramref name="owner"/>: its <c>StructParents</c> entry, given a key and an array when it has none.</summary>
    private static CosArray ContentArray(StructureContext context, CosDictionary owner)
    {
        PdfNumberTree tree = ParentTree(context);
        if (ViewReading.Int32(context.Document, owner, StructureNames.StructParents) is { } key and >= 0)
        {
            if (tree.TryGetValue(key, out CosObject? value) && value is CosArray existing)
            {
                return existing;
            }

            var array = new CosArray();
            tree.Set(key, context.Document.AddObject(array));
            if ((ViewReading.Int32(context.Document, context.Root, StructureNames.ParentTreeNextKey) ?? 0) <= key)
            {
                context.Root[StructureNames.ParentTreeNextKey] = new CosInteger((long)key + 1);
            }

            return array;
        }

        int next = NextKey(context, tree);
        var created = new CosArray();
        owner[StructureNames.StructParents] = new CosInteger(next);
        tree.Set(next, context.Document.AddObject(created));
        return created;
    }

    /// <summary>The next MCID of a content stream: past every MCID its parent-tree array and the elements' <c>K</c> entries use.</summary>
    private static int NextMcid(StructureContext context, CosArray parents, CosObject streamKey)
    {
        int next = parents.Count;
        if (context.Walk.MarkedContent(streamKey) is { } owned)
        {
            foreach (int used in owned.Keys)
            {
                next = Math.Max(next, used + 1);
            }
        }

        return next;
    }

    private static void UnlinkMarkedContent(StructureContext context, PdfNumberTree tree, CosDictionary element, PdfMarkedContentReference content)
    {
        CosDictionary? owner = content.ContentStream?.Dictionary ?? content.PageObject;
        if (owner is null
            || ViewReading.Int32(context.Document, owner, StructureNames.StructParents) is not { } key
            || !tree.TryGetValue(key, out CosObject? value)
            || value is not CosArray parents
            || content.Mcid >= parents.Count
            || !ReferenceEquals(context.Resolve(parents[content.Mcid]), element))
        {
            return;
        }

        parents[content.Mcid] = CosNull.Instance;
    }

    private static void UnlinkObject(StructureContext context, PdfNumberTree tree, CosDictionary element, PdfObjectReference reference)
    {
        CosDictionary? target = reference.ReferencedObject as CosDictionary ?? (reference.ReferencedObject as CosStream)?.Dictionary;
        if (target is null || ViewReading.Int32(context.Document, target, StructureNames.StructParent) is not { } key)
        {
            return;
        }

        if (tree.TryGetValue(key, out CosObject? value) && ReferenceEquals(value, element))
        {
            tree.Remove(key);
            target.Remove(StructureNames.StructParent);
        }
    }

    private static CosArray? NamespacesArray(StructureContext context, bool create)
    {
        if (context.Root.TryGetValue(StructureNames.Namespaces, out CosObject? value) && context.Resolve(value) is CosArray array)
        {
            return array;
        }

        if (!create)
        {
            return null;
        }

        var created = new CosArray();
        context.Root[StructureNames.Namespaces] = created;
        return created;
    }

    /// <summary>Replaces the direct entry <paramref name="kid"/> of <paramref name="owner"/>'s <c>K</c> with <paramref name="replacement"/>.</summary>
    private static bool ReplaceKid(StructureContext context, CosDictionary owner, CosDictionary kid, CosObject replacement)
    {
        if (!owner.TryGetValue(StructureNames.K, out CosObject? value))
        {
            return false;
        }

        if (ReferenceEquals(value, kid))
        {
            owner[StructureNames.K] = replacement;
            return true;
        }

        if (context.Resolve(value) is CosArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                if (ReferenceEquals(array[index], kid))
                {
                    array[index] = replacement;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Removes every entry of <paramref name="owner"/>'s <c>K</c> that is (or refers to) <paramref name="kid"/>.</summary>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    private static bool RemoveKid(StructureContext context, CosDictionary owner, CosDictionary kid)
    {
        if (!owner.TryGetValue(StructureNames.K, out CosObject? value))
        {
            return false;
        }

        CosObject resolved = context.Resolve(value);
        if (ReferenceEquals(resolved, kid))
        {
            owner.Remove(StructureNames.K);
            return true;
        }

        bool removed = false;
        if (resolved is CosArray array)
        {
            for (int index = array.Count - 1; index >= 0; index--)
            {
                if (ReferenceEquals(context.Resolve(array[index]), kid))
                {
                    array.RemoveAt(index);
                    removed = true;
                }
            }
        }

        return removed;
    }
}
