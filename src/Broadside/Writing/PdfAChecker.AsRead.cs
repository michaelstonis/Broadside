using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>
/// The read mode of the PDF/A check (issue #651): the file as it is, not as a save would write it. Where a save writes something
/// conformant by itself (the header, the binary comment, the trailer's <c>ID</c>, the bytes after <c>%%EOF</c>, the output intent, the
/// XMP identification, PDF/A-4's document information dictionary and catalog version), the read mode checks what the file holds, and
/// every repair the parser made of the file structure is a violation: leniency never hides non-conformance (ADR 0005).
/// </summary>
/// <remarks>
/// <para>
/// ISO 19005-2 and ISO 19005-3, cited by veraPDF rule identifiers: 6.1.2-1 (the header is <c>%PDF-1.n</c>, n at most 7, at byte 0),
/// 6.1.2-2 (a comment of four bytes above 127 follows it), 6.1.3-1 (the last trailer has an <c>ID</c>), 6.1.3-2 (no
/// <c>Encrypt</c>), 6.1.3-3 (nothing but one end-of-line marker after the last <c>%%EOF</c>), 6.1.6-2 (a hexadecimal string holds
/// only hexadecimal digits: the parser's <c>HexStringInvalidDigit</c>), 6.1.7.1-1 (a stream's <c>Length</c> is its data's:
/// <c>StreamLengthInvalid</c>), 6.1.7.1-2 (an end-of-line marker after <c>stream</c>: <c>StreamKeywordEndOfLine</c>), 6.2.3-1 (the
/// destination profile is an output or monitor profile of RGB, CMYK or gray, below version 5), 6.2.4.3-2 to -4 (device colour without
/// a PDF/A output intent of its model or a device-independent default colour space), 6.2.10-2 (a page using transparency without a
/// PDF/A output intent has a group <c>CS</c>), 6.6.2.1-1 to -4 (a catalog metadata stream, without <c>bytes</c> or <c>encoding</c> in its
/// <c>xpacket</c> header, that parses as XMP), 6.6.4-1 to -3 (the identification, its part and its conformance).
/// </para>
/// <para>
/// ISO 19005-4: 6.1.2-1 (<c>%PDF-2.n</c>), 6.1.2-2, 6.1.3-1 to -3 as above, 6.1.3-4 and -5 (a trailer <c>Info</c> only with the
/// catalog's <c>PieceInfo</c>, holding <c>ModDate</c> alone), 6.1.5-2, 6.1.6.1-1, 6.1.12-1 (a catalog <c>Version</c> is 2.n), 6.2.3-1,
/// 6.2.3-4 (at most one PDF/A output intent), 6.2.4.3-2 to -4, 6.2.9-2, 6.7.2-1 to -4, 6.7.3-1 to -3 and 6.7.3-5 (<c>pdfaid:rev</c> 2020).
/// </para>
/// <para>
/// Any other repair or deviation the parser recorded in the file structure or the object syntax (ISO 32000-2 §7.3 and §7.5: a
/// cross-reference table rebuilt by scanning, a wrong <c>startxref</c>, a broken <c>Prev</c> chain) has no veraPDF rule of its own:
/// it is reported under the ISO 32000-2 clause its diagnostic cites, as <c>ISO 32000-2 7.5.5-1</c>, so a repaired file is never
/// conformant.
/// </para>
/// </remarks>
internal sealed partial class PdfAChecker
{
    private static readonly CosName PieceInfo = new("PieceInfo");
    private static readonly CosName ModDate = new("ModDate");
    private static readonly CosName PdfAOutputIntentSubtype = new("GTS_PDFA1");

    /// <summary>How many bytes of the start and of the end of the file the byte-level rules read.</summary>
    private const int FileEdge = 1024;

    private static readonly CosName Group = new("Group");
    private static readonly CosName SMaskInData = new("SMaskInData");
    private static readonly CosName FillAlpha = new("ca");
    private static readonly CosName StrokeAlpha = new("CA");

    private bool _asRead;

    /// <summary>Whether the page being checked uses transparency: a constant alpha below 1, a soft mask or a blend mode other than Normal.</summary>
    private bool _transparency;

    /// <summary>The resource dictionary of the content or resources being checked, whose <c>Default*</c> colour spaces apply.</summary>
    private CosDictionary? _resources;

