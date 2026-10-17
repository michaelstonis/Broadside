using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>
/// Where each object of a linearized file goes and the number it gets: the parts of ISO 32000-2 Annex F, F.3, and what the hint
/// tables (F.4) say about them.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.3.1 to F.3.10. Which part an object goes in follows qpdf 12.4 (validated against Acrobat; <c>qpdf
/// --check-linearization</c> recomputes the partition from the written file, so the two must agree). Each page, then each trailer
/// entry but <c>Root</c>, then each catalog entry, reaches objects through indirect references: a page reaches everything its
/// dictionary does except other pages and page tree nodes (F.3.7; its <c>Parent</c> is not followed, its <c>Thumb</c> is reached by
/// its thumbnail); an entry reaches a page dictionary only when it is the entry's own value. A stream's <c>Length</c> is not
/// followed, because the written stream has a direct one (§7.3.8.2). Every reach updates the object's category, the earlier part
/// winning:
/// </para>
/// <list type="bullet">
/// <item>Part 4 (F.3.5): the catalog, and what the trailer's <c>Encrypt</c> or the catalog's <c>ViewerPreferences</c>,
/// <c>PageMode</c>, <c>Threads</c>, <c>OpenAction</c> or <c>AcroForm</c> reaches (qpdf also places what the interactive form
/// dictionary refers to here, where F.3.5 names only the dictionary itself).</item>
/// <item>Part 6 (F.3.7): the first page's page object, the other objects it reaches (those another page reaches too are its shared
/// objects), and the outline hierarchy when <c>PageMode</c> is <c>UseOutlines</c>.</item>
/// <item>Part 7 (F.3.8): each other page's page object followed by the objects only that page reaches (a document-level entry
/// reaching them too does not make them shared).</item>
/// <item>Part 8 (F.3.9): the objects several other pages reach and the first page does not.</item>
/// <item>Part 9 (F.3.10): the page tree, thumbnails, the outline hierarchy (unless in part 6) and everything else; then the objects
/// nothing reaches, which a save keeps unless it removes unreachable objects.</item>
/// </list>
/// <para>
/// Numbering (F.3.1): parts 7, 8 and 9 are numbered from 1 in the order written (the main cross-reference stream, if any, after
/// them); the linearization parameter dictionary, the first-page cross-reference stream (if any), part 4 and part 6 follow, and the
/// primary hint stream takes the last number (F.3.6). Every object has generation 0. Object streams (F.3.1, §7.5.7) hold only part 9
/// objects that no page, thumbnail or outline uses and that are not needed at open, numbered last in the main section.
/// </para>
/// <para>
/// The page tree is written without the inheritable attributes (<c>Resources</c>, <c>MediaBox</c>, <c>CropBox</c>, <c>Rotate</c>,
/// §7.7.3.4): each page that inherits one gets it as its own entry (F.3.7, F.3.10). The document is not changed: the page and page
/// tree dictionaries written are copies.
/// </para>
/// </remarks>
internal sealed class LinearizedLayout
{
    private static readonly CosName Thumb = new("Thumb");
    private static readonly CosName OutlinesKey = new("Outlines");
    private static readonly CosName PageMode = new("PageMode");
    private static readonly CosName UseOutlines = new("UseOutlines");

    /// <summary>The page attributes a page inherits from its page tree ancestors (§7.7.3.4, Table 31).</summary>
    private static readonly CosName[] Inheritable = [KnownNames.Resources, KnownNames.MediaBox, KnownNames.CropBox, KnownNames.Rotate];

    /// <summary>The catalog entries whose values are needed at open (F.3.5).</summary>
    private static readonly HashSet<CosName> OpenDocumentKeys =
    [
        new CosName("ViewerPreferences"), PageMode, new CosName("Threads"), new CosName("OpenAction"), new CosName("AcroForm"),
    ];

