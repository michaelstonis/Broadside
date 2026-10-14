using System.Text;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>
/// Appends a range of one document's pages to another, with the outline items, named destinations, form fields and page labels that
/// belong to them (issue #160): the work behind <see cref="PdfDocument.AppendPages(PdfDocument, Range)"/>, and so behind merging and
/// splitting.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.7.3, §12.3.2.4, §12.3.3, §12.4.2 and §12.7. Everything is copied through one <see cref="PdfObjectCopier"/> session, pages
/// first, so references to pages in what is copied later (destinations, widgets) name the copies. Names the target already has
/// (destination names, top-level field names) are renamed in the copies only; the target's own objects are never renamed.
/// </remarks>
internal sealed class DocumentAssembly
{
    private static readonly CosName Count = new("Count");
    private static readonly CosName Prev = new("Prev");
    private static readonly CosName Widget = new("Widget");
    private static readonly CosName ResetForm = new("ResetForm");
    private static readonly CosName SubmitForm = new("SubmitForm");
    private static readonly CosName Hide = new("Hide");

    /// <summary>Outline item entries the item's copy gets from its new place rather than from the source (Table 151), or not at all (<c>SE</c>).</summary>
    private static readonly HashSet<CosName> OutlineLinks =
        [NavigationNames.Title, KnownNames.Parent, NavigationNames.First, NavigationNames.Last, NavigationNames.Next, Prev, Count, NavigationNames.SE];

    private readonly PdfDocument _source;
    private readonly PdfDocument _target;
    private readonly int _first;
    private readonly int _count;
    private readonly bool _whole;
    private readonly PdfObjectCopier _copier;
    private readonly CosReference[] _pageCopies;
    private readonly Dictionary<string, CosString> _renamedStrings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CosName> _renamedNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _renamedFields = new(StringComparer.Ordinal);

    public DocumentAssembly(PdfDocument source, PdfDocument target, int first, int count)
    {
        _source = source;
        _target = target;
        _first = first;
        _count = count;
        _whole = first == 0 && count == source.Pages.Count;
        _copier = new PdfObjectCopier(source, target);
        _pageCopies = new CosReference[count];
    }

    /// <summary>Appends the pages and what belongs to them.</summary>
    public void Run()
    {
        // A page reading its Annots from a node is recorded in the source before anything changes, so strict mode throws first.
        for (int index = _first; index < _first + _count; index++)
        {
            PdfPage page = _source.Pages[index];
            if (page.FindInheritedAnnots() is not null)
            {
                _source.AnnotationIndex.ReportInherited(page.Reference);
            }
        }

        List<Label>? labels = PlanLabels(_target.Pages.Count);
        for (int index = 0; index < _count; index++)
        {
            _pageCopies[index] = _copier.CopyPage(_source.Pages[_first + index]);
        }

        _target.Pages.AddRange(_pageCopies);
        CopyNamedDestinations();
        CopyOutline();
        RenameOnPages();
        CopyFields();
        if (labels is not null)
        {
            WriteLabels(labels);
        }
    }

    private bool InRange(int? pageIndex) => pageIndex is { } index && index >= _first && index < _first + _count;

    // ----- Destinations (§12.3.2) -----

    /// <summary>The index of the source page a destination value (array, name, string or dictionary with <c>D</c>) shows, or null.</summary>
    private int? SourcePageOf(CosObject? value)
    {
        switch (_source.Resolve(value))
        {
            case CosArray array:
                return new PdfExplicitDestination(_source, array, isRemote: false, owner: value as CosReference).PageIndex;
            case CosName name:
                return _source.GetNamedDestination(name)?.PageIndex;
            case CosString text:
                return _source.GetNamedDestination(text)?.PageIndex;
            case CosDictionary dictionary when dictionary.TryGetValue(NavigationNames.D, out CosObject? destination):
                return _source.Resolve(destination) is CosDictionary ? null : SourcePageOf(destination);
            default:
                return null;
        }
    }

    /// <summary>The source page an outline item shows through <c>Dest</c> or a go-to action, or null.</summary>
    private int? SourcePageOf(CosDictionary item)
    {
        if (item.TryGetValue(NavigationNames.Dest, out CosObject? destination))
        {
            return SourcePageOf(destination);
        }

        return item.TryGetValue(NavigationNames.A, out CosObject? action) && _source.Resolve(action) is CosDictionary goTo && IsGoTo(_source, goTo)
            ? SourcePageOf(goTo.GetValueOrDefault(NavigationNames.D))
            : null;
    }

    private static bool IsGoTo(PdfDocument document, CosDictionary action) =>
        document.Resolve(action.GetValueOrDefault(NavigationNames.S)) is CosName type && type.Equals(NavigationNames.GoTo);

    /// <summary>
    /// Adds the source's named destinations that show a copied page (every one when every page is copied), renaming those whose name
    /// the target has.
    /// </summary>
    private void CopyNamedDestinations()
    {
        var strings = new List<(CosString Key, CosObject Value, bool Kept)>();
        if (_source.Names?.Dests is { } tree)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach ((CosString key, CosObject value) in tree.Reader.EnumerateRaw())
            {
                if (seen.Add(Key(key.Bytes)))
                {
                    strings.Add((key, value, Keep(value)));
                }
            }
        }

