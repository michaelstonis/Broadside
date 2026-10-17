using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>
/// Converts a document to a PDF/A level in place (issue #229): changes what the level forbids and a converter can fix, and lists
/// what it changed and what it could not fix. Whether the result conforms is then decided by <see cref="PdfAChecker"/>, the check
/// step of the save, never by the converter. ISO 19005 is cited by veraPDF validation-profile rule identifiers (open decision 12).
/// </summary>
/// <remarks>
/// <para>
/// Fixed: encryption (6.1.3-2, removed by the save), a catalog version above the part's base (6.1.2-1), LZWDecode streams
/// (6.1.7.2-1, stored decoded and recompressed by the save), output intents' <c>DestOutputProfileRef</c> (6.2.3-3), transfer
/// functions, halftone phases and halftones of other types (6.2.5-1 to 6.2.5-4), image alternates, OPI and interpolation (6.2.8-1
/// to 6.2.8-3), OPI, PostScript passthrough and reference entries of form XObjects (6.2.9-1, 6.2.9-2), blend modes ISO 32000-1
/// does not define (6.2.10-1, made Normal), non-embedded simple fonts (6.2.11.4.1-1), annotations PDF/A forbids (6.3.1-1, removed
/// with their content: 3D, Sound, Screen, Movie, RichMedia and other unknown types), annotation flags (6.3.2-1, 6.3.2-2), appearance
/// dictionaries' D and R entries (6.3.3-2), forbidden actions and additional actions (6.4.1-1, 6.4.1-2, 6.5.1-1, 6.5.1-2, 6.5.2-1,
/// 6.5.2-2; PDF/A-4's 6.6.1-1 and 6.6.3-1), NeedAppearances, XFA and NeedsRendering (6.4.1-3, 6.4.2-1, 6.4.2-2), the JavaScript
/// and AlternatePresentations name trees (6.5.1-1, 6.10-1), PresSteps (6.10-2), Requirements (6.11-1) and, for PDF/A-3 and
/// PDF/A-4f, embedded files' MIME types, <c>AFRelationship</c> and <c>AF</c> (6.8-1, 6.8-3, 6.8-4; ISO 19005-4 6.9-2, 6.9-4).
/// </para>
/// <para>
/// A non-embedded Type 1 or TrueType font is embedded through the document's font resolvers, the program its text renders with
/// (<see cref="PdfSimpleFont.Substitute"/>): the font dictionary is replaced, in every resource dictionary that names it, by a
/// Type 0 font whose embedded one-byte CMap maps each code of the original encoding to the glyph it rendered with, over a
/// subset CIDFont (§9.7.5.3, §9.9.2) shared with the document's other uses of that program, and whose ToUnicode keeps the text the
/// original encoding gave (§9.10.3). It is refused, with the reason, when no program is found, when a code the content shows has no
/// glyph in it (rule 6.2.11.8-1 forbids the .notdef glyph), or when the program's widths differ from the font's by more than a
/// unit for a code the content shows (rule 6.2.11.5-1 would fail, or the text would move).
/// </para>
/// </remarks>
internal sealed class PdfAConverter
{
    private static readonly CosName A = new("A");
    private static readonly CosName AA = new("AA");
    private static readonly CosName AcroForm = new("AcroForm");
    private static readonly CosName Alternates = new("Alternates");
    private static readonly CosName AlternatePresentations = new("AlternatePresentations");
    private static readonly CosName Annots = new("Annots");
    private static readonly CosName AP = new("AP");
    private static readonly CosName AF = new("AF");
    private static readonly CosName AS = new("AS");
    private static readonly CosName CharSet = new("CharSet");
    private static readonly CosName CIDSet = new("CIDSet");
    private static readonly CosName Configs = new("Configs");
    private static readonly CosName FT = new("FT");
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName Name = new("Name");
    private static readonly CosName OCGs = new("OCGs");
    private static readonly CosName Order = new("Order");
    private static readonly CosName Parent = new("Parent");
    private static readonly CosName BM = new("BM");
    private static readonly CosName CharProcs = new("CharProcs");
    private static readonly CosName D = new("D");
    private static readonly CosName DecodeParms = new("DecodeParms");
    private static readonly CosName DestOutputProfileRef = new("DestOutputProfileRef");
    private static readonly CosName DR = new("DR");
    private static readonly CosName EmbeddedFiles = new("EmbeddedFiles");
    private static readonly CosName ExtGState = new("ExtGState");
    private static readonly CosName F = new("F");
    private static readonly CosName Fields = new("Fields");
    private static readonly CosName First = new("First");
    private static readonly CosName Font = new("Font");
    private static readonly CosName G = new("G");
    private static readonly CosName HalftoneType = new("HalftoneType");
    private static readonly CosName HT = new("HT");
    private static readonly CosName HTO = new("HTO");
    private static readonly CosName HTP = new("HTP");
    private static readonly CosName Interpolate = new("Interpolate");
    private static readonly CosName JavaScript = new("JavaScript");
    private static readonly CosName N = new("N");
    private static readonly CosName Names = new("Names");
    private static readonly CosName NeedAppearances = new("NeedAppearances");
    private static readonly CosName NeedsRendering = new("NeedsRendering");
    private static readonly CosName Next = new("Next");
    private static readonly CosName Normal = new("Normal");
    private static readonly CosName OpenAction = new("OpenAction");
    private static readonly CosName OPI = new("OPI");
    private static readonly CosName Outlines = new("Outlines");
    private static readonly CosName OutputIntents = new("OutputIntents");
    private static readonly CosName Pattern = new("Pattern");
    private static readonly CosName PatternType = new("PatternType");
    private static readonly CosName PresSteps = new("PresSteps");
    private static readonly CosName PS = new("PS");
    private static readonly CosName R = new("R");
    private static readonly CosName Ref = new("Ref");
    private static readonly CosName Requirements = new("Requirements");
    private static readonly CosName S = new("S");
    private static readonly CosName SMask = new("SMask");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Subtype2 = new("Subtype2");
    private static readonly CosName ToUnicodeKey = new("ToUnicode");
    private static readonly CosName TR = new("TR");
    private static readonly CosName TR2 = new("TR2");
    private static readonly CosName XFA = new("XFA");
    private static readonly CosName XObject = new("XObject");

    /// <summary>The annotation types PDF/A keeps (veraPDF rule 6.3.1-1; the same list as the check step's).</summary>
    private static readonly HashSet<string> AnnotationTypes =
    [
        "Text", "Link", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly", "StrikeOut",
        "Stamp", "Caret", "Ink", "Popup", "FileAttachment", "Widget", "PrinterMark", "TrapNet", "Watermark", "Redact",
    ];

    private static readonly HashSet<string> ActionTypes = ["GoTo", "GoToR", "GoToE", "Thread", "URI", "Named", "SubmitForm"];
    private static readonly HashSet<string> PdfA4ActionTypes = ["JavaScript", "GoToDp"];
    private static readonly HashSet<string> NamedActions = ["NextPage", "PrevPage", "FirstPage", "LastPage"];
    private static readonly HashSet<string> PdfA4Triggers = ["E", "X", "D", "U", "Fo", "Bl"];

    private static readonly HashSet<string> BlendModes =
    [
        "Normal", "Compatible", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight",
        "SoftLight", "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity",
    ];

    /// <summary>The abbreviated filter names of inline images (ISO 32000-1 §8.9.7, Table 94) and the names they stand for.</summary>
    private static readonly Dictionary<string, string> FilterAbbreviations = new(StringComparer.Ordinal)
    {
        ["AHx"] = "ASCIIHexDecode",
        ["A85"] = "ASCII85Decode",
        ["LZW"] = "LZWDecode",
        ["Fl"] = "FlateDecode",
        ["RL"] = "RunLengthDecode",
        ["CCF"] = "CCITTFaxDecode",
        ["DCT"] = "DCTDecode",
    };

