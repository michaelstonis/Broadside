using System.Globalization;
using System.Text.RegularExpressions;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Writing;

/// <summary>
/// The check step of a PDF/UA-1 save (issue #204): the failure conditions of ISO 14289-1 a document can be checked for before it is
/// written, each reported under the identifier of the veraPDF PDF/UA-1 validation-profile rule that would fail (clause and test number,
/// e.g. <c>7.3-1</c>; the Matterhorn Protocol 1.1 lists the same conditions). Reads the document, never changes it. Part 2 (PDF/UA-2 and
/// Well-Tagged PDF, issue #205) reports the same conditions under veraPDF's PDF/UA-2 rule identifiers (ISO 14289-2 clause and test,
/// or an ISO/TS 32005 <c>Table 5.</c> containment rule), drops those PDF/UA-2 does not have (Suspects, heading nesting, Note IDs,
/// annotation descriptions), and adds a single PDF 2.0 <c>Document</c> root, no <c>H</c> or <c>Note</c>, <c>ListNumbering</c> on lists
/// with labels, links in <c>Link</c> or <c>Reference</c>, and <c>Annot</c> only for markup annotations.
/// </summary>
/// <remarks>
/// <para>
/// Checked: the structure tree and <c>MarkInfo</c> <c>Suspects</c> (7.1-4, 7.1-11), real content tagged and artifacts not mixed with it
/// (7.1-1 to 7.1-3, page content and the form XObjects it draws), structure types standard or role-mapped to one and standard types
/// not remapped (7.1-5, 7.1-7), the title (7.1-9), the catalog language and every <c>Lang</c> well formed (7.2-29, 7.2-33), table and
/// list nesting (7.2-3 to 7.2-20, 7.2-26, 7.2-27, 7.2-36 to 7.2-40), figures' alternate text (7.3-1), heading nesting and the H/Hn
/// mix (7.4.2-1, 7.4.4-1 to 7.4.4-3), header cells' scope (7.5-1), notes' IDs (7.9-1, 7.9-2), dynamic XFA (7.15-1), annotations
/// tagged and described (7.18.1-1, 7.18.1-2, 7.18.2-1, 7.18.4-1, 7.18.5-1, 7.18.5-2), and every font used embedded and mapped to
/// Unicode (7.21.4.1-1, 7.21.7-1, 7.21.7-2).
/// </para>
/// <para>
/// What the save itself writes is not checked here: identification (5-1 to 5-5), metadata (7.1-8), <c>DisplayDocTitle</c> (7.1-10),
/// <c>MarkInfo</c> <c>Marked</c> (6.2-1) and <c>Tabs</c> (7.18.3-1); the header version (6.1-1) is checked against the save's planned
/// features. Not checked (the flow layout never produces them; a document from elsewhere may): table regularity (7.2-15, 7.2-41 to
/// 7.2-43), outline and form-field languages (7.2-2, 7.2-25), optional content configurations (7.10), embedded file specifications
/// (7.11-1), encryption permissions (7.16-1), media clips (7.18.6.2), reference XObjects (7.20), font program internals and CMaps
/// (7.21.3, 7.21.4.1-2, 7.21.4.2, 7.21.5, 7.21.6, 7.21.8).
/// </para>
/// </remarks>
internal sealed partial class PdfUAChecker
{
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName F = new("F");
    private static readonly CosName Font = new("Font");
    private static readonly CosName Headers = new("Headers");
    private static readonly CosName MCID = new("MCID");
    private static readonly CosName NeedsRendering = new("NeedsRendering");
    private static readonly CosName Properties = new("Properties");
    private static readonly CosName Scope = new("Scope");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Table = new("Table");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName DescendantFonts = new("DescendantFonts");
    private static readonly CosName FontDescriptor = new("FontDescriptor");
    private static readonly CosName BaseFont = new("BaseFont");
    private static readonly CosName[] FontFiles = [new("FontFile"), new("FontFile2"), new("FontFile3")];