        var names = new List<(CosName Key, CosObject Value, bool Kept)>();
        if (_source.Catalog.TryGetValue(NavigationNames.Dests, out CosObject? legacy) && _source.Resolve(legacy) is CosDictionary dests)
        {
            foreach ((CosName key, CosObject value) in dests)
            {
                names.Add((key, value, Keep(value)));
            }
        }

        if (strings.Count == 0 && names.Count == 0)
        {
            return;
        }

        // A name is free when neither the target nor the source has it; names and strings share one space (§12.3.2.4). Every source
        // name the target has is renamed, kept or not, so a copied reference to a name not copied never finds the target's.
        HashSet<string> targetNames = TargetDestinationNames();
        var taken = new HashSet<string>(targetNames, StringComparer.Ordinal);
        taken.UnionWith(strings.Select(entry => Key(entry.Key.Bytes)));
        taken.UnionWith(names.Select(entry => Key(entry.Key.Bytes)));

        PdfNameTree? targetTree = null;
        foreach ((CosString key, CosObject value, bool kept) in strings)
        {
            CosString name = key;
            if (targetNames.Contains(Key(key.Bytes)))
            {
                name = new CosString(FreeName(key.Bytes, taken));
                _renamedStrings[Key(key.Bytes)] = name;
            }

            if (kept)
            {
                (targetTree ??= _target.DestsTreeForWriting()).Set(name, CopyDestination(value));
            }
        }

        CosDictionary? targetDests = null;
        foreach ((CosName key, CosObject value, bool kept) in names)
        {
            CosName name = key;
            if (targetNames.Contains(Key(key.Bytes)))
            {
                name = new CosName(FreeName(key.Bytes, taken));
                _renamedNames[Key(key.Bytes)] = name;
            }

            if (kept)
            {
                (targetDests ??= TargetLegacyDestsForWriting())[name] = CopyDestination(value);
            }
        }