    private const string ObjectMetadataRemoved =
        "the XMP metadata of objects other than the catalog (pages, images, form XObjects) is removed: its properties are not verified to use the schemas PDF/A predefines (rule 6.6.2.3.1-1)";

    private static readonly CosName FFilter = new("FFilter");
    private static readonly CosName FS = new("FS");

    private readonly PdfDocument _document;
    private readonly PdfAConformance _level;
    private readonly List<string> _changes = [];
    private readonly List<string> _unfixed = [];
    private readonly HashSet<string> _noted = [];
    private readonly HashSet<CosObject> _visited = new(ReferenceEqualityComparer.Instance);
    private readonly List<CosDictionary> _fontResources = [];
    private readonly Dictionary<CosDictionary, HashSet<byte>> _shown = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<CosDictionary> _replaced = new(ReferenceEqualityComparer.Instance);
    private readonly OperandArena _arena = new();
    private int _converted;

    private PdfAConverter(PdfDocument document, PdfAConformance level)
    {
        _document = document;
        _level = level;
    }

    /// <summary>Converts <paramref name="document"/> in place; the caller records the changes (issue #306) and checks the result.</summary>
    /// <returns>What was changed, and what could not be and why, one sentence each, in document order.</returns>
    public static (IReadOnlyList<string> Changes, IReadOnlyList<string> Unfixed) Convert(PdfDocument document, PdfAConformance level)
    {
        var converter = new PdfAConverter(document, level);
        if (document.Security is not null)
        {
            converter.Changed("the encryption is removed (rule 6.1.3-2)");
        }

        converter.ConvertPageTree();
        converter.ConvertFieldAppearances();
        converter.ConvertCatalog();
        foreach (PdfPage page in document.Pages)
        {
            converter.ConvertPage(page);
        }

        converter.EmbedFonts();
        converter.ConvertEmbeddedFonts();
        if (level.NeedsUnicode())
        {
            converter.ConvertUnicode();
        }

        converter.ConvertOptionalContent();
        converter.ConvertMetadata();
        foreach (CosReference reference in document.EnumerateObjectReferences())
        {
            CosObject value = document.Resolve(reference);
            if (value is CosStream stream)
            {
                converter.ConvertStream(stream);
            }

            CosDictionary? dictionary = value switch
            {
                CosStream data => data.Dictionary,
                CosDictionary plain => plain,
                _ => null,
            };
            if (dictionary is not null && !ReferenceEquals(dictionary, document.Catalog))
            {
                _ = converter.Remove(dictionary, Metadata, ObjectMetadataRemoved);
            }
        }

        return (converter._changes, converter._unfixed);
    }

    /// <summary>
    /// Generates the appearance streams of form fields a widget lacks one for, or all of them when the form asks viewers to
    /// (<c>NeedAppearances</c>), through the forms API (text, choice, check box and radio button fields; ISO 32000-1 §12.7.3.3):
    /// PDF/A requires an appearance for every widget (rule 6.3.3-1) and forbids NeedAppearances (6.4.1-3).
    /// </summary>
    private void ConvertFieldAppearances()
    {
        if (_document.AcroForm is not { } form)
        {
            return;
        }

        bool regenerate = Dictionary(_document.Catalog, AcroForm) is { } dictionary && Resolve(dictionary.GetValueOrDefault(NeedAppearances)) is CosBoolean { Value: true };
        foreach (PdfField field in form.AllFields)
        {
            if (field is not PdfTerminalField terminal
                || (!regenerate && terminal.Widgets.All(widget => Dictionary(widget.Dictionary, AP)?.ContainsKey(N) == true)))
            {
                continue;
            }

            try
            {
                switch (terminal)
                {
                    case PdfTextField text:
                        text.GenerateAppearances();
                        break;
                    case PdfChoiceField choice:
                        choice.GenerateAppearances();
                        break;
                    case PdfToggleButtonField button:
                        button.GenerateAppearances();
                        break;
                    default:
                        continue;
                }

                Changed("form fields' appearance streams are generated from their values (rules 6.3.3-1, 6.4.1-3)");
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                NotFixed($"the form field {terminal.FullyQualifiedName} has no appearance stream and none can be generated: {exception.Message} (rule 6.3.3-1)");
            }
        }
    }

    /// <summary>
    /// Rebuilds a damaged page tree (§7.7.3: a kid that is not a page or page tree node, a node reached twice, a wrong Count) as one
    /// node over the pages the document reads, each given the attributes it inherited: a validator cannot read a file whose page tree
    /// loops or dangles, so it cannot conform.
    /// </summary>
    private void ConvertPageTree()
    {
        if (IsSound(_document.Catalog.GetValueOrDefault(KnownNames.Pages), new HashSet<CosObject>(ReferenceEqualityComparer.Instance), out _))
        {
            return;
        }

        PdfPage[] pages = [.. _document.Pages];
        var root = new CosDictionary { [KnownNames.Type] = KnownNames.Pages, [KnownNames.Count] = new CosInteger(pages.Length) };
        CosReference rootReference = _document.AddObject(root);
        var kids = new CosArray();
        foreach (PdfPage page in pages)
        {
            CosDictionary dictionary = page.Dictionary;
            foreach (CosName key in (CosName[])[KnownNames.Resources, KnownNames.MediaBox, KnownNames.CropBox, KnownNames.Rotate])
            {
                if (!dictionary.ContainsKey(key) && Inherited(dictionary, key) is { } value)
                {
                    dictionary[key] = value;
                }
            }

            dictionary[KnownNames.Parent] = rootReference;
            kids.Add((CosObject?)page.Reference ?? _document.AddObject(dictionary));
        }

        root[KnownNames.Kids] = kids;
        _document.Catalog[KnownNames.Pages] = rootReference;
        Changed("the damaged page tree is rebuilt over the pages the document reads (ISO 32000-1 §7.7.3)");
    }

    /// <summary>Whether a page tree node and everything below it are pages and nodes reached once, with a Count of their pages.</summary>
    private bool IsSound(CosObject? node, HashSet<CosObject> seen, out long pages)
    {
        pages = 0;
        if (Resolve(node) is not CosDictionary dictionary || !seen.Add(dictionary))
        {
            return false;
        }

        if (Resolve(dictionary.GetValueOrDefault(KnownNames.Kids)) is not CosArray kids)
        {
            pages = 1;
            return NameOf(dictionary, KnownNames.Type) is null or "Page";
        }

        foreach (CosObject kid in kids)
        {
            if (!IsSound(kid, seen, out long below))
            {
                return false;
            }

            pages += below;
        }

        return NameOf(dictionary, KnownNames.Type) is null or "Pages"
            && Resolve(dictionary.GetValueOrDefault(KnownNames.Count)) is CosInteger { Value: long count } && count == pages;
    }

    /// <summary>
    /// Rewrites the document's XMP packet from its properties (title, author, subject, keywords, creator, producer, creation date),
    /// which the save lays out with the PDF/A identification: an existing packet's other properties may use schemas PDF/A does not
    /// predefine and would need extension schemas (veraPDF rules 6.6.2.3.1-1, 6.6.2.3.1-2), so they are dropped.
    /// </summary>
    private void ConvertMetadata()
    {
        if (!_document.Catalog.ContainsKey(Metadata))
        {
            return;
        }

        PdfDocumentProperties properties = _document.Properties;
        (string? title, string? author, string? subject, string? keywords, string? creator, string? producer, DateTimeOffset? created) =
            (properties.Title, properties.Author, properties.Subject, properties.Keywords, properties.Creator, properties.Producer, properties.CreationDate);
        _ = _document.Catalog.Remove(Metadata);
        if (title is not null)
        {
            properties.Title = title;
        }

        if (author is not null)
        {
            properties.Author = author;
        }

        if (subject is not null)
        {
            properties.Subject = subject;
        }

        if (keywords is not null)
        {
            properties.Keywords = keywords;
        }

        if (creator is not null)
        {
            properties.Creator = creator;
        }

        if (producer is not null)
        {
            properties.Producer = producer;
        }

        if (created is not null)
        {
            properties.CreationDate = created;
        }

        Changed("the XMP metadata is rewritten from the document's title, author, subject, keywords, creator, producer and dates; its other properties are dropped (rules 6.6.2.3.1-1, 6.6.2.3.1-2)");
    }

