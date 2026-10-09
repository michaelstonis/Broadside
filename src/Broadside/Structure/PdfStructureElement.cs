using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>A structure element: one node of a document's structure tree, a live view over its structure element dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.2, Table 355. Every property reads the dictionary when called (ADR 0004); <see cref="Children"/> enumerates
/// <c>K</c> on each call. Two views of the same dictionary are equal.
/// </para>
/// <para>
/// The tree's shape comes from <c>K</c>, read downward from the root: <see cref="Parent"/> is the element whose <c>K</c> holds this one,
/// not what this element's <c>P</c> entry says. A <c>P</c> that disagrees is a <c>StructElemParentMismatch</c> diagnostic. A child
/// that is the element itself or one of its ancestors (a cycle) is left out of <see cref="Children"/> with a <c>StructTreeCycle</c>
/// diagnostic, and the hierarchy is cut below 256 levels (<c>StructTreeDepthExceeded</c>), so a recursive walk over
/// <see cref="Children"/> always ends.
/// </para>
/// </remarks>
public sealed class PdfStructureElement : PdfStructureItem, IEquatable<PdfStructureElement>
{
    private readonly StructureContext _context;
    private readonly PdfStructureElement? _parent;
    private readonly bool _parentKnown;

    internal PdfStructureElement(StructureContext context, CosDictionary dictionary, CosReference? reference, PdfStructureElement? parent, bool parentKnown, int depth)
    {
        _context = context;
        Dictionary = dictionary;
        Reference = reference;
        _parent = parent;
        _parentKnown = parentKnown;
        Depth = depth;
    }

