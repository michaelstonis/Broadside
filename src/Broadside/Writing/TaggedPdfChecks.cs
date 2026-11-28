using System.Text.RegularExpressions;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Writing;

/// <summary>
/// The tagged-PDF predicates the PDF/UA and PDF/A level A checks share (issue #650): each checker reports them under its own
/// standard's rule identifiers, never the other's.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7.3 (role maps), §14.8 (tagged PDF), §14.9.2 (natural language), §14.9.3 (alternate descriptions); ISO 19005-2
/// §6.7 and ISO 19005-3 §6.7 (level A); ISO 14289-1 §7.1, §7.2 and §7.3.
/// </remarks>
internal static partial class TaggedPdfChecks
{
    private static readonly CosName TU = new("TU");
    private static readonly CosName ParentKey = new("Parent");

    /// <summary>How a structure element describes itself in words other than its content.</summary>
    public enum Description
    {
        /// <summary>An <c>Alt</c> or <c>ActualText</c> that is not auto-tagging's placeholder.</summary>
        Present,

        /// <summary>Neither <c>Alt</c> nor <c>ActualText</c>.</summary>
        Missing,

        /// <summary>The placeholder <c>Alt</c> auto-tagging wrote and nobody replaced.</summary>
        Placeholder,
    }

    /// <summary>Whether <paramref name="language"/> is a well-formed language tag, as veraPDF tests it (PDF/UA-1 7.2-29, PDF/A-2 6.7.4-1).</summary>
    /// <remarks>ISO 32000-2 §14.9.2.2.</remarks>
    public static bool IsLanguageTag(string language) => LanguageTag().IsMatch(language);

