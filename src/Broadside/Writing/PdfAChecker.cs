using System.Globalization;
using Broadside.Content;
using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>A PDF/A rule a document cannot meet: the veraPDF rule identifier (clause and test number) and what breaks it.</summary>
/// <param name="Rule">The rule, as veraPDF's validation profile identifies it: <c>6.2.11.4.1-1</c>.</param>
/// <param name="Message">What in the document breaks it.</param>
/// <param name="Reference">The object that breaks it, when it is an indirect object.</param>
internal readonly record struct PdfAViolation(string Rule, string Message, CosReference? Reference);

/// <summary>
/// The check step of a PDF/A-2b save (issue #201): walks what the file will hold and lists every rule of veraPDF's PDF/A-2B validation
/// profile the save cannot meet by itself. It reads and changes nothing in the document.
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
/// Not checked (left to the writing APIs, which produce conformant objects, and to veraPDF in CI): the file syntax rules the writer
/// always meets (6.1.2-2, 6.1.3-1, 6.1.3-3, 6.1.4, 6.1.6, 6.1.7.1-1/-2, 6.1.9), implementation limits (6.1.13), font program internals
/// (6.2.11.2, 6.2.11.3, 6.2.11.5 to 6.2.11.8), ICC profile versions (6.2.4.2), JPEG 2000 data (6.2.8.3), optional content
/// configurations (6.9) and XMP extension schemas of an existing packet (6.6.2.3). A device colour space under a <c>Default*</c>
/// colour space is still reported (conservative). Converting an existing file to conform is issue #229.
/// </para>
/// </remarks>
internal sealed class PdfAChecker
{
    private static readonly PdfVersion Pdf17 = new(1, 7);
    private static readonly CosName A = new("A");
    private static readonly CosName AA = new("AA");
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
    private static readonly CosName Font = new("Font");
    private static readonly CosName FontDescriptor = new("FontDescriptor");
    private static readonly CosName[] FontFiles = [new("FontFile"), new("FontFile2"), new("FontFile3")];
    private static readonly CosName G = new("G");
    private static readonly CosName HalftoneType = new("HalftoneType");
    private static readonly CosName HT = new("HT");
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

    /// <summary>The blend modes ISO 32000-1 defines (6.2.10-1).</summary>
    private static readonly HashSet<string> BlendModes =
    [
        "Normal", "Compatible", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight",
        "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity",
    ];

    private readonly PdfDocument _document;
    private readonly List<PdfAViolation> _violations = [];
    private readonly HashSet<(string, string)> _reported = [];
    private readonly HashSet<CosObject> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly OperandArena _arena = new();
    private string _outputSpace = "RGB ";

    private PdfAChecker(PdfDocument document) => _document = document;

    /// <summary>Lists the PDF/A-2b rules a save of <paramref name="document"/> with <paramref name="options"/> cannot meet.</summary>
    /// <param name="document">The document, as it is: nothing is changed.</param>
    /// <param name="options">The save's options.</param>
    /// <returns>The rules not met, in document order; empty when the save can conform.</returns>
    public static IReadOnlyList<PdfAViolation> Check(PdfDocument document, PdfSaveOptions options)
    {
        var checker = new PdfAChecker(document);
        checker.CheckFile(options);
        checker.CheckOutputIntents();
        checker.CheckCatalog();
        foreach (PdfPage page in document.Pages)
        {
            checker.CheckPage(page);
        }

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

    private void CheckFile(PdfSaveOptions options)
    {
        if (options.Encryption is not null || options.PublicKeyEncryption is not null || (_document.Security is not null && !options.RemoveEncryption))
        {
            Report("6.1.3-2", "the file would be encrypted (PDF/A forbids the Encrypt entry)");
        }

        if (options.IncrementalUpdate ? _document.Version > Pdf17 : CatalogVersion() > Pdf17)
        {
            Report("6.1.2-1", string.Create(CultureInfo.InvariantCulture, $"the document is PDF {_document.Version}; PDF/A-2 is based on PDF 1.7"));
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
        if (catalog.ContainsKey(AA))
        {
            Report("6.5.2-1", "the catalog has an AA (additional actions) entry");
        }

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
            if (names.ContainsKey(JavaScript))
            {
                Report("6.5.1-1", "the name dictionary has a JavaScript tree");
            }

            if (names.ContainsKey(AlternatePresentations))
            {
                Report("6.10-1", "the name dictionary has an AlternatePresentations entry");
            }

            if (names.ContainsKey(EmbeddedFiles))
            {
                Report("6.8-5", "the document embeds files, which PDF/A-2 allows only when each is PDF/A-1 or PDF/A-2 itself (not verified; PDF/A-3 allows any)");
            }
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
    }

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

            if (field.ContainsKey(A) || field.ContainsKey(AA))
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
            if (type is null || !ActionTypes.Contains(type))
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
        if (dictionary.ContainsKey(AA))
        {
            Report("6.5.2-2", "a page has an AA (additional actions) entry", page.Reference);
        }

        if (dictionary.ContainsKey(PresSteps))
        {
            Report("6.10-2", "a page has a PresSteps entry", page.Reference);
        }

        CheckContent(dictionary.GetValueOrDefault(KnownNames.Contents), page.Resources);
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
        if (!AnnotationTypes.Contains(subtype))
        {
            Report("6.3.1-1", $"a {subtype} annotation, which PDF/A-2 forbids", reference);
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

        if (subtype == "Widget" && annotation.ContainsKey(AA))
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
                ScanContent(stream);
                break;
            case CosArray parts:
                foreach (CosObject part in parts)
                {
                    if (Resolve(part) is CosStream stream)
                    {
                        ScanContent(stream);
                    }
                }

                break;
        }
    }

    /// <summary>Reads a content stream's colour operators and inline images (6.2.4.3, 6.2.8-3, 6.1.10-1).</summary>
    private void ScanContent(CosStream stream)
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
        while (reader.Next(out ReadOperator op))
        {
            ContentOperands operands = _arena.Operands;
            switch (op.Code)
            {
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
                        ScanContent(stream);
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

        CheckResources(Dictionary(dictionary, KnownNames.Resources));
        ScanContent(form);
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

        if (state.ContainsKey(HTP))
        {
            Report("6.2.5-3", "a graphics state has an HTP entry", reference);
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
}