    /// <summary>Gets the structure element dictionary.</summary>
    /// <remarks>ISO 32000-2 §14.7.2, Table 355.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the element, when it was reached through one.</summary>
    public CosReference? Reference { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §14.7.2. The element whose <c>K</c> holds this one, found by walking the tree from the root; <see langword="null"/>
    /// for a child of the root, and for an element that is not reachable from the root.
    /// </remarks>
    public override PdfStructureElement? Parent =>
        _parentKnown ? _parent : _context.Walk.TryGet(Dictionary, out PdfStructureElement? walked) ? walked.Parent : null;

    /// <summary>Gets the structure type (<c>S</c>), or <see langword="null"/> when the element has none.</summary>
    /// <remarks>ISO 32000-2 §14.7.3, Table 355 (required).</remarks>
    public CosName? StructureType => StructureValues.Name(_context.Document, Dictionary, StructureNames.S);

    /// <summary>
    /// Gets the namespace the element is in: its <c>NS</c> namespace dictionary, or <see cref="PdfStructureNamespace.Pdf17"/>, the
    /// default namespace, when it has none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §14.7.4, Table 355 (<c>NS</c>, PDF 2.0) and §14.8.6.2: a namespace an element uses shall be listed in the root's
    /// <c>Namespaces</c> array; one that is not is still used, with a <c>NamespaceNotDeclared</c> diagnostic.
    /// </remarks>
    public PdfStructureNamespace Namespace
    {
        get
        {
            if (!Dictionary.TryGetValue(StructureNames.NS, out CosObject? value) || _context.Resolve(value) is not CosDictionary ns)
            {
                return PdfStructureNamespace.Pdf17;
            }

            if (!_context.IsDeclared(ns))
            {
                _context.Report(DiagnosticCodes.NamespaceNotDeclared, "A structure element's namespace is not in the structure tree root's Namespaces array (§14.8.6.2); used anyway.", Reference);
            }

            return _context.Namespace(ns, value as CosReference);
        }
    }

    /// <summary>Gets the standard structure type the element's type resolves to through the role maps, or <see langword="null"/> when it resolves to none.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.7.3, §14.7.4.2, §14.8.6.2; ISO/TS 32005 §5.6. In a tagged PDF (<see cref="PdfMarkInfo.Marked"/>) every element shall
    /// resolve to a standard type (§14.8.4.1); one that does not is a <c>StructureTypeUnresolved</c> diagnostic. See
    /// <see cref="RoleMapping"/> for the chain followed.
    /// </remarks>
    public PdfStructureType? StandardType
    {
        get
        {
            if (StructureType is not { } type)
            {
                return null;
            }

            RoleResolution resolution = _context.ResolveRole(type, Namespace);
            if (resolution.Standard is null && _context.IsTagged)
            {
                _context.Report(DiagnosticCodes.StructureTypeUnresolved, $"The structure type /{type.Value} does not resolve to a standard structure type through the role maps (§14.8.4.1).", Reference);
            }

            return resolution.Standard;
        }
    }

    /// <summary>
    /// Gets the role-mapping chain: the element's own type in its namespace, then each type the root's <c>RoleMap</c> (default
    /// namespace) or a namespace's <c>RoleMapNS</c> maps it to, until no map applies or a type repeats.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.7.3 (cycles are permitted, NOTE 2), §14.7.4.2, §14.8.6.2. Empty when the element has no type.</remarks>
    public IReadOnlyList<PdfStructureType> RoleMapping => StructureType is { } type ? _context.ResolveRole(type, Namespace).Chain : [];

    /// <summary>Gets the element identifier (<c>ID</c>), a byte string unique in the document, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355; looked up through <see cref="PdfStructureTreeRoot.FindElementById(ReadOnlySpan{byte})"/>.</remarks>
    public CosString? Id => StructureValues.Get(_context.Document, Dictionary, StructureNames.ID) as CosString;

    /// <summary>Gets the title (<c>T</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355.</remarks>
    public string? Title => StructureValues.Text(_context.Document, Dictionary, StructureNames.T);

    /// <summary>Gets the element's own natural language (<c>Lang</c>, a BCP 47 tag), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 1.4), §14.9.2.</remarks>
    public string? Language => StructureValues.Text(_context.Document, Dictionary, StructureNames.Lang);

    /// <summary>Gets the natural language in effect: <see cref="Language"/>, else the nearest ancestor's, else the catalog's <c>Lang</c>; <see langword="null"/> when none says.</summary>
    /// <remarks>ISO 32000-2 §14.9.2.3: a language specification applies to the element and everything nested in it.</remarks>
    public string? EffectiveLanguage
    {
        get
        {
            for (PdfStructureElement? element = this; element is not null; element = element.Parent)
            {
                if (element.Language is { } language)
                {
                    return language;
                }
            }

            return StructureValues.Text(_context.Document, _context.Document.Catalog, StructureNames.Lang);
        }
    }

    /// <summary>Gets the alternate description (<c>Alt</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355, §14.9.3.</remarks>
    public string? AlternateDescription => StructureValues.Text(_context.Document, Dictionary, StructureNames.Alt);

    /// <summary>Gets the expansion of an abbreviation or acronym (<c>E</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 1.5), §14.9.5.</remarks>
    public string? Expansion => StructureValues.Text(_context.Document, Dictionary, StructureNames.E);

    /// <summary>Gets the replacement text (<c>ActualText</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 1.4), §14.9.4.</remarks>
    public string? ActualText => StructureValues.Text(_context.Document, Dictionary, StructureNames.ActualText);

    /// <summary>Gets the element's revision number (<c>R</c>); 0 when absent.</summary>
    /// <remarks>ISO 32000-2 Table 355, §14.7.6.3 (deprecated in PDF 2.0).</remarks>
    public int Revision => StructureValues.Integer(_context.Document, Dictionary, StructureNames.R) is { } revision and >= 0 ? revision : 0;

    /// <summary>Gets the phonetic alphabet in effect for <see cref="Phoneme"/>: the element's <c>PhoneticAlphabet</c>, else the nearest ancestor's, else <c>ipa</c>.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 2.0), §14.9.6.</remarks>
    public string PhoneticAlphabet
    {
        get
        {
            for (PdfStructureElement? element = this; element is not null; element = element.Parent)
            {
                if (StructureValues.Name(_context.Document, element.Dictionary, StructureNames.PhoneticAlphabet) is { } alphabet)
                {
                    return alphabet.Value;
                }
            }

            return "ipa";
        }
    }

    /// <summary>Gets the pronunciation hint (<c>Phoneme</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 2.0), §14.9.6.</remarks>
    public string? Phoneme => StructureValues.Text(_context.Document, Dictionary, StructureNames.Phoneme);

    /// <summary>Gets the page named by the element's own <c>Pg</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355. Content items carry their effective page (<see cref="PdfMarkedContentReference.Page"/>).</remarks>
    public PdfPage? Page => _context.FindPage(Dictionary.TryGetValue(StructureNames.Pg, out CosObject? page) ? page : null);

    /// <summary>Gets the elements this element refers to (<c>Ref</c>), such as the footnote a reference points at.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 2.0).</remarks>
    public IReadOnlyList<PdfStructureElement> References
    {
        get
        {
            if (StructureValues.Get(_context.Document, Dictionary, StructureNames.Ref) is not CosArray array)
            {
                return [];
            }

            var references = new List<PdfStructureElement>(array.Count);
            foreach (CosObject item in array)
            {
                if (_context.Resolve(item) is CosDictionary element)
                {
                    references.Add(_context.Walk.TryGet(element, out PdfStructureElement? walked)
                        ? walked
                        : new PdfStructureElement(_context, element, item as CosReference, null, parentKnown: false, depth: 0));
                }
            }

            return references;
        }
    }

    /// <summary>Gets the associated files (<c>AF</c>) as their COS array, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 355 (PDF 2.0), §14.13. Typed by the file specification view (issue #76).</remarks>
    public CosArray? AssociatedFiles => StructureValues.Get(_context.Document, Dictionary, StructureNames.AF) as CosArray;

    /// <summary>Gets the attribute objects of the <c>A</c> entry, in order, each with its revision number.</summary>
    /// <remarks>ISO 32000-2 Table 355 (<c>A</c>), §14.7.6.1, §14.7.6.3.</remarks>
    public IReadOnlyList<PdfAttributeObject> Attributes =>
        AttributeReader.ReadObjects(_context, Dictionary.TryGetValue(StructureNames.A, out CosObject? value) ? value : null, Reference);

    /// <summary>Gets the attribute class names of the <c>C</c> entry, in order.</summary>
    /// <remarks>ISO 32000-2 Table 355 (<c>C</c>), §14.7.6.2.</remarks>
    public IReadOnlyList<CosName> ClassNames => [.. ReadClassNames().Select(static entry => entry.Name)];

    /// <summary>Gets the attribute objects the classes of <see cref="ClassNames"/> stand for in the root's <c>ClassMap</c>, in class order.</summary>
    /// <remarks>ISO 32000-2 §14.7.6.2. A class not in the class map contributes nothing.</remarks>
    public IReadOnlyList<PdfAttributeObject> ClassAttributes
    {
        get
        {
            var attributes = new List<PdfAttributeObject>();
            foreach ((CosName name, int revision) in ReadClassNames())
            {
                attributes.AddRange(AttributeReader.ReadClass(_context, name, revision, Reference));
            }

            return attributes;
        }
    }

    /// <summary>
    /// Gets the children (<c>K</c>), in logical order: structure elements, marked-content references and object references.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 Table 355 (<c>K</c>), §14.7.5, §14.8.2.5 (the order of <c>K</c> is the logical reading order). Entries that are not
    /// valid children, and children that would close a cycle or exceed the depth limit, are left out with a diagnostic.
    /// </remarks>
    public IReadOnlyList<PdfStructureItem> Children
    {
        get
        {
            var children = new List<PdfStructureItem>();
            foreach (CosObject entry in KidReader.Entries(_context, Dictionary))
            {
                Kid kid = KidReader.Classify(_context, entry, Reference);
                switch (kid.Kind)
                {
                    case KidKind.Element:
                        if (IsSelfOrAncestor(kid.Dictionary!))
                        {
                            _context.Report(DiagnosticCodes.StructTreeCycle, "A structure element's K holds the element itself or one of its ancestors (§14.7.2); left out.", kid.Reference ?? Reference);
                        }
                        else if (Depth + 1 >= StructureContext.MaxDepth)
                        {
                            _context.Report(DiagnosticCodes.StructTreeDepthExceeded, $"The structure hierarchy nests deeper than {StructureContext.MaxDepth} levels; the rest is not read.", Reference);
                        }
                        else
                        {
                            children.Add(new PdfStructureElement(_context, kid.Dictionary!, kid.Reference, this, parentKnown: true, Depth + 1));
                        }

                        break;
                    case KidKind.MarkedContent:
                        children.Add(CreateMarkedContent(kid));
                        break;
                    case KidKind.Object:
                        children.Add(CreateObjectReference(kid));
                        break;
                }
            }

            return children;
        }
    }

    /// <summary>The depth of the element below the root (a child of the root is 0), as far as the view knows it.</summary>
    internal int Depth { get; }

    /// <summary>Returns the content items among <see cref="Children"/>: the marked-content and object references, in order.</summary>
    /// <returns>The content items.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.1.1.</remarks>
    public IReadOnlyList<PdfStructureItem> GetContentItems() => [.. Children.Where(static item => item is not PdfStructureElement)];

    /// <summary>
    /// Returns the value of attribute <paramref name="name"/> of <paramref name="owner"/> in effect for this element: from its
    /// <c>A</c> entry, else from its classes (<c>C</c>), else inherited from the parent when the attribute is inheritable, else the
    /// attribute's default.
    /// </summary>
    /// <param name="owner">The attribute owner, such as <c>Layout</c>, <c>List</c>, <c>PrintField</c> or <c>Table</c>.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The value, or <see langword="null"/> when no source gives one (or the default depends on the element, such as <c>Placement</c>).</returns>
    /// <remarks>
    /// ISO 32000-2 §14.8.5.3 steps 2-5 and §14.7.6.2: within <c>A</c>, and within the classes, a later object wins; <c>A</c> wins over
    /// <c>C</c>. Inheritable attributes and defaults follow Tables 378-385 (errata Table 377); <c>TextDecorationType</c> is not
    /// inherited. Format owners (step 1, <c>CSS-3</c> and the like) are not consulted.
    /// </remarks>
    public CosObject? GetAttributeValue(CosName owner, CosName name)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(name);
        for (PdfStructureElement? element = this; element is not null; element = element.Parent)
        {
            if ((Find(element.Attributes, owner, name) ?? Find(element.ClassAttributes, owner, name)) is { } value)
            {
                return value;
            }

            if (!StandardAttributes.IsInheritable(owner, name))
            {
                break;
            }
        }

        return StandardAttributes.DefaultValue(owner, name);
    }

