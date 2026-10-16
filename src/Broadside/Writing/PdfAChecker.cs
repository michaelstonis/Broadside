using System.Globalization;
using System.Text.RegularExpressions;
using Broadside.Content;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>A PDF/A rule a document cannot meet: the veraPDF rule identifier (clause and test number) and what breaks it.</summary>
/// <param name="Rule">The rule, as veraPDF's validation profile identifies it: <c>6.2.11.4.1-1</c>.</param>
/// <param name="Message">What in the document breaks it.</param>
/// <param name="Reference">The object that breaks it, when it is an indirect object.</param>
internal readonly record struct PdfAViolation(string Rule, string Message, CosReference? Reference);

/// <summary>
/// The check step of a PDF/A save (issues #201, #202, #203): walks what the file will hold and lists every rule of veraPDF's validation
/// profile for the level (PDF/A-2B, -2U, -3B, -3U, -4, -4F) the save cannot meet by itself. It reads and changes nothing in the document.
/// </summary>
/// <remarks>
/// <para>
/// ISO 19005-2 is not in the repository (open decision 12): rules are cited by veraPDF's identifiers. Checked here: 6.1.2-1 (catalog
/// version above 1.7, or an incremental update of a file above 1.7), 6.1.3-2 (encryption), 6.1.7.1-3 and 6.1.7.2-1 (external stream
/// data, LZWDecode), 6.1.10-1 (inline image filters), 6.2.3-2 and 6.2.3-3 (output intents), 6.2.4.3-2 and 6.2.4.3-3 (DeviceRGB or
/// DeviceCMYK used in content, images, shadings or colour space resources the output intent's profile does not cover), 6.2.5-1 to
/// 6.2.5-4 (transfer functions, halftones), 6.2.8-1 to 6.2.8-3 (image alternates, OPI, interpolation), 6.2.9-1 to 6.2.9-3 (OPI,
/// PostScript and reference XObjects), 6.2.10-1 (blend modes), 6.2.11.4.1-1 (font programs not embedded), 6.3.1-1, 6.3.2-1,
/// 6.3.2-2, 6.3.3-1 and 6.3.3-2 (annotation types, flags and appearances), 6.4.1-1 to 6.4.1-3 and 6.4.2-1, 6.4.2-2 (form actions,
/// NeedAppearances, XFA, NeedsRendering), 6.5.1-1, 6.5.1-2, 6.5.2-1 and 6.5.2-2 (actions), 6.8-5 (embedded files, which PDF/A-2
/// allows only when they are PDF/A-1 or -2 themselves, which is not verified: each is reported), 6.10-1, 6.10-2 and 6.11-1.
/// </para>
/// <para>
/// Level U (PDF/A-2u, -3u, issue #202) adds 6.2.11.7.2-1 and -2: every character code a text-showing operator shows in the content
/// the check walks maps to Unicode through the font's ToUnicode CMap, its encoding's glyph names (Adobe Glyph List) or a predefined
/// CJK collection (<see cref="PdfFont.GetUnicode(CharacterCode, Span{char}, out UnicodeSource)"/>, but not the embedded program's
/// own "cmap", which ISO 19005 does not accept), and never to U+0000, U+FEFF or U+FFFE. The text state is followed per content
/// stream (a font set before <c>Q</c> is kept: conservative). PDF/A-3 (issue #202) replaces 6.8-5 with 6.8-1, 6.8-3 and 6.8-4: each
/// embedded file (in the EmbeddedFiles tree or a file attachment annotation) names a MIME type, has an <c>AFRelationship</c> and is
/// listed in an <c>AF</c> array of the catalog, a page, an annotation, an XObject or a structure element.
/// </para>
/// <para>
/// PDF/A-4 (ISO 19005-4, issue #203) numbers its rules differently: the same checks are reported under the PDF/A-4 profile's
/// identifiers (<see cref="PdfA4Rules"/>), and those PDF/A-4 drops are not reported (PostScript XObjects, a field's actions, a
/// widget's AA, JavaScript, an embedded file missing from <c>AF</c>). 6.1.2-1 is an incremental update of a file whose header is not
/// <c>%PDF-2.n</c> (a full save writes 2.0); 6.2.5-3 is <c>HTO</c>; 6.6.3-1 an additional-actions dictionary of the catalog or a
/// page with a trigger other than E, X, D, U, Fo and Bl; 6.9-3 any embedded file in a PDF/A-4 save (each must be PDF/A-1, -2 or -4,
/// not verified); in a PDF/A-4f save 6.9-1, 6.9-2 (<c>F</c> and <c>UF</c>), 6.9-4 for each embedded file and 6.9-5 (the
/// EmbeddedFiles tree). The document information dictionary (6.1.3-4, 6.1.3-5) and the catalog's version (6.1.12-1) are made
/// conformant by the save.
/// </para>
/// <para>
/// Not checked (left to the writing APIs, which produce conformant objects, and to veraPDF in CI): the file syntax rules the writer
/// always meets (6.1.2-2, 6.1.3-1, 6.1.3-3, 6.1.4, 6.1.6, 6.1.7.1-1/-2, 6.1.9), implementation limits (6.1.13), font program internals
/// (6.2.11.2, 6.2.11.3, 6.2.11.5 to 6.2.11.8), ICC profile versions (6.2.4.2), JPEG 2000 data (6.2.8.3), optional content
/// configurations (6.9) and XMP extension schemas of an existing packet (6.6.2.3). A device colour space under a <c>Default*</c>
/// colour space is still reported (conservative). Converting an existing file to conform is issue #229.
/// </para>
/// </remarks>
internal sealed partial class PdfAChecker
{
    private static readonly PdfVersion Pdf17 = new(1, 7);
    private static readonly CosName A = new("A");
    private static readonly CosName AA = new("AA");
    private static readonly CosName AF = new("AF");
    private static readonly CosName AcroForm = new("AcroForm");
    private static readonly CosName Alternate = new("Alternates");
    private static readonly CosName AlternatePresentations = new("AlternatePresentations");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName AP = new("AP");
    private static readonly CosName BaseFont = new("BaseFont");
    private static readonly CosName BM = new("BM");
    private static readonly CosName CharProcs = new("CharProcs");
    private static readonly CosName ColorSpace = new("ColorSpace");
    private static readonly CosName CS = new("CS");
    private static readonly CosName DescendantFonts = new("DescendantFonts");
    private static readonly CosName DestOutputProfile = new("DestOutputProfile");
    private static readonly CosName DestOutputProfileRef = new("DestOutputProfileRef");
    private static readonly CosName EmbeddedFiles = new("EmbeddedFiles");
    private static readonly CosName ExtGState = new("ExtGState");
    private static readonly CosName F = new("F");
    private static readonly CosName Fields = new("Fields");
    private static readonly CosName First = new("First");
    private static readonly CosName FS = new("FS");
    private static readonly CosName K = new("K");
    private static readonly CosName Font = new("Font");
    private static readonly CosName FontDescriptor = new("FontDescriptor");
    private static readonly CosName[] FontFiles = [new("FontFile"), new("FontFile2"), new("FontFile3")];
    private static readonly CosName G = new("G");
    private static readonly CosName HalftoneType = new("HalftoneType");
    private static readonly CosName HT = new("HT");
    private static readonly CosName HTO = new("HTO");
    private static readonly CosName HTP = new("HTP");
    private static readonly CosName I = new("I");
    private static readonly CosName Interpolate = new("Interpolate");
    private static readonly CosName JavaScript = new("JavaScript");
    private static readonly CosName N = new("N");
    private static readonly CosName Names = new("Names");
    private static readonly CosName NeedAppearances = new("NeedAppearances");
    private static readonly CosName NeedsRendering = new("NeedsRendering");
    private static readonly CosName Next = new("Next");
    private static readonly CosName OpenAction = new("OpenAction");
    private static readonly CosName OPI = new("OPI");
    private static readonly CosName Outlines = new("Outlines");
    private static readonly CosName OutputIntents = new("OutputIntents");
    private static readonly CosName Pattern = new("Pattern");
    private static readonly CosName PatternType = new("PatternType");
    private static readonly CosName PresSteps = new("PresSteps");
    private static readonly CosName Rect = new("Rect");
    private static readonly CosName Ref = new("Ref");
    private static readonly CosName Requirements = new("Requirements");
    private static readonly CosName S = new("S");
    private static readonly CosName Shading = new("Shading");
    private static readonly CosName SMask = new("SMask");
    private static readonly CosName StructTreeRoot = new("StructTreeRoot");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Subtype2 = new("Subtype2");
    private static readonly CosName TR = new("TR");
    private static readonly CosName TR2 = new("TR2");
    private static readonly CosName XFA = new("XFA");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName[] ExternalStreamKeys = [new("F"), new("FFilter"), new("FDecodeParms")];