    private readonly Dictionary<CosReference, CosObject> _values;
    private readonly Dictionary<CosReference, CosObject> _substitutes = [];
    private readonly Dictionary<CosReference, Placement> _categories = [];
    private readonly List<List<CosReference>> _pageObjects = [];
    private readonly List<CosReference> _discovered = [];
    private readonly HashSet<CosReference> _pages = [];
    private readonly HashSet<CosReference> _nodes = [];

    private LinearizedLayout(Dictionary<CosReference, CosObject> values) => _values = values;

    /// <summary>What uses an object (qpdf's <c>ObjUser</c>).</summary>
    private enum UserKind
    {
        Page,
        Thumbnail,
        TrailerEntry,
        CatalogEntry,
        Catalog,
    }

    /// <summary>Gets the objects to write, in written order (parts 4, 6, 7, 8, 9, then unreached objects), under their old references.</summary>
    public IReadOnlyList<(CosReference Reference, CosObject Value)> Objects { get; private set; } = [];

    /// <summary>Gets the number each old reference is written with; a reference it does not hold is written as <c>null</c>.</summary>
    public Dictionary<CosReference, CosReference> References { get; } = [];

    /// <summary>Gets the number of objects in part 4, the first ones in <see cref="Objects"/>.</summary>
    public int OpenDocumentCount { get; private set; }

    /// <summary>Gets the number of objects of each page in written order, the first page's being part 6 (Table F.4, item 1).</summary>
    public IReadOnlyList<int> PageObjectCounts { get; private set; } = [];

    /// <summary>Gets the number of objects in part 8.</summary>
    public int SharedCount { get; private set; }

    /// <summary>For each page, the shared object hint table entries it uses (Table F.4, item 4); none for the first page.</summary>
    public IReadOnlyList<int[]> SharedIdentifiers { get; private set; } = [];

    /// <summary>Gets where the outline hierarchy is in <see cref="Objects"/>, the outline dictionary first, or <see langword="null"/>.</summary>
    public (int Start, int Count)? Outlines { get; private set; }

    /// <summary>Gets the number of objects at the end of <see cref="Objects"/> stored in object streams.</summary>
    public int CompressedCount { get; private set; }

    /// <summary>Gets the number of object streams that hold them.</summary>
    public int ObjectStreamCount { get; private set; }

    /// <summary>Gets the number of the first object stream; the others follow it.</summary>
    public int FirstObjectStreamNumber { get; private set; }

    /// <summary>Gets the main cross-reference stream's number, or 0 for a classic table.</summary>
    public int MainCrossReferenceNumber { get; private set; }

    /// <summary>Gets the first page's page object's number (Table F.1, <c>O</c>).</summary>
    public int FirstPageObjectNumber { get; private set; }

    /// <summary>Gets the linearization parameter dictionary's number: the first number of the first-page section.</summary>
    public int LinearizationNumber { get; private set; }

    /// <summary>Gets the first-page cross-reference stream's number, or 0 for a classic table.</summary>
    public int FirstPageCrossReferenceNumber { get; private set; }

    /// <summary>Gets the primary hint stream's number: the last in the file (F.3.6).</summary>
    public int HintStreamNumber { get; private set; }

    /// <summary>Gets the number of entries of the main section, object 0 included: every number below the first-page section.</summary>
    public int MainSectionSize => LinearizationNumber;

    /// <summary>Gets the number of objects in the first part of <see cref="Objects"/> that the first-page section lists (parts 4 and 6).</summary>
    public int FirstPageSectionObjectCount => OpenDocumentCount + PageObjectCounts[0];

    /// <summary>Returns whether <paramref name="reference"/>'s written value is a copy made for the layout (a page or page tree node).</summary>
    public bool IsSubstituted(CosReference reference) => _substitutes.ContainsKey(reference);