    /// <inheritdoc/>
    public bool Equals(PdfStructureElement? other) => other is not null && ReferenceEquals(Dictionary, other.Dictionary);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfStructureElement);

    /// <inheritdoc/>
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Dictionary);

    /// <inheritdoc/>
    public override string ToString() => Reference is { } reference ? $"{StructureType?.Value} {reference}" : StructureType?.Value ?? string.Empty;

    /// <summary>Creates the marked-content reference for a classified <c>K</c> entry, with its effective page.</summary>
    internal PdfMarkedContentReference CreateMarkedContent(Kid kid)
    {
        CosDictionary? mcr = kid.Dictionary;
        CosStream? stream = mcr is null ? null : StructureValues.Get(_context.Document, mcr, StructureNames.Stm) as CosStream;
        CosObject? owner = mcr is null ? null : StructureValues.Get(_context.Document, mcr, StructureNames.StmOwn);
        CosDictionary? pageObject = EffectivePage(mcr, kid.Mcid, reportMissing: stream is null);
        PdfPage? page = pageObject is not null && _context.Document.Pages.TryGetPage(pageObject, out PdfPage? found) ? found : null;
        return new PdfMarkedContentReference(this, mcr, kid.Mcid, page, pageObject, stream, owner);
    }

    /// <summary>Creates the object reference for a classified <c>K</c> entry.</summary>
    internal PdfObjectReference CreateObjectReference(Kid kid)
    {
        CosDictionary objr = kid.Dictionary!;
        objr.TryGetValue(StructureNames.Obj, out CosObject? target);
        CosObject referenced = _context.Resolve(target);
        CosDictionary? pageObject = StructureValues.Get(_context.Document, objr, StructureNames.Pg) as CosDictionary
            ?? StructureValues.Get(_context.Document, Dictionary, StructureNames.Pg) as CosDictionary
            ?? (referenced is CosDictionary annotation ? StructureValues.Get(_context.Document, annotation, StructureNames.P) as CosDictionary : null);
        PdfPage? page = pageObject is not null && _context.Document.Pages.TryGetPage(pageObject, out PdfPage? found) ? found : null;
        return new PdfObjectReference(this, objr, referenced, target as CosReference, page);
    }

    /// <summary>
    /// The page object of a marked-content item: the MCR's <c>Pg</c>, else the element's (Table 357); else, as a repair, the nearest
    /// ancestor's, with a <c>StructElemPageMissing</c> diagnostic. Failing that, the page whose parent-tree array lists the element at
    /// that MCID.
    /// </summary>
    internal CosDictionary? EffectivePage(CosDictionary? item, int mcid, bool reportMissing)
    {
        if (item is not null && StructureValues.Get(_context.Document, item, StructureNames.Pg) is CosDictionary itemPage)
        {
            return itemPage;
        }

        if (StructureValues.Get(_context.Document, Dictionary, StructureNames.Pg) is CosDictionary ownPage)
        {
            return ownPage;
        }

        for (PdfStructureElement? ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (StructureValues.Get(_context.Document, ancestor.Dictionary, StructureNames.Pg) is CosDictionary inherited)
            {
                _context.Report(DiagnosticCodes.StructElemPageMissing, "A structure element holds MCIDs but has no Pg (Table 355); the nearest ancestor's page is used.", Reference);
                return inherited;
            }
        }

        if (!reportMissing)
        {
            return null;
        }

        CosDictionary? fromParentTree = _context.PageFromParentTree(Dictionary, mcid);
        _context.Report(
            DiagnosticCodes.StructElemPageMissing,
            fromParentTree is null
                ? "A structure element holds MCIDs but no Pg names their page (Table 355); their page is unknown."
                : "A structure element holds MCIDs but no Pg names their page (Table 355); the page whose parent-tree entry lists it is used.",
            Reference);
        return fromParentTree;
    }

    private static CosObject? Find(IReadOnlyList<PdfAttributeObject> attributes, CosName owner, CosName name)
    {
        for (int index = attributes.Count - 1; index >= 0; index--)
        {
            PdfAttributeObject candidate = attributes[index];
            if (owner.Equals(candidate.Owner) && candidate.GetValue(name) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private List<(CosName Name, int Revision)> ReadClassNames() =>
        AttributeReader.ReadClassNames(_context, Dictionary.TryGetValue(StructureNames.C, out CosObject? value) ? value : null, Reference);

    private bool IsSelfOrAncestor(CosDictionary candidate)
    {
        int steps = 0;
        for (PdfStructureElement? element = this; element is not null && steps <= StructureContext.MaxDepth; element = element.Parent, steps++)
        {
            if (ReferenceEquals(element.Dictionary, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