    /// <summary>The annotation types ISO 32000-1 defines that PDF/A-2 keeps (6.3.1-1: not 3D, Sound, Screen or Movie).</summary>
    private static readonly HashSet<string> AnnotationTypes =
    [
        "Text", "Link", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly", "StrikeOut",
        "Stamp", "Caret", "Ink", "Popup", "FileAttachment", "Widget", "PrinterMark", "TrapNet", "Watermark", "Redact",
    ];

    /// <summary>The action types PDF/A-2 allows (6.5.1-1).</summary>
    private static readonly HashSet<string> ActionTypes = ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"];

    /// <summary>The named actions PDF/A-2 allows (6.5.1-2).</summary>
    private static readonly HashSet<string> NamedActions = ["NextPage", "PrevPage", "FirstPage", "LastPage"];

    /// <summary>The action types PDF/A-4 adds to <see cref="ActionTypes"/>: it forbids neither (ISO 19005-4, veraPDF rule 6.6.1-1).</summary>
    private static readonly HashSet<string> PdfA4ActionTypes = ["JavaScript", "GoToDp"];

    /// <summary>The triggers an additional-actions dictionary of the catalog or a page may have in PDF/A-4 (veraPDF rule 6.6.3-1).</summary>
    private static readonly HashSet<string> PdfA4Triggers = ["E", "X", "D", "U", "Fo", "Bl"];

    /// <summary>
    /// The PDF/A-4 profile's identifier for each PDF/A-2 rule this checker reports where the two differ (ISO 19005-4 renumbers
    /// clauses 6.1.6 to 6.12); <see langword="null"/> where PDF/A-4 has no such rule.
    /// </summary>
    private static readonly Dictionary<string, string?> PdfA4Rules = new(StringComparer.Ordinal)
    {
        ["6.1.7.1-3"] = "6.1.6.1-2",
        ["6.1.7.2-1"] = "6.1.6.2-1",
        ["6.1.10-1"] = "6.1.9-1",
        ["6.2.8-1"] = "6.2.7.1-1",
        ["6.2.8-2"] = "6.2.7.1-2",
        ["6.2.8-3"] = "6.2.7.1-3",
        ["6.2.9-1"] = "6.2.8.1-1",
        ["6.2.9-2"] = "6.2.8.2-1",
        ["6.2.9-3"] = null,
        ["6.2.10-1"] = "6.2.9-1",
        ["6.2.11.4.1-1"] = "6.2.10.4.1-1",
        ["6.4.1-2"] = null,
        ["6.4.1-3"] = "6.4.1-2",
        ["6.5.1-1"] = "6.6.1-1",
        ["6.5.1-2"] = "6.6.1-2",
        ["6.8-1"] = "6.9-1",
        ["6.8-3"] = "6.9-4",
        ["6.8-4"] = null,
        ["6.10-1"] = "6.11-1",
        ["6.10-2"] = "6.11-2",
        ["6.11-1"] = "6.12-1",
    };