    /// <summary>
    /// Gives every optional content configuration a unique <c>Name</c> (veraPDF rules 6.9-1, 6.9-2), lists every group in its
    /// <c>Order</c> (6.9-3, appended at the end) and removes its <c>AS</c> (6.9-4).
    /// </summary>
    private void ConvertOptionalContent()
    {
        if (Dictionary(_document.Catalog, FileAndLayerNames.OCProperties) is not { } properties)
        {
            return;
        }

        CosObject[] groups = Resolve(properties.GetValueOrDefault(OCGs)) is CosArray all ? [.. all.Where(group => Resolve(group) is CosDictionary)] : [];
        var configurations = new List<CosDictionary>();
        if (Dictionary(properties, D) is { } defaults)
        {
            configurations.Add(defaults);
        }

        if (Resolve(properties.GetValueOrDefault(Configs)) is CosArray configs)
        {
            configurations.AddRange(configs.Select(item => Resolve(item)).OfType<CosDictionary>());
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < configurations.Count; i++)
        {
            CosDictionary configuration = configurations[i];
            string? name = Resolve(configuration.GetValueOrDefault(Name)) is CosString text ? text.DecodeText() : null;
            if (string.IsNullOrEmpty(name) || !names.Add(name))
            {
                string unique = i == 0 ? "Default" : string.Create(CultureInfo.InvariantCulture, $"Configuration {i}");
                for (int n = 2; !names.Add(unique); n++)
                {
                    unique = string.Create(CultureInfo.InvariantCulture, $"Configuration {i} ({n})");
                }

                configuration[Name] = new CosString(Encoding.ASCII.GetBytes(unique));
                Changed("optional content configurations are given unique names (rules 6.9-1, 6.9-2)");
            }

            if (Resolve(configuration.GetValueOrDefault(Order)) is CosArray order)
            {
                var listed = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
                var pending = new Stack<CosArray>([order]);
                var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
                while (pending.Count > 0)
                {
                    CosArray current = pending.Pop();
                    if (!seen.Add(current))
                    {
                        continue;
                    }

                    foreach (CosObject item in current)
                    {
                        switch (Resolve(item))
                        {
                            case CosDictionary group:
                                listed.Add(group);
                                break;
                            case CosArray nested:
                                pending.Push(nested);
                                break;
                        }
                    }
                }

                foreach (CosObject group in groups.Where(group => !listed.Contains(Resolve(group))))
                {
                    order.Add(group);
                    Changed("optional content groups missing from a configuration's Order are listed at its end (rule 6.9-3)");
                }
            }

            if (_level.Part() != 4)
            {
                _ = Remove(configuration, AS, "optional content configurations' automatic state changes (AS) are removed (rule 6.9-4)");
            }
        }
    }