    /// <summary>
    /// Whether the current resource dictionary sets a default colour space for the device family <paramref name="name"/>
    /// (<c>DefaultRGB</c>, <c>DefaultCMYK</c>, <c>DefaultGray</c>, ISO 32000-2 §8.6.5.6), which device colour then uses: veraPDF
    /// rules 6.2.4.3-2 to -4 allow device colour under a device-independent default (issue #651).
    /// </summary>
    private bool HasDefaultSpace(string name)
    {
        string? key = name switch
        {
            "DeviceRGB" or "RGB" => "DefaultRGB",
            "DeviceCMYK" or "CMYK" => "DefaultCMYK",
            "DeviceGray" or "G" => "DefaultGray",
            _ => null,
        };
        return key is not null && _resources is not null && Dictionary(_resources, ColorSpace)?.GetValueOrDefault(new CosName(key)) is { } space
            && Resolve(space) is CosArray { Count: > 1 } array && Resolve(array[0]) is CosName { Value: "ICCBased" or "CalRGB" or "CalGray" or "Lab" };
    }

    /// <summary>
    /// Checks <paramref name="document"/> as it is against <paramref name="conformance"/> (issue #651): the rules of a save, with
    /// what the save would write checked in the file instead. It reads and changes nothing in the document, so it may run while other
    /// threads render it.
    /// </summary>
    /// <param name="document">The document, as read.</param>
    /// <param name="conformance">The level: any but <see cref="PdfAConformance.None"/>.</param>
    /// <returns>The rules not met, the file's first; empty when no rule checked is broken.</returns>
    public static IReadOnlyList<PdfAViolation> Validate(PdfDocument document, PdfAConformance conformance) =>
        Check(document, new PdfSaveOptions().WithPdfAConformance(conformance), asRead: true);

    /// <summary>What breaks rule 6.2.4.3-2 or -3: device colour of a model the PDF/A output intent does not have, or no such intent (as read).</summary>
    private string Uncovered(string space) => _outputSpace is null
        ? $"{space} is used, and the file has no PDF/A output intent"
        : $"{space} is used, which the output intent's {_outputSpace.Trim()} profile does not cover";

    /// <summary>Reports a rule the read mode adds, under the identifier of the level's profile: <paramref name="pdfA4Rule"/> for PDF/A-4 (none: not a PDF/A-4 rule).</summary>
    private void ReportAsRead(string pdfA2Rule, string? pdfA4Rule, string message, CosReference? reference = null)
    {
        string? rule = _conformance.Part() == 4 ? pdfA4Rule : pdfA2Rule;
        if (rule is not null)
        {
            Report(rule, message, reference, renumber: false);
        }
    }

    /// <summary>The file-level rules of the file as read: its bytes, its trailer, its encryption, its metadata.</summary>
    private void CheckFileAsRead()
    {
        if (_document.Pages.Count == 0)
        {
            Report("6.1.1-1", "the document has no pages: its page tree is missing or damaged");
        }

        CheckFileBytes();
        CosDictionary trailer = _document.NewestTrailer;
        if (Resolve(trailer.GetValueOrDefault(KnownNames.ID)) is not CosArray { Count: > 0 } id || Resolve(id[0]) is not CosString { Bytes.Length: > 0 })
        {
            Report("6.1.3-1", "the last trailer has no ID (file identifiers)", renumber: false);
        }

        if (_document.Security is not null || _document.Trailer.ContainsKey(KnownNames.Encrypt))
        {
            Report("6.1.3-2", "the file is encrypted (its trailer has an Encrypt entry)", renumber: false);
        }

        if (_conformance.Part() == 4)
        {
            CheckPdfA4Information();
        }

        CheckMetadataAsRead();
    }