    /// <summary>The blend modes ISO 32000-1 defines (6.2.10-1).</summary>
    private static readonly HashSet<string> BlendModes =
    [
        "Normal", "Compatible", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight",
        "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity",
    ];

    private readonly PdfDocument _document;
    private readonly PdfAConformance _conformance;
    private readonly List<PdfAViolation> _violations = [];
    private readonly HashSet<(CosDictionary Font, string Rule)> _unicodeReported = new(UnicodeReportComparer.Instance);
    private readonly List<(CosDictionary Specification, CosReference? Reference)> _embeddedFiles = [];
    private readonly HashSet<CosObject> _associatedFiles = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(string, string)> _reported = [];
    private readonly HashSet<CosObject> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly OperandArena _arena = new();
    private string _outputSpace = "RGB ";

    private PdfAChecker(PdfDocument document, PdfAConformance conformance)
    {
        _document = document;
        _conformance = conformance;
    }

    /// <summary>Lists the rules of the options' PDF/A level a save of <paramref name="document"/> with <paramref name="options"/> cannot meet.</summary>
    /// <param name="document">The document, as it is: nothing is changed.</param>
    /// <param name="options">The save's options.</param>
    /// <returns>The rules not met, in document order; empty when the save can conform.</returns>
    public static IReadOnlyList<PdfAViolation> Check(PdfDocument document, PdfSaveOptions options)
    {
        var checker = new PdfAChecker(document, options.PdfAConformance);
        checker.CheckFile(options);
        checker.CheckOutputIntents();
        checker.CheckCatalog();
        foreach (PdfPage page in document.Pages)
        {
            checker.CheckPage(page);
        }

        checker.CheckEmbeddedFiles();
        return checker._violations;
    }

    /// <summary>Finds the destination profile of the first catalog output intent that has one, or <see langword="null"/>.</summary>
    public static CosStream? FindDestinationProfile(PdfDocument document)
    {
        if (document.Resolve(document.Catalog.GetValueOrDefault(OutputIntents)) is not CosArray intents)
        {
            return null;
        }

        foreach (CosObject item in intents)
        {
            if (document.Resolve(item) is CosDictionary intent && document.Resolve(intent.GetValueOrDefault(DestOutputProfile)) is CosStream profile)
            {
                return profile;
            }
        }

        return null;
    }