    /// <summary>The kids each grouping element may have (7.2-3, 7.2-10, 7.2-19, 7.2-20, 7.2-27, 7.2-36 to 7.2-38).</summary>
    private static readonly Dictionary<string, (string Rule, string[] Kids)> AllowedKids = new(StringComparer.Ordinal)
    {
        ["Table"] = ("7.2-3", ["TR", "THead", "TBody", "TFoot", "Caption"]),
        ["TR"] = ("7.2-10", ["TH", "TD"]),
        ["THead"] = ("7.2-36", ["TR"]),
        ["TBody"] = ("7.2-37", ["TR"]),
        ["TFoot"] = ("7.2-38", ["TR"]),
        ["L"] = ("7.2-19", ["L", "LI", "Caption"]),
        ["LI"] = ("7.2-20", ["Lbl", "LBody"]),
        ["TOC"] = ("7.2-27", ["TOC", "TOCI", "Caption"]),
    };

    /// <summary>The parents each element may have (7.2-4 to 7.2-9, 7.2-17, 7.2-18, 7.2-26).</summary>
    private static readonly Dictionary<string, (string Rule, string[] Parents)> AllowedParents = new(StringComparer.Ordinal)
    {
        ["TR"] = ("7.2-4", ["Table", "THead", "TBody", "TFoot"]),
        ["THead"] = ("7.2-5", ["Table"]),
        ["TBody"] = ("7.2-6", ["Table"]),
        ["TFoot"] = ("7.2-7", ["Table"]),
        ["TH"] = ("7.2-8", ["TR"]),
        ["TD"] = ("7.2-9", ["TR"]),
        ["LI"] = ("7.2-17", ["L"]),
        ["LBody"] = ("7.2-18", ["LI"]),
        ["TOCI"] = ("7.2-26", ["TOC"]),
    };

    /// <summary>
    /// The PDF/UA-2 rule (ISO 14289-2, or an ISO/TS 32005 containment rule, veraPDF PDF/UA-2 profile) each PDF/UA-1 failure condition
    /// the checker finds is reported under in a PDF/UA-2 check (issue #205); <see langword="null"/> when PDF/UA-2 has no such rule.
    /// </summary>
    private static readonly Dictionary<string, string?> PdfUA2Rules = new(StringComparer.Ordinal)
    {
        ["7.1-1"] = null,
        ["7.1-2"] = null,
        ["7.1-3"] = "8.2.2-1",
        ["7.1-4"] = null,
        ["7.1-5"] = "8.2.4-1",
        ["7.1-7"] = "8.2.4-4",
        ["7.1-9"] = "8.11.1-1",
        ["7.1-11"] = "8.2.1-1",
        ["7.2-11"] = "Table 5. Table-THead-1",
        ["7.2-12"] = "Table 5. Table-TFoot-1",
        ["7.2-13"] = null,
        ["7.2-14"] = null,
        ["7.2-16"] = "8.2.5.27-1",
        ["7.2-29"] = "8.4.4-2",
        ["7.2-33"] = "8.4.4-1",
        ["7.2-39"] = "Table 5. Table-Caption-1",
        ["7.2-40"] = "8.2.5.27-1",
        ["7.3-1"] = "8.2.5.28.2-1",
        ["7.4.2-1"] = null,
        ["7.4.4-1"] = null,
        ["7.4.4-2"] = null,
        ["7.5-1"] = "8.2.5.26-5",
        ["7.9-1"] = null,
        ["7.9-2"] = null,
        ["7.15-1"] = "8.10.1-3",
        ["7.18.1-1"] = "8.9.2.3-1",
        ["7.18.1-2"] = null,
        ["7.18.2-1"] = "8.9.2.4.15-1",
        ["7.18.4-1"] = "8.10.1-1",
        ["7.18.5-1"] = "8.2.5.20-1",
        ["7.18.5-2"] = null,
        ["7.21.4.1-1"] = "8.4.5.5.1-1",
        ["7.21.7-1"] = "8.4.5.8-1",
        ["7.21.7-2"] = "8.4.5.8-2",
    };