    /// <summary>Lays out the objects of a full save as a linearized file.</summary>
    /// <param name="trailer">The trailer entries written (the catalog is its <c>Root</c>).</param>
    /// <param name="kept">The objects written, under their old references.</param>
    /// <param name="layout">The cross-reference layout: streams number their sections, and object streams hold part 9 objects.</param>
    /// <param name="canBeCompressed">Whether an object may go in an object stream.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="NotSupportedException">The document has no catalog or no page, its page tree lists a page twice or holds a page that is not an indirect object, or a page is used as something else.</exception>
    public static LinearizedLayout Create(
        CosDictionary trailer,
        IReadOnlyList<(CosReference Reference, CosObject Value)> kept,
        PdfCrossReferenceLayout layout,
        Func<CosObject, bool> canBeCompressed)
    {
        var values = new Dictionary<CosReference, CosObject>(kept.Count);
        foreach ((CosReference reference, CosObject value) in kept)
        {
            values[reference] = value;
        }

        var plan = new LinearizedLayout(values);
        if (!trailer.TryGetValue(KnownNames.Root, out CosObject? rootEntry) || rootEntry is not CosReference root || plan.Lookup(root) is not CosDictionary catalog)
        {
            throw new NotSupportedException("A document without a catalog cannot be linearized.");
        }

        List<CosReference> pages = plan.ReadPageTree(catalog);
        plan.FindUsers(trailer, root, catalog, pages);
        plan.Place(kept, root, catalog, pages, layout, canBeCompressed);
        return plan;
    }

    /// <summary>The value written for <paramref name="reference"/>: the layout's copy, the kept object, or <see langword="null"/> when nothing is written for it.</summary>
    private CosObject? Lookup(CosReference reference) =>
        _substitutes.TryGetValue(reference, out CosObject? copy) ? copy : _values.GetValueOrDefault(reference);

    private CosObject? Resolve(CosObject? value) => value is CosReference reference ? Lookup(reference) : value;

    /// <summary>
    /// Walks the page tree depth first (§7.7.3) for the pages in order, and makes copies of the nodes holding inheritable attributes
    /// (without them) and of the pages inheriting any (with them). A node is a dictionary with <c>Kids</c>; a kid that is not a
    /// dictionary is skipped.
    /// </summary>
    private List<CosReference> ReadPageTree(CosDictionary catalog)
    {
        if (!catalog.TryGetValue(KnownNames.Pages, out CosObject? pagesEntry) || pagesEntry is not CosReference pagesRoot || Lookup(pagesRoot) is not CosDictionary)
        {
            throw new NotSupportedException("A document without a page tree cannot be linearized.");
        }

        var pages = new List<CosReference>();
        var visited = new HashSet<CosReference>();
        var pending = new Stack<(CosReference Node, Dictionary<CosName, CosObject> Inherited, bool IsPage)>();
        pending.Push((pagesRoot, [], false));
        while (pending.TryPop(out (CosReference Node, Dictionary<CosName, CosObject> Inherited, bool IsPage) item))
        {
            if (!visited.Add(item.Node))
            {
                throw new NotSupportedException("A document whose page tree reaches a page or node twice cannot be linearized: each page needs its own place in the file.");
            }

            var dictionary = (CosDictionary)Lookup(item.Node)!;
            if (item.IsPage)
            {
                pages.Add(item.Node);
                _pages.Add(item.Node);
                if (item.Inherited.Keys.Any(key => !dictionary.ContainsKey(key)))
                {
                    CosDictionary copy = Copy(dictionary, exclude: null);
                    foreach (CosName key in Inheritable)
                    {
                        if (!copy.ContainsKey(key) && item.Inherited.TryGetValue(key, out CosObject? value))
                        {
                            copy[key] = value;
                        }
                    }

                    _substitutes[item.Node] = copy;
                }

                continue;
            }

            _nodes.Add(item.Node);
            Dictionary<CosName, CosObject> inherited = item.Inherited;
            if (Inheritable.Any(dictionary.ContainsKey))
            {
                inherited = new Dictionary<CosName, CosObject>(item.Inherited);
                foreach (CosName key in Inheritable)
                {
                    if (dictionary.TryGetValue(key, out CosObject? value))
                    {
                        inherited[key] = value;
                    }
                }

                _substitutes[item.Node] = Copy(dictionary, Inheritable);
            }

            if (Resolve(dictionary.GetValueOrDefault(KnownNames.Kids)) is not CosArray kids)
            {
                continue;
            }

            for (int index = kids.Count - 1; index >= 0; index--)
            {
                if (kids[index] is not CosReference kid)
                {
                    if (kids[index] is CosDictionary)
                    {
                        throw new NotSupportedException("A document whose page tree holds a page that is not an indirect object cannot be linearized.");
                    }

                    continue;
                }

                if (Lookup(kid) is CosDictionary child)
                {
                    pending.Push((kid, inherited, !child.ContainsKey(KnownNames.Kids)));
                }
            }
        }

        if (pages.Count == 0)
        {
            throw new NotSupportedException("A document without pages cannot be linearized: a linearized file is organized around its first page (F.3.7).");
        }

        return pages;
    }