    /// <summary>The header, the binary comment and the end of the file (6.1.2-1, 6.1.2-2, 6.1.3-3), from the bytes the document was read from.</summary>
    private void CheckFileBytes()
    {
        long length = _document.SourceLength;
        Span<byte> start = stackalloc byte[(int)Math.Min(FileEdge, length)];
        start = start[.._document.ReadSource(0, start)];
        int lineEnd = start.IndexOfAny((byte)'\r', (byte)'\n');
        ReadOnlySpan<byte> header = lineEnd < 0 ? start : start[..lineEnd];
        bool part4 = _conformance.Part() == 4;
        bool valid = header.Length == 8 && header.StartsWith(part4 ? "%PDF-2."u8 : "%PDF-1."u8)
            && header[7] is >= (byte)'0' and <= (byte)'9' && (part4 || header[7] <= (byte)'7');
        if (!valid)
        {
            Report("6.1.2-1", $"the file does not begin at byte 0 with a {(part4 ? "%PDF-2.n" : "%PDF-1.n header, n at most 7,")} header followed by an end-of-line marker", renumber: false);
        }

        int comment = lineEnd < 0 ? -1 : lineEnd + (start[lineEnd] == '\r' && lineEnd + 1 < start.Length && start[lineEnd + 1] == '\n' ? 2 : 1);
        if (comment < 0 || comment + 5 > start.Length || start[comment] != '%' || start.Slice(comment + 1, 4).IndexOfAnyInRange((byte)0, (byte)127) >= 0)
        {
            Report("6.1.2-2", "the header is not followed by a comment line of at least four bytes above 127", renumber: false);
        }

        Span<byte> end = stackalloc byte[(int)Math.Min(FileEdge, length)];
        end = end[.._document.ReadSource(length - end.Length, end)];
        int marker = end.LastIndexOf("%%EOF"u8);
        ReadOnlySpan<byte> after = marker < 0 ? end : end[(marker + 5)..];
        if (marker < 0 || !(after.IsEmpty || after.SequenceEqual("\n"u8) || after.SequenceEqual("\r"u8) || after.SequenceEqual("\r\n"u8)))
        {
            Report("6.1.3-3", marker < 0 ? "the file does not end with an end-of-file marker" : $"{after.Length} bytes follow the last end-of-file marker", renumber: false);
        }
    }

    /// <summary>
    /// The document information dictionary and the catalog's version as PDF/A-4 allows them (ISO 19005-4, veraPDF rules 6.1.3-4,
    /// 6.1.3-5 and 6.1.12-1), which a PDF/A-4 save makes conformant.
    /// </summary>
    private void CheckPdfA4Information()
    {
        if (_document.Trailer.TryGetValue(KnownNames.Info, out CosObject? entry))
        {
            if (!_document.Catalog.ContainsKey(PieceInfo))
            {
                Report("6.1.3-4", "the trailer has an Info (document information) entry, and the catalog has no PieceInfo", renumber: false);
            }
            else if (Resolve(entry) is CosDictionary info && (info.Count != 1 || !info.ContainsKey(ModDate)))
            {
                Report("6.1.3-5", "the document information dictionary holds entries other than ModDate", entry as CosReference, renumber: false);
            }
        }

        if (_document.Catalog.ContainsKey(KnownNames.Version)
            && Resolve(_document.Catalog.GetValueOrDefault(KnownNames.Version)) is not CosName { Value: ['2', '.', >= '0' and <= '9'] })
        {
            Report("6.1.12-1", "the catalog's Version is not 2.n, as a PDF/A-4 file's is", renumber: false);
        }
    }