    /// <summary>The markup annotation subtypes (ISO 32000-2 §12.5.6.2, Table 172), which PDF/UA-2 encloses in <c>Annot</c> elements (8.9.2.3-1).</summary>
    private static readonly HashSet<string> MarkupAnnotations = new(StringComparer.Ordinal)
    {
        "Text", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly", "StrikeOut", "Caret",
        "Stamp", "Ink", "FileAttachment", "Sound", "Redact", "Projection",
    };

    private static readonly CosName ListOwner = new("List");
    private static readonly CosName ListNumbering = new("ListNumbering");

    private readonly PdfDocument _document;
    private readonly int _part;
    private readonly List<PdfAViolation> _violations = [];
    private readonly HashSet<(string, string)> _reported = [];
    private readonly HashSet<CosObject> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CosObject> _fonts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _noteIds = new(StringComparer.Ordinal);
    private readonly OperandArena _arena = new();
    private PdfStructureTreeRoot? _root;
    private int _headingLevel;
    private bool _usesH;
    private bool _usesHn;

    private PdfUAChecker(PdfDocument document, int part)
    {
        _document = document;
        _part = part;
    }

    /// <summary>
    /// Lists the PDF/UA rules a save of <paramref name="document"/> cannot meet, in document order; empty when it can conform. Part 2
    /// reports under veraPDF's PDF/UA-2 rule identifiers, which Well-Tagged PDF 1.0 shares (issue #205).
    /// </summary>
    /// <param name="document">The document, as it is: nothing is changed.</param>
    /// <param name="part">The part of ISO 14289: 1 or 2.</param>
    public static IReadOnlyList<PdfAViolation> Check(PdfDocument document, int part = 1)
    {
        var checker = new PdfUAChecker(document, part);
        checker.CheckCatalog();
        checker.CheckStructure();
        foreach (PdfPage page in document.Pages)
        {
            checker.CheckPage(page);
        }

        return checker._violations;
    }

    /// <summary>Whether <paramref name="language"/> is a well-formed language tag, as veraPDF's rule 7.2-29 tests it.</summary>
    public static bool IsLanguageTag(string language) => LanguageTag().IsMatch(language);