    [GeneratedRegex("^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTag();

    /// <summary>Whether the document is tagged: <c>MarkInfo</c> <c>Marked</c> true and a structure tree in the catalog (§14.8.1, §14.7.2).</summary>
    public static bool IsTagged(PdfDocument document) => document.MarkInfo.Marked && document.StructureTree is not null;

    /// <summary>The standard structure types of the PDF 1.7 set the root's <c>RoleMap</c> remaps (§14.7.3, §14.8.4).</summary>
    public static IEnumerable<string> RemappedStandardTypes(PdfStructureTreeRoot root) =>
        root.RoleMap is { } roleMap
            ? roleMap.Keys.Select(type => type.Value).Where(PdfStructureNamespace.Pdf17.IsStandardType)
            : [];

    /// <summary>
    /// Whether the element's type is a standard type of the PDF 1.7 set or role-maps to one (§14.7.3, §14.8.4): the test of PDF/A-2
    /// and PDF/A-3, which predate structure namespaces.
    /// </summary>
    public static bool MapsToPdf17StandardType(PdfStructureElement element) =>
        element.ResolvedStandardType is { } standard && PdfStructureNamespace.Pdf17.IsStandardType(standard.Name);

    /// <summary>Whether the element's own type (before role mapping) is a standard type of the PDF 1.7 set.</summary>
    public static bool HasPdf17StandardType(PdfStructureElement element) =>
        element.StructureType is { } type && PdfStructureNamespace.Pdf17.IsStandardType(type.Value);

    /// <summary>Whether the role-mapping chain of the element's type returns to a type it has already visited (§14.7.3, NOTE 2).</summary>
    public static bool HasCircularMapping(PdfStructureElement element) => element.RoleMappingEndsInCycle;

    /// <summary>Whether the element gives any content it encloses directly (a marked-content reference): text a language applies to.</summary>
    public static bool EnclosesContent(PdfStructureElement element) => element.Children.Any(static kid => kid is PdfMarkedContentReference);

    /// <summary>
    /// How the element is described: an <c>Alt</c> or an <c>ActualText</c> (§14.9.3, §14.9.4), or neither, or only the placeholder
    /// <c>Alt</c> of <see cref="PdfDocument.AutoTag(Layout.AutoTaggingOptions)"/>.
    /// </summary>
    public static Description Describe(PdfDocument document, PdfStructureElement element)
    {
        if (element.ActualText is not null)
        {
            return Description.Present;
        }

        if (string.IsNullOrEmpty(element.AlternateDescription))
        {
            return Description.Missing;
        }

        return document.PlaceholderAlternateText is { } placeholders
            && placeholders.TryGetValue(element.Dictionary, out string? placeholder) && element.AlternateDescription == placeholder
            ? Description.Placeholder
            : Description.Present;
    }

    /// <summary>Whether a widget's field (the widget itself when merged, else its parent) has a non-empty <c>TU</c> (ISO 32000-2 §12.7.4.1, Table 226).</summary>
    public static bool HasToolTip(PdfDocument document, CosDictionary widget) =>
        document.Resolve(widget.GetValueOrDefault(TU)) is CosString { Bytes.Length: > 0 }
        || (document.Resolve(widget.GetValueOrDefault(ParentKey)) is CosDictionary field && document.Resolve(field.GetValueOrDefault(TU)) is CosString { Bytes.Length: > 0 });
}

/// <summary>What a <see cref="TaggedContentWalker"/> tells its checker while it walks a page's content.</summary>
internal interface ITaggedContentVisitor
{
    /// <summary>A marked-content sequence begins (before <paramref name="state"/> records it).</summary>
    /// <param name="state">The marked content open where it begins.</param>
    /// <param name="properties">Its property list, inline or from the resources, or <see langword="null"/>.</param>
    /// <param name="artifact">Whether it is an <c>Artifact</c> sequence.</param>
    /// <param name="tagged">Whether its MCID is in the structure tree.</param>
    void MarkedContentBegun(TaggedContentState state, CosDictionary? properties, bool artifact, bool tagged);

    /// <summary>An operator paints (a path, text, a shading, an image).</summary>
    void Painted(TaggedContentState state);

    /// <summary>A <c>Tf</c> selects the font resource <paramref name="font"/>.</summary>
    void FontSet(CosObject font);

    /// <summary>A text-showing operator shows <paramref name="text"/> in <paramref name="font"/>, after <see cref="Painted"/>.</summary>
    void TextShown(PdfFont font, string resourceName, ReadOnlySpan<byte> text, TaggedContentState state);
}

/// <summary>
/// The walk of a page's content that tagged-PDF checks share (issue #650, from the PDF/UA check of issue #204): marked content, the
/// operators that paint, and the form XObjects the content draws, each walked inside the marked content it is drawn in.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.6 (marked content), §14.7.5 (structure content), §14.8.2.2 (real content and artifacts), §7.8.2 (content streams);
/// ISO 19005-2 §6.7 and ISO 19005-3 §6.7; ISO 14289-1 §7.1.
/// </remarks>
internal sealed class TaggedContentWalker(PdfDocument document, PdfStructureTreeRoot? root, ITaggedContentVisitor visitor)
{
    private static readonly CosName Font = new("Font");
    private static readonly CosName MCID = new("MCID");
    private static readonly CosName Properties = new("Properties");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName ActualText = new("ActualText");

    private readonly HashSet<CosObject> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly OperandArena _arena = new();

    /// <summary>Walks the page's content streams as one (§7.8.2: marked content may span them) and the forms it draws.</summary>
    public void WalkPage(PdfPage page)
    {
        var state = new TaggedContentState(page.Reference, root?.GetMarkedContentElements(page));
        switch (document.Resolve(page.Dictionary.GetValueOrDefault(KnownNames.Contents)))
        {
            case CosStream stream:
                Walk(stream, page.Resources, state);
                break;
            case CosArray parts:
                foreach (CosObject part in parts)
                {
                    if (document.Resolve(part) is CosStream stream)
                    {
                        Walk(stream, page.Resources, state);
                    }
                }

                break;
        }
    }

    private CosDictionary? Dictionary(CosDictionary owner, CosName key) => document.Resolve(owner.GetValueOrDefault(key)) switch
    {
        CosDictionary dictionary => dictionary,
        CosStream stream => stream.Dictionary,
        _ => null,
    };

    private void Walk(CosStream stream, CosDictionary? resources, TaggedContentState state)
    {
        ReadOnlyMemory<byte> data;
        try
        {
            data = document.DecodeStream(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        var forms = new List<(CosStream Form, TaggedContentState State)>();
        _arena.Clear();
        _arena.Overflowed = false;
        var reader = new ContentReader(data.Span, _arena);
        PdfFont? font = null;
        string fontName = string.Empty;
        while (reader.Next(out ReadOperator op))
        {
            ContentOperands operands = _arena.Operands;
            switch (op.Code)
            {
                case ContentOperatorCode.BeginMarkedContent or ContentOperatorCode.BeginMarkedContentWithProperties:
                    BeginMarkedContent(state, operands, resources);
                    break;
                case ContentOperatorCode.EndMarkedContent:
                    state.End();
                    break;
                case ContentOperatorCode.SetFont when operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name:
                    fontName = System.Text.Encoding.ASCII.GetString(operands[0].Bytes);
                    CosObject? fontObject = resources is null ? null : Dictionary(resources, Font)?.GetValueOrDefault(new CosName(fontName));
                    font = document.GetFont(fontObject);
                    if (fontObject is not null)
                    {
                        visitor.FontSet(fontObject);
                    }

                    break;
                case ContentOperatorCode.ShowText or ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing:
                    visitor.Painted(state);
                    if (font is not null && operands.Count > 0 && operands[operands.Count - 1].Kind == ContentOperandKind.String)
                    {
                        visitor.TextShown(font, fontName, operands[operands.Count - 1].Bytes, state);
                    }

                    break;
                case ContentOperatorCode.ShowTextAdjusted:
                    visitor.Painted(state);
                    if (font is not null && operands.Count > 0 && operands[0].Kind == ContentOperandKind.Array)
                    {
                        foreach (ContentOperand item in operands[0].Items)
                        {
                            if (item.Kind == ContentOperandKind.String)
                            {
                                visitor.TextShown(font, fontName, item.Bytes, state);
                            }
                        }
                    }

                    break;
                case ContentOperatorCode.Stroke or ContentOperatorCode.CloseAndStroke or ContentOperatorCode.Fill or ContentOperatorCode.FillObsolete
                    or ContentOperatorCode.FillEvenOdd or ContentOperatorCode.FillAndStroke or ContentOperatorCode.FillEvenOddAndStroke
                    or ContentOperatorCode.CloseFillAndStroke or ContentOperatorCode.CloseFillEvenOddAndStroke or ContentOperatorCode.PaintShading
                    or ContentOperatorCode.BeginInlineImage:
                    visitor.Painted(state);
                    break;
                case ContentOperatorCode.PaintXObject when operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name && resources is not null:
                    CosObject? xobject = Dictionary(resources, XObject)?.GetValueOrDefault(new CosName(System.Text.Encoding.ASCII.GetString(operands[0].Bytes)));
                    if (document.Resolve(xobject) is CosStream drawn)
                    {
                        if (document.Resolve(drawn.Dictionary.GetValueOrDefault(Subtype)) is CosName { Value: "Form" })
                        {
                            forms.Add((drawn, state.Inherited(root?.GetMarkedContentElements(drawn))));
                        }
                        else
                        {
                            visitor.Painted(state);
                        }
                    }

                    break;
            }

            _arena.Clear();
        }

        _arena.Trim();
        foreach ((CosStream form, TaggedContentState inherited) in forms)
        {
            if (_visited.Add(form))
            {
                Walk(form, Dictionary(form.Dictionary, KnownNames.Resources) ?? resources, inherited);
                _visited.Remove(form);
            }
        }
    }

    private void BeginMarkedContent(TaggedContentState state, ContentOperands operands, CosDictionary? resources)
    {
        string tag = operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name ? System.Text.Encoding.ASCII.GetString(operands[0].Bytes) : string.Empty;
        CosDictionary? properties = null;
        if (operands.Count > 1)
        {
            properties = operands[1].Kind == ContentOperandKind.Name
                ? (resources is null ? null : Dictionary(resources, Properties) is { } named ? document.Resolve(named.GetValueOrDefault(new CosName(System.Text.Encoding.ASCII.GetString(operands[1].Bytes)))) as CosDictionary : null)
                : operands[1].ToCosObject() as CosDictionary;
        }

        bool artifact = tag == "Artifact";
        PdfStructureElement? element = null;
        bool tagged = !artifact && properties is not null && document.Resolve(properties.GetValueOrDefault(MCID)) is CosInteger mcid && state.TryGetElement((int)mcid.Value, out element);
        bool actualText = properties is not null && document.Resolve(properties.GetValueOrDefault(ActualText)) is CosString;
        for (PdfStructureElement? ancestor = element; !actualText && ancestor is not null; ancestor = ancestor.Parent)
        {
            actualText = ancestor.ActualText is not null;
        }

        visitor.MarkedContentBegun(state, properties, artifact, tagged);
        state.Begin(artifact, tagged, actualText);
    }
}

/// <summary>The marked content open at a point of a content stream, and the MCIDs its structure tree knows.</summary>
internal sealed class TaggedContentState(CosReference? page, IReadOnlyDictionary<int, PdfStructureElement>? tree, bool tagged = false, bool artifact = false, bool actualText = false)
{
    private readonly List<(bool Artifact, bool Tagged, bool ActualText)> _open = [];

    /// <summary>The page whose content this is.</summary>
    public CosReference? Page { get; } = page;

    /// <summary>Whether the content here is in the structure tree.</summary>
    public bool Tagged => tagged || _open.Exists(mark => mark.Tagged);

    /// <summary>Whether the content here is marked as an artifact.</summary>
    public bool Artifact => artifact || _open.Exists(mark => mark.Artifact);

    /// <summary>Whether the content here has replacement text: an open sequence's <c>ActualText</c>, or its structure element's or an ancestor's (§14.9.4).</summary>
    public bool HasActualText => actualText || _open.Exists(mark => mark.ActualText);

    /// <summary>The structure element of an MCID of this content stream, when the structure tree has it.</summary>
    public bool TryGetElement(int mcid, out PdfStructureElement? element)
    {
        element = null;
        return tree is not null && tree.TryGetValue(mcid, out element);
    }

    /// <summary>Records a marked-content sequence that begins.</summary>
    public void Begin(bool isArtifact, bool isTagged, bool hasActualText) => _open.Add((isArtifact, isTagged, hasActualText));

    /// <summary>Records the end of the innermost open sequence.</summary>
    public void End()
    {
        if (_open.Count > 0)
        {
            _open.RemoveAt(_open.Count - 1);
        }
    }

    /// <summary>The state a form XObject drawn here starts in: inside what is open, with its own MCIDs.</summary>
    public TaggedContentState Inherited(IReadOnlyDictionary<int, PdfStructureElement>? formTree) => new(Page, formTree, Tagged, Artifact, HasActualText);
}