        bool Keep(CosObject value) => _whole || InRange(SourcePageOf(value));
    }

    private HashSet<string> TargetDestinationNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (_target.Names?.Dests is { } tree)
        {
            foreach ((CosString key, _) in tree.Reader.EnumerateRaw())
            {
                names.Add(Key(key.Bytes));
            }
        }

        if (_target.Catalog.TryGetValue(NavigationNames.Dests, out CosObject? legacy) && _target.Resolve(legacy) is CosDictionary dests)
        {
            foreach ((CosName key, _) in dests)
            {
                names.Add(Key(key.Bytes));
            }
        }

        return names;
    }

    /// <summary>The target catalog's PDF 1.1 <c>Dests</c> dictionary (Table 29: an indirect reference), created when absent.</summary>
    private CosDictionary TargetLegacyDestsForWriting()
    {
        if (_target.Catalog.TryGetValue(NavigationNames.Dests, out CosObject? entry) && _target.Resolve(entry) is CosDictionary dests)
        {
            return dests;
        }

        dests = new CosDictionary();
        _target.Catalog[NavigationNames.Dests] = _target.AddObject(dests);
        return dests;
    }

    /// <summary>
    /// <paramref name="name"/> with <c>_1</c>, <c>_2</c>, … appended, the first not in <paramref name="taken"/> (which it joins); in
    /// UTF-16BE after a UTF-16BE name (§7.9.2.2), else in ASCII.
    /// </summary>
    private static byte[] FreeName(ReadOnlySpan<byte> name, HashSet<string> taken)
    {
        bool utf16 = name.Length >= 2 && name[0] == 0xFE && name[1] == 0xFF;
        for (int number = 1; ; number++)
        {
            string suffix = "_" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            byte[] candidate = [.. name, .. utf16 ? Encoding.BigEndianUnicode.GetBytes(suffix) : Encoding.ASCII.GetBytes(suffix)];
            if (taken.Add(Key(candidate)))
            {
                return candidate;
            }
        }
    }

    /// <summary>A byte string as a dictionary key (one character per byte).</summary>
    private static string Key(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    /// <summary>Copies a destination value into the target and points what it names at the copies.</summary>
    private CosObject CopyDestination(CosObject value)
    {
        CosObject copy = _copier.Copy(value);
        var holder = new CosDictionary { [NavigationNames.D] = copy };
        RenameDestination(holder, NavigationNames.D);
        return holder[NavigationNames.D];
    }

    /// <summary>
    /// Renames the named destination <paramref name="owner"/>'s <paramref name="key"/> entry gives (a copy in the target), and gives
    /// an explicit destination whose page is a source page number the copied page.
    /// </summary>
    private void RenameDestination(CosDictionary owner, CosName key)
    {
        switch (_target.Resolve(owner.GetValueOrDefault(key)))
        {
            case CosName name when _renamedNames.TryGetValue(Key(name.Bytes), out CosName? renamed):
                owner[key] = renamed;
                break;
            case CosString text when _renamedStrings.TryGetValue(Key(text.Bytes), out CosString? renamed):
                owner[key] = renamed;
                break;
            case CosArray array when array.Count > 0 && array[0] is CosInteger number:
                // §12.3.2.2 wants a page object; a source page number (read as an index) is given the copied page.
                array[0] = number.Value >= _first && number.Value < _first + _count ? _pageCopies[number.Value - _first] : CosNull.Instance;
                break;
            case CosDictionary dictionary when dictionary.ContainsKey(NavigationNames.D) && !ReferenceEquals(dictionary, owner):
                RenameDestination(dictionary, NavigationNames.D);
                break;
        }
    }

    /// <summary>Renames the destinations of go-to actions in an action (a copy in the target) and its <c>Next</c> actions.</summary>
    private void RenameInActions(CosObject? action, HashSet<CosDictionary> visited)
    {
        var pending = new Stack<CosObject?>();
        pending.Push(action);
        while (pending.TryPop(out CosObject? next))
        {
            switch (_target.Resolve(next))
            {
                case CosArray array:
                    foreach (CosObject element in array)
                    {
                        pending.Push(element);
                    }

                    break;
                case CosDictionary dictionary when visited.Add(dictionary):
                    if (IsGoTo(_target, dictionary))
                    {
                        RenameDestination(dictionary, NavigationNames.D);
                    }

                    if (_renamedFields.Count > 0)
                    {
                        RenameFieldsInAction(dictionary);
                    }

                    pending.Push(dictionary.GetValueOrDefault(NavigationNames.Next));
                    break;
            }
        }
    }

    /// <summary>Renames destinations and field names in the annotations and actions of the copied pages.</summary>
    private void RenameOnPages()
    {
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var annotations = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (CosReference reference in _pageCopies)
        {
            if (_target.Resolve(reference) is not CosDictionary page)
            {
                continue;
            }

            RenameInAdditionalActions(page, visited);
            if (_target.Resolve(page.GetValueOrDefault(AnnotationNames.Annots)) is not CosArray annots)
            {
                continue;
            }

            foreach (CosObject entry in annots)
            {
                if (_target.Resolve(entry) is CosDictionary annotation && annotations.Add(annotation))
                {
                    if (annotation.ContainsKey(AnnotationNames.Dest))
                    {
                        RenameDestination(annotation, AnnotationNames.Dest);
                    }

                    RenameInActions(annotation.GetValueOrDefault(AnnotationNames.A), visited);
                    RenameInAdditionalActions(annotation, visited);
                }
            }
        }
    }

    private void RenameInAdditionalActions(CosDictionary owner, HashSet<CosDictionary> visited)
    {
        if (_target.Resolve(owner.GetValueOrDefault(AnnotationNames.AA)) is CosDictionary additional)
        {
            foreach ((_, CosObject action) in additional)
            {
                RenameInActions(action, visited);
            }
        }
    }

    // ----- Outline (§12.3.3) -----

    /// <summary>Adds the source's outline items (those showing a copied page, and their ancestors, unless every page is copied) after the target's.</summary>
    private void CopyOutline()
    {
        if (_source.Outline is not { } outline)
        {
            return;
        }

        // Depth first, parents before children, without recursion (an outline may be deeply nested).
        var items = new List<(PdfOutlineItem Item, int Parent)>();
        var pending = new Stack<(PdfOutlineItem Item, int Parent)>();
        for (int index = outline.Items.Count - 1; index >= 0; index--)
        {
            pending.Push((outline.Items[index], -1));
        }

        while (pending.TryPop(out (PdfOutlineItem Item, int Parent) next))
        {
            int position = items.Count;
            items.Add(next);
            IReadOnlyList<PdfOutlineItem> children = next.Item.Children;
            for (int index = children.Count - 1; index >= 0; index--)
            {
                pending.Push((children[index], position));
            }
        }

        var pages = new int?[items.Count];
        var kept = new bool[items.Count];
        for (int index = items.Count - 1; index >= 0; index--)
        {
            pages[index] = SourcePageOf(items[index].Item.Dictionary);
            kept[index] |= _whole || InRange(pages[index]);
            if (kept[index] && items[index].Parent >= 0)
            {
                kept[items[index].Parent] = true;
            }
        }

        if (Array.IndexOf(kept, true) < 0)
        {
            return;
        }

        PdfOutline target = _target.Outline ?? _target.CreateOutline();
        var copies = new PdfOutlineItem?[items.Count];
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < items.Count; index++)
        {
            if (!kept[index])
            {
                continue;
            }

            (PdfOutlineItem item, int parent) = items[index];
            PdfOutlineItem copy = parent < 0 ? target.Add(item.Title) : copies[parent]!.Add(item.Title);
            copies[index] = copy;
            CopyItemEntries(item.Dictionary, copy.Dictionary, dropTarget: pages[index] is not null && !InRange(pages[index]), visited);
        }

        // Children opened their parents as they were added (an item opens with its first child); close those the source had closed.
        for (int index = items.Count - 1; index >= 0; index--)
        {
            if (copies[index] is { } copy && items[index].Item.Count < 0)
            {
                copy.IsOpen = false;
            }
        }
    }

    private void CopyItemEntries(CosDictionary item, CosDictionary copy, bool dropTarget, HashSet<CosDictionary> visited)
    {
        if (_source.Resolve(item.GetValueOrDefault(NavigationNames.Title)) is CosString title)
        {
            copy[NavigationNames.Title] = title;
        }

        foreach ((CosName key, CosObject value) in item)
        {
            if (OutlineLinks.Contains(key))
            {
                continue;
            }

            if (key.Equals(NavigationNames.Dest))
            {
                if (!dropTarget)
                {
                    copy[key] = CopyDestination(value);
                }

                continue;
            }

            if (key.Equals(NavigationNames.A) && dropTarget && _source.Resolve(value) is CosDictionary action && IsGoTo(_source, action))
            {
                continue;
            }

            copy[key] = _copier.Copy(value);
            if (key.Equals(NavigationNames.A))
            {
                RenameInActions(copy[key], visited);
            }
        }

        CarriedFeatures.InDictionary(_source, item, isStream: false, _target.RequireFeature);
    }

    // ----- Form fields (§12.7) -----

    /// <summary>Adds the source's fields with a widget on a copied page (every field when every page is copied) to the target's form.</summary>
    private void CopyFields()
    {
        if (_source.AcroForm is not { } form || _source.Resolve(form.Dictionary.GetValueOrDefault(FormNames.Fields)) is not CosArray roots)
        {
            return;
        }

        // The widgets on the copied pages, as the source has them.
        var onPages = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        for (int index = _first; index < _first + _count; index++)
        {
            if (_source.Resolve(_source.Pages[index].Dictionary.GetValueOrDefault(AnnotationNames.Annots)) is CosArray annots)
            {
                foreach (CosObject annotation in annots)
                {
                    if (_source.Resolve(annotation) is CosDictionary dictionary)
                    {
                        onPages.Add(dictionary);
                    }
                }
            }
        }

        // The field tree depth first without recursion, each node once (a damaged tree may share or cycle).
        var nodes = new List<(CosDictionary Node, int Parent)>();
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CosObject Node, int Parent)>();
        for (int index = roots.Count - 1; index >= 0; index--)
        {
            pending.Push((roots[index], -1));
        }

        while (pending.TryPop(out (CosObject Node, int Parent) next))
        {
            if (_source.Resolve(next.Node) is not CosDictionary node || !seen.Add(node))
            {
                continue;
            }

            int position = nodes.Count;
            nodes.Add((node, next.Parent));
            if (_source.Resolve(node.GetValueOrDefault(FormNames.Kids)) is CosArray kids)
            {
                for (int index = kids.Count - 1; index >= 0; index--)
                {
                    pending.Push((kids[index], position));
                }
            }
        }

        var kept = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var keptFlags = new bool[nodes.Count];
        for (int index = nodes.Count - 1; index >= 0; index--)
        {
            (CosDictionary node, int parent) = nodes[index];
            keptFlags[index] |= _whole || (IsWidget(node) && onPages.Contains(node));
            if (keptFlags[index])
            {
                kept.Add(node);
                if (parent >= 0)
                {
                    keptFlags[parent] = true;
                }
            }
        }

        var copiedRoots = new List<CosObject>();
        foreach (CosObject root in roots)
        {
            if (_source.Resolve(root) is CosDictionary node && kept.Contains(node))
            {
                CosObject copy = _copier.Copy(root);
                PruneKids(node, copy, kept);
                copiedRoots.Add(copy);
            }
        }

        if (copiedRoots.Count == 0)
        {
            return;
        }

        CosDictionary targetForm = TargetFormForWriting();
        CosArray fields = TargetArray(targetForm, FormNames.Fields);
        RenameRootFields(fields, copiedRoots);
        foreach (CosObject root in copiedRoots)
        {
            fields.Add(root);
        }

        CombineForms(form.Dictionary, targetForm, copiedRoots, kept);
        if (_renamedFields.Count > 0)
        {
            RenameFieldReferences(copiedRoots);
        }
    }

    private bool IsWidget(CosDictionary node) =>
        _source.Resolve(node.GetValueOrDefault(AnnotationNames.Subtype)) is CosName subtype && subtype.Equals(Widget);

    /// <summary>
    /// Removes from the copied field tree the kids whose source is not kept (fields and widgets only on pages not copied), and their
    /// copies from the target; the copy session copied each field's kids with it.
    /// </summary>
    private void PruneKids(CosDictionary sourceRoot, CosObject copiedRoot, HashSet<CosDictionary> kept)
    {
        var pending = new Stack<(CosDictionary Source, CosObject Copy)>();
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        pending.Push((sourceRoot, copiedRoot));
        while (pending.TryPop(out (CosDictionary Source, CosObject Copy) next))
        {
            if (!visited.Add(next.Source)
                || _source.Resolve(next.Source.GetValueOrDefault(FormNames.Kids)) is not CosArray sourceKids
                || _target.Resolve(next.Copy) is not CosDictionary copy
                || _target.Resolve(copy.GetValueOrDefault(FormNames.Kids)) is not CosArray copiedKids
                || copiedKids.Count != sourceKids.Count)
            {
                continue;
            }

            for (int index = sourceKids.Count - 1; index >= 0; index--)
            {
                if (_source.Resolve(sourceKids[index]) is CosDictionary kid && kept.Contains(kid))
                {
                    pending.Push((kid, copiedKids[index]));
                }
                else
                {
                    RemoveCopiedSubtree(sourceKids[index], copiedKids[index], kept);
                    copiedKids.RemoveAt(index);
                }
            }
        }
    }

    /// <summary>Removes the target objects copied for a field subtree that is not kept.</summary>
    private void RemoveCopiedSubtree(CosObject sourceNode, CosObject copiedNode, HashSet<CosDictionary> kept)
    {
        var pending = new Stack<(CosObject Source, CosObject Copy)>();
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        pending.Push((sourceNode, copiedNode));
        while (pending.TryPop(out (CosObject Source, CosObject Copy) next))
        {
            if (_source.Resolve(next.Source) is not CosDictionary source || kept.Contains(source) || !visited.Add(source))
            {
                continue;
            }

            if (_target.Resolve(next.Copy) is CosDictionary copy
                && _source.Resolve(source.GetValueOrDefault(FormNames.Kids)) is CosArray sourceKids
                && _target.Resolve(copy.GetValueOrDefault(FormNames.Kids)) is CosArray copiedKids
                && copiedKids.Count == sourceKids.Count)
            {
                for (int index = 0; index < sourceKids.Count; index++)
                {
                    pending.Push((sourceKids[index], copiedKids[index]));
                }
            }

            if (next.Copy is CosReference reference)
            {
                _target.RemoveObject(reference);
            }
        }
    }

    /// <summary>The target's interactive form dictionary, created (indirect, with an empty <c>Fields</c>) when absent.</summary>
    private CosDictionary TargetFormForWriting()
    {
        if (_target.Catalog.TryGetValue(FormNames.AcroForm, out CosObject? entry) && _target.Resolve(entry) is CosDictionary form)
        {
            return form;
        }

        form = new CosDictionary { [FormNames.Fields] = new CosArray() };
        _target.Catalog[FormNames.AcroForm] = _target.AddObject(form);
        return form;
    }

    private CosArray TargetArray(CosDictionary owner, CosName key)
    {
        if (_target.Resolve(owner.GetValueOrDefault(key)) is CosArray array)
        {
            return array;
        }

        array = new CosArray();
        owner[key] = array;
        return array;
    }

    /// <summary>Renames each copied top-level field whose partial name a top-level field of the target has (§12.7.4.2: names are unique).</summary>
    private void RenameRootFields(CosArray targetFields, List<CosObject> copiedRoots)
    {
        var targetNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (CosObject field in targetFields)
        {
            if (PartialName(_target.Resolve(field)) is { } name)
            {
                targetNames.Add(name);
            }
        }

        var taken = new HashSet<string>(targetNames, StringComparer.Ordinal);
        foreach (CosObject root in copiedRoots)
        {
            if (PartialName(_target.Resolve(root)) is { } name)
            {
                taken.Add(name);
            }
        }

        foreach (CosObject root in copiedRoots)
        {
            if (_target.Resolve(root) is not CosDictionary field || PartialName(field) is not { } name || !targetNames.Contains(name))
            {
                continue;
            }

            if (!_renamedFields.TryGetValue(name, out string? renamed))
            {
                for (int number = 1; ; number++)
                {
                    renamed = name + "_" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (taken.Add(renamed))
                    {
                        break;
                    }
                }

                _renamedFields[name] = renamed;
            }

            field[FormNames.T] = new CosString(TextStringEncoder.Encode(renamed));
        }
    }

    private string? PartialName(CosObject field) =>
        field is CosDictionary dictionary && _target.Resolve(dictionary.GetValueOrDefault(FormNames.T)) is CosString name ? name.DecodeText() : null;

    /// <summary>Carries the source form's form-wide entries (Table 224) the copied fields rely on into the target's form.</summary>
    private void CombineForms(CosDictionary sourceForm, CosDictionary targetForm, List<CosObject> copiedRoots, HashSet<CosDictionary> kept)
    {
        // DR first: a font the target has a different one under the same name is added under a free name, which the copied DA
        // strings then name (#308).
        Dictionary<CosName, CosName> renamedFonts = CombineDefaultResources(sourceForm, targetForm);
        if (renamedFonts.Count > 0)
        {
            RenameDefaultAppearanceFonts(copiedRoots, renamedFonts);
        }

        // DA and Q are inherited by fields that do not set them (§12.7.4.3): when the target's differ, the copied roots get the source's.
        foreach (CosName key in (ReadOnlySpan<CosName>)[FormNames.DA, FormNames.Q])
        {
            CosObject value = _source.Resolve(sourceForm.GetValueOrDefault(key));
            if (value is not (CosString or CosInteger))
            {
                continue;
            }

            CosObject copied = _copier.Copy(value);
            if (!targetForm.TryGetValue(key, out CosObject? existing))
            {
                targetForm[key] = existing = Carried(value, copied, renamedFonts, fieldFonts: null);
            }

            CosObject current = _target.Resolve(existing);
            foreach (CosObject root in copiedRoots)
            {
                if (_target.Resolve(root) is CosDictionary field && !field.ContainsKey(key)
                    && Carried(value, copied, renamedFonts, FieldFonts(field, inherited: null)) is var carried
                    && !Equals(current, carried))
                {
                    field[key] = carried;
                }
            }
        }

        if (_source.Resolve(sourceForm.GetValueOrDefault(FormNames.NeedAppearances)) is CosBoolean { Value: true })
        {
            targetForm[FormNames.NeedAppearances] = CosBoolean.True;
        }

        if (_source.Resolve(sourceForm.GetValueOrDefault(FormNames.SigFlags)) is CosInteger { Value: not 0 } sourceFlags)
        {
            long flags = _target.Resolve(targetForm.GetValueOrDefault(FormNames.SigFlags)) is CosInteger targetFlags ? targetFlags.Value : 0;
            targetForm[FormNames.SigFlags] = new CosInteger(flags | sourceFlags.Value);
        }

        // CO: the calculation order of the kept fields, after the target's.
        if (_source.Resolve(sourceForm.GetValueOrDefault(FormNames.CO)) is CosArray order)
        {
            CosArray? targetOrder = null;
            foreach (CosObject field in order)
            {
                if (field is CosReference && _source.Resolve(field) is CosDictionary dictionary && kept.Contains(dictionary))
                {
                    (targetOrder ??= TargetArray(targetForm, FormNames.CO)).Add(_copier.Copy(field));
                }
            }
        }
    }

    /// <summary>
    /// A form-wide <c>DA</c> or <c>Q</c> as a copied field (whose own <c>DR</c> fonts are <paramref name="fieldFonts"/>) inherits it:
    /// a <c>DA</c> naming a font that <paramref name="renamedFonts"/> renames, and that the field's own resources do not hold, names
    /// the new name.
    /// </summary>
    private static CosObject Carried(CosObject value, CosObject copied, Dictionary<CosName, CosName> renamedFonts, CosDictionary? fieldFonts) =>
        renamedFonts.Count > 0
            && value is CosString appearance
            && PdfDefaultAppearance.RenameFonts(appearance.Bytes, Renaming(renamedFonts, fieldFonts)) is { } renamed
            ? new CosString(renamed)
            : copied;

    /// <summary>
    /// The new name of a font a copied <c>DA</c> names: none when the nearest field-chain <c>DR</c> (<paramref name="fieldFonts"/>,
    /// which readers search before the form's) holds it.
    /// </summary>
    private static Func<CosName, CosName?> Renaming(Dictionary<CosName, CosName> renamedFonts, CosDictionary? fieldFonts) =>
        name => fieldFonts is not null && fieldFonts.ContainsKey(name) ? null : renamedFonts.GetValueOrDefault(name);

    /// <summary>The <c>Font</c> subdictionary of a copied field's own <c>DR</c>, else <paramref name="inherited"/>.</summary>
    private CosDictionary? FieldFonts(CosDictionary field, CosDictionary? inherited) =>
        _target.Resolve(field.GetValueOrDefault(FormNames.DR)) is CosDictionary resources
            && _target.Resolve(resources.GetValueOrDefault(Fonts.FontNames.Font)) is CosDictionary fonts
            ? fonts
            : inherited;

    /// <summary>
    /// Rewrites the <c>DA</c> of every node of the copied field trees (fields and widgets) whose font <paramref name="renamedFonts"/>
    /// renames, through the default appearance tokenizer; the rest of each string is kept as written.
    /// </summary>
    private void RenameDefaultAppearanceFonts(List<CosObject> copiedRoots, Dictionary<CosName, CosName> renamedFonts)
    {
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CosObject Node, CosDictionary? Fonts)>();
        foreach (CosObject root in copiedRoots)
        {
            pending.Push((root, null));
        }

        while (pending.TryPop(out (CosObject Node, CosDictionary? Fonts) next))
        {
            if (_target.Resolve(next.Node) is not CosDictionary node || !visited.Add(node))
            {
                continue;
            }

            CosDictionary? fonts = FieldFonts(node, next.Fonts);
            if (_target.Resolve(node.GetValueOrDefault(FormNames.DA)) is CosString appearance
                && PdfDefaultAppearance.RenameFonts(appearance.Bytes, Renaming(renamedFonts, fonts)) is { } renamed)
            {
                node[FormNames.DA] = new CosString(renamed);
            }

            if (_target.Resolve(node.GetValueOrDefault(FormNames.Kids)) is CosArray kids)
            {
                foreach (CosObject kid in kids)
                {
                    pending.Push((kid, fonts));
                }
            }
        }
    }

    /// <summary>
    /// Adds the source form's default resources (<c>DR</c>) to the target's, by category and name, and returns the fonts renamed: a
    /// name the target lacks is added as it is; a font under a name the target gives a different font is added under a free name
    /// (<c>_1</c>, <c>_2</c>, … appended); any other resource under a name the target has keeps the target's.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224 (<c>DR</c>) and §12.7.4.3: a <c>DA</c> font names an entry of the <c>DR</c> <c>Font</c> subdictionary.</remarks>
    private Dictionary<CosName, CosName> CombineDefaultResources(CosDictionary sourceForm, CosDictionary targetForm)
    {
        var renamedFonts = new Dictionary<CosName, CosName>();
        if (_source.Resolve(sourceForm.GetValueOrDefault(FormNames.DR)) is CosDictionary sourceResources)
        {
            if (_target.Resolve(targetForm.GetValueOrDefault(FormNames.DR)) is not CosDictionary targetResources)
            {
                targetResources = new CosDictionary();
                targetForm[FormNames.DR] = targetResources;
            }

            foreach ((CosName category, CosObject entries) in sourceResources)
            {
                if (_source.Resolve(entries) is not CosDictionary sourceCategory)
                {
                    continue;
                }

                if (_target.Resolve(targetResources.GetValueOrDefault(category)) is not CosDictionary targetCategory)
                {
                    targetCategory = new CosDictionary();
                    targetResources[category] = targetCategory;
                }

                bool fonts = category.Equals(Fonts.FontNames.Font);
                HashSet<string>? taken = null;
                foreach ((CosName name, CosObject resource) in sourceCategory)
                {
                    if (!targetCategory.TryGetValue(name, out CosObject? existing))
                    {
                        targetCategory[name] = _copier.Copy(resource);
                    }
                    else if (fonts && !SameResource(resource, existing))
                    {
                        // Free in both dictionaries, so it takes neither a target font nor a source name added later.
                        taken ??= [.. targetCategory.Select(entry => Key(entry.Key.Bytes)), .. sourceCategory.Select(entry => Key(entry.Key.Bytes))];
                        var renamed = CosName.FromOwnedBytes(FreeName(name.Bytes, taken));
                        targetCategory[renamed] = _copier.Copy(resource);
                        renamedFonts[name] = renamed;
                    }
                }
            }
        }

        return renamedFonts;
    }

    /// <summary>
    /// Whether a source resource and a target resource are the same: equal structure (as <see cref="CosObject.DeepEquals"/>, stream
    /// data compared as encoded) with references followed in each one's document. A pair of references met again compares equal, so
    /// cycles end; too large or too deep a graph compares different, which only costs a copy.
    /// </summary>
    private bool SameResource(CosObject source, CosObject target)
    {
        const int maxDepth = 64;
        var assumed = new HashSet<(CosReference, CosReference)>();
        int budget = 10_000;
        return Same(source, target, 0);

        bool Same(CosObject left, CosObject right, int depth)
        {
            if (--budget < 0 || depth > maxDepth)
            {
                return false;
            }

            if (left is CosReference leftReference && right is CosReference rightReference && !assumed.Add((leftReference, rightReference)))
            {
                return true;
            }

            return (_source.Resolve(left), _target.Resolve(right)) switch
            {
                (CosArray a, CosArray b) => a.Count == b.Count && Enumerable.Range(0, a.Count).All(index => Same(a[index], b[index], depth + 1)),
                (CosStream a, CosStream b) => SameDictionaries(a.Dictionary, b.Dictionary, depth, ignoreLength: true)
                    && a.EncodedData.Span.SequenceEqual(b.EncodedData.Span),
                (CosDictionary a, CosDictionary b) => SameDictionaries(a, b, depth, ignoreLength: false),
                (CosArray or CosDictionary or CosStream, _) or (_, CosArray or CosDictionary or CosStream) => false,
                (var a, var b) => a.Equals(b),
            };
        }

        bool SameDictionaries(CosDictionary left, CosDictionary right, int depth, bool ignoreLength)
        {
            int Count(CosDictionary dictionary) => dictionary.Count - (ignoreLength && dictionary.ContainsKey(KnownNames.Length) ? 1 : 0);
            return Count(left) == Count(right)
                && left.All(entry => ignoreLength && entry.Key.Equals(KnownNames.Length)
                    || right.TryGetValue(entry.Key, out CosObject? other) && Same(entry.Value, other, depth + 1));
        }
    }

    /// <summary>
    /// Renames the field names that copied actions and signature field locks give as text (§12.7.6.2 Table 240 <c>Fields</c>,
    /// §12.6.4.11 hide action <c>T</c>, §12.7.5.5 Table 236 <c>Fields</c>) for the top-level fields renamed.
    /// </summary>
    private void RenameFieldReferences(List<CosObject> copiedRoots)
    {
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var actions = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosObject>(copiedRoots);
        while (pending.TryPop(out CosObject? next))
        {
            if (_target.Resolve(next) is not CosDictionary field || !visited.Add(field))
            {
                continue;
            }

            RenameInActions(field.GetValueOrDefault(AnnotationNames.A), actions);
            RenameInAdditionalActions(field, actions);
            if (_target.Resolve(field.GetValueOrDefault(FormNames.Lock)) is CosDictionary fieldLock)
            {
                RenameFieldNames(fieldLock, FormNames.Fields);
            }

            if (_target.Resolve(field.GetValueOrDefault(FormNames.Kids)) is CosArray kids)
            {
                foreach (CosObject kid in kids)
                {
                    pending.Push(kid);
                }
            }
        }

        // Annotations on the copied pages other than the fields' widgets (a link with a reset-form action, say).
        foreach (CosReference reference in _pageCopies)
        {
            if (_target.Resolve(reference) is CosDictionary page && _target.Resolve(page.GetValueOrDefault(AnnotationNames.Annots)) is CosArray annots)
            {
                foreach (CosObject annotation in annots)
                {
                    if (_target.Resolve(annotation) is CosDictionary dictionary && !visited.Contains(dictionary))
                    {
                        RenameInActions(dictionary.GetValueOrDefault(AnnotationNames.A), actions);
                        RenameInAdditionalActions(dictionary, actions);
                    }
                }
            }
        }
    }

    private void RenameFieldsInAction(CosDictionary action)
    {
        if (_target.Resolve(action.GetValueOrDefault(NavigationNames.S)) is not CosName type)
        {
            return;
        }

        if (type.Equals(ResetForm) || type.Equals(SubmitForm))
        {
            RenameFieldNames(action, FormNames.Fields);
        }
        else if (type.Equals(Hide))
        {
            RenameFieldNames(action, ActionNames.T);
        }
    }

    /// <summary>Renames the text strings among <paramref name="owner"/>'s <paramref name="key"/> entry (a string or an array) that name a renamed field or its descendants.</summary>
    private void RenameFieldNames(CosDictionary owner, CosName key)
    {
        switch (_target.Resolve(owner.GetValueOrDefault(key)))
        {
            case CosString name when Renamed(name) is { } renamed:
                owner[key] = renamed;
                break;
            case CosArray names:
                for (int index = 0; index < names.Count; index++)
                {
                    if (_target.Resolve(names[index]) is CosString name && Renamed(name) is { } renamed)
                    {
                        names[index] = renamed;
                    }
                }

                break;
        }

        CosString? Renamed(CosString name)
        {
            string text = name.DecodeText();
            int dot = text.IndexOf('.', StringComparison.Ordinal);
            string root = dot < 0 ? text : text[..dot];
            return _renamedFields.TryGetValue(root, out string? renamed) ? new CosString(TextStringEncoder.Encode(renamed + text[root.Length..])) : null;
        }
    }

    // ----- Page labels (§12.4.2) -----

    /// <summary>The label ranges of the target once the pages are appended, or null when neither document has page labels.</summary>
    private List<Label>? PlanLabels(int targetCount)
    {
        PdfPageLabels? targetLabels = _target.PageLabels;
        PdfPageLabels? sourceLabels = _source.PageLabels;
        if (targetLabels is null && sourceLabels is null)
        {
            return null;
        }

        // A document without labels numbers its pages in decimal from 1, as a reader shows them.
        var labels = new List<Label>();
        foreach (Label range in Ranges(targetLabels))
        {
            if (range.Start < targetCount)
            {
                labels.Add(range);
            }
        }

        Label[] source = Ranges(sourceLabels);
        Label active = source.LastOrDefault(range => range.Start <= _first) ?? new Label(0, PdfPageLabelStyle.Arabic, null, 1);
        labels.Add(active with { Start = targetCount, FirstNumber = (int)Math.Min(int.MaxValue, (long)active.FirstNumber + _first - active.Start) });
        foreach (Label range in source)
        {
            if (range.Start > _first && range.Start < _first + _count)
            {
                labels.Add(range with { Start = targetCount + range.Start - _first });
            }
        }

        return labels;

        static Label[] Ranges(PdfPageLabels? labels) => labels is null
            ? [new Label(0, PdfPageLabelStyle.Arabic, null, 1)]
            : [.. labels.Ranges.Select(range => new Label(range.StartPageIndex, range.Style, range.Prefix, range.FirstNumber))];
    }

    private void WriteLabels(List<Label> labels)
    {
        PdfPageLabels written = _target.CreatePageLabels();
        foreach (Label label in labels)
        {
            written.SetRange(label.Start, label.Style, label.Prefix?.Replace("\0", string.Empty, StringComparison.Ordinal), Math.Max(1, label.FirstNumber));
        }
    }

    private sealed record Label(int Start, PdfPageLabelStyle Style, string? Prefix, int FirstNumber);
}
