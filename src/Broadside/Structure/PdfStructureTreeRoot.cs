using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>The root of a document's structure tree: its top-level elements, role and class maps, namespaces, and the lookups from content to elements.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.2, Table 354. A live view over the structure tree root dictionary (ADR 0004): <see cref="Children"/> and the map
/// properties read it on every call. The lookups (<see cref="FindElement(PdfPage, int)"/>, <see cref="FindElementById(ReadOnlySpan{byte})"/>,
/// <see cref="Elements"/>) use indexes built on first use and kept: they are a snapshot of the tree as it was then, safe to use from
/// several threads while nobody changes the document. Replacing the catalog's <c>StructTreeRoot</c> starts a new snapshot.
/// </para>
/// <para>
/// Reading never changes the file's objects; every repair (a wrong <c>P</c>, a missing <c>Pg</c>, a cycle) is made in the view and
/// recorded as a diagnostic when the part concerned is first read.
/// </para>
/// </remarks>
public sealed class PdfStructureTreeRoot
{
    private readonly StructureContext _context;

    internal PdfStructureTreeRoot(StructureContext context) => _context = context;

    /// <summary>Gets the structure tree root dictionary.</summary>
    /// <remarks>ISO 32000-2 §14.7.2, Table 354.</remarks>
    public CosDictionary Dictionary => _context.Root;

    /// <summary>Gets the indirect reference to the root, when the catalog holds one.</summary>
    public CosReference? Reference => _context.RootReference;

    /// <summary>Gets the top-level structure elements (<c>K</c>), in logical order.</summary>
    /// <remarks>ISO 32000-2 Table 354: the root's children are structure elements only; anything else is left out with a diagnostic.</remarks>
    public IReadOnlyList<PdfStructureElement> Children
    {
        get
        {
            var children = new List<PdfStructureElement>();
            foreach (CosObject entry in KidReader.Entries(_context, Dictionary))
            {
                Kid kid = KidReader.Classify(_context, entry, Reference);
                if (kid.Kind == KidKind.Element && !ReferenceEquals(kid.Dictionary, Dictionary))
                {
                    children.Add(new PdfStructureElement(_context, kid.Dictionary!, kid.Reference, null, parentKnown: true, depth: 0));
                }
                else if (kid.Kind != KidKind.Invalid)
                {
                    _context.Report(DiagnosticCodes.StructTreeRootInvalid, "The structure tree root's K holds something other than a structure element (Table 354); skipped.", Reference);
                }
            }

            return children;
        }
    }

    /// <summary>Gets the role map (<c>RoleMap</c>): structure types of the default namespace mapped to other types, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 354, §14.7.3. Applied by <see cref="PdfStructureElement.StandardType"/>.</remarks>
    public CosDictionary? RoleMap => StructureValues.Get(_context.Document, Dictionary, StructureNames.RoleMap) as CosDictionary;

    /// <summary>Gets the class map (<c>ClassMap</c>): attribute class names mapped to attribute objects, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 354, §14.7.6.2. Applied by <see cref="PdfStructureElement.ClassAttributes"/>.</remarks>
    public CosDictionary? ClassMap => StructureValues.Get(_context.Document, Dictionary, StructureNames.ClassMap) as CosDictionary;

    /// <summary>Gets the namespaces the document declares (<c>Namespaces</c>), in order.</summary>
    /// <remarks>ISO 32000-2 Table 354 (PDF 2.0), §14.7.4.</remarks>
    public IReadOnlyList<PdfStructureNamespace> Namespaces
    {
        get
        {
            if (StructureValues.Get(_context.Document, Dictionary, StructureNames.Namespaces) is not CosArray array)
            {
                return [];
            }

            var namespaces = new List<PdfStructureNamespace>(array.Count);
            foreach (CosObject item in array)
            {
                if (_context.Resolve(item) is CosDictionary ns)
                {
                    namespaces.Add(_context.Namespace(ns, item as CosReference));
                }
            }

            return namespaces;
        }
    }

    /// <summary>Gets <c>ParentTreeNextKey</c>, the key the next parent-tree entry will use, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 354, §14.7.5.4.</remarks>
    public int? ParentTreeNextKey => StructureValues.Integer(_context.Document, Dictionary, StructureNames.ParentTreeNextKey);

    /// <summary>Gets the pronunciation lexicons (<c>PronunciationLexicon</c>) as their COS array of file specifications, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 354 (PDF 2.0), §14.9.6. Typed by the file specification view (issue #76).</remarks>
    public CosArray? PronunciationLexicon => StructureValues.Get(_context.Document, Dictionary, StructureNames.PronunciationLexicon) as CosArray;

    /// <summary>Gets the associated files of the whole tree (<c>AF</c>) as their COS array, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 354 (PDF 2.0), §14.13. Typed by the file specification view (issue #76).</remarks>
    public CosArray? AssociatedFiles => StructureValues.Get(_context.Document, Dictionary, StructureNames.AF) as CosArray;

    /// <summary>Gets every element reachable from the root, in document order: depth first, each element before its children, children in <c>K</c> order; an element reachable twice is listed once.</summary>
    /// <remarks>ISO 32000-2 §14.7.2, §14.8.2.5 (logical order). A snapshot, built once.</remarks>
    public IReadOnlyList<PdfStructureElement> Elements => _context.Walk.Elements;