    /// <summary>
    /// The catalog's XMP packet and its PDF/A identification (6.6.2.1-1 to -4 and 6.6.4-1 to -3; ISO 19005-4 6.7.2-1 to -4, 6.7.3-1 to
    /// -3 and 6.7.3-5), which a save writes.
    /// </summary>
    private void CheckMetadataAsRead()
    {
        bool part4 = _conformance.Part() == 4;
        if (_document.Metadata is not { } metadata)
        {
            ReportAsRead("6.6.2.1-1", "6.7.2-1", "the catalog has no Metadata stream");
            return;
        }

        CosReference? reference = _document.Catalog.GetValueOrDefault(Metadata) as CosReference;
        ReadOnlySpan<byte> data;
        try
        {
            data = metadata.Data.Span;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            data = [];
        }

        int start = data.IndexOf("<?xpacket"u8);
        if (start >= 0)
        {
            ReadOnlySpan<byte> rest = data[start..];
            int close = rest.IndexOf("?>"u8);
            ReadOnlySpan<byte> processing = close < 0 ? rest : rest[..close];
            if (processing.IndexOf(" bytes="u8) >= 0)
            {
                ReportAsRead("6.6.2.1-2", "6.7.2-2", "the XMP packet header has a bytes attribute", reference);
            }

            if (processing.IndexOf(" encoding="u8) >= 0)
            {
                ReportAsRead("6.6.2.1-3", "6.7.2-3", "the XMP packet header has an encoding attribute", reference);
            }
        }

        if (metadata.Packet is not { } packet)
        {
            ReportAsRead("6.6.2.1-4", "6.7.2-4", "the catalog's metadata stream is not a well-formed XMP packet", reference);
            return;
        }

        if (packet.PdfAPart is not { } part)
        {
            ReportAsRead("6.6.4-1", "6.7.3-1", "the XMP metadata has no PDF/A identification (pdfaid:part)", reference);
            return;
        }

        if (part != _conformance.Part())
        {
            ReportAsRead("6.6.4-2", "6.7.3-2", string.Create(CultureInfo.InvariantCulture, $"pdfaid:part is {part}; a {_conformance.Describe()} file states {_conformance.Part()}"), reference);
        }

        string? stated = packet.PdfAConformance;
        if (part4)
        {
            string? expected = _conformance.Level();
            if (!string.Equals(stated, expected, StringComparison.Ordinal))
            {
                Report("6.7.3-3", $"pdfaid:conformance is {stated ?? "absent"}; a {_conformance.Describe()} file states {expected ?? "none"}", reference, renumber: false);
            }

            string? revision = packet.GetText(XmpNamespaces.PdfAIdentification, "rev");
            if (revision != _conformance.Revision())
            {
                Report("6.7.3-5", $"pdfaid:rev is {revision ?? "absent"}; a PDF/A-4 file states {_conformance.Revision()}", reference, renumber: false);
            }
        }
        else if (!AcceptedConformance(_conformance.Level()).Contains(stated ?? string.Empty, StringComparer.Ordinal))
        {
            ReportAsRead("6.6.4-3", null, $"pdfaid:conformance is {stated ?? "absent"}, which a {_conformance.Describe()} file does not state", reference);
        }

        // Level A meets level U, which meets level B: the profile of a level accepts the stricter ones' identification.
        static string[] AcceptedConformance(string? level) => level switch
        {
            "A" => ["A"],
            "U" => ["U", "A"],
            _ => ["B", "U", "A"],
        };
    }

    /// <summary>
    /// The output intents as the file has them (6.2.3-1 for the first destination profile; ISO 19005-4 6.2.3-4, at most one PDF/A
    /// output intent), and the colour space device colour needs, from the <c>GTS_PDFA1</c> intent alone: without one, no device colour
    /// is allowed (6.2.4.3-2 to -4).
    /// </summary>
    private void CheckOutputIntentsAsRead(CosArray? intents)
    {
        _outputSpace = null;
        int pdfA = 0;
        foreach (CosObject item in intents ?? [])
        {
            if (Resolve(item) is not CosDictionary intent || !PdfAOutputIntentSubtype.Equals(Resolve(intent.GetValueOrDefault(S))))
            {
                continue;
            }

            pdfA++;
            if (_outputSpace is null && Resolve(intent.GetValueOrDefault(DestOutputProfile)) is CosStream profile)
            {
                ReadOnlySpan<byte> data = _document.DecodeStream(profile).Span;
                _outputSpace = data.Length >= 20 ? Encoding.ASCII.GetString(data[16..20]) : string.Empty;
                string deviceClass = data.Length >= 16 ? Encoding.ASCII.GetString(data[12..16]) : string.Empty;
                if (deviceClass is not ("prtr" or "mntr") || _outputSpace is not ("RGB " or "CMYK" or "GRAY") || data.Length < 9 || data[8] >= 5)
                {
                    Report("6.2.3-1", $"the output intent's destination profile is not an output or monitor profile of RGB, CMYK or gray below ICC version 5 (class '{deviceClass}', space '{_outputSpace}')", intent.GetValueOrDefault(DestOutputProfile) as CosReference, renumber: false);
                }
            }
        }

        if (pdfA > 1 && _conformance.Part() == 4)
        {
            Report("6.2.3-4", "the catalog has more than one GTS_PDFA1 output intent", renumber: false);
        }
    }