    /// <summary>
    /// The dictionaries of the fonts the document embeds (see the remarks): a CIDFontType2 without <c>CIDToGIDMap</c> is given
    /// <c>Identity</c>, the default readers apply (rule 6.2.11.3.2-1); <c>CharSet</c> and <c>CIDSet</c>, optional and often
    /// incomplete, are removed (6.2.11.4.2-1, -2); a shown code's width is made its glyph's in the program (6.2.11.5-1); a symbolic
    /// TrueType font's <c>Encoding</c> is removed when its codes select the same glyphs without it (6.2.11.6-3).
    /// </summary>
    private void ConvertEmbeddedFonts()
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        foreach (CosDictionary fonts in _fontResources)
        {
            foreach ((CosName _, CosObject value) in fonts)
            {
                if (Resolve(value) is not CosDictionary font || !seen.Add(font))
                {
                    continue;
                }

                CosDictionary described = font;
                if (NameOf(font, Subtype) == "Type0" && Resolve(font.GetValueOrDefault(CompositeFontNames.DescendantFonts)) is CosArray { Count: > 0 } descendants
                    && Resolve(descendants[0]) is CosDictionary descendant)
                {
                    described = descendant;
                    if (NameOf(descendant, Subtype) == "CIDFontType2" && !descendant.ContainsKey(CompositeFontNames.CidToGidMap))
                    {
                        descendant[CompositeFontNames.CidToGidMap] = new CosName("Identity");
                        Changed("TrueType CIDFonts without a CIDToGIDMap are given Identity (rule 6.2.11.3.2-1)");
                    }
                }

                if (Dictionary(described, FontNames.FontDescriptor) is not { } descriptor)
                {
                    continue;
                }

                _ = Remove(descriptor, CharSet, "font descriptors' CharSet entries are removed (rule 6.2.11.4.2-1)");
                _ = Remove(descriptor, CIDSet, "font descriptors' CIDSet streams are removed (rule 6.2.11.4.2-2)");
                if (_document.GetFont(value) is PdfSimpleFont simple && simple.IsEmbedded)
                {
                    ConvertEmbeddedWidths(simple, font);
                    ConvertSymbolicEncoding(simple, font, descriptor, value as CosReference);
                }
            }
        }
    }

    /// <summary>
    /// Level U (rules 6.2.11.7.2-1, -2): a simple font one of whose shown codes maps to Unicode other than through its ToUnicode CMap
    /// (its encoding's glyph names, its program's cmap) is given a ToUnicode CMap with the text every code of it extracts to, so the
    /// mapping no longer depends on how a reader interprets its encoding. A shown code that maps to no text is reported.
    /// </summary>
    private void ConvertUnicode()
    {
        Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
        foreach ((CosDictionary dictionary, HashSet<byte> shown) in _shown)
        {
            if (shown.Count == 0 || _replaced.Contains(dictionary) || _document.GetFont(dictionary) is not PdfSimpleFont font)
            {
                continue;
            }

            bool needed = false;
            var texts = new Dictionary<int, string>();
            for (int code = 0; code < 256; code++)
            {
                int written = font.GetUnicode(new CharacterCode((uint)code, 1, true), buffer, out UnicodeSource source);
                string text = buffer[..written].ToString();
                if (text.Length == 0 && source == UnicodeSource.ToUnicode
                    && AdobeGlyphList.TryGetUnicode(font.GetGlyphName((byte)code), zapfDingbats: false, out ReadOnlySpan<char> named))
                {
                    // A ToUnicode CMap that maps the code to nothing: its glyph name says what it is (§9.10.2).
                    (text, source) = (named.ToString(), UnicodeSource.GlyphName);
                }

                bool usable = source != UnicodeSource.Unmapped && text.Length > 0 && text.AsSpan().IndexOfAny('\0', '﻿', '￾') < 0;
                if (usable)
                {
                    texts[code] = text;
                }

                if (shown.Contains((byte)code))
                {
                    if (!usable)
                    {
                        NotFixed(string.Create(CultureInfo.InvariantCulture, $"the font {font.BaseFont ?? "(unnamed)"} shows the code {code}, which maps to no Unicode text (rule 6.2.11.7.2-1)"));
                        needed = false;
                        break;
                    }

                    needed |= source != UnicodeSource.ToUnicode;
                }
            }

            if (needed)
            {
                dictionary[ToUnicodeKey] = _document.AddObject(new CosStream(new CosDictionary(), ToUnicode(texts)));
                Changed($"fonts whose text maps to Unicode only through their encoding or program are given a ToUnicode CMap (rule 6.2.11.7.2-1)");
            }
        }
    }

    private void ConvertEmbeddedWidths(PdfSimpleFont font, CosDictionary dictionary)
    {
        if (!_shown.TryGetValue(dictionary, out HashSet<byte>? codes)
            || Resolve(dictionary.GetValueOrDefault(FontNames.Widths)) is not CosArray widths
            || Resolve(dictionary.GetValueOrDefault(FontNames.FirstChar)) is not CosInteger { Value: long first })
        {
            return;
        }

        foreach (byte code in codes.Order())
        {
            long index = code - first;
            if (PdfAFontGlyphs.Find(font, code) is not ({ } glyph and > 0) || index < 0 || index >= widths.Count)
            {
                continue;
            }

            double program = PdfAFontGlyphs.ProgramWidth(font, glyph);
            if (Math.Abs(program - font.GetWidth(code)) > PdfAFontGlyphs.WidthTolerance)
            {
                widths[(int)index] = new CosReal(Math.Round(program, 3));
                Changed($"the font {font.BaseFont}'s widths are made its embedded program's where they differ (rule 6.2.11.5-1)");
            }
        }
    }

    private void ConvertSymbolicEncoding(PdfSimpleFont font, CosDictionary dictionary, CosDictionary descriptor, CosReference? reference)
    {
        if (font is not PdfTrueTypeFont trueType || !dictionary.ContainsKey(FontNames.Encoding)
            || (PdfFontDescriptor.ReadFlags(descriptor, _document) & PdfFontFlags.Symbolic) == 0)
        {
            return;
        }

        byte[] codes = _shown.TryGetValue(dictionary, out HashSet<byte>? shown) ? [.. shown] : [];
        int[] before = [.. codes.Select(trueType.GetGlyphId)];
        CosObject encoding = dictionary[FontNames.Encoding];
        _ = dictionary.Remove(FontNames.Encoding);
        if (PdfFont.Create(_document, dictionary, reference) is PdfTrueTypeFont without && codes.Select(without.GetGlyphId).SequenceEqual(before))
        {
            Changed($"the symbolic TrueType font {font.BaseFont}'s Encoding is removed; its codes select the same glyphs (rule 6.2.11.6-3)");
            return;
        }

        dictionary[FontNames.Encoding] = encoding;
        NotFixed($"the symbolic TrueType font {font.BaseFont} keeps its Encoding: without it its codes would select other glyphs (rule 6.2.11.6-3)");
    }

    private void Changed(string what)
    {
        if (_noted.Add(what))
        {
            _changes.Add(what);
        }
    }

    private void NotFixed(string what)
    {
        if (_noted.Add(what))
        {
            _unfixed.Add(what);
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

    private bool Remove(CosDictionary owner, CosName key, string what)
    {
        if (!owner.Remove(key))
        {
            return false;
        }

        Changed(what);
        return true;
    }

    private void ConvertCatalog()
    {
        CosDictionary catalog = _document.Catalog;
        ConvertAdditionalActions(catalog, "the catalog", "6.5.2-1");
        _ = Remove(catalog, Requirements, "the catalog's Requirements entry is removed (rule 6.11-1)");
        if (Resolve(catalog.GetValueOrDefault(NeedsRendering)) is CosBoolean { Value: true })
        {
            _ = Remove(catalog, NeedsRendering, "the catalog's NeedsRendering entry is removed (rule 6.4.2-2)");
        }

        if (Resolve(catalog.GetValueOrDefault(KnownNames.Version)) is CosName version && PdfVersion.TryParse(version.Bytes, out PdfVersion declared)
            && declared > _level.BaseVersion())
        {
            _ = Remove(catalog, KnownNames.Version, string.Create(CultureInfo.InvariantCulture, $"the catalog's Version {declared} is removed: the file is written as PDF {_level.BaseVersion()} (rule 6.1.2-1)"));
        }

        if (Resolve(catalog.GetValueOrDefault(OpenAction)) is CosDictionary)
        {
            ConvertAction(catalog, OpenAction, "the document's open action");
        }

        if (Dictionary(catalog, Names) is { } names)
        {
            if (_level.Part() != 4)
            {
                _ = Remove(names, JavaScript, "the document-level JavaScript (the name dictionary's JavaScript tree) is removed (rule 6.5.1-1)");
            }

            _ = Remove(names, AlternatePresentations, "the alternate presentations (slide shows) are removed (rule 6.10-1)");
            if (names.ContainsKey(EmbeddedFiles))
            {
                ConvertEmbeddedFiles();
            }
        }

        if (Resolve(catalog.GetValueOrDefault(OutputIntents)) is CosArray intents)
        {
            foreach (CosObject item in intents)
            {
                if (Resolve(item) is CosDictionary intent)
                {
                    _ = Remove(intent, DestOutputProfileRef, "an output intent's DestOutputProfileRef is removed (rule 6.2.3-3)");
                }
            }
        }

        if (Dictionary(catalog, AcroForm) is { } form)
        {
            if (Resolve(form.GetValueOrDefault(NeedAppearances)) is CosBoolean { Value: true })
            {
                _ = Remove(form, NeedAppearances, "the interactive form's NeedAppearances flag is removed: fields show their appearance streams (rule 6.4.1-3)");
            }

            _ = Remove(form, XFA, "the interactive form's XFA form is removed; its AcroForm fields remain (rule 6.4.2-1)");
            if (Resolve(form.GetValueOrDefault(Fields)) is CosArray fields)
            {
                ConvertFields(fields);
            }

            ConvertResources(Dictionary(form, DR));
        }

        if (Dictionary(catalog, Outlines) is { } outlines)
        {
            ConvertOutlineItems(outlines.GetValueOrDefault(First));
        }
    }

    /// <summary>
    /// PDF/A-2 forbids additional actions of the catalog and pages (rules 6.5.2-1, 6.5.2-2); PDF/A-4 keeps the triggers E, X, D, U, Fo
    /// and Bl (rule 6.6.3-1). A widget's AA is forbidden by PDF/A-2 and -3 (rule 6.4.1-1), allowed by PDF/A-4.
    /// </summary>
    private void ConvertAdditionalActions(CosDictionary owner, string ownerName, string rule)
    {
        if (!owner.ContainsKey(AA))
        {
            return;
        }

        if (_level.Part() != 4)
        {
            _ = Remove(owner, AA, $"{ownerName}'s additional actions (AA) are removed (rule {rule})");
            return;
        }

        if (Dictionary(owner, AA) is not { } actions)
        {
            _ = Remove(owner, AA, $"{ownerName}'s damaged additional actions (AA) are removed (rule 6.6.3-1)");
            return;
        }

        foreach (CosName trigger in actions.Keys.Where(trigger => !PdfA4Triggers.Contains(trigger.Value)).ToArray())
        {
            _ = Remove(actions, trigger, $"{ownerName}'s {trigger.Value} additional action is removed (rule 6.6.3-1)");
        }
    }

    private bool IsAllowed(CosDictionary action) =>
        NameOf(action, S) is { } type
        && (ActionTypes.Contains(type) || (_level.Part() == 4 && PdfA4ActionTypes.Contains(type)))
        && (type != "Named" || (NameOf(action, N) is { } named && NamedActions.Contains(named)));

    /// <summary>
    /// Removes the action <paramref name="owner"/>'s <paramref name="key"/> names when PDF/A forbids it, else the forbidden actions of
    /// its <c>Next</c> sequence (§12.6.2; rules 6.5.1-1, 6.5.1-2).
    /// </summary>
    private void ConvertAction(CosDictionary owner, CosName key, string ownerName)
    {
        if (Resolve(owner.GetValueOrDefault(key)) is not CosDictionary action)
        {
            return;
        }

        if (!IsAllowed(action))
        {
            _ = Remove(owner, key, $"{ownerName}'s {NameOf(action, S) ?? "typeless"} action is removed (rule 6.5.1-1)");
            return;
        }

        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance) { action };
        var pending = new Stack<CosDictionary>([action]);
        while (pending.Count > 0)
        {
            CosDictionary current = pending.Pop();
            switch (Resolve(current.GetValueOrDefault(Next)))
            {
                case CosDictionary next when !IsAllowed(next):
                    _ = Remove(current, Next, $"{ownerName}'s {NameOf(next, S) ?? "typeless"} action is removed (rule 6.5.1-1)");
                    break;
                case CosDictionary next when seen.Add(next):
                    pending.Push(next);
                    break;
                case CosArray nexts:
                    for (int i = nexts.Count - 1; i >= 0; i--)
                    {
                        if (Resolve(nexts[i]) is not CosDictionary next)
                        {
                            continue;
                        }

                        if (!IsAllowed(next))
                        {
                            nexts.RemoveAt(i);
                            Changed($"{ownerName}'s {NameOf(next, S) ?? "typeless"} action is removed (rule 6.5.1-1)");
                        }
                        else if (seen.Add(next))
                        {
                            pending.Push(next);
                        }
                    }

                    break;
            }
        }
    }

    private void ConvertFields(CosArray fields)
    {
        var pending = new Stack<CosObject>(fields);
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            if (Resolve(pending.Pop()) is not CosDictionary field || !seen.Add(field))
            {
                continue;
            }

            bool widget = NameOf(field, Subtype) == "Widget";
            if (_level.Part() != 4 || widget)
            {
                _ = Remove(field, A, $"a form field's action (A) is removed (rule {(widget ? "6.4.1-1" : "6.4.1-2")})");
            }

            if (_level.Part() != 4)
            {
                _ = Remove(field, AA, $"a form field's additional actions (AA, such as JavaScript calculations and validations) are removed (rule {(widget ? "6.4.1-1" : "6.4.1-2")})");
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

    private void ConvertOutlineItems(CosObject? first)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<CosObject?>();
        pending.Push(first);
        while (pending.Count > 0)
        {
            CosObject? current = pending.Pop();
            while (Resolve(current) is CosDictionary item && seen.Add(item))
            {
                ConvertAction(item, A, "an outline item");
                pending.Push(item.GetValueOrDefault(First));
                current = item.GetValueOrDefault(Next);
            }
        }
    }

    /// <summary>
    /// PDF/A-2 allows an embedded file only when it is PDF/A-1 or -2 itself and PDF/A-4 only when it is PDF/A-1, -2 or -4, which is not
    /// verified: those are reported. PDF/A-3 and PDF/A-4f keep every file of the EmbeddedFiles tree as an associated file of the
    /// document (<see cref="AssociateFile"/>), listed in the catalog's <c>AF</c> (rule 6.8-4).
    /// </summary>
    private void ConvertEmbeddedFiles()
    {
        if (!_level.AllowsAnyEmbeddedFile())
        {
            NotFixed($"embedded files are kept, which {_level.Describe()} allows only when each is a PDF/A file itself (not verified); convert to PDF/A-3 or PDF/A-4f to keep them as associated files");
            return;
        }

        if (Dictionary(_document.Catalog, Names) is not { } names)
        {
            return;
        }

        var pending = new Stack<CosObject?>([names.GetValueOrDefault(EmbeddedFiles)]);
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            if (Resolve(pending.Pop()) is not CosDictionary node || !seen.Add(node))
            {
                continue;
            }

            if (Resolve(node.GetValueOrDefault(Names)) is CosArray entries)
            {
                for (int i = 1; i < entries.Count; i += 2)
                {
                    string name = Resolve(entries[i - 1]) is CosString key ? key.DecodeText() : "(unnamed)";
                    if (AssociateFile(entries[i], name) is { } reference)
                    {
                        entries[i] = reference;
                        AddToAssociatedFiles(_document.Catalog, reference);
                    }
                }
            }

            if (Resolve(node.GetValueOrDefault(KnownNames.Kids)) is CosArray kids)
            {
                foreach (CosObject kid in kids)
                {
                    pending.Push(kid);
                }
            }
        }
    }

    /// <summary>
    /// Makes an embedded file specification an associated file PDF/A-3 and PDF/A-4f accept: a MIME type
    /// (<c>application/octet-stream</c> when unknown, rule 6.8-1), <c>AFRelationship</c> <c>Unspecified</c> (6.8-3), both <c>F</c> and
    /// <c>UF</c> (6.8-2; ISO 19005-4 6.9-2) and an indirect object an <c>AF</c> array can share (6.8-4).
    /// </summary>
    /// <returns>The specification's reference, or <see langword="null"/> when <paramref name="value"/> is not an embedded file's.</returns>
    private CosReference? AssociateFile(CosObject value, string name)
    {
        if (Resolve(value) is not CosDictionary specification || Dictionary(specification, FileAndLayerNames.EF) is not { } streams)
        {
            return null;
        }

        foreach ((CosName _, CosObject stream) in streams)
        {
            if (Resolve(stream) is CosStream file && (NameOf(file.Dictionary, Subtype) is not { } type || !type.Contains('/', StringComparison.Ordinal)))
            {
                file.Dictionary[Subtype] = new CosName("application/octet-stream");
                Changed($"the embedded file {name} is given the MIME type application/octet-stream (rule 6.8-1)");
            }
        }

        if (Resolve(specification.GetValueOrDefault(FileAndLayerNames.AFRelationship)) is not CosName)
        {
            specification[FileAndLayerNames.AFRelationship] = new CosName("Unspecified");
            Changed($"the embedded file {name} is made an associated file with AFRelationship Unspecified (rules 6.8-3, 6.8-4)");
        }

        CosObject? f = specification.GetValueOrDefault(F);
        CosObject? uf = specification.GetValueOrDefault(FileAndLayerNames.UF);
        if (Resolve(f) is CosString && Resolve(uf) is not CosString)
        {
            specification[FileAndLayerNames.UF] = f!;
            Changed($"the embedded file {name}'s specification is given a UF entry (rule 6.8-2)");
        }
        else if (Resolve(uf) is CosString && Resolve(f) is not CosString)
        {
            specification[F] = uf!;
            Changed($"the embedded file {name}'s specification is given an F entry (rule 6.8-2)");
        }

        return value as CosReference ?? _document.AddObject(specification);
    }

    private void AddToAssociatedFiles(CosDictionary owner, CosReference reference)
    {
        if (Resolve(owner.GetValueOrDefault(AF)) is not CosArray associated)
        {
            associated = [];
            owner[AF] = associated;
        }

        if (!associated.Any(item => item is CosReference existing && existing.Equals(reference)))
        {
            associated.Add(reference);
        }
    }

    private void ConvertPage(PdfPage page)
    {
        CosDictionary dictionary = page.Dictionary;
        ConvertAdditionalActions(dictionary, "a page", "6.5.2-2");
        _ = Remove(dictionary, PresSteps, "a page's presentation steps (PresSteps) are removed (rule 6.10-2)");
        _ = Remove(dictionary, Metadata, ObjectMetadataRemoved);
        CosDictionary? resources = page.Resources;
        if (resources is not null && !dictionary.ContainsKey(KnownNames.Resources))
        {
            // Resources inherited from the page tree (§7.7.3.4) are given to the page itself, as veraPDF wants (rule 6.2.2-2).
            dictionary[KnownNames.Resources] = Inherited(dictionary, KnownNames.Resources) ?? resources;
            Changed("pages that inherit their resources from the page tree are given them explicitly (rule 6.2.2-2)");
        }

        ConvertResources(resources);
        switch (Resolve(dictionary.GetValueOrDefault(KnownNames.Contents)))
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

        if (Resolve(dictionary.GetValueOrDefault(Annots)) is CosArray annotations)
        {
            for (int i = annotations.Count - 1; i >= 0; i--)
            {
                if (Resolve(annotations[i]) is CosDictionary annotation && !ConvertAnnotation(annotation))
                {
                    annotations.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>The <paramref name="key"/> entry of the nearest page tree node above a page that has one, as written (a reference stays one).</summary>
    private CosObject? Inherited(CosDictionary page, CosName key)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance) { page };
        for (CosDictionary? node = Resolve(page.GetValueOrDefault(Parent)) as CosDictionary; node is not null && seen.Add(node); node = Resolve(node.GetValueOrDefault(Parent)) as CosDictionary)
        {
            if (node.GetValueOrDefault(key) is { } value && Resolve(value) is not CosNull)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Converts an annotation; <see langword="false"/> when it is to be removed from the page.</summary>
    private bool ConvertAnnotation(CosDictionary annotation)
    {
        string subtype = NameOf(annotation, Subtype) ?? "typeless";
        if (!AnnotationTypes.Contains(subtype))
        {
            Changed($"a {subtype} annotation (multimedia, 3D or a type ISO 32000-1 does not define) is removed with its content (rule 6.3.1-1)");
            return false;
        }

        if (subtype == "FileAttachment" && _level.AllowsAnyEmbeddedFile() && annotation.GetValueOrDefault(FS) is { } attached
            && AssociateFile(attached, "attached to a FileAttachment annotation") is { } file)
        {
            annotation[FS] = file;
            AddToAssociatedFiles(annotation, file);
        }

        const int invisible = 1, hidden = 2, print = 4, noView = 32, toggleNoView = 256;
        if (Resolve(annotation.GetValueOrDefault(F)) is CosInteger { Value: long flags })
        {
            if ((flags & (hidden | noView)) != 0 && subtype is not ("Widget" or "Popup"))
            {
                Changed($"a hidden {subtype} annotation is removed: PDF/A has no hidden annotations (rule 6.3.2-2)");
                return false;
            }

            long conforming = (flags | print) & ~(long)(invisible | hidden | noView | toggleNoView);
            if (conforming != flags)
            {
                annotation[F] = new CosInteger(conforming);
                Changed($"{Article(subtype)} {subtype} annotation's flags are set to print and show it (rule 6.3.2-2)");
            }
        }
        else if (subtype != "Popup")
        {
            annotation[F] = new CosInteger(print);
            Changed($"{Article(subtype)} {subtype} annotation is given flags that print it (rule 6.3.2-1)");
        }

        if (Dictionary(annotation, AP) is { } appearances)
        {
            _ = Remove(appearances, D, "annotations' down appearances (AP D) are removed (rule 6.3.3-2)");
            _ = Remove(appearances, R, "annotations' rollover appearances (AP R) are removed (rule 6.3.3-2)");
            if (subtype == "Widget" && FieldType(annotation) == "Btn" && appearances.GetValueOrDefault(N) is { } single && Resolve(single) is CosStream)
            {
                // A button's normal appearance is a dictionary of states (rule 6.3.3-3): the one stream becomes the state AS names.
                CosName state = Resolve(annotation.GetValueOrDefault(AS)) as CosName ?? new CosName("On");
                appearances[N] = new CosDictionary { [state] = single };
                annotation[AS] = state;
                Changed("button widgets' single normal appearance streams are made appearance states (rule 6.3.3-3)");
            }

            foreach ((CosName _, CosObject appearance) in appearances)
            {
                switch (Resolve(appearance))
                {
                    case CosStream stream:
                        ConvertForm(stream, null);
                        break;
                    case CosDictionary states:
                        foreach ((CosName _, CosObject state) in states)
                        {
                            if (Resolve(state) is CosStream stateStream)
                            {
                                ConvertForm(stateStream, null);
                            }
                        }

                        break;
                }
            }
        }

        if (subtype == "Widget")
        {
            _ = Remove(annotation, A, "a widget annotation's action (A) is removed (rule 6.4.1-1)");
            if (_level.Part() != 4)
            {
                _ = Remove(annotation, AA, "a widget annotation's additional actions (AA) are removed (rule 6.4.1-1)");
            }
        }
        else
        {
            ConvertAction(annotation, A, $"{Article(subtype)} {subtype} annotation");
        }

        return true;
    }

    /// <summary>A widget's field type, <c>FT</c>, inherited from its parent fields (§12.7.4.1).</summary>
    private string? FieldType(CosDictionary widget)
    {
        var seen = new HashSet<CosObject>(ReferenceEqualityComparer.Instance);
        for (CosDictionary? field = widget; field is not null && seen.Add(field); field = Resolve(field.GetValueOrDefault(Parent)) as CosDictionary)
        {
            if (NameOf(field, FT) is { } type)
            {
                return type;
            }
        }

        return null;
    }

    private static string Article(string word) => word.Length > 0 && "AEIOU".Contains(word[0], StringComparison.Ordinal) ? "an" : "a";

    private void ConvertResources(CosDictionary? resources)
    {
        if (resources is null || !_visited.Add(resources))
        {
            return;
        }

        if (Dictionary(resources, Font) is { } fonts)
        {
            _fontResources.Add(fonts);
            foreach ((CosName _, CosObject value) in fonts.ToArray())
            {
                if (Resolve(value) is CosDictionary font && NameOf(font, Subtype) == "Type3" && _visited.Add(font))
                {
                    CosDictionary? glyphResources = Dictionary(font, KnownNames.Resources);
                    if (glyphResources is null)
                    {
                        font[KnownNames.Resources] = resources;
                        glyphResources = resources;
                        Changed("Type 3 fonts whose glyphs use their page's resources are given them explicitly (rule 6.2.2-2)");
                    }

                    ConvertResources(glyphResources);
                    if (Dictionary(font, CharProcs) is { } procedures)
                    {
                        foreach ((CosName _, CosObject procedure) in procedures)
                        {
                            if (Resolve(procedure) is CosStream stream)
                            {
                                ScanContent(stream, glyphResources);
                            }
                        }
                    }
                }
            }
        }

        if (Dictionary(resources, XObject) is { } xobjects)
        {
            foreach ((CosName _, CosObject value) in xobjects)
            {
                if (Resolve(value) is not CosStream stream)
                {
                    continue;
                }

                switch (NameOf(stream.Dictionary, Subtype))
                {
                    case "Image":
                        ConvertImage(stream);
                        break;
                    case "Form":
                        ConvertForm(stream, resources);
                        break;
                    case "PS":
                        NotFixed("a PostScript XObject is kept: what it draws cannot be converted (rule 6.2.9-3)");
                        break;
                }
            }
        }

        if (Dictionary(resources, ExtGState) is { } states)
        {
            foreach ((CosName _, CosObject value) in states)
            {
                if (Resolve(value) is CosDictionary state)
                {
                    ConvertGraphicsState(state, resources);
                }
            }
        }

        if (Dictionary(resources, Pattern) is { } patterns)
        {
            foreach ((CosName _, CosObject value) in patterns)
            {
                if (Resolve(value) is CosStream tiling && Resolve(tiling.Dictionary.GetValueOrDefault(PatternType)) is CosInteger { Value: 1 })
                {
                    ConvertForm(tiling, resources);
                }
            }
        }
    }

    private void ConvertImage(CosStream image)
    {
        if (!_visited.Add(image))
        {
            return;
        }

        ConvertStream(image);
        CosDictionary dictionary = image.Dictionary;
        _ = Remove(dictionary, Alternates, "images' alternate images (Alternates) are removed (rule 6.2.8-1)");
        _ = Remove(dictionary, Metadata, ObjectMetadataRemoved);
        _ = Remove(dictionary, OPI, "images' OPI (prepress proxy) entries are removed (rule 6.2.8-2)");
        if (Resolve(dictionary.GetValueOrDefault(Interpolate)) is CosBoolean { Value: true })
        {
            dictionary[Interpolate] = CosBoolean.False;
            Changed("image interpolation is turned off (rule 6.2.8-3)");
        }

        if (Resolve(dictionary.GetValueOrDefault(SMask)) is CosStream mask)
        {
            ConvertImage(mask);
        }
    }

    /// <summary>Converts a form XObject (or tiling pattern or appearance stream) and the content it draws with its own resources, else <paramref name="inherited"/>.</summary>
    private void ConvertForm(CosStream form, CosDictionary? inherited)
    {
        if (_visited.Contains(form))
        {
            return;
        }

        CosDictionary dictionary = form.Dictionary;
        _ = Remove(dictionary, OPI, "form XObjects' OPI (prepress proxy) entries are removed (rule 6.2.9-1)");
        _ = Remove(dictionary, Metadata, ObjectMetadataRemoved);
        if (NameOf(dictionary, Subtype2) == "PS")
        {
            _ = dictionary.Remove(Subtype2);
            _ = dictionary.Remove(PS);
            Changed("form XObjects' PostScript passthrough (Subtype2 PS) is removed; their PDF content draws (rule 6.2.9-1)");
        }

        _ = Remove(dictionary, Ref, "reference XObjects' links to external pages (Ref) are removed; their own content draws (rule 6.2.9-2)");
        CosDictionary? resources = Dictionary(dictionary, KnownNames.Resources);
        if (resources is null && inherited is not null)
        {
            // A form without resources draws with its page's (§7.8.3, deprecated); PDF/A wants them explicit (rule 6.2.2-2).
            dictionary[KnownNames.Resources] = inherited;
            resources = inherited;
            Changed("form XObjects that use their page's resources are given them explicitly (rule 6.2.2-2)");
        }

        ConvertResources(resources);
        ScanContent(form, resources);
    }

    private void ConvertGraphicsState(CosDictionary state, CosDictionary? resources)
    {
        if (!_visited.Add(state))
        {
            return;
        }

        _ = Remove(state, TR, "graphics states' transfer functions (TR) are removed (rule 6.2.5-1)");
        if (state.ContainsKey(TR2) && NameOf(state, TR2) != "Default")
        {
            state[TR2] = new CosName("Default");
            Changed("graphics states' transfer functions (TR2) are set to Default (rule 6.2.5-2)");
        }

        _ = _level.Part() == 4
            ? Remove(state, HTO, "graphics states' halftone origins (HTO) are removed (ISO 19005-4 rule 6.2.5-3)")
            : Remove(state, HTP, "graphics states' halftone phases (HTP) are removed (rule 6.2.5-3)");
        if (Dictionary(state, HT) is { } halftone && Resolve(halftone.GetValueOrDefault(HalftoneType)) is CosInteger { Value: not (1 or 5) })
        {
            _ = Remove(state, HT, "halftones of a type other than 1 or 5 are removed (rule 6.2.5-4)");
        }

        switch (Resolve(state.GetValueOrDefault(BM)))
        {
            case CosName mode when !BlendModes.Contains(mode.Value):
                state[BM] = Normal;
                Changed($"the blend mode {mode.Value}, which ISO 32000-1 does not define, is replaced by Normal (rule 6.2.10-1)");
                break;
            case CosArray modes when modes.Any(item => Resolve(item) is CosName name && !BlendModes.Contains(name.Value)):
                CosName? first = modes.Select(item => Resolve(item)).OfType<CosName>().FirstOrDefault(name => BlendModes.Contains(name.Value));
                state[BM] = first ?? Normal;
                Changed("blend modes ISO 32000-1 does not define are dropped from a graphics state's BM array (rule 6.2.10-1)");
                break;
        }

        if (Dictionary(state, SMask) is { } mask && Resolve(mask.GetValueOrDefault(G)) is CosStream group)
        {
            ConvertForm(group, resources);
        }
    }

    /// <summary>Stores an LZWDecode stream decoded (the save compresses it with FlateDecode; rule 6.1.7.2-1).</summary>
    private void ConvertStream(CosStream stream)
    {
        if (stream.Dictionary.ContainsKey(F) || stream.Dictionary.ContainsKey(FFilter))
        {
            NotFixed("a stream whose data is in an external file is kept (rule 6.1.7.1-3)");
        }

        // Abbreviated filter names are an inline image's only (§8.9.7); a stream gets the full names (rule 6.1.7.2-1).
        switch (stream.Dictionary.GetValueOrDefault(FilterNames.Filter))
        {
            case CosName abbreviated when FilterAbbreviations.TryGetValue(abbreviated.Value, out string? full):
                stream.Dictionary[FilterNames.Filter] = new CosName(full);
                Changed("streams' abbreviated filter names are written in full (rule 6.1.7.2-1)");
                break;
            case CosArray chain:
                for (int i = 0; i < chain.Count; i++)
                {
                    if (chain[i] is CosName name && FilterAbbreviations.TryGetValue(name.Value, out string? expanded))
                    {
                        chain[i] = new CosName(expanded);
                        Changed("streams' abbreviated filter names are written in full (rule 6.1.7.2-1)");
                    }
                }

                break;
        }

        CosObject filter = Resolve(stream.Dictionary.GetValueOrDefault(FilterNames.Filter));
        CosObject[] filters = filter is CosArray array ? [.. array.Select(item => Resolve(item))] : [filter];
        if (!filters.Any(item => item is CosName { Value: "LZWDecode" or "LZW" }))
        {
            return;
        }

        if (filters.Any(item => item is not CosName { Value: "LZWDecode" or "ASCIIHexDecode" or "ASCII85Decode" or "FlateDecode" or "RunLengthDecode" }))
        {
            // An image filter in the chain: its data would be stored as decoded samples, a different encoding of the image.
            NotFixed("a stream whose filters chain LZWDecode with an image filter is kept (rule 6.1.7.2-1)");
            return;
        }

        ReadOnlyMemory<byte> decoded;
        try
        {
            decoded = _document.DecodeStream(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            NotFixed("an LZWDecode stream cannot be decoded and is kept (rule 6.1.7.2-1)");
            return;
        }

        _ = stream.Dictionary.Remove(FilterNames.Filter);
        _ = stream.Dictionary.Remove(DecodeParms);
        stream.EncodedData = decoded.ToArray();
        Changed("LZWDecode streams (and the general-purpose filters chained with them) are decoded and stored compressed with FlateDecode (rule 6.1.7.2-1)");
    }

    /// <summary>Walks a content stream for its LZWDecode filter and the character codes each font shows.</summary>
    private void ScanContent(CosStream stream, CosDictionary? resources)
    {
        if (!_visited.Add(stream))
        {
            return;
        }

        ConvertStream(stream);
        ReadOnlyMemory<byte> data;
        try
        {
            data = _document.DecodeStream(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return;
        }

        CosDictionary? fonts = resources is null ? null : Dictionary(resources, Font);
        _arena.Clear();
        var reader = new ContentReader(data.Span, _arena);
        HashSet<byte>? shown = null;
        while (reader.Next(out ReadOperator op))
        {
            ContentOperands operands = _arena.Operands;
            switch (op.Code)
            {
                case ContentOperatorCode.SetFont when operands.Count > 0 && operands[0].Kind == ContentOperandKind.Name:
                    shown = Resolve(fonts?.GetValueOrDefault(new CosName(Encoding.ASCII.GetString(operands[0].Bytes)))) is CosDictionary font
                        ? ShownCodes(font)
                        : null;
                    break;
                case ContentOperatorCode.ShowText or ContentOperatorCode.NextLineShowText or ContentOperatorCode.NextLineShowTextWithSpacing
                    when shown is not null && operands.Count > 0 && operands[operands.Count - 1].Kind == ContentOperandKind.String:
                    shown.UnionWith(operands[operands.Count - 1].Bytes.ToArray());
                    break;
                case ContentOperatorCode.ShowTextAdjusted when shown is not null && operands.Count > 0 && operands[0].Kind == ContentOperandKind.Array:
                    foreach (ContentOperand item in operands[0].Items)
                    {
                        if (item.Kind == ContentOperandKind.String)
                        {
                            shown.UnionWith(item.Bytes.ToArray());
                        }
                    }

                    break;
            }

            _arena.Clear();
        }

        _arena.Trim();
    }

    private HashSet<byte> ShownCodes(CosDictionary font)
    {
        if (!_shown.TryGetValue(font, out HashSet<byte>? codes))
        {
            codes = [];
            _shown[font] = codes;
        }

        return codes;
    }

    /// <summary>Replaces every non-embedded simple font a resource dictionary names by its embedded conversion (see the remarks).</summary>
    private void EmbedFonts()
    {
        var converted = new Dictionary<CosDictionary, PendingFont?>(ReferenceEqualityComparer.Instance);
        var pending = new List<PendingFont>();
        foreach (CosDictionary fonts in _fontResources)
        {
            foreach ((CosName name, CosObject value) in fonts.ToArray())
            {
                if (Resolve(value) is not CosDictionary font || NameOf(font, Subtype) is not ("Type1" or "MMType1" or "TrueType"))
                {
                    continue;
                }

                if (!converted.TryGetValue(font, out PendingFont? replacement))
                {
                    replacement = Embed(font, value as CosReference);
                    converted[font] = replacement;
                    if (replacement is not null)
                    {
                        pending.Add(replacement);
                        _replaced.Add(font);
                    }
                }

                if (replacement is not null)
                {
                    fonts[name] = replacement.Reference;
                }
            }
        }

        // The subset tag of a shared CIDFont changes as later fonts add glyphs to it: the Type 0 fonts take its final name.
        foreach (PendingFont font in pending)
        {
            font.Dictionary[FontNames.BaseFont] = Resolve(((CosDictionary)Resolve(font.Subset.CidFont)).GetValueOrDefault(FontNames.BaseFont));
        }
    }

    private sealed record PendingFont(CosReference Reference, CosDictionary Dictionary, SharedFontSubset Subset);

    private PendingFont? Embed(CosDictionary dictionary, CosReference? reference)
    {
        if (_document.GetFont((CosObject?)reference ?? dictionary) is not PdfSimpleFont font || font.IsEmbedded)
        {
            return null;
        }

        string name = font.BaseFont ?? "(unnamed)";
        if (font.Substitute is not { Resolution: { } resolution } substitute)
        {
            NotFixed($"the font {name} is not embedded: the document's font resolvers have no program for it (configure PdfOptions.UseStandard14Fonts or a font resolver; rule 6.2.11.4.1-1)");
            return null;
        }

        if (substitute.Program is Fonts.Type1.Type1FontProgram)
        {
            NotFixed($"the font {name} is not embedded: its program {substitute.Name} is a Type 1 program, whose glyphs the conversion cannot address (rule 6.2.11.4.1-1)");
            return null;
        }

        HashSet<byte> shown = _shown.GetValueOrDefault(dictionary) ?? [];
        var glyphs = new Dictionary<int, int>();
        var codes = new SortedDictionary<int, int>();
        var texts = new Dictionary<int, string>();
        Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
        for (int code = 0; code < 256; code++)
        {
            int glyph = font.GetSubstituteGlyphId((byte)code, out int[]? components);
            bool usable = glyph > 0 && components is null;
            if (!usable)
            {
                if (shown.Contains((byte)code))
                {
                    NotFixed(string.Create(CultureInfo.InvariantCulture, $"the font {name} is not embedded: its program {substitute.Name} has no glyph for the code {code} the text shows (rules 6.2.11.4.1-1, 6.2.11.8-1)"));
                    return null;
                }

                continue;
            }

            if (shown.Count > 0 && !shown.Contains((byte)code))
            {
                // The subset holds the glyphs the content shows; a font no content shows (a form's default resources) keeps them all.
                continue;
            }

            if (shown.Contains((byte)code) && Math.Abs(font.GetWidth((byte)code) - substitute.GetWidth(glyph)) > PdfAFontGlyphs.WidthTolerance)
            {
                NotFixed(string.Create(CultureInfo.InvariantCulture, $"the font {name} is not embedded: its program {substitute.Name} is {substitute.GetWidth(glyph):0.#} units wide for the code {code}, the font {font.GetWidth((byte)code):0.#} (rules 6.2.11.4.1-1, 6.2.11.5-1)"));
                return null;
            }

            codes[code] = glyph;
            int written = font.GetUnicode(new CharacterCode((uint)code, 1, true), buffer, out UnicodeSource source);
            string text = source is UnicodeSource.Unmapped ? string.Empty : buffer[..written].ToString();
            if (text.Length > 0 && text.AsSpan().IndexOfAny('\0', '﻿', '￾') < 0)
            {
                texts[code] = text;
            }

            int scalar = texts.TryGetValue(code, out string? mapped) && Rune.TryGetRuneAt(mapped, 0, out Rune rune) && rune.Utf16SequenceLength == mapped.Length
                ? rune.Value
                : 0xF0000 + glyph;
            glyphs.TryAdd(scalar, glyph);
        }

        if (codes.Count == 0)
        {
            NotFixed($"the font {name} is not embedded: its program {substitute.Name} has no glyph for any of its codes (rule 6.2.11.4.1-1)");
            return null;
        }

        if (EmbeddedCanvasFont.Embed(_document, resolution, glyphs, out string? problem) is not { } subset)
        {
            NotFixed($"the font {name} is not embedded: its program {substitute.Name} cannot be embedded, {problem} (rule 6.2.11.4.1-1)");
            return null;
        }

        string cmapName = string.Create(CultureInfo.InvariantCulture, $"Broadside-PdfA-{string.Concat(name.Where(char.IsAsciiLetterOrDigit))}-{++_converted}");
        var cmapDictionary = new CosDictionary
        {
            [KnownNames.Type] = new CosName("CMap"),
            [new CosName("CMapName")] = new CosName(cmapName),
            [CompositeFontNames.CidSystemInfo] = new CosDictionary
            {
                [CompositeFontNames.Registry] = new CosString("Adobe"u8),
                [CompositeFontNames.Ordering] = new CosString("Identity"u8),
                [CompositeFontNames.Supplement] = new CosInteger(0),
            },
        };
        var type0 = new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [FontNames.Subtype] = FontNames.Type0,
            [FontNames.BaseFont] = new CosName(name),
            [FontNames.Encoding] = _document.AddObject(new CosStream(cmapDictionary, CodeCMap(cmapName, codes))),
            [CompositeFontNames.DescendantFonts] = new CosArray([subset.CidFont]),
            [ToUnicodeKey] = _document.AddObject(new CosStream(new CosDictionary(), ToUnicode(texts))),
        };
        Changed($"the font {name} is embedded as a subset of {substitute.Name} (rule 6.2.11.4.1-1)");
        return new PendingFont(_document.AddObject(type0), type0, subset);
    }

    /// <summary>A one-byte CMap (§9.7.5.4, Adobe TN 5014) mapping each code to the CID of its glyph (the glyph id in the program).</summary>
    private static byte[] CodeCMap(string name, SortedDictionary<int, int> codes)
    {
        var text = new StringBuilder();
        text.Append("%!PS-Adobe-3.0 Resource-CMap\n/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> def\n")
            .Append(CultureInfo.InvariantCulture, $"/CMapName /{name} def\n/CMapType 1 def\n1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
        foreach (KeyValuePair<int, int>[] chunk in codes.Chunk(100))
        {
            text.Append(CultureInfo.InvariantCulture, $"{chunk.Length} begincidchar\n");
            foreach ((int code, int glyph) in chunk)
            {
                text.Append(CultureInfo.InvariantCulture, $"<{code:X2}> {glyph}\n");
            }

            text.Append("endcidchar\n");
        }

        text.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>A ToUnicode CMap (§9.10.3) for one-byte codes.</summary>
    private static byte[] ToUnicode(Dictionary<int, string> texts)
    {
        var text = new StringBuilder();
        text.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
        foreach (KeyValuePair<int, string>[] chunk in texts.OrderBy(pair => pair.Key).Chunk(100))
        {
            text.Append(CultureInfo.InvariantCulture, $"{chunk.Length} beginbfchar\n");
            foreach ((int code, string mapped) in chunk)
            {
                text.Append(CultureInfo.InvariantCulture, $"<{code:X2}> <{System.Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(mapped))}>\n");
            }

            text.Append("endbfchar\n");
        }

        text.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