    /// <summary>Finds the element whose <c>ID</c> is <paramref name="id"/>.</summary>
    /// <param name="id">The identifier's bytes (IDs are byte strings, compared byte for byte).</param>
    /// <returns>The element, or <see langword="null"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 Table 354 (<c>IDTree</c>) and Table 355 (<c>ID</c>): looked up in the ID tree; when the tree is missing, does not
    /// list the ID, or names an element whose <c>ID</c> differs, the elements reachable through <c>K</c> are searched instead.
    /// </remarks>
    public PdfStructureElement? FindElementById(ReadOnlySpan<byte> id)
    {
        if (_context.IdTree is { } tree && tree.TryGetValue(id, out CosObject? value) && value is CosDictionary dictionary)
        {
            if (StructureValues.Get(_context.Document, dictionary, StructureNames.ID) is CosString own && own.Bytes.SequenceEqual(id))
            {
                return new PdfStructureElement(_context, dictionary, null, null, parentKnown: false, depth: 0);
            }

            _context.Report(DiagnosticCodes.IdTreeEntryInvalid, "The IDTree maps an ID to an element whose ID differs (Table 354); the element is searched through K.", Reference);
        }

        return _context.Walk.TryGetById(id, out PdfStructureElement? element) ? element : null;
    }

    /// <summary>Finds the structure element that owns the marked-content sequence <paramref name="mcid"/> in the content of <paramref name="page"/>.</summary>
    /// <param name="page">The page whose content stream holds the sequence.</param>
    /// <param name="mcid">The sequence's MCID.</param>
    /// <returns>The element, or <see langword="null"/> when the sequence is not a content item.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.4: through the page's <c>StructParents</c> entry and the parent tree.</remarks>
    public PdfStructureElement? FindElement(PdfPage page, int mcid)
    {
        ArgumentNullException.ThrowIfNull(page);
        return GetMarkedContentElements(page).TryGetValue(mcid, out PdfStructureElement? element) ? element : null;
    }

    /// <summary>Finds the structure element that owns the marked-content sequence <paramref name="mcid"/> in <paramref name="contentStream"/>, a form XObject or another stream with its own <c>StructParents</c>.</summary>
    /// <param name="contentStream">The content stream holding the sequence.</param>
    /// <param name="mcid">The sequence's MCID.</param>
    /// <returns>The element, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.4, Table 359: MCIDs are scoped to their content stream, so MCID 0 of a form is not MCID 0 of the page.</remarks>
    public PdfStructureElement? FindElement(CosStream contentStream, int mcid)
    {
        ArgumentNullException.ThrowIfNull(contentStream);
        return GetMarkedContentElements(contentStream).TryGetValue(mcid, out PdfStructureElement? element) ? element : null;
    }

    /// <summary>Returns the MCID-to-element map of the content of <paramref name="page"/>.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The elements owning the page's marked-content content items, by MCID.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.4. Built once per page on first use.</remarks>
    public IReadOnlyDictionary<int, PdfStructureElement> GetMarkedContentElements(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return _context.MarkedContent(page.Dictionary).Elements;
    }

    /// <summary>Returns the MCID-to-element map of <paramref name="contentStream"/>.</summary>
    /// <param name="contentStream">A form XObject or other content stream with its own <c>StructParents</c>.</param>
    /// <returns>The elements owning its marked-content content items, by MCID.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.4. Built once per stream on first use.</remarks>
    public IReadOnlyDictionary<int, PdfStructureElement> GetMarkedContentElements(CosStream contentStream)
    {
        ArgumentNullException.ThrowIfNull(contentStream);
        return _context.MarkedContent(contentStream).Elements;
    }

    /// <summary>Finds the structure element that has <paramref name="contentItem"/>, an annotation or XObject with a <c>StructParent</c> entry, as an object content item.</summary>
    /// <param name="contentItem">The object: a dictionary, or a stream (an XObject).</param>
    /// <returns>The element, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.3, §14.7.5.4, Table 359.</remarks>
    public PdfStructureElement? FindElementForObject(CosObject contentItem)
    {
        ArgumentNullException.ThrowIfNull(contentItem);
        CosObject resolved = _context.Resolve(contentItem);
        CosDictionary? dictionary = resolved as CosDictionary ?? (resolved as CosStream)?.Dictionary;
        if (_context.ParentTree is { } tree
            && dictionary is not null
            && StructureValues.Integer(_context.Document, dictionary, StructureNames.StructParent) is { } key
            && tree.TryGetValue(key, out CosObject? value))
        {
            if (value is CosDictionary parent)
            {
                var element = new PdfStructureElement(_context, parent, null, null, parentKnown: false, depth: 0);
                if (element.Children.Any(item => item is PdfObjectReference reference && ReferenceEquals(reference.ReferencedObject, resolved)))
                {
                    return element;
                }
            }

            _context.Report(DiagnosticCodes.ParentTreeEntryInvalid, $"The parent tree entry {key} of an object content item does not name an element whose K holds the object (§14.7.5.4); the element is searched through K.", contentItem as CosReference);
        }

        return _context.Walk.TryGetObjectParent(resolved, out PdfStructureElement? found) ? found : null;
    }
}