    /// <summary>Whether a graphics state makes what it paints transparent (ISO 32000-2 §11.6.4.4: CA, ca, SMask, BM).</summary>
    private bool IsTransparent(CosDictionary state) =>
        Number(state.GetValueOrDefault(FillAlpha) ?? CosNull.Instance) is < 1
        || Number(state.GetValueOrDefault(StrokeAlpha) ?? CosNull.Instance) is < 1
        || Resolve(state.GetValueOrDefault(SMask)) is CosDictionary
        || Resolve(state.GetValueOrDefault(BM)) switch
        {
            CosName name => name.Value is not ("Normal" or "Compatible"),
            CosArray modes => modes.Any(mode => Resolve(mode) is CosName { Value: not ("Normal" or "Compatible") }),
            _ => false,
        };

    /// <summary>
    /// As read only: a page that uses transparency, in a file without a PDF/A output intent, has a page output intent or a group whose
    /// <c>CS</c> is the blending colour space (veraPDF rule 6.2.10-2; ISO 19005-4 6.2.9-2). A save adds the output intent.
    /// </summary>
    private void CheckPageTransparency(PdfPage page)
    {
        if (!_asRead || !_transparency || _outputSpace is not null || Dictionary(page.Dictionary, Group)?.ContainsKey(CS) == true
            || Resolve(page.Dictionary.GetValueOrDefault(OutputIntents)) is CosArray { Count: > 0 })
        {
            return;
        }

        ReportAsRead("6.2.10-2", "6.2.9-2", "a page uses transparency without a blending colour space (a Group CS), and the file has no PDF/A output intent", page.Reference);
    }

    /// <summary>
    /// The repairs and deviations the parser recorded while the check read the file (issue #651): the cross-reference information
    /// rebuilt, a wrong stream <c>Length</c>, and any other diagnostic of the file structure or the object syntax. Only as read: a
    /// save writes the file anew.
    /// </summary>
    private void CheckRepairs()
    {
        if (!_asRead)
        {
            return;
        }

        foreach (Diagnostic diagnostic in _document.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Information || !DiagnosticCatalog.TryGet(diagnostic.Code, out DiagnosticCode? code)
                || code.Area is not (DiagnosticArea.FileStructure or DiagnosticArea.ObjectSyntax))
            {
                continue;
            }

            switch (code.Name)
            {
                case nameof(ObjectSyntaxDiagnostics.StreamLengthInvalid):
                    ReportAsRead("6.1.7.1-1", "6.1.6.1-1", $"a stream's Length does not match its data ({diagnostic.Message})", diagnostic.ObjectReference);
                    break;
                case nameof(ObjectSyntaxDiagnostics.StreamKeywordEndOfLine):
                    ReportAsRead("6.1.7.1-2", null, "a stream keyword is not followed by CRLF or LF", diagnostic.ObjectReference);
                    break;
                case nameof(ObjectSyntaxDiagnostics.HexStringInvalidDigit):
                    ReportAsRead("6.1.6-2", "6.1.5-2", "a hexadecimal string holds a character that is not a hexadecimal digit", diagnostic.ObjectReference);
                    break;
                case nameof(FileStructureDiagnostics.HeaderMissing) or nameof(FileStructureDiagnostics.HeaderInvalid) or nameof(FileStructureDiagnostics.HeaderVersionInvalid)
                    or nameof(FileStructureDiagnostics.HeaderBinaryCommentMissing) or nameof(FileStructureDiagnostics.EndOfFileMarkerMissing)
                    or nameof(FileStructureDiagnostics.EndOfFileMarkerNotLast) or nameof(FileStructureDiagnostics.TrailerIdMissing):
                    // Checked from the bytes by CheckFileBytes and CheckFileAsRead (6.1.2-1, 6.1.2-2, 6.1.3-1, 6.1.3-3).
                    break;
                default:
                    Report(RepairRule(code), $"the parser repaired or ignored a deviation from ISO 32000-2: {diagnostic.Message} ({code.Name})", diagnostic.ObjectReference, renumber: false);
                    break;
            }
        }
    }

    /// <summary>The identifier a repair is reported under: the ISO 32000-2 clause its code cites, as <c>ISO 32000-2 7.5.4-1</c>.</summary>
    private static string RepairRule(DiagnosticCode code)
    {
        string clause = code.Clause is { } cited && cited.StartsWith("ISO 32000-2 §", StringComparison.Ordinal) ? cited["ISO 32000-2 §".Length..] : "7.5";
        int space = clause.IndexOfAny([' ', ',', ';']);
        return $"ISO 32000-2 {(space < 0 ? clause : clause[..space])}-1";
    }
}