    [GeneratedRegex("^[a-zA-Z]{1,8}(-[a-zA-Z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTag();

    private void Report(string rule, string message, CosReference? reference = null)
    {
        if (_part == 2 && rule[0] == '7')
        {
            if (PdfUA2Rules[rule] is not { } translated)
            {
                return;
            }

            rule = translated;
        }

        if (_reported.Add((rule, message)))
        {
            _violations.Add(new PdfAViolation(rule, message, reference));
        }
    }

    private CosObject Resolve(CosObject? value) => _document.Resolve(value);

    private CosDictionary? Dictionary(CosDictionary owner, CosName key) => Resolve(owner.GetValueOrDefault(key)) switch
    {
        CosDictionary dictionary => dictionary,
        CosStream stream => stream.Dictionary,
        _ => null,
    };

    private string? NameOf(CosDictionary owner, CosName key) => (Resolve(owner.GetValueOrDefault(key)) as CosName)?.Value;

    private void CheckCatalog()
    {
        _root = _document.StructureTree;
        if (_root is null)
        {
            Report("7.1-11", "the document has no structure tree (no StructTreeRoot in the catalog)");
        }

        if (_document.MarkInfo.Suspects)
        {
            Report("7.1-4", "the catalog's MarkInfo has Suspects true");
        }

        if (string.IsNullOrWhiteSpace(_document.Properties.Title))
        {
            Report("7.1-9", "the document has no title (dc:title), which a viewer shows in its window");
        }

        string? language = _document.Language;
        if (string.IsNullOrEmpty(language))
        {
            Report("7.2-33", "the catalog has no Lang entry: the language of the metadata, and of text no structure element gives a language to, cannot be determined");
        }
        else if (!IsLanguageTag(language))
        {
            Report("7.2-29", $"the catalog's Lang '{language}' is not a well-formed language tag");
        }

        if (Resolve(_document.Catalog.GetValueOrDefault(NeedsRendering)) is CosBoolean { Value: true })
        {
            Report("7.15-1", "the catalog's NeedsRendering is true (a dynamic XFA form)");
        }
    }

    private void CheckStructure()
    {
        if (_root is null)
        {
            return;
        }

        if (_root.RoleMap is { } roleMap)
        {
            foreach ((CosName type, CosObject _) in roleMap)
            {
                if (PdfStructureNamespace.Pdf17.IsStandardType(type.Value))
                {
                    Report("7.1-7", $"the role map remaps the standard structure type {type.Value}");
                }
            }
        }

        if (_part == 2)
        {
            // ISO 14289-2 §8.2.5.2: one Document element, of the PDF 2.0 namespace, is the structure tree root's only child.
            PdfStructureType? document = _root.Children is [PdfStructureElement only] ? only.StandardType : null;
            if (document?.Name != "Document")
            {
                Report("8.2.5.2-1", "the structure tree root does not have a single Document element as its only child", _root.Reference);
            }
            else if (!document.Namespace.Equals(PdfStructureNamespace.Pdf20))
            {
                Report("8.2.5.2-2", "the structure tree root's Document element is not of the PDF 2.0 structure namespace (a PDF/UA-1 flow's FlowStructureNamespace.Pdf17)", _root.Reference);
            }
        }

        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        foreach (PdfStructureElement element in _root.Children)
        {
            CheckElement(element, null, seen);
        }

        if (_usesH && _usesHn)
        {
            Report("7.4.4-2", "the structure tree uses both H and numbered headings (H1 to H6)");
        }
    }

    private void CheckElement(PdfStructureElement element, string? parentType, HashSet<CosDictionary> seen)
    {
        if (!seen.Add(element.Dictionary))
        {
            return;
        }

        CosReference? reference = element.Reference;
        string? type = element.StandardType?.Name;
        if (type is null)
        {
            Report("7.1-5", $"the structure type {element.StructureType?.Value ?? "(none)"} is not role-mapped to a standard type", reference);
        }

        if (element.Language is { } language && !IsLanguageTag(language))
        {
            Report("7.2-29", $"a structure element's Lang '{language}' is not a well-formed language tag", reference);
        }

        List<PdfStructureElement> kids = [.. element.Children.OfType<PdfStructureElement>()];
        List<string?> kidTypes = [.. kids.Select(kid => kid.StandardType?.Name)];
        switch (type)
        {
            case "Figure" when string.IsNullOrEmpty(element.AlternateDescription) && element.ActualText is null:
                Report("7.3-1", "a Figure has no alternate text (Alt) or replacement text (ActualText)", reference);
                break;
            case "Note":
                string? id = element.Id is { } value ? System.Text.Encoding.Latin1.GetString(value.Bytes) : null;
                if (id is null)
                {
                    Report("7.9-1", "a Note has no ID", reference);
                }
                else if (!_noteIds.Add(id))
                {
                    Report("7.9-2", $"two Notes have the ID '{id}'", reference);
                }

                break;
            case "Table":
                CheckTable(element, kidTypes, reference);
                break;
            case "L" when kidTypes.IndexOf("Caption") > 0:
                Report("7.2-40", "a list's Caption is not its first kid", reference);
                break;
            case "H":
                _usesH = true;
                break;
            case ['H', >= '1' and <= '6']:
                _usesHn = true;
                int level = type[1] - '0';
                if (level > _headingLevel + 1)
                {
                    Report("7.4.2-1", string.Create(CultureInfo.InvariantCulture, $"an {type} follows {(_headingLevel == 0 ? "no heading" : $"an H{_headingLevel}")}: heading levels are skipped"), reference);
                }

                _headingLevel = level;
                break;
        }

        if (type is not null && AllowedKids.TryGetValue(type, out (string Rule, string[] Kids) allowedKids))
        {
            foreach (string? kid in kidTypes.Where(kid => kid is not null && !allowedKids.Kids.Contains(kid)))
            {
                Report(_part == 2 ? Containment(type, kid!) : allowedKids.Rule, $"a {type} element has a {kid} kid", reference);
            }
        }

        if (type is not null && AllowedParents.TryGetValue(type, out (string Rule, string[] Parents) allowedParents) && (parentType is null || !allowedParents.Parents.Contains(parentType)))
        {
            Report(_part == 2 ? Containment(parentType ?? "StructTreeRoot", type) : allowedParents.Rule, $"a {type} element is in a {parentType ?? "(root)"} element", reference);
        }

        if (_part == 2)
        {
            CheckPdfUA2Element(element, type, kids, reference);
        }

        if (kidTypes.Count(kid => kid == "H") > 1)
        {
            Report("7.4.4-1", "a structure element has more than one H kid", reference);
        }

        foreach (PdfStructureElement kid in kids)
        {
            CheckElement(kid, type, seen);
        }
    }

    /// <summary>
    /// The element rules PDF/UA-2 adds (issue #205): no <c>H</c> (ISO 14289-2 §8.2.5.12, rule 8.2.5.12-1), no <c>Note</c> of the PDF 1.7
    /// namespace (§8.2.5.14, 8.2.5.14-1; PDF 2.0 has <c>FENote</c>), and a <c>ListNumbering</c> other than <c>None</c> on a list whose
    /// items have labels (§8.2.5.25, 8.2.5.25-1).
    /// </summary>
    private void CheckPdfUA2Element(PdfStructureElement element, string? type, List<PdfStructureElement> kids, CosReference? reference)
    {
        switch (type)
        {
            case "H":
                Report("8.2.5.12-1", "the structure tree uses the H structure type", reference);
                break;
            case "Note":
                Report("8.2.5.14-1", "the structure tree uses the Note structure type (PDF 2.0 has FENote)", reference);
                break;
            case "L" when kids.Exists(kid => kid.StandardType?.Name == "LI" && kid.Children.OfType<PdfStructureElement>().Any(label => label.StandardType?.Name == "Lbl"))
                && element.GetAttributeValue(ListOwner, ListNumbering) is not CosName { Value: not "None" }:
                Report("8.2.5.25-1", "a list whose items have labels (Lbl) has no ListNumbering attribute, or None", reference);
                break;
        }
    }

    /// <summary>
    /// The ISO/TS 32005 containment rule (Table 5) a <paramref name="parent"/> element with a <paramref name="kid"/> kid breaks, as veraPDF's
    /// PDF/UA-2 profile names it: <c>Table 5. L-P-1</c>, numbered headings as <c>Hn</c>.
    /// </summary>
    private static string Containment(string parent, string kid) => $"Table 5. {Generic(parent)}-{Generic(kid)}-1";

    private static string Generic(string type) => type is ['H', >= '1' and <= '9', ..] ? "Hn" : type;

    /// <summary>Table rules: kid counts and the Caption's place (7.2-11 to 7.2-14, 7.2-16, 7.2-39) and header cells' scope (7.5-1).</summary>
    private void CheckTable(PdfStructureElement table, List<string?> kidTypes, CosReference? reference)
    {
        if (kidTypes.Count(kid => kid == "THead") > 1)
        {
            Report("7.2-11", "a Table has more than one THead", reference);
        }

        if (kidTypes.Count(kid => kid == "TFoot") > 1)
        {
            Report("7.2-12", "a Table has more than one TFoot", reference);
        }

        if ((kidTypes.Contains("THead") || kidTypes.Contains("TFoot")) && !kidTypes.Contains("TBody"))
        {
            Report(kidTypes.Contains("TFoot") ? "7.2-13" : "7.2-14", "a Table with a THead or TFoot has no TBody", reference);
        }

        int captions = kidTypes.Count(kid => kid == "Caption");
        if (captions > 1)
        {
            Report("7.2-39", "a Table has more than one Caption", reference);
        }
        else if (captions == 1 && kidTypes[0] != "Caption" && kidTypes[^1] != "Caption")
        {
            Report("7.2-16", "a Table's Caption is neither its first nor its last kid", reference);
        }

        // 7.5-1: unless every data cell names its header cells (Headers), every header cell needs a Scope.
        List<PdfStructureElement> cells = [.. Descendants(table).Where(cell => cell.StandardType?.Name is "TH" or "TD")];
        if (cells.Any(cell => cell.StandardType!.Name == "TD" && cell.GetAttributeValue(Table, Headers) is null))
        {
            foreach (PdfStructureElement header in cells.Where(cell => cell.StandardType!.Name == "TH" && cell.GetAttributeValue(Table, Scope) is null))
            {
                Report("7.5-1", "a TH has no Scope and the table's data cells do not all name their header cells (Headers)", header.Reference);
            }
        }
    }

    /// <summary>The elements under a table, without descending into nested tables.</summary>
    private static IEnumerable<PdfStructureElement> Descendants(PdfStructureElement element)
    {
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfStructureElement>(element.Children.OfType<PdfStructureElement>());
        while (pending.TryPop(out PdfStructureElement? current))
        {
            if (!seen.Add(current.Dictionary))
            {
                continue;
            }

            yield return current;
            if (current.StandardType?.Name != "Table")
            {
                foreach (PdfStructureElement kid in current.Children.OfType<PdfStructureElement>())
                {
                    pending.Push(kid);
                }
            }
        }
    }

    private void CheckPage(PdfPage page)
    {
        IReadOnlyDictionary<int, PdfStructureElement>? tagged = _root?.GetMarkedContentElements(page);
        var scan = new ContentState(page.Reference, tagged);
        switch (Resolve(page.Dictionary.GetValueOrDefault(KnownNames.Contents)))
        {
            case CosStream stream:
                ScanContent(stream, page.Resources, scan);
                break;
            case CosArray parts:
                // The parts of a page's content are one stream (ISO 32000-2 §7.8.2): marked content may span them.
                foreach (CosObject part in parts)
                {
                    if (Resolve(part) is CosStream stream)
                    {
                        ScanContent(stream, page.Resources, scan);
                    }
                }

                break;
        }

        if (Resolve(page.Dictionary.GetValueOrDefault(Annots)) is CosArray annotations)
        {
            foreach (CosObject item in annotations)
            {
                if (Resolve(item) is CosDictionary annotation)
                {
                    CheckAnnotation(annotation, item as CosReference);
                }
            }
        }
    }

    private void CheckAnnotation(CosDictionary annotation, CosReference? reference)
    {
        string subtype = NameOf(annotation, Subtype) ?? string.Empty;
        if (subtype == "TrapNet")
        {
            Report("7.18.2-1", "a TrapNet annotation", reference);
            return;
        }

        bool hidden = Resolve(annotation.GetValueOrDefault(F)) is CosInteger flags && (flags.Value & 2) != 0;
        if (hidden || subtype == "PrinterMark")
        {
            return;
        }

        PdfStructureElement? parent = reference is null ? null : _root?.FindElementForObject(reference);
        string? parentType = parent?.StandardType?.Name;
        bool described = Resolve(annotation.GetValueOrDefault(Contents)) is CosString { Bytes.Length: > 0 } || !string.IsNullOrEmpty(parent?.AlternateDescription);
        switch (subtype)
        {
            case "Widget":
                if (parentType != "Form")
                {
                    Report("7.18.4-1", "a widget annotation is not nested in a Form structure element", reference);
                }

                break;
            case "Link":
                if (parentType != "Link" && !(_part == 2 && parentType == "Reference"))
                {
                    Report("7.18.5-1", "a link annotation is not nested in a Link structure element", reference);
                }

                if (!described)
                {
                    Report("7.18.5-2", "a link annotation has no alternate description (Contents)", reference);
                }

                break;
            default:
                if (_part == 2 && !MarkupAnnotations.Contains(subtype))
                {
                    // ISO 14289-2 §8.9.2.3: only markup annotations must be in Annot elements (a popup must not be in the tree at all).
                    break;
                }

                if (parentType != "Annot")
                {
                    Report("7.18.1-1", $"a {subtype} annotation is not nested in an Annot structure element", reference);
                }

                if (!described)
                {
                    Report("7.18.1-2", $"a {subtype} annotation has no alternate description (Contents)", reference);
                }

                break;
        }
    }

    /// <summary>
    /// Reads a content stream's marked content and painting operators (7.1-1 to 7.1-3) and the fonts its text is shown in (7.21.4.1-1,
    /// 7.21.7-1, 7.21.7-2), following the form XObjects it draws with the marked content they are drawn in.
    /// </summary>
    private void ScanContent(CosStream stream, CosDictionary? resources, ContentState state)
    {
        ReadOnlyMemory<byte> data;
        try
        {
            data = _document.DecodeStream(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        var forms = new List<(CosStream Form, ContentState State)>();
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
                    font = _document.GetFont(fontObject);
                    if (fontObject is not null)
                    {
                        CheckFontEmbedded(fontObject);
                    }

                    break;
                case ContentOperatorCode.ShowText or ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing:
                    Paint(state);
                    if (font is not null && operands.Count > 0 && operands[operands.Count - 1].Kind == ContentOperandKind.String)
                    {
                        CheckUnicode(font, fontName, operands[operands.Count - 1].Bytes);
                    }

                    break;
                case ContentOperatorCode.ShowTextAdjusted:
                    Paint(state);
                    if (font is not null && operands.Count > 0 && operands[0].Kind == ContentOperandKind.Array)
                    {
                        foreach (ContentOperand item in operands[0].Items)
                        {
                            if (item.Kind == ContentOperandKind.String)
                            {
                                CheckUnicode(font, fontName, item.Bytes);
                            }
                        }
                    }

                    break;
                case ContentOperatorCode.Stroke or ContentOperatorCode.CloseAndStroke or ContentOperatorCode.Fill or ContentOperatorCode.FillObsolete
                    or ContentOperatorCode.FillEvenOdd or ContentOperatorCode.FillAndStroke or ContentOperatorCode.FillEvenOddAndStroke
                    or ContentOperatorCode.CloseFillAndStroke or ContentOperatorCode.CloseFillEvenOddAndStroke or ContentOperatorCode.PaintShading
                    or ContentOperatorCode.BeginInlineImage:
                    Paint(state);
                    break;
                case ContentOperatorCode.PaintXObject when operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name && resources is not null:
                    CosObject? xobject = Dictionary(resources, XObject)?.GetValueOrDefault(new CosName(System.Text.Encoding.ASCII.GetString(operands[0].Bytes)));
                    if (Resolve(xobject) is CosStream drawn)
                    {
                        if (NameOf(drawn.Dictionary, Subtype) == "Form")
                        {
                            forms.Add((drawn, state.Inherited(_root?.GetMarkedContentElements(drawn))));
                        }
                        else
                        {
                            Paint(state);
                        }
                    }

                    break;
            }

            _arena.Clear();
        }

        _arena.Trim();
        foreach ((CosStream form, ContentState inherited) in forms)
        {
            if (_visited.Add(form))
            {
                ScanContent(form, Dictionary(form.Dictionary, KnownNames.Resources) ?? resources, inherited);
                _visited.Remove(form);
            }
        }
    }

    private void BeginMarkedContent(ContentState state, ContentOperands operands, CosDictionary? resources)
    {
        string tag = operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name ? System.Text.Encoding.ASCII.GetString(operands[0].Bytes) : string.Empty;
        CosDictionary? properties = null;
        if (operands.Count > 1)
        {
            properties = operands[1].Kind == ContentOperandKind.Name
                ? (resources is null ? null : Dictionary(resources, Properties) is { } named ? Resolve(named.GetValueOrDefault(new CosName(System.Text.Encoding.ASCII.GetString(operands[1].Bytes)))) as CosDictionary : null)
                : operands[1].ToCosObject() as CosDictionary;
        }

        bool artifact = tag == "Artifact";
        bool tagged = !artifact && properties is not null && Resolve(properties.GetValueOrDefault(MCID)) is CosInteger mcid && state.IsInTree((int)mcid.Value);
        if (artifact && state.Tagged)
        {
            Report("7.1-1", "content marked as Artifact is inside tagged content", state.Page);
        }
        else if (tagged && state.Artifact)
        {
            Report("7.1-2", "tagged content is inside content marked as Artifact", state.Page);
        }

        state.Begin(artifact, tagged);
    }

    private void Paint(ContentState state)
    {
        if (!state.Tagged && !state.Artifact)
        {
            Report("7.1-3", "content on a page is neither tagged (in the structure tree) nor marked as an Artifact", state.Page);
        }
    }

    private void CheckFontEmbedded(CosObject value)
    {
        if (Resolve(value) is not CosDictionary font || !_fonts.Add(font))
        {
            return;
        }

        string? subtype = NameOf(font, Subtype);
        if (subtype == "Type3")
        {
            return;
        }

        CosDictionary described = font;
        if (subtype == "Type0" && Resolve(font.GetValueOrDefault(DescendantFonts)) is CosArray { Count: > 0 } descendants && Resolve(descendants[0]) is CosDictionary descendant)
        {
            described = descendant;
        }

        CosDictionary? descriptor = Dictionary(described, FontDescriptor);
        if (descriptor is null || !FontFiles.Any(key => Resolve(descriptor.GetValueOrDefault(key)) is CosStream))
        {
            Report("7.21.4.1-1", $"the font {NameOf(font, BaseFont) ?? "(unnamed)"} is not embedded", value as CosReference);
        }
    }

    /// <summary>
    /// Checks that every character code of a shown string maps to Unicode through the font dictionary (7.21.7-1: a ToUnicode CMap, a
    /// standard encoding or glyph names; a font program's own cmap does not count) and not to U+0000, U+FEFF or U+FFFE (7.21.7-2).
    /// </summary>
    private void CheckUnicode(PdfFont font, string resourceName, ReadOnlySpan<byte> text)
    {
        Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
        while (!text.IsEmpty)
        {
            ShownGlyph glyph = font.ReadShownGlyph(text);
            int written = font.ReadUnicode(text, buffer, out UnicodeSource source);
            if (source is UnicodeSource.Unmapped or UnicodeSource.FontProgram)
            {
                Report("7.21.7-1", string.Create(CultureInfo.InvariantCulture, $"the font {resourceName}{Named(font)} maps the character code {glyph.Code} to no Unicode value"), font.Reference);
            }
            else if (buffer[..written].IndexOfAny('\0', '﻿', '￾') >= 0)
            {
                Report("7.21.7-2", string.Create(CultureInfo.InvariantCulture, $"the font {resourceName}{Named(font)} maps the character code {glyph.Code} to U+0000, U+FEFF or U+FFFE"), font.Reference);
            }

            text = text[Math.Clamp(glyph.Length, 1, text.Length)..];
        }

        static string Named(PdfFont font) => font.BaseFont is { } name ? $" ({name})" : string.Empty;
    }

    /// <summary>The marked content open at a point of a content stream, and the MCIDs its structure tree knows.</summary>
    private sealed class ContentState(CosReference? page, IReadOnlyDictionary<int, PdfStructureElement>? tree, bool tagged = false, bool artifact = false)
    {
        private readonly List<(bool Artifact, bool Tagged)> _open = [];
        private readonly bool _baseTagged = tagged;
        private readonly bool _baseArtifact = artifact;

        public CosReference? Page { get; } = page;

        public bool Tagged => _baseTagged || _open.Exists(mark => mark.Tagged);

        public bool Artifact => _baseArtifact || _open.Exists(mark => mark.Artifact);

        public bool IsInTree(int mcid) => tree is not null && tree.ContainsKey(mcid);

        public void Begin(bool artifact, bool tagged) => _open.Add((artifact, tagged));

        public void End()
        {
            if (_open.Count > 0)
            {
                _open.RemoveAt(_open.Count - 1);
            }
        }

        /// <summary>The state a form XObject drawn here starts in: inside what is open, with its own MCIDs.</summary>
        public ContentState Inherited(IReadOnlyDictionary<int, PdfStructureElement>? formTree) => new(Page, formTree, Tagged, Artifact);
    }
}