    private static CosDictionary Copy(CosDictionary dictionary, CosName[]? exclude)
    {
        var copy = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            if (exclude is null || Array.IndexOf(exclude, entry.Key) < 0)
            {
                copy[entry.Key] = entry.Value;
            }
        }

        return copy;
    }

    /// <summary>
    /// Finds the category of every object: each page's traversal, then each trailer entry but <c>Root</c>, then each catalog entry
    /// (in key order), then the catalog itself.
    /// </summary>
    private void FindUsers(CosDictionary trailer, CosReference root, CosDictionary catalog, List<CosReference> pages)
    {
        for (int page = 0; page < pages.Count; page++)
        {
            _pageObjects.Add([]);
            Traverse(new User(UserKind.Page, page, null), pages[page]);
        }

        foreach (KeyValuePair<CosName, CosObject> entry in Sorted(trailer))
        {
            if (!entry.Key.Equals(KnownNames.Root) && Resolve(entry.Value) is not (null or CosNull))
            {
                Traverse(new User(UserKind.TrailerEntry, -1, entry.Key), entry.Value);
            }
        }

        foreach (KeyValuePair<CosName, CosObject> entry in Sorted(catalog))
        {
            if (Resolve(entry.Value) is not (null or CosNull))
            {
                Traverse(new User(UserKind.CatalogEntry, -1, entry.Key), entry.Value);
            }
        }

        Update(root, new User(UserKind.Catalog, -1, null), top: true);
    }

    /// <summary>
    /// Updates the category of every indirect object <paramref name="start"/> reaches for <paramref name="user"/> (qpdf's
    /// <c>updateObjectMaps</c>): one traversal visits an object once; a page dictionary other than <paramref name="start"/> is not
    /// entered, nor is a page tree node from a page; a page's <c>Parent</c> is not followed and its <c>Thumb</c> is reached by the
    /// page's thumbnail; an entry whose value is null, directly or through a reference, is skipped; entries are taken in key order
    /// and the last pushed first, as qpdf does.
    /// </summary>
    private void Traverse(User user, CosObject start)
    {
        var visited = new HashSet<CosReference>();
        var pending = new Stack<(User User, CosObject Value, bool Top)>();
        pending.Push((user, start, true));
        while (pending.TryPop(out (User User, CosObject Value, bool Top) item))
        {
            CosObject? value = item.Value;
            bool isPage = false;
            if (item.Value is CosReference reference)
            {
                value = Lookup(reference);
                if (value is null || !visited.Add(reference))
                {
                    continue;
                }

                // qpdf gives the pages and page tree nodes it walks the right Type before linearizing, so they count as such here
                // whatever their Type says.
                CosName? type = value is CosDictionary dictionary ? dictionary.GetValueOrDefault(KnownNames.Type) as CosName : null;
                if (_pages.Contains(reference) || (!_nodes.Contains(reference) && KnownNames.Page.Equals(type)))
                {
                    isPage = true;
                    if (!item.Top)
                    {
                        continue;
                    }
                }
                else if (user.Kind == UserKind.Page && (_nodes.Contains(reference) || KnownNames.Pages.Equals(type)))
                {
                    continue;
                }

                Update(reference, item.User, item.Top);
                if (item.User.Kind == UserKind.Page)
                {
                    _pageObjects[item.User.Page].Add(reference);
                }
            }

            switch (value)
            {
                case CosArray array:
                    foreach (CosObject element in array)
                    {
                        pending.Push((item.User, element, false));
                    }

                    break;
                case CosDictionary or CosStream:
                    bool isStream = value is CosStream;
                    CosDictionary entries = value as CosDictionary ?? ((CosStream)value).Dictionary;
                    foreach (KeyValuePair<CosName, CosObject> entry in Sorted(entries))
                    {
                        if (Resolve(entry.Value) is null or CosNull || (isStream && entry.Key.Equals(KnownNames.Length)) || (isPage && entry.Key.Equals(KnownNames.Parent)))
                        {
                            continue;
                        }

                        User next = isPage && entry.Key.Equals(Thumb) ? new User(UserKind.Thumbnail, item.User.Page, null) : item.User;
                        pending.Push((next, entry.Value, false));
                    }

                    break;
                default:
                    break;
            }
        }
    }

    private static IEnumerable<KeyValuePair<CosName, CosObject>> Sorted(CosDictionary dictionary) =>
        dictionary.OrderBy(entry => entry.Key.Bytes.ToArray(), ByteOrder.Instance);

    /// <summary>Folds one more user of <paramref name="reference"/> into its category.</summary>
    private void Update(CosReference reference, User user, bool top)
    {
        if (!_categories.TryGetValue(reference, out Placement current))
        {
            current = new Placement(Category.Other, -1);
            _discovered.Add(reference);
        }

        _categories[reference] = current.Update(user.PlacementOf(top));
    }

    /// <summary>Sorts the objects into parts (qpdf's <c>calculateLinearizationData</c>), orders and numbers them, and works out the hint data.</summary>
    private void Place(
        IReadOnlyList<(CosReference Reference, CosObject Value)> kept,
        CosReference root,
        CosDictionary catalog,
        List<CosReference> pages,
        PdfCrossReferenceLayout layout,
        Func<CosObject, bool> canBeCompressed)
    {
        var parts = new Dictionary<Category, List<CosReference>>();
        var pagePrivate = new List<CosReference>[pages.Count];
        var thumbnailPrivate = new List<CosReference>[pages.Count];
        for (int page = 0; page < pages.Count; page++)
        {
            pagePrivate[page] = [];
            thumbnailPrivate[page] = [];
        }

        foreach (Category category in Enum.GetValues<Category>())
        {
            parts[category] = [];
        }

        foreach (CosReference reference in _discovered)
        {
            Placement placement = _categories[reference];
            switch (placement.Category)
            {
                case Category.FirstPagePrivate:
                    if (!reference.Equals(pages[0]))
                    {
                        pagePrivate[0].Add(reference);
                    }

                    break;
                case Category.OtherPagePrivate when placement.Page > 0:
                    if (!reference.Equals(pages[placement.Page]))
                    {
                        pagePrivate[placement.Page].Add(reference);
                    }

                    break;
                case Category.OtherPagePrivate:
                    parts[Category.Other].Add(reference);
                    break;
                case Category.ThumbnailPrivate when placement.Page >= 0:
                    thumbnailPrivate[placement.Page].Add(reference);
                    break;
                default:
                    parts[placement.Category].Add(reference);
                    break;
            }
        }

        for (int page = 0; page < pages.Count; page++)
        {
            Placement placement = _categories.GetValueOrDefault(pages[page], new Placement(Category.Other, -1));
            if (placement.Category != (page == 0 ? Category.FirstPagePrivate : Category.OtherPagePrivate) || placement.Page != page)
            {
                throw new NotSupportedException("A document whose page object is also used as something other than that page cannot be linearized.");
            }
        }

        bool outlinesFirst = Resolve(catalog.GetValueOrDefault(PageMode)) is CosName mode && mode.Equals(UseOutlines) && catalog.ContainsKey(OutlinesKey);
        List<CosReference> outlines = [.. parts[Category.OutlinesObject], .. parts[Category.Outlines]];

        // Part 4: the catalog, then what is needed at open.
        List<CosReference> part4 = [root, .. parts[Category.OpenDocument]];

        // Part 6: the first page, the objects only it uses, the objects it shares, the outlines when shown at open.
        List<CosReference> part6 = [pages[0], .. pagePrivate[0], .. parts[Category.FirstPageShared]];
        if (outlinesFirst && outlines.Count > 0)
        {
            Outlines = (part4.Count + part6.Count, outlines.Count);
            part6.AddRange(outlines);
        }

        // Part 7: each other page and the objects only it uses.
        var part7 = new List<CosReference>();
        var pageCounts = new int[pages.Count];
        pageCounts[0] = part6.Count;
        for (int page = 1; page < pages.Count; page++)
        {
            part7.Add(pages[page]);
            part7.AddRange(pagePrivate[page]);
            pageCounts[page] = 1 + pagePrivate[page].Count;
        }

        // Part 8: the objects several other pages share.
        List<CosReference> part8 = parts[Category.OtherPageShared];

        // Part 9: the page tree, thumbnails, the outlines, everything else; then what nothing reaches.
        List<CosReference> part9 = [.. parts[Category.PagesObject]];
        for (int page = 0; page < pages.Count; page++)
        {
            part9.AddRange(thumbnailPrivate[page]);
        }

        part9.AddRange(parts[Category.ThumbnailPrivate]);
        part9.AddRange(parts[Category.ThumbnailShared]);
        if (!outlinesFirst && outlines.Count > 0)
        {
            Outlines = (part4.Count + part6.Count + part7.Count + part8.Count + part9.Count, outlines.Count);
            part9.AddRange(outlines);
        }

        var compressed = new List<CosReference>();
        bool objectStreams = layout == PdfCrossReferenceLayout.StreamWithObjectStreams;
        IEnumerable<CosReference> rest = parts[Category.Other].Concat(kept.Select(item => item.Reference).Where(reference => !_categories.ContainsKey(reference)));
        foreach (CosReference reference in rest)
        {
            (objectStreams && canBeCompressed(Lookup(reference)!) ? compressed : part9).Add(reference);
        }

        // Numbering (F.3.1): parts 7, 8, 9 from 1, then the object streams, the main cross-reference stream and the compressed
        // objects (F.3.1: compressed objects take the highest numbers of the main section); then the first-page section.
        int number = 0;
        void Number(List<CosReference> part)
        {
            foreach (CosReference reference in part)
            {
                References[reference] = new CosReference(++number, 0);
            }
        }

        Number(part7);
        Number(part8);
        Number(part9);
        if (compressed.Count > 0)
        {
            ObjectStreamCount = (compressed.Count + FileWriter.ObjectStreamCapacity - 1) / FileWriter.ObjectStreamCapacity;
            FirstObjectStreamNumber = number + 1;
            number += ObjectStreamCount;
        }

        if (layout != PdfCrossReferenceLayout.Table)
        {
            MainCrossReferenceNumber = ++number;
        }

        Number(compressed);
        LinearizationNumber = ++number;
        if (layout != PdfCrossReferenceLayout.Table)
        {
            FirstPageCrossReferenceNumber = ++number;
        }

        Number(part4);
        Number(part6);
        HintStreamNumber = ++number;

        var ordered = new List<(CosReference Reference, CosObject Value)>(kept.Count);
        foreach (List<CosReference> part in (List<CosReference>[])[part4, part6, part7, part8, part9, compressed])
        {
            foreach (CosReference reference in part)
            {
                ordered.Add((reference, Lookup(reference)!));
            }
        }

        // Shared object hint table (F.4.3): one entry per part 6 object, then one per part 8 object, as qpdf writes; a page lists
        // the shared ones it reaches (those referenced from more than one page).
        var sharedIndex = new Dictionary<CosReference, int>();
        foreach (CosReference reference in part6.Concat(part8))
        {
            sharedIndex[reference] = sharedIndex.Count;
        }

        var identifiers = new int[pages.Count][];
        identifiers[0] = [];
        for (int page = 1; page < pages.Count; page++)
        {
            identifiers[page] = [.. _pageObjects[page]
                .Where(reference => _categories[reference].Category is Category.FirstPageShared or Category.OtherPageShared)
                .Select(reference => sharedIndex[reference])
                .Distinct()
                .Order()];
        }

        Objects = ordered;
        FirstPageObjectNumber = References[pages[0]].ObjectNumber;
        OpenDocumentCount = part4.Count;
        PageObjectCounts = pageCounts;
        SharedCount = part8.Count;
        SharedIdentifiers = identifiers;
        CompressedCount = compressed.Count;
    }

    /// <summary>One user of objects: a page or its thumbnail (by page index), a trailer or catalog entry (by key), or the catalog.</summary>
    private readonly record struct User(UserKind Kind, int Page, CosName? Key)
    {
        /// <summary>The category an object only this user reached would have (qpdf's <c>ObjUser::obj_category</c>).</summary>
        public Placement PlacementOf(bool top) => Kind switch
        {
            UserKind.Page => new Placement(Page == 0 ? Category.FirstPagePrivate : Category.OtherPagePrivate, Page),
            UserKind.Thumbnail => new Placement(Category.ThumbnailPrivate, Page),
            UserKind.Catalog => new Placement(Category.Root, -1),
            UserKind.TrailerEntry => new Placement(KnownNames.Encrypt.Equals(Key) ? Category.OpenDocument : Category.Other, -1),
            _ when OpenDocumentKeys.Contains(Key!) => new Placement(Category.OpenDocument, -1),
            _ when OutlinesKey.Equals(Key) => new Placement(top ? Category.OutlinesObject : Category.Outlines, -1),
            _ when KnownNames.Pages.Equals(Key) => new Placement(Category.PagesObject, -1),
            _ => new Placement(Category.Other, -1),
        };
    }

    /// <summary>
    /// An object's category and the page it belongs to (qpdf's <c>ObjCategory</c>): updated as each user reaches it, an earlier
    /// part taking precedence; an object two pages reach is shared, and one the first page and another page reach is the first
    /// page's shared object.
    /// </summary>
    private readonly record struct Placement(Category Category, int Page)
    {
        public Placement Update(Placement other)
        {
            Category min = (Category)Math.Min((int)Category, (int)other.Category);
            Category max = (Category)Math.Max((int)Category, (int)other.Category);
            int page = Page <= 0 || other.Page <= 0 ? Math.Max(Page, other.Page) : Math.Min(Page, other.Page);
            Category category = min switch
            {
                <= Category.FirstPageShared => min,
                Category.FirstPagePrivate => max is Category.OtherPagePrivate or Category.OtherPageShared ? Category.FirstPageShared : Category.FirstPagePrivate,
                Category.OtherPagePrivate => max == Category.OtherPagePrivate && Page != other.Page ? Category.OtherPageShared : Category.OtherPagePrivate,
                Category.ThumbnailPrivate => max == Category.ThumbnailPrivate && Page != other.Page ? Category.ThumbnailShared : Category.ThumbnailPrivate,
                _ => min,
            };
            return new Placement(category, page);
        }
    }

    /// <summary>The part categories, earlier ones taking precedence (qpdf's <c>obj_category_e</c>).</summary>
    private enum Category
    {
        Root,
        OpenDocument,
        FirstPageShared,
        FirstPagePrivate,
        OtherPageShared,
        OtherPagePrivate,
        ThumbnailShared,
        ThumbnailPrivate,
        PagesObject,
        OutlinesObject,
        Outlines,
        Other,
    }

    /// <summary>Orders names by their bytes, as qpdf's dictionaries (a sorted map of strings) iterate.</summary>
    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