    private void Report(string rule, string message, CosReference? reference = null)
    {
        if (_conformance.Part() == 4 && PdfA4Rules.TryGetValue(rule, out string? renumbered))
        {
            if (renumbered is null)
            {
                return;
            }

            rule = renumbered;
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

    /// <summary>
    /// Checks an additional-actions dictionary of the catalog or a page: PDF/A-2 and -3 forbid it (<paramref name="pdfA2Rule"/>),
    /// PDF/A-4 allows only the triggers E, X, D, U, Fo and Bl (veraPDF rule 6.6.3-1).
    /// </summary>
    private void CheckAdditionalActions(CosDictionary owner, string pdfA2Rule, string ownerName, CosReference? reference)
    {
        if (!owner.ContainsKey(AA))
        {
            return;
        }

        if (_conformance.Part() != 4)
        {
            Report(pdfA2Rule, $"{ownerName} has an AA (additional actions) entry", reference);
        }
        else if (Dictionary(owner, AA) is not { } actions || actions.Keys.Any(trigger => !PdfA4Triggers.Contains(trigger.Value)))
        {
            Report("6.6.3-1", $"{ownerName}'s AA (additional actions) has a trigger other than E, X, D, U, Fo or Bl", reference);
        }
    }

    private void CheckFile(PdfSaveOptions options)
    {
        if (options.Encryption is not null || options.PublicKeyEncryption is not null || (_document.Security is not null && !options.RemoveEncryption))
        {
            Report("6.1.3-2", "the file would be encrypted (PDF/A forbids the Encrypt entry)");
        }

        if (_conformance.Part() == 4)
        {
            // A full save writes a 2.0 header (VersionedFeatures.PdfA4Output); an update keeps the file's.
            if (options.IncrementalUpdate && _document.HeaderVersion is { } header && header.Major != 2)
            {
                Report("6.1.2-1", string.Create(CultureInfo.InvariantCulture, $"an incremental update keeps the file's PDF {header} header; a PDF/A-4 file has a %PDF-2.n header"));
            }
        }
        else if (options.IncrementalUpdate ? _document.Version > Pdf17 : CatalogVersion() > Pdf17)
        {
            Report("6.1.2-1", string.Create(CultureInfo.InvariantCulture, $"the document is PDF {_document.Version}; PDF/A-{_conformance.Part()} is based on PDF 1.7"));
        }
    }

    private PdfVersion? CatalogVersion() =>
        Resolve(_document.Catalog.GetValueOrDefault(KnownNames.Version)) is CosName name && PdfVersion.TryParse(name.Bytes, out PdfVersion version) ? version : null;

    private void CheckOutputIntents()
    {
        if (Resolve(_document.Catalog.GetValueOrDefault(OutputIntents)) is not CosArray intents)
        {
            return;
        }

        foreach (CosObject item in intents)
        {
            if (Resolve(item) is CosDictionary intent && intent.ContainsKey(DestOutputProfileRef))
            {
                Report("6.2.3-3", "an output intent has a DestOutputProfileRef entry", item as CosReference);
            }
        }

        CosStream[] profiles = [.. intents
            .Select(item => Resolve(item))
            .OfType<CosDictionary>()
            .Select(intent => Resolve(intent.GetValueOrDefault(DestOutputProfile)))
            .OfType<CosStream>()
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<CosStream>()];
        if (profiles.Length > 1)
        {
            Report("6.2.3-2", "the output intents have different destination profiles");
        }

        if (profiles.Length > 0)
        {
            ReadOnlySpan<byte> data = _document.DecodeStream(profiles[0]).Span;
            _outputSpace = data.Length >= 20 ? System.Text.Encoding.ASCII.GetString(data[16..20]) : string.Empty;
        }
    }

    private void CheckCatalog()
    {
        CosDictionary catalog = _document.Catalog;
        CheckAdditionalActions(catalog, "6.5.2-1", "the catalog", reference: null);

        if (catalog.ContainsKey(Requirements))
        {
            Report("6.11-1", "the catalog has a Requirements entry");
        }

        if (Resolve(catalog.GetValueOrDefault(NeedsRendering)) is CosBoolean { Value: true })
        {
            Report("6.4.2-2", "the catalog's NeedsRendering is true");
        }

        if (Resolve(catalog.GetValueOrDefault(OpenAction)) is CosDictionary openAction)
        {
            CheckAction(openAction, catalog.GetValueOrDefault(OpenAction) as CosReference);
        }

        if (Dictionary(catalog, Names) is { } names)
        {
            if (names.ContainsKey(JavaScript) && _conformance.Part() != 4)
            {
                Report("6.5.1-1", "the name dictionary has a JavaScript tree");
            }

            if (names.ContainsKey(AlternatePresentations))
            {
                Report("6.10-1", "the name dictionary has an AlternatePresentations entry");
            }

            if (names.ContainsKey(EmbeddedFiles))
            {
                if (_conformance.AllowsAssociatedFiles())
                {
                    // PDF/A-4 too: its check step reports 6.9-3 for each file it collects (CheckEmbeddedFiles).
                    foreach (PdfEmbeddedFileEntry entry in _document.EmbeddedFiles)
                    {
                        AddEmbeddedFile(entry.File.Dictionary, entry.File.Reference);
                    }
                }
                else
                {
                    Report("6.8-5", "the document embeds files, which PDF/A-2 allows only when each is PDF/A-1 or PDF/A-2 itself (not verified; PDF/A-3 allows any)");
                }
            }
        }

        if (_conformance == PdfAConformance.PdfA4f && (Dictionary(catalog, Names) is not { } nameTrees || !nameTrees.ContainsKey(EmbeddedFiles)))
        {
            Report("6.9-5", "the catalog's name dictionary has no EmbeddedFiles tree, which a PDF/A-4f file has");
        }

        if (Dictionary(catalog, AcroForm) is { } form)
        {
            if (Resolve(form.GetValueOrDefault(NeedAppearances)) is CosBoolean { Value: true })
            {
                Report("6.4.1-3", "the interactive form's NeedAppearances is true");
            }

            if (form.ContainsKey(XFA))
            {
                Report("6.4.2-1", "the interactive form has an XFA entry");
            }

            if (Resolve(form.GetValueOrDefault(Fields)) is CosArray fields)
            {
                CheckFields(fields);
            }
        }

        if (Dictionary(catalog, Outlines) is { } outlines)
        {
            CheckOutlineItems(outlines.GetValueOrDefault(First));
        }

        if (_conformance.AllowsAssociatedFiles())
        {
            AddAssociatedFiles(catalog);
            AddStructureAssociatedFiles(catalog.GetValueOrDefault(StructTreeRoot), new HashSet<CosObject>(ReferenceEqualityComparer.Instance));
        }
    }

    /// <summary>Records an embedded file's specification (a dictionary with <c>EF</c>) for the PDF/A-3 checks of <see cref="CheckEmbeddedFiles"/>.</summary>
    private void AddEmbeddedFile(CosDictionary? specification, CosReference? reference)
    {
        if (specification is not null && Dictionary(specification, FileAndLayerNames.EF) is not null && !_embeddedFiles.Exists(file => ReferenceEquals(file.Specification, specification)))
        {
            _embeddedFiles.Add((specification, reference));
        }
    }

    /// <summary>Records the file specifications an object's <c>AF</c> entry (an array, or one dictionary) associates with it (ISO 32000-2 §14.13).</summary>
    private void AddAssociatedFiles(CosDictionary owner)
    {
        switch (Resolve(owner.GetValueOrDefault(AF)))
        {
            case CosArray files:
                foreach (CosObject file in files)
                {
                    if (Resolve(file) is CosDictionary specification)
                    {
                        _associatedFiles.Add(specification);
                    }
                }

                break;
            case CosDictionary specification:
                _associatedFiles.Add(specification);
                break;
        }
    }

    /// <summary>Records the associated files of every structure element below <paramref name="node"/> (ISO 32000-2 §14.7.2, Table 355, AF).</summary>
    private void AddStructureAssociatedFiles(CosObject? node, HashSet<CosObject> seen)
    {
        switch (Resolve(node))
        {
            case CosDictionary element when seen.Add(element):
                AddAssociatedFiles(element);
                AddStructureAssociatedFiles(element.GetValueOrDefault(K), seen);
                break;
            case CosArray kids when seen.Add(kids):
                foreach (CosObject kid in kids)
                {
                    AddStructureAssociatedFiles(kid, seen);
                }

                break;
        }
    }

    /// <summary>
    /// The PDF/A-3 embedded file rules (veraPDF 6.8-1, 6.8-3, 6.8-4): each embedded file's stream names a MIME type in
    /// <c>Subtype</c>, its specification has an <c>AFRelationship</c> and an <c>AF</c> array lists it.
    /// </summary>
    private void CheckEmbeddedFiles()
    {
        foreach ((CosDictionary specification, CosReference? reference) in _embeddedFiles)
        {
            string name = _document.Resolve(specification.GetValueOrDefault(FileAndLayerNames.UF) ?? specification.GetValueOrDefault(F)) is CosString file
                ? file.DecodeText()
                : "(unnamed)";
            if (_conformance == PdfAConformance.PdfA4)
            {
                Report("6.9-3", $"the embedded file {name}, which PDF/A-4 allows only when it is PDF/A-1, -2 or -4 itself (not verified; PDF/A-4f allows any)", reference);
                continue;
            }

            if (_conformance.Part() == 4 && (Resolve(specification.GetValueOrDefault(F)) is not CosString || Resolve(specification.GetValueOrDefault(FileAndLayerNames.UF)) is not CosString))
            {
                Report("6.9-2", $"the embedded file {name}'s specification lacks F or UF", reference);
            }
            if (Dictionary(specification, FileAndLayerNames.EF) is { } streams)
            {
                foreach ((CosName _, CosObject value) in streams)
                {
                    if (Resolve(value) is CosStream stream && (NameOf(stream.Dictionary, Subtype) is not { } mediaType || !MediaType().IsMatch(mediaType)))
                    {
                        Report("6.8-1", $"the embedded file {name} names no MIME type in its Subtype", value as CosReference ?? reference);
                    }
                }
            }

            if (Resolve(specification.GetValueOrDefault(FileAndLayerNames.AFRelationship)) is not CosName)
            {
                Report("6.8-3", $"the embedded file {name} has no AFRelationship", reference);
            }

            if (!_associatedFiles.Contains(specification))
            {
                Report("6.8-4", $"the embedded file {name} is not an associated file (no AF array lists it)", reference);
            }
        }
    }

    /// <summary>A MIME type as veraPDF's rule 6.8-1 accepts it: <c>type/subtype</c>.</summary>
    [GeneratedRegex(@"^[-\w+.]+/[-\w+.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaType();

    private void CheckFields(CosArray fields)
    {
        var pending = new Stack<CosObject>(fields);
        while (pending.Count > 0)
        {
            CosObject item = pending.Pop();
            if (Resolve(item) is not CosDictionary field || !_visited.Add(field))
            {
                continue;
            }

            if (_conformance.Part() == 4 ? field.ContainsKey(A) && NameOf(field, Subtype) == "Widget" : field.ContainsKey(A) || field.ContainsKey(AA))
            {
                Report(NameOf(field, Subtype) == "Widget" ? "6.4.1-1" : "6.4.1-2", "a form field has an A or AA (actions) entry", item as CosReference);
            }

            if (Resolve(field.GetValueOrDefault(KnownNames.Kids)) is CosArray kids)
            {
                foreach (CosObject kid in kids)
                {
                    pending.Push(kid);
                }
            }
        }
    }

    private void CheckOutlineItems(CosObject? first)
    {
        CosObject? current = first;
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosObject?>();
        pending.Push(current);
        while (pending.Count > 0)
        {
            current = pending.Pop();
            while (Resolve(current) is CosDictionary item && seen.Add(item))
            {
                if (Resolve(item.GetValueOrDefault(A)) is CosDictionary action)
                {
                    CheckAction(action, item.GetValueOrDefault(A) as CosReference);
                }

                pending.Push(item.GetValueOrDefault(First));
                current = item.GetValueOrDefault(Next);
            }
        }
    }

    private void CheckAction(CosDictionary action, CosReference? reference)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(CosDictionary, CosReference?)>();
        pending.Push((action, reference));
        while (pending.Count > 0)
        {
            (CosDictionary current, CosReference? currentReference) = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            string? type = NameOf(current, S);
            if (type is null || !(ActionTypes.Contains(type) || (_conformance.Part() == 4 && PdfA4ActionTypes.Contains(type))))
            {
                Report("6.5.1-1", $"a {type ?? "typeless"} action, which PDF/A forbids", currentReference);
            }
            else if (type == "Named" && (NameOf(current, N) is not { } named || !NamedActions.Contains(named)))
            {
                Report("6.5.1-2", $"the named action {NameOf(current, N)}, which PDF/A forbids", currentReference);
            }

            switch (Resolve(current.GetValueOrDefault(Next)))
            {
                case CosDictionary next:
                    pending.Push((next, current.GetValueOrDefault(Next) as CosReference));
                    break;
                case CosArray nexts:
                    foreach (CosObject item in nexts)
                    {
                        if (Resolve(item) is CosDictionary next)
                        {
                            pending.Push((next, item as CosReference));
                        }
                    }

                    break;
            }
        }
    }

    private void CheckPage(PdfPage page)
    {
        CosDictionary dictionary = page.Dictionary;
        CheckAdditionalActions(dictionary, "6.5.2-2", "a page", page.Reference);

        if (dictionary.ContainsKey(PresSteps))
        {
            Report("6.10-2", "a page has a PresSteps entry", page.Reference);
        }

        CheckContent(dictionary.GetValueOrDefault(KnownNames.Contents), page.Resources);
        if (_conformance.AllowsAssociatedFiles())
        {
            AddAssociatedFiles(dictionary);
        }

        if (Resolve(dictionary.GetValueOrDefault(Annots)) is CosArray annotations)
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
        if (_conformance.AllowsAssociatedFiles())
        {
            AddAssociatedFiles(annotation);
            if (subtype == "FileAttachment")
            {
                AddEmbeddedFile(Dictionary(annotation, FS), annotation.GetValueOrDefault(FS) as CosReference);
            }
        }

        if (!AnnotationTypes.Contains(subtype))
        {
            Report("6.3.1-1", $"a {subtype} annotation, which PDF/A forbids", reference);
        }

        CosObject flags = Resolve(annotation.GetValueOrDefault(F));
        if (flags is CosInteger { Value: long value })
        {
            // Print (4) set; Invisible (1), Hidden (2), ToggleNoView (256) and NoView (32) clear.
            if ((value & 4) == 0 || (value & (1 | 2 | 32 | 256)) != 0)
            {
                Report("6.3.2-2", $"a {subtype} annotation is not printed or is hidden (F {value})", reference);
            }
        }
        else if (subtype != "Popup")
        {
            Report("6.3.2-1", $"a {subtype} annotation has no F (flags) entry", reference);
        }

        CosDictionary? appearances = Dictionary(annotation, AP);
        if (appearances is null && subtype is not ("Popup" or "Link") && !HasEmptyRect(annotation))
        {
            Report("6.3.3-1", $"a {subtype} annotation has no appearance stream", reference);
        }

        if (appearances is not null)
        {
            if (appearances.Count != 1 || !appearances.ContainsKey(N))
            {
                Report("6.3.3-2", $"a {subtype} annotation's appearance dictionary has entries other than N", reference);
            }

            foreach ((CosName _, CosObject appearance) in appearances)
            {
                switch (Resolve(appearance))
                {
                    case CosStream stream:
                        CheckForm(stream);
                        break;
                    case CosDictionary states:
                        foreach ((CosName _, CosObject state) in states)
                        {
                            if (Resolve(state) is CosStream stateStream)
                            {
                                CheckForm(stateStream);
                            }
                        }

                        break;
                }
            }
        }

        if (Resolve(annotation.GetValueOrDefault(A)) is CosDictionary action)
        {
            if (subtype == "Widget")
            {
                Report("6.4.1-1", "a widget annotation has an A (action) entry", reference);
            }
            else
            {
                CheckAction(action, annotation.GetValueOrDefault(A) as CosReference);
            }
        }

        if (subtype == "Widget" && annotation.ContainsKey(AA) && _conformance.Part() != 4)
        {
            Report("6.4.1-1", "a widget annotation has an AA (additional actions) entry", reference);
        }
    }

    private bool HasEmptyRect(CosDictionary annotation) =>
        Resolve(annotation.GetValueOrDefault(Rect)) is CosArray { Count: 4 } rect
        && Number(rect[0]) == Number(rect[2]) && Number(rect[1]) == Number(rect[3]);

    private double? Number(CosObject value) => Resolve(value) switch
    {
        CosInteger integer => integer.Value,
        CosReal real => real.Value,
        _ => null,
    };

    /// <summary>Checks a stream's own entries: external data and LZW (6.1.7.1-3, 6.1.7.2-1).</summary>
    private void CheckStream(CosStream stream)
    {
        if (ExternalStreamKeys.Any(stream.Dictionary.ContainsKey))
        {
            Report("6.1.7.1-3", "a stream has an F, FFilter or FDecodeParms entry");
        }

        CosObject filter = Resolve(stream.Dictionary.GetValueOrDefault(FilterNames.Filter));
        IEnumerable<CosObject> filters = filter is CosArray array ? array : [filter];
        if (filters.Any(item => Resolve(item) is CosName name && (name.Value is "LZWDecode" or "LZW")))
        {
            Report("6.1.7.2-1", "a stream is LZWDecode-encoded");
        }
    }

    private void CheckContent(CosObject? contents, CosDictionary? resources)
    {
        CheckResources(resources);
        switch (Resolve(contents))
        {
            case CosStream stream:
                ScanContent(stream, resources);
                break;
            case CosArray parts:
                foreach (CosObject part in parts)
                {
                    if (Resolve(part) is CosStream stream)
                    {
                        ScanContent(stream, resources);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// Reads a content stream's colour operators and inline images (6.2.4.3, 6.2.8-3, 6.1.10-1) and, for level U, the character codes
    /// its text-showing operators show in the fonts of <paramref name="resources"/> (6.2.11.7.2).
    /// </summary>
    private void ScanContent(CosStream stream, CosDictionary? resources)
    {
        if (!_visited.Add(stream))
        {
            return;
        }

        CheckStream(stream);
        ReadOnlyMemory<byte> data;
        try
        {
            data = _document.DecodeStream(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        _arena.Clear();
        _arena.Overflowed = false;
        var reader = new ContentReader(data.Span, _arena);
        bool unicode = _conformance.NeedsUnicode();
        (PdfFont Font, string Name)? font = null;
        while (reader.Next(out ReadOperator op))
        {
            ContentOperands operands = _arena.Operands;
            switch (op.Code)
            {
                case ContentOperatorCode.SetFont when unicode && operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name:
                    string fontName = System.Text.Encoding.ASCII.GetString(operands[0].Bytes);
                    CosObject? fontObject = resources is null ? null : Dictionary(resources, Font)?.GetValueOrDefault(new CosName(fontName));
                    font = _document.GetFont(fontObject) is { } shown ? (shown, fontName) : null;
                    break;
                case ContentOperatorCode.ShowText or ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing
                    when font is { } current && operands.Count > 0 && operands[operands.Count - 1].Kind == ContentOperandKind.String:
                    CheckUnicode(current.Font, current.Name, operands[operands.Count - 1].Bytes);
                    break;
                case ContentOperatorCode.ShowTextAdjusted when font is { } current && operands.Count > 0 && operands[0].Kind == ContentOperandKind.Array:
                    foreach (ContentOperand item in operands[0].Items)
                    {
                        if (item.Kind == ContentOperandKind.String)
                        {
                            CheckUnicode(current.Font, current.Name, item.Bytes);
                        }
                    }

                    break;
                case ContentOperatorCode.SetFillRgb or ContentOperatorCode.SetStrokeRgb:
                    CheckDeviceSpace("DeviceRGB");
                    break;
                case ContentOperatorCode.SetFillCmyk or ContentOperatorCode.SetStrokeCmyk:
                    CheckDeviceSpace("DeviceCMYK");
                    break;
                case ContentOperatorCode.SetFillColorSpace or ContentOperatorCode.SetStrokeColorSpace when operands.Count > 0 && operands[operands.Count - 1].Kind == ContentOperandKind.Name:
                    CheckDeviceSpace(System.Text.Encoding.ASCII.GetString(operands[operands.Count - 1].Bytes));
                    break;
                case ContentOperatorCode.BeginInlineImage when operands.Count > 0 && operands[0].ToCosObject() is CosDictionary inline:
                    CheckInlineImage(inline);
                    break;
            }

            _arena.Clear();
        }

        _arena.Trim();
    }

    /// <summary>
    /// Checks that every character code of a shown string maps to Unicode by a means ISO 19005 accepts (veraPDF 6.2.11.7.2-1), and
    /// not to U+0000, U+FEFF or U+FFFE (6.2.11.7.2-2); each rule is reported once per font.
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
                ReportUnicode(font, resourceName, "6.2.11.7.2-1", string.Create(CultureInfo.InvariantCulture, $"maps the character code {glyph.Code} to no Unicode value (no ToUnicode entry for it, and no standard encoding or glyph name gives one)"));
            }
            else if (buffer[..written].IndexOfAny('\0', '\uFEFF', '\uFFFE') >= 0)
            {
                ReportUnicode(font, resourceName, "6.2.11.7.2-2", string.Create(CultureInfo.InvariantCulture, $"maps the character code {glyph.Code} to U+0000, U+FEFF or U+FFFE"));
            }

            text = text[Math.Clamp(glyph.Length, 1, text.Length)..];
        }
    }

    private void ReportUnicode(PdfFont font, string resourceName, string rule, string message)
    {
        if (_unicodeReported.Add((font.Dictionary, rule)))
        {
            Report(rule, $"the font {resourceName}{(font.BaseFont is { } name ? $" ({name})" : string.Empty)} {message}", font.Reference);
        }
    }

    private void CheckInlineImage(CosDictionary inline)
    {
        CosObject? space = inline.GetValueOrDefault(CS) ?? inline.GetValueOrDefault(ColorSpace);
        if (space is CosName name)
        {
            CheckDeviceSpace(name.Value switch
            {
                "RGB" => "DeviceRGB",
                "CMYK" => "DeviceCMYK",
                string other => other,
            });
        }
        else if (space is CosArray array)
        {
            CheckColorSpace(array);
        }

        if ((inline.GetValueOrDefault(I) ?? inline.GetValueOrDefault(Interpolate)) is CosBoolean { Value: true })
        {
            Report("6.2.8-3", "an inline image is interpolated");
        }

        CosObject? filter = inline.GetValueOrDefault(F) ?? inline.GetValueOrDefault(FilterNames.Filter);
        IEnumerable<CosObject> filters = filter is CosArray filterArray ? filterArray : filter is null ? [] : [filter];
        if (filters.Any(item => item is CosName { } filterName && filterName.Value is "LZW" or "LZWDecode" or "Crypt"))
        {
            Report("6.1.10-1", "an inline image is LZW- or Crypt-encoded");
        }
    }

    private void CheckDeviceSpace(string name)
    {
        switch (name)
        {
            case "DeviceRGB" when _outputSpace != "RGB ":
                Report("6.2.4.3-2", $"DeviceRGB is used, which the output intent's {_outputSpace.Trim()} profile does not cover");
                break;
            case "DeviceCMYK" when _outputSpace != "CMYK":
                Report("6.2.4.3-3", $"DeviceCMYK is used, which the output intent's {_outputSpace.Trim()} profile does not cover");
                break;
        }
    }

    private void CheckColorSpace(CosObject? space)
    {
        switch (Resolve(space))
        {
            case CosName name:
                CheckDeviceSpace(name.Value);
                break;
            case CosArray { Count: > 0 } array when _visited.Add(array):
                string family = (Resolve(array[0]) as CosName)?.Value ?? string.Empty;
                switch (family)
                {
                    case "Indexed" or "I" or "Pattern" when array.Count > 1:
                        CheckColorSpace(array[1]);
                        break;
                    case "Separation" when array.Count > 2:
                    case "DeviceN" when array.Count > 2:
                        CheckColorSpace(array[2]);
                        break;
                }

                break;
        }
    }

    private void CheckResources(CosDictionary? resources)
    {
        if (resources is null || !_visited.Add(resources))
        {
            return;
        }

        foreach (CosObject font in Values(resources, Font))
        {
            CheckFont(font);
        }

        foreach (CosObject xobject in Values(resources, XObject))
        {
            if (Resolve(xobject) is CosStream stream)
            {
                switch (NameOf(stream.Dictionary, Subtype))
                {
                    case "Image":
                        CheckImage(stream);
                        break;
                    case "Form":
                        CheckForm(stream);
                        break;
                    case "PS":
                        Report("6.2.9-3", "a PostScript XObject", xobject as CosReference);
                        break;
                }
            }
        }

        foreach (CosObject state in Values(resources, ExtGState))
        {
            if (Resolve(state) is CosDictionary graphicsState)
            {
                CheckGraphicsState(graphicsState, state as CosReference);
            }
        }

        foreach (CosObject space in Values(resources, ColorSpace))
        {
            CheckColorSpace(space);
        }

        foreach (CosObject pattern in Values(resources, Pattern))
        {
            CheckPattern(pattern);
        }

        foreach (CosObject shading in Values(resources, Shading))
        {
            CheckShading(shading);
        }
    }

    private IEnumerable<CosObject> Values(CosDictionary resources, CosName category) =>
        Resolve(resources.GetValueOrDefault(category)) is CosDictionary entries ? [.. entries.Select(entry => entry.Value)] : [];

    private void CheckFont(CosObject value)
    {
        if (Resolve(value) is not CosDictionary font || !_visited.Add(font))
        {
            return;
        }

        string? subtype = NameOf(font, Subtype);
        if (subtype == "Type3")
        {
            CheckResources(Dictionary(font, KnownNames.Resources));
            if (Dictionary(font, CharProcs) is { } procedures)
            {
                foreach ((CosName _, CosObject procedure) in procedures)
                {
                    if (Resolve(procedure) is CosStream stream)
                    {
                        ScanContent(stream, Dictionary(font, KnownNames.Resources));
                    }
                }
            }

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
            Report("6.2.11.4.1-1", $"the font {NameOf(font, BaseFont) ?? "(unnamed)"} is not embedded", value as CosReference);
        }
    }

    private void CheckImage(CosStream image)
    {
        if (!_visited.Add(image))
        {
            return;
        }

        CheckStream(image);
        CosDictionary dictionary = image.Dictionary;
        if (_conformance.AllowsAssociatedFiles())
        {
            AddAssociatedFiles(dictionary);
        }

        if (dictionary.ContainsKey(Alternate))
        {
            Report("6.2.8-1", "an image has an Alternates entry");
        }

        if (dictionary.ContainsKey(OPI))
        {
            Report("6.2.8-2", "an image has an OPI entry");
        }

        if (Resolve(dictionary.GetValueOrDefault(Interpolate)) is CosBoolean { Value: true })
        {
            Report("6.2.8-3", "an image is interpolated");
        }

        CheckColorSpace(dictionary.GetValueOrDefault(ColorSpace));
        if (Resolve(dictionary.GetValueOrDefault(SMask)) is CosStream mask)
        {
            CheckImage(mask);
        }
    }

    private void CheckForm(CosStream form)
    {
        if (_visited.Contains(form))
        {
            return;
        }

        CosDictionary dictionary = form.Dictionary;
        if (dictionary.ContainsKey(OPI) || NameOf(dictionary, Subtype2) == "PS")
        {
            Report("6.2.9-1", "a form XObject has an OPI entry or Subtype2 PS");
        }

        if (dictionary.ContainsKey(Ref))
        {
            Report("6.2.9-2", "a reference XObject");
        }

        if (_conformance.AllowsAssociatedFiles())
        {
            AddAssociatedFiles(dictionary);
        }

        CosDictionary? resources = Dictionary(dictionary, KnownNames.Resources);
        CheckResources(resources);
        ScanContent(form, resources);
    }

    private void CheckGraphicsState(CosDictionary state, CosReference? reference)
    {
        if (!_visited.Add(state))
        {
            return;
        }

        if (state.ContainsKey(TR))
        {
            Report("6.2.5-1", "a graphics state has a TR (transfer function) entry", reference);
        }

        if (state.ContainsKey(TR2) && NameOf(state, TR2) != "Default")
        {
            Report("6.2.5-2", "a graphics state has a TR2 entry other than Default", reference);
        }

        if (_conformance.Part() == 4 ? state.ContainsKey(HTO) : state.ContainsKey(HTP))
        {
            Report("6.2.5-3", _conformance.Part() == 4 ? "a graphics state has an HTO entry" : "a graphics state has an HTP entry", reference);
        }

        if (Dictionary(state, HT) is { } halftone && Resolve(halftone.GetValueOrDefault(HalftoneType)) is CosInteger { Value: not (1 or 5) })
        {
            Report("6.2.5-4", "a halftone whose type is not 1 or 5", reference);
        }

        CosObject blend = Resolve(state.GetValueOrDefault(BM));
        IEnumerable<CosObject> modes = blend is CosArray array ? array : blend is CosName ? [blend] : [];
        foreach (CosObject mode in modes)
        {
            if (Resolve(mode) is CosName name && !BlendModes.Contains(name.Value))
            {
                Report("6.2.10-1", $"the blend mode {name.Value}, which ISO 32000-1 does not define", reference);
            }
        }

        if (Dictionary(state, SMask) is { } mask && Resolve(mask.GetValueOrDefault(G)) is CosStream group)
        {
            CheckForm(group);
        }
    }

    private void CheckPattern(CosObject value)
    {
        switch (Resolve(value))
        {
            case CosStream tiling when Resolve(tiling.Dictionary.GetValueOrDefault(PatternType)) is CosInteger { Value: 1 }:
                CheckForm(tiling);
                break;
            case CosDictionary shadingPattern:
                CheckShading(shadingPattern.GetValueOrDefault(Shading));
                break;
        }
    }

    private void CheckShading(CosObject? value)
    {
        CosDictionary? shading = Resolve(value) switch
        {
            CosDictionary dictionary => dictionary,
            CosStream stream => stream.Dictionary,
            _ => null,
        };
        if (shading is not null && _visited.Add(shading))
        {
            CheckColorSpace(shading.GetValueOrDefault(ColorSpace));
        }
    }

    /// <summary>Compares (font, rule) pairs by the font dictionary's identity.</summary>
    private sealed class UnicodeReportComparer : IEqualityComparer<(CosDictionary Font, string Rule)>
    {
        public static readonly UnicodeReportComparer Instance = new();

        public bool Equals((CosDictionary Font, string Rule) x, (CosDictionary Font, string Rule) y) =>
            ReferenceEquals(x.Font, y.Font) && string.Equals(x.Rule, y.Rule, StringComparison.Ordinal);

        public int GetHashCode((CosDictionary Font, string Rule) obj) =>
            HashCode.Combine(ReferenceEqualityComparer.Instance.GetHashCode(obj.Font), StringComparer.Ordinal.GetHashCode(obj.Rule));
    }
}
