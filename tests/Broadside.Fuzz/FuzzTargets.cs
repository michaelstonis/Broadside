using System.Buffers;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security;
using Broadside.TestSupport;
using SharpFuzz;

namespace Broadside.Fuzz;

/// <summary>
/// The fuzz targets, by name. A target takes one input and must either return or throw; the harness treats any escaped
/// exception as a finding. Every parser and codec gets a target here as it lands (CLAUDE.md, "Code conventions").
/// </summary>
internal static class FuzzTargets
{
    /// <summary>Every registered target. Names are the stable identifiers used on the command line and in CI.</summary>
    public static IReadOnlyDictionary<string, ReadOnlySpanAction> All { get; } = new Dictionary<string, ReadOnlySpanAction>(StringComparer.Ordinal)
    {
        ["lexer"] = Lexer,
        ["content-lexer"] = ContentLexer,
        ["content-interpreter"] = ContentInterpreterTarget,
        ["object-parser"] = ObjectParser,
        ["document"] = Document,
        ["windowed-document"] = WindowedDocument,
        ["save"] = Save,
        ["hint-tables"] = HintTables,
        ["xref-stream"] = XrefStream,
        ["object-stream"] = ObjectStreamTarget,
        ["repair"] = Repair,
        ["filter-asciihex"] = data => Filter(new AsciiHexDecodeFilter(), data, parameters: null, maxRatio: 1),
        ["filter-ascii85"] = data => Filter(new Ascii85DecodeFilter(), data, parameters: null, maxRatio: 4),
        ["filter-lzw"] = Lzw,
        ["filter-flate"] = data => Filter(new FlateDecodeFilter(), data, parameters: null, maxRatio: 1100),
        ["filter-runlength"] = data => Filter(new RunLengthDecodeFilter(), data, parameters: null, maxRatio: 128),
        ["filter-predictor"] = PredictorTarget,
        ["encrypted-document"] = EncryptedDocument,
        ["decrypt"] = Decrypt,
        ["mac-token"] = MacToken,
        ["public-key"] = PublicKey.Target,
        ["xmp"] = Xmp,
        ["pdf-date"] = PdfDateTarget,
        ["structure-tree"] = StructureTree.Target,
        ["function-type4"] = FunctionType4,
        ["function-sampled"] = FunctionSampled,
        ["optional-content"] = OptionalContentTarget.Target,
    };

    private static readonly Lazy<PdfDocument> EmptyDocument = new(() => PdfDocument.Create());

    /// <summary>
    /// Names the targets use, kept out of this class's static constructor: under libFuzzer, code of the instrumented library must
    /// not run before <c>Fuzzer.LibFuzzer.Run</c> has attached the coverage memory, and looking up a target runs that constructor.
    /// </summary>
    private static class Names
    {
        public static readonly CosName Contents = new("Contents");
        public static readonly CosName Font = new("Font");
        public static readonly CosName Info = new("Info");
    }

    /// <summary>
    /// Opens the input as a whole file in lenient mode and reads everything the document model exposes: version, trailer, every
    /// page's boxes, rotation, user unit, resources, fonts and content, the revisions, the linearization dictionary and hint tables,
    /// the outline and the named destinations, then walks every object and decodes every stream (<see cref="DocumentWalker"/>). A
    /// <see cref="DiagnosticException"/> (no catalog even after a scan) and the password, certificate and unsupported-encryption
    /// exceptions are the documented outcomes; any other exception is a finding. Every box must be normalized and every rotation one
    /// of 0, 90, 180, 270; every revision must be a non-empty prefix of the input at its own index.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.1 to §7.5.6, §7.7.2, §7.7.3, Annex F.</remarks>
    private static void Document(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = OpenOrNull(data);
        if (document is null)
        {
            return;
        }

        _ = document.Version;
        _ = document.Trailer.Count;
        for (int index = 0; index < document.Revisions.Count; index++)
        {
            PdfRevision revision = document.Revisions[index];
            if (revision.Index != index || revision.Length <= 0 || revision.Length > data.Length)
            {
                throw new InvalidOperationException($"Revision {revision.Index} at position {index} has length {revision.Length} in a file of {data.Length} bytes.");
            }
        }

        _ = document.IsLinearized;
        if (document.Linearization is { } linearization)
        {
            _ = (linearization.FileLength, linearization.PageCount, linearization.FirstPageObjects.Count);
            _ = linearization.Hints?.Pages.Count;
        }

        ReadCatalogEssentials(document);
        foreach (PdfPage page in document.Pages)
        {
            foreach (PdfRectangle box in (ReadOnlySpan<PdfRectangle>)[page.MediaBox, page.CropBox, page.BleedBox, page.TrimBox, page.ArtBox])
            {
                if (!(box.Left <= box.Right && box.Bottom <= box.Top))
                {
                    throw new InvalidOperationException($"Page box {box} is not normalized.");
                }
            }

            if (page.Rotation is not (0 or 90 or 180 or 270) || page.UserUnit <= 0)
            {
                throw new InvalidOperationException($"Rotation {page.Rotation} or user unit {page.UserUnit} is out of range.");
            }

            ReadFonts(document, page);
            page.ProcessContent(new CheckingProcessor());
        }

        Navigation(document);

        // Then everything else the document model exposes: every object reachable from the trailer or numbered below Size, and
        // every stream decoded through its filters (the walk the real-world corpus gate runs, issue #47).
        DocumentWalkResult walk = DocumentWalker.Walk(document);
        if (walk.Pages != document.Pages.Count || walk.Streams > walk.Objects)
        {
            throw new InvalidOperationException($"The walk saw {walk.Pages} pages and {walk.Streams} streams in {walk.Objects} objects; the document has {document.Pages.Count} pages.");
        }

        _ = ActionWalker.Walk(document);
        Annotations(document);
    }

    /// <summary>
    /// Reads every annotation of every page with every appearance (issue #71) and checks what the model promises: every rectangle
    /// normalized, every annotation on the page that lists it, the same view for the same dictionary.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.5.</remarks>
    private static void Annotations(PdfDocument document)
    {
        _ = Broadside.TestSupport.AnnotationWalker.Walk(document);
        foreach (PdfPage page in document.Pages)
        {
            IReadOnlyList<Broadside.Annotations.PdfAnnotation> annotations = page.Annotations;
            for (int index = 0; index < annotations.Count; index++)
            {
                Broadside.Annotations.PdfAnnotation annotation = annotations[index];
                PdfRectangle rect = annotation.Rect;
                if (!(rect.Left <= rect.Right && rect.Bottom <= rect.Top) || annotation.Page is null || !ReferenceEquals(annotation, page.Annotations[index]))
                {
                    throw new InvalidOperationException("An annotation's rectangle is not normalized, it has no page, or a second read gave another view.");
                }
            }
        }
    }

    /// <summary>
    /// Walks the outline and the named destinations (issue #70) and checks what the walk promises: every item below the 256-level cap,
    /// each item reached once, every resolved page index inside the page list, every name-tree key enumerated once.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.9.6, §12.3.2, §12.3.3.</remarks>
    private static void Navigation(PdfDocument document)
    {
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfOutlineItem>(document.Outline?.Items ?? []);
        while (pending.TryPop(out PdfOutlineItem? item))
        {
            if (!seen.Add(item.Dictionary) || item.Level >= 256)
            {
                throw new InvalidOperationException($"Outline item at level {item.Level} is repeated or below the depth cap.");
            }

            _ = (item.Title, item.Color, item.Flags, item.Count, item.StructureElement);
            CheckDestination(document, item.Destination);
            if (item.Action is PdfGoToAction goTo)
            {
                CheckDestination(document, goTo.Destination);
            }

            foreach (PdfOutlineItem child in item.Children)
            {
                pending.Push(child);
            }
        }

        if (document.Names?.Dests is { } tree)
        {
            // After a complete walk, lookups agree with enumeration even in a damaged tree (the walk's first deviation switches
            // lookups to the index); before it, a lookup follows the Limits it finds.
            var keys = new HashSet<CosString>();
            foreach (KeyValuePair<CosString, CosObject> entry in tree.ToList())
            {
                if (!keys.Add(entry.Key) || !tree.TryGetValue(entry.Key, out CosObject? value) || !ReferenceEquals(value, entry.Value))
                {
                    throw new InvalidOperationException("A name tree key is enumerated twice or looks up to another value.");
                }

                CheckDestination(document, document.GetNamedDestination(entry.Key));
            }
        }
    }

    private static void CheckDestination(PdfDocument document, PdfDestination? destination)
    {
        PdfExplicitDestination? resolved = destination is PdfNamedDestination named ? named.Resolve() : destination as PdfExplicitDestination;
        if (resolved?.PageIndex is { } index && (index < 0 || index >= document.Pages.Count))
        {
            throw new InvalidOperationException($"Destination page index {index} is outside the {document.Pages.Count} pages.");
        }

        _ = (resolved?.View, resolved?.Left, resolved?.Top, resolved?.Right, resolved?.Bottom, resolved?.Zoom, resolved?.IsValid, resolved?.TargetKind);
    }

    /// <summary>Reads every font of a page's resources through the font model (#49): each simple font's 256 names and widths, and its descriptor.</summary>
    private static void ReadFonts(PdfDocument document, PdfPage page)
    {
        if (page.Resources is not { } resources
            || !resources.TryGetValue(Names.Font, out CosObject? value)
            || document.Resolve(value) is not CosDictionary fonts)
        {
            return;
        }

        foreach (KeyValuePair<CosName, CosObject> entry in fonts)
        {
            PdfFont? font = document.GetFont(entry.Value);
            if (font?.Descriptor is { } descriptor)
            {
                _ = (descriptor.Flags, descriptor.FontBBox, descriptor.FontStretch, descriptor.FontWeight, descriptor.MissingWidth, descriptor.FontFamily);
            }

            if (font is PdfSimpleFont simple)
            {
                for (int code = 0; code < 256; code++)
                {
                    if (simple.GetGlyphName((byte)code) is not { Length: > 0 } || !double.IsFinite(simple.GetWidth((byte)code)))
                    {
                        throw new InvalidOperationException($"Code {code} of font {entry.Key.Value} has no glyph name or a width that is not finite.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Opens the input twice, from memory (the whole file in one view) and through a seekable stream (read in place, in windows that
    /// grow while an object does not end inside them), and requires the two to read the same: the same outcome when the file cannot
    /// be opened, else the same pages, the same object for every number below the trailer's Size (capped), and the same diagnostics.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.4: random access to indirect objects through the cross-reference table (issue #45).</remarks>
    private static void WindowedDocument(ReadOnlySpan<byte> data)
    {
        byte[] bytes = data.ToArray();
        string whole = ReadEverything(() => PdfDocument.Open(bytes));
        using var stream = new MemoryStream(bytes, writable: false);
        string windowed = ReadEverything(() => PdfDocument.Open(stream));
        if (whole != windowed)
        {
            throw new InvalidOperationException($"Reading in windows differs from reading the whole file:\n{whole}\n---\n{windowed}");
        }

        // Windows that start at 16 bytes and double: every object and section is read through the growth paths.
        using var tiny = new MemoryStream(bytes, writable: false);
        string grown = ReadEverything(() => PdfDocument.Read(new StreamSource(tiny, ownsStream: false, initialWindow: 16), EngineConfiguration.From(new PdfOptions())));
        if (whole != grown)
        {
            throw new InvalidOperationException($"Reading in growing windows differs from reading the whole file:\n{whole}\n---\n{grown}");
        }
    }

    private static string ReadEverything(Func<PdfDocument> open)
    {
        PdfDocument document;
        try
        {
            document = open();
        }
        catch (DiagnosticException exception)
        {
            return exception.Diagnostic.ToString();
        }
        catch (PdfPasswordException exception)
        {
            return exception.Failure.ToString();
        }
        catch (PdfEncryptionNotSupportedException exception)
        {
            return exception.Reason.ToString();
        }
        catch (PdfCertificateException exception)
        {
            return exception.Failure.ToString();
        }

        using (document)
        {
            var text = new System.Text.StringBuilder();
            foreach (PdfPage page in document.Pages)
            {
                text.Append(page.Reference).Append(' ').Append(page.MediaBox).Append('\n');
            }

            long size = document.Trailer.TryGetValue(new CosName("Size"), out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
            for (int number = 1; number < Math.Min(size, 2048); number++)
            {
                text.Append(document.Resolve(new CosReference(number, 0))).Append('\n');
            }

            foreach (Diagnostic diagnostic in document.Diagnostics)
            {
                text.Append(diagnostic).Append('\n');
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Opens the input as a whole file in lenient mode, saves it in the layout the input's length selects, and reopens the output.
    /// Whatever opened must save, and what was saved must open again with the same number of pages: a full save writes every object
    /// (repaired ones re-serialized), so nothing the first open could read may become unreadable. <see cref="NotSupportedException"/>
    /// is the documented outcome for an encrypted file or one with object numbers above the save limit.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5 (issue #44).</remarks>
    private static void Save(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = OpenOrNull(data);
        if (document is null)
        {
            return;
        }

        int pages = document.Pages.Count;
        var layout = (PdfCrossReferenceLayout)(data.Length % 3);
        using var output = new MemoryStream();
        try
        {
            document.Save(output, new PdfSaveOptions().WithCrossReferenceLayout(layout));
        }
        catch (NotSupportedException)
        {
            return;
        }

        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        if (saved.Pages.Count != pages)
        {
            throw new InvalidOperationException($"The saved file has {saved.Pages.Count} pages; the input had {pages}.");
        }
    }

    /// <summary>
    /// Decodes the input as a raw stream body with one filter in lenient mode. The output must stay within <paramref name="maxRatio"/>
    /// bytes per input byte (plus one group), the most any encoding of that filter can expand.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.2 to §7.4.5.</remarks>
    private static void Filter(IStreamFilter filter, ReadOnlySpan<byte> data, CosDictionary? parameters, long maxRatio)
    {
        var output = new ArrayBufferWriter<byte>();
        filter.Decode(data.ToArray(), output, new FilterContext { Parameters = parameters });
        if (output.WrittenCount > (data.Length * maxRatio) + 8)
        {
            throw new InvalidOperationException($"{filter.Name.Value} decoded {data.Length} bytes to {output.WrittenCount}, more than the encoding allows.");
        }
    }

    /// <summary>LZWDecode: the first byte's low bit selects EarlyChange (Table 8); the rest is the stream body.</summary>
    /// <remarks>ISO 32000-2 §7.4.4.2 and §7.4.4.3.</remarks>
    private static void Lzw(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        var parameters = new CosDictionary { [new CosName("EarlyChange")] = new CosInteger(data[0] & 1) };

        // One code (at least 9 bits) expands to at most 4096 bytes.
        Filter(new LzwDecodeFilter(), data[1..], parameters, maxRatio: 4096);
    }

    /// <summary>
    /// The LZW and Flate predictor functions over the input: the first four bytes select Predictor (1, 2, 10 to 15, or an invalid 3),
    /// Colors (1 to 4), BitsPerComponent (1, 2, 4, 8, 16) and Columns (1 to 64, or a power of two up to 2^30 when byte 3 is 192 or
    /// more: rows far longer than the data, issue #48); the rest is the filter's output to undo. The output is never more than twice
    /// the input, and when the data holds at least one row it is whole rows, no more than the input holds.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4.3 Table 8 and §7.4.4.4.</remarks>
    private static void PredictorTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        ReadOnlySpan<int> predictors = [1, 2, 10, 11, 12, 13, 14, 15, 3];
        ReadOnlySpan<int> depths = [1, 2, 4, 8, 16];
        int predictor = predictors[data[0] % predictors.Length];
        int colors = 1 + (data[1] % 4);
        int bitsPerComponent = depths[data[2] % depths.Length];
        long columns = data[3] >= 192 ? 1L << Math.Min(data[3] - 192, 30) : 1 + (data[3] % 64);
        var parameters = new CosDictionary
        {
            [new CosName("Predictor")] = new CosInteger(predictor),
            [new CosName("Colors")] = new CosInteger(colors),
            [new CosName("BitsPerComponent")] = new CosInteger(bitsPerComponent),
            [new CosName("Columns")] = new CosInteger(columns),
        };
        ReadOnlySpan<byte> body = data[4..];
        var output = new ArrayBufferWriter<byte>();
        Predictor.Decode(body, output, new FilterContext { Parameters = parameters });

        if (output.WrittenCount > (2L * body.Length) + 8)
        {
            throw new InvalidOperationException($"Predictor {predictor} with Columns {columns} turned {body.Length} bytes into {output.WrittenCount}.");
        }

        long row = (((long)colors * bitsPerComponent * columns) + 7) / 8;
        bool passedThrough = predictor is 1 or 3 || row > body.Length;
        long rowsIn = passedThrough ? 0 : predictor >= 10 ? (body.Length + row) / (row + 1) : (body.Length + row - 1) / row;
        if (!passedThrough && (output.WrittenCount % row != 0 || output.WrittenCount > rowsIn * row))
        {
            throw new InvalidOperationException($"Predictor {predictor} turned {body.Length} bytes into {output.WrittenCount}, not whole rows of {row}.");
        }
    }

    /// <summary>
    /// Decodes the input as linearization hint data: byte 0 gives the page count (1 to 16), bytes 1 and 2 the position of the shared
    /// object table, the rest is the hint stream. Decoding either fails with a reason or yields one entry per page, groups of at
    /// least one object, and never throws.
    /// </summary>
    /// <remarks>ISO 32000-2 F.4.1 to F.4.3.</remarks>
    private static void HintTables(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        int pages = (data[0] & 15) + 1;
        ReadOnlySpan<byte> hints = data[3..];
        int shared = ((data[1] << 8) | data[2]) % hints.Length;
        var layout = new HintTableLayout(HeaderOffset: 0, HintOffset: 100, HintLength: 50, FirstPageObjectNumber: 1, PageCount: pages);

        PdfLinearizationHints? decoded = HintTableReader.Parse(hints, shared, layout, out string? error);

        if ((decoded is null) == (error is null))
        {
            throw new InvalidOperationException("Hint decoding must yield either tables or a reason, not both or neither.");
        }

        if (decoded is not null
            && (decoded.Pages.Count != pages
                || decoded.SharedObjects.Any(group => group.ObjectCount < 1)
                || decoded.Pages.Any(page => page.ObjectCount < 0 || page.Length < 0)))
        {
            throw new InvalidOperationException("Decoded hint tables are inconsistent with the layout.");
        }
    }

    /// <summary>
    /// Scans the input as a damaged file and rebuilds its cross-reference information from the scan, as a lenient open does when the
    /// file's own cannot be read. Every position the scan reports must lie inside the input, in file order, and every rebuilt in-use
    /// entry must point at an object header the scan found.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.5 and §7.5.7 (repair itself is not specified; issue #41).</remarks>
    private static void Repair(ReadOnlySpan<byte> data)
    {
        var diagnostics = new DiagnosticSink(strict: false);
        var streams = new StreamDecoder(FilterRegistry.Create([]), PdfOptions.DefaultMaxDecodedStreamLength, diagnostics, static value => value ?? CosNull.Instance);
        using PdfSource source = PdfSource.FromMemory(data.ToArray());
        FileHeader header = FileHeader.Locate(source, diagnostics);

        FileScan scan = FileScan.Run(source);
        CrossReference? crossReference = CrossReferenceReconstructor.Reconstruct(source, header, scan, streams, diagnostics);

        IReadOnlyList<long>[] found =
        [
            scan.XrefKeywords, scan.TrailerKeywords, scan.XrefStreamNames, scan.ObjectStreamNames, scan.CatalogNames,
            [.. scan.Objects.Select(static item => item.Offset)],
        ];
        foreach (IReadOnlyList<long> positions in found)
        {
            for (int index = 0; index < positions.Count; index++)
            {
                if (positions[index] < 0 || positions[index] >= data.Length || (index > 0 && positions[index] <= positions[index - 1]))
                {
                    throw new InvalidOperationException($"Scan position {positions[index]} is outside the input or out of order.");
                }
            }
        }

        if (crossReference is null)
        {
            return;
        }

        HashSet<long> headers = [.. scan.Objects.Select(static item => item.Offset)];
        foreach (XrefEntry entry in crossReference.Sections.SelectMany(static section => section.Entries.Values))
        {
            if (entry.Kind == XrefEntryKind.InUse && !headers.Contains(header.Offset + entry.Offset))
            {
                throw new InvalidOperationException($"Rebuilt entry offset {entry.Offset} is not an object header the scan found.");
            }
        }

        if (!crossReference.Trailer.ContainsKey(KnownNames.Root))
        {
            throw new InvalidOperationException("A rebuilt cross-reference has a trailer without Root.");
        }
    }

    /// <summary>
    /// Reads the input as cross-reference stream data: bytes 0 to 2 are the field widths of <c>W</c> (0 to 9, so invalid widths are
    /// tried too), byte 3 selects whether <c>Index</c> is <c>[0 Size]</c> by default or two subsections, the rest is the data. The
    /// section must have at most one entry per whole row of data, never object number 0, and in-use offsets and compressed indexes
    /// that are not negative.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.8.2 Table 17 and §7.5.8.3 Table 18.</remarks>
    private static void XrefStream(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return;
        }

        int[] widths = [data[0] % 10, data[1] % 10, data[2] % 10];
        ReadOnlySpan<byte> rows = data[4..];
        string index = (data[3] & 1) == 0 ? string.Empty : "/Index [0 3 1000 1000000]";
        byte[] head = System.Text.Encoding.ASCII.GetBytes(
            $"7 0 obj\n<< /Type /XRef /Size 1000 {index} /W [{widths[0]} {widths[1]} {widths[2]}] /Length {rows.Length} >>\nstream\n");
        byte[] file = [.. head, .. rows, .. "\nendstream\nendobj\n"u8];
        var diagnostics = new DiagnosticSink(strict: false);
        var streams = new StreamDecoder(FilterRegistry.Create([]), PdfOptions.DefaultMaxDecodedStreamLength, diagnostics, static value => value ?? CosNull.Instance);
        using PdfSource source = PdfSource.FromMemory(file);

        XrefSection? section = XrefStreamReader.Read(source, 0, streams, diagnostics);

        if (section is null)
        {
            return;
        }

        int entryWidth = widths.Sum();
        if (section.Entries.Count > rows.Length / entryWidth
            || section.Entries.ContainsKey(0)
            || section.Entries.Values.Any(entry => entry.Offset < 0 || entry.Generation < 0)
            || section.Trailer.ContainsKey(new CosName("W")))
        {
            throw new InvalidOperationException("The cross-reference stream section is inconsistent with its data.");
        }
    }

    /// <summary>
    /// Reads the input as decoded object stream data: byte 0 is <c>N</c>, byte 1 is <c>First</c> (both modulo the data length plus
    /// one), the rest is the data. Every member the header names is then parsed, at its own index and at a wrong one; none may throw.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.7 Table 16.</remarks>
    private static void ObjectStreamTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        byte[] body = data[2..].ToArray();
        var diagnostics = new DiagnosticSink(strict: false);
        var reference = new CosReference(1, 0);
        ObjectStream contents = ObjectStream.Read(reference, body, new CosInteger(data[0]), new CosInteger(data[1] % (body.Length + 1)), diagnostics);
        for (int index = 0; index < contents.ObjectNumbers.Count; index++)
        {
            var member = new CosReference(contents.ObjectNumbers[index], 0);
            _ = contents.Parse(member, index, diagnostics);
            _ = contents.Parse(member, index + 1, diagnostics);
        }
    }

    internal static PdfDocument? OpenOrNull(ReadOnlySpan<byte> data)
    {
        try
        {
            return PdfDocument.Open(data.ToArray());
        }
        catch (DiagnosticException)
        {
            return null;
        }
        catch (PdfPasswordException)
        {
            return null;
        }
        catch (PdfEncryptionNotSupportedException)
        {
            return null;
        }
        catch (PdfCertificateException)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens the input as an encrypted file with the corpus owner password <c>owner</c> (so mutations of the encrypted corpus files
    /// get past authentication) and decodes every page's content streams and the Info dictionary's strings. A password or
    /// unsupported-encryption exception is a documented outcome; anything else is a finding. A decoded stream is never longer than
    /// the engine's limit.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6; ISO/TS 32003; ISO/TS 32004.</remarks>
    private static void EncryptedDocument(ReadOnlySpan<byte> data)
    {
        PdfDocument document;
        try
        {
            document = new PdfEngine().Open(data.ToArray(), new PdfPassword("owner"));
        }
        catch (DiagnosticException)
        {
            return;
        }
        catch (PdfPasswordException)
        {
            return;
        }
        catch (PdfEncryptionNotSupportedException)
        {
            return;
        }
        catch (PdfCertificateException)
        {
            return;
        }

        using (document)
        {
            _ = (document.Permissions, document.Security?.Integrity);
            foreach (PdfPage page in document.Pages)
            {
                CosObject contents = document.Resolve(page.Dictionary.TryGetValue(Names.Contents, out CosObject? value) ? value : null);
                IEnumerable<CosObject> streams = contents is CosArray array ? array : [contents];
                foreach (CosObject item in streams)
                {
                    if (document.Resolve(item) is CosStream stream && document.DecodeStream(stream).Length > PdfOptions.DefaultMaxDecodedStreamLength)
                    {
                        throw new InvalidOperationException("A decoded stream exceeds the decoded-length limit.");
                    }
                }
            }

            if (document.Resolve(document.Trailer.TryGetValue(Names.Info, out CosObject? info) ? info : null) is CosDictionary dictionary)
            {
                foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                {
                    _ = (document.Resolve(entry.Value) as CosString)?.DecodeText();
                }
            }
        }
    }

    /// <summary>
    /// Decrypts the input as a string of object 1 0 with the crypt filter method its first byte selects (RC4, AES-128-CBC with
    /// Algorithm 1's object key, AES-256-CBC, AES-256-GCM) and a fixed key, in lenient mode. The plaintext is never longer than the
    /// ciphertext.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.6.3 Algorithms 1 and 1.A; ISO/TS 32003 §5.2.</remarks>
    private static void Decrypt(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        (int version, string method) = (data[0] % 4) switch
        {
            0 => (4, "V2"),
            1 => (4, "AESV2"),
            2 => (5, "AESV3"),
            _ => (6, "AESV4"),
        };
        byte[] key = new byte[version == 4 ? 16 : 32];
        key.AsSpan().Fill(0x42);
        var encryption = (CosDictionary)CosObject.Parse(System.Text.Encoding.ASCII.GetBytes(
            $"<< /V {version} /CF << /StdCF << /CFM /{method} >> >> /StmF /StdCF /StrF /StdCF >>"));
        var diagnostics = new DiagnosticSink(strict: false);
        DocumentDecryptor decryptor = DocumentDecryptor.Create(
            encryption, version, new SecurityHandlerResult(key, PdfAccessLevel.User, PdfPermissions.All, -4), diagnostics, value => value ?? CosNull.Instance);
        ReadOnlyMemory<byte> plain = decryptor.Apply(decryptor.Strings, data[1..], new CosReference(1, 0));
        if (plain.Length > data.Length - 1)
        {
            throw new InvalidOperationException($"{plain.Length} bytes of plaintext from {data.Length - 1} bytes of ciphertext.");
        }
    }

    /// <summary>
    /// Compiles the input as a Type 4 program and evaluates it at fixed points. Byte 0 picks m (1 to 4 inputs, Domain [-1 1] each)
    /// and n (1 to 4 outputs, Range [-10 10] each); the rest is the program. Compiling never throws in lenient mode; every output
    /// must lie in the range, and once warm an evaluation must allocate nothing.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.10.5, Annex B.</remarks>
    private static void FunctionType4(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        int inputs = 1 + (data[0] % 4);
        int outputs = 1 + ((data[0] >> 2) % 4);
        var dictionary = new CosDictionary
        {
            [new CosName("FunctionType")] = new CosInteger(4),
            [new CosName("Domain")] = Pairs(inputs, -1, 1),
            [new CosName("Range")] = Pairs(outputs, -10, 10),
        };
        EvaluateFunction(new CosStream(dictionary, data[1..].ToArray()), inputs, outputs, -10, 10);
    }

    /// <summary>
    /// Builds a Type 0 function from the input and evaluates it at fixed points. Byte 0 picks m (1 to 3) and n (1 to 4), byte 1
    /// BitsPerSample (one of the eight, or 3 or 5), Order (1 or 3) and whether Encode reverses the first input, bytes 2 to 4 the Size
    /// of each input (1 to 8); the rest is the sample data, as short or long as it comes. Every output must lie in the Range [0 1].
    /// </summary>
    /// <remarks>ISO 32000-2 §7.10.2.</remarks>
    private static void FunctionSampled(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
        {
            return;
        }

        int[] widths = [1, 2, 4, 8, 12, 16, 24, 32, 3, 5];
        int inputs = 1 + (data[0] % 3);
        int outputs = 1 + ((data[0] >> 2) % 4);
        var size = new CosArray();
        for (int i = 0; i < inputs; i++)
        {
            size.Add(new CosInteger(1 + (data[2 + i] % 8)));
        }

        var dictionary = new CosDictionary
        {
            [new CosName("FunctionType")] = new CosInteger(0),
            [new CosName("Domain")] = Pairs(inputs, -1, 1),
            [new CosName("Range")] = Pairs(outputs, 0, 1),
            [new CosName("Size")] = size,
            [new CosName("BitsPerSample")] = new CosInteger(widths[data[1] % widths.Length]),
            [new CosName("Order")] = new CosInteger((data[1] & 0x10) != 0 ? 3 : 1),
        };
        if ((data[1] & 0x20) != 0)
        {
            var encode = Pairs(inputs, 0, 1);
            encode[0] = new CosInteger(1 + (data[2] % 8));
            encode[1] = new CosInteger(0);
            dictionary[new CosName("Encode")] = encode;
        }

        EvaluateFunction(new CosStream(dictionary, data[5..].ToArray()), inputs, outputs, 0, 1);
    }

    private static CosArray Pairs(int count, int low, int high)
    {
        var pairs = new CosArray();
        for (int i = 0; i < count; i++)
        {
            pairs.Add(new CosInteger(low));
            pairs.Add(new CosInteger(high));
        }

        return pairs;
    }

    /// <summary>Evaluates a function at inputs below, inside and above its domain; checks the outputs and that warm evaluations allocate nothing.</summary>
    private static void EvaluateFunction(CosStream stream, int inputs, int outputs, double low, double high)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.GetFunction(stream) ?? throw new InvalidOperationException("A function stream was not seen as a function.");
        if (function.IsValid && (function.InputCount != inputs || function.OutputCount != outputs))
        {
            throw new InvalidOperationException($"A valid function has {function.InputCount} inputs and {function.OutputCount} outputs; the dictionary says {inputs} and {outputs}.");
        }

        float[] points = [-2f, -1f, -0.5f, 0f, 0.3f, 1f, 2f, float.NaN];
        Span<float> input = stackalloc float[inputs];
        Span<float> output = stackalloc float[outputs];
        long allocated = long.MaxValue;
        for (int round = 0; round < 3 && allocated != 0; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (float point in points)
            {
                input.Fill(point);
                input[0] = -point;
                function.Evaluate(input, output);
                foreach (float value in output[..function.OutputCount])
                {
                    if (!(value >= low && value <= high))
                    {
                        throw new InvalidOperationException($"Output {value} is outside the range [{low} {high}].");
                    }
                }
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        if (allocated != 0)
        {
            throw new InvalidOperationException($"Evaluating allocated {allocated} bytes in every round.");
        }
    }

    /// <summary>Parses the input as a DER-encoded PDF MAC token (a CMS AuthenticatedData) and checks its structure.</summary>
    /// <remarks>ISO/TS 32004 §6.2-6.3; RFC 5652 §9.</remarks>
    private static void MacToken(ReadOnlySpan<byte> data) => _ = IntegrityVerifier.TryParseToken(data.ToArray(), out _);

    /// <summary>
    /// Tokenizes the whole input. Every token must lie inside the input and start at or after the previous one's end, and the lexer
    /// must reach the end of input.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.2.</remarks>
    private static void Lexer(ReadOnlySpan<byte> data)
    {
        var lexer = new CosLexer(data);
        int previousEnd = 0;
        while (true)
        {
            CosToken token = lexer.Next();
            if (token.Start < previousEnd || token.Length < 0 || token.End > data.Length)
            {
                throw new InvalidOperationException($"Token {token} lies outside the input of {data.Length} bytes or before the previous token's end {previousEnd}.");
            }

            if (token.Kind == CosTokenKind.EndOfInput)
            {
                return;
            }

            if (token.Length == 0)
            {
                throw new InvalidOperationException($"Token {token} is empty; the lexer would not make progress.");
            }

            previousEnd = token.End;
        }
    }

    /// <summary>
    /// Reads the input as decoded content: operator by operator, with operands. Every operator's ranges must lie inside the input,
    /// after the previous operator, with a keyword of one byte or more (so the reader always progresses), and every operand,
    /// nested ones included, must be readable.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.8.2 (issue #55).</remarks>
    private static void ContentLexer(ReadOnlySpan<byte> data)
    {
        var arena = new OperandArena();
        var reader = new ContentReader(data, arena);
        int previousEnd = 0;
        while (reader.Next(out ReadOperator op))
        {
            if (op.Start > op.KeywordStart || op.KeywordStart < previousEnd || op.KeywordLength < 1 || op.KeywordStart + op.KeywordLength > op.End
                || op.End > data.Length || op.DataStart < 0 || op.DataLength < 0 || op.DataStart + op.DataLength > data.Length)
            {
                throw new InvalidOperationException($"Operator {op} lies outside the input of {data.Length} bytes or before the previous end {previousEnd}.");
            }

            _ = Walk(arena.Operands);
            arena.Clear();
            previousEnd = op.KeywordStart + op.KeywordLength;
        }
    }

    private static double Walk(ContentOperands operands)
    {
        double sum = 0;
        foreach (ContentOperand operand in operands)
        {
            sum += operand.Number + operand.Bytes.Length + Walk(operand.Items);
        }

        return sum;
    }

    /// <summary>
    /// Runs the input as a page's decoded content through the interpreter with a processor that asks for every event and checks
    /// what it receives: path verbs and points agree, clip handles resolve and chain back to the initial clip, the state stack is
    /// balanced at the end of the run. Lenient mode: no exception may escape.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.8.2, §8.4, §8.5 (issue #55).</remarks>
    private static void ContentInterpreterTarget(ReadOnlySpan<byte> data) =>
        ContentInterpreter.RunBytes(data, EmptyDocument.Value, new CheckingProcessor(), ContentInterpreter.DefaultOptions);

    /// <summary>
    /// Parses the input as a sequence of objects in lenient mode, the way a reader scans a file body, then checks the round-trip
    /// property for each object: what <see cref="CosObject.WriteTo"/> writes parses back, with no repair, to an equal object. Also
    /// runs the strict public <see cref="CosObject.TryParse"/> over the whole input, which must not throw.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.3.</remarks>
    private static void ObjectParser(ReadOnlySpan<byte> data)
    {
        _ = CosObject.TryParse(data, out _);

        var parser = new CosParser(data, repairs: null);
        while (!parser.IsAtEnd())
        {
            int start = parser.Position;
            CosObject parsed = parser.ParseObject();
            if (parser.Position <= start)
            {
                throw new InvalidOperationException($"The parser did not advance past offset {start}.");
            }

            CheckRoundTrip(parsed);
        }
    }

    private static void CheckRoundTrip(CosObject parsed)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        parsed.WriteTo(buffer);

        var repairs = new CosRepairLog();
        var reparser = new CosParser(buffer.WrittenSpan, repairs);
        CosObject reparsed = reparser.ParseObject();
        reparser.ExpectEndOfInput();
        if (repairs.First is { } repair)
        {
            throw new InvalidOperationException($"Writing {parsed.GetType().Name} produced syntax that needs a repair: {repair}.");
        }

        if (!CosObject.DeepEquals(parsed, reparsed))
        {
            throw new InvalidOperationException($"Writing and reparsing a {parsed.GetType().Name} changed it: {parsed} became {reparsed}.");
        }
    }

    /// <summary>
    /// Reads every document-level entry of issue #69 (version, extensions, requirements, layout, mode, viewer preferences, language,
    /// page labels of every page, Info, XMP packet, resolved properties, file identifier): none may throw in lenient mode.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.2, §7.12, §12.2, §12.4.2, §12.11, §14.3, §14.4.</remarks>
    private static void ReadCatalogEssentials(PdfDocument document)
    {
        _ = (document.HeaderVersion, document.CatalogVersion, document.PageLayout, document.PageMode, document.Language);
        foreach (PdfDeveloperExtension extension in document.Extensions)
        {
            _ = (extension.BaseVersion, extension.ExtensionLevel, extension.Url, extension.ExtensionRevision);
        }

        foreach (PdfRequirement requirement in document.Requirements)
        {
            _ = (requirement.RequirementType, requirement.Version, requirement.Penalty);
            foreach (PdfRequirementHandler handler in requirement.Handlers)
            {
                _ = (handler.HandlerType, handler.Script);
            }
        }

        if (document.ViewerPreferences is { } preferences)
        {
            _ = (preferences.HideToolbar, preferences.HideMenubar, preferences.HideWindowUI, preferences.FitWindow, preferences.CenterWindow);
            _ = (preferences.DisplayDocTitle, preferences.NonFullScreenPageMode, preferences.Direction, preferences.ViewArea, preferences.ViewClip);
            _ = (preferences.PrintArea, preferences.PrintClip, preferences.PrintScaling, preferences.Duplex, preferences.PickTrayByPdfSize);
            _ = (preferences.PrintPageRange, preferences.NumCopies, preferences.Enforce);
        }

        if (document.PageLabels is { } labels)
        {
            IReadOnlyList<string> all = labels.GetLabels();
            if (all.Count != document.Pages.Count || all.Any(label => label.Length > PdfPageLabelRange.MaxNumeralLength + 4096))
            {
                throw new InvalidOperationException("Page labels must give one bounded label per page.");
            }

            foreach (PdfPageLabelRange range in labels.Ranges)
            {
                _ = (range.Style, range.Prefix, range.FirstNumber);
            }
        }

        if (document.Information is { } info)
        {
            _ = (info.Title, info.Author, info.Subject, info.Keywords, info.Creator, info.Producer, info.CreationDate, info.ModificationDate, info.Trapped);
        }

        if (document.Metadata?.Packet is { } packet)
        {
            WalkPacket(packet);
        }

        PdfDocumentProperties properties = document.Properties;
        _ = (properties.Title, properties.Author, properties.Subject, properties.Keywords, properties.Creator, properties.Producer);
        _ = (properties.CreationDate, properties.ModificationDate, document.FileIdentifier);
    }

    /// <summary>
    /// Reads the input as an XMP packet (ISO 16684-1 §7) through the reader the document uses. The packet is null exactly when an
    /// Error diagnostic says why; a readable packet's every property, item, field and qualifier, and every typed getter, must read.
    /// The XML reader must refuse document type declarations, so no input can expand entities or fetch anything.
    /// </summary>
    private static void Xmp(ReadOnlySpan<byte> data)
    {
        // Whole corpus files are the smoke seeds: start at a packet when the input holds one, so mutations reach the XML.
        int packet = data.IndexOf("<?xpacket"u8);
        XmpSlice(data);
        if (packet > 0)
        {
            XmpSlice(data[packet..]);
        }
    }

    private static void XmpSlice(ReadOnlySpan<byte> data)
    {
        var errors = new List<string>();
        XmpPacket? packet = XmpPacketReader.Read(data, (code, severity, _) =>
        {
            if (severity == DiagnosticSeverity.Error)
            {
                errors.Add(code);
            }
        });
        if ((packet is null) != (errors.Count > 0))
        {
            throw new InvalidOperationException($"An XMP packet must be null exactly when an error is reported; errors: {string.Join(", ", errors)}.");
        }

        if (XmpPacket.TryParse(data, out XmpPacket? again) != packet is not null || (again?.Properties.Count ?? 0) != (packet?.Properties.Count ?? 0))
        {
            throw new InvalidOperationException("TryParse must agree with the document's reader.");
        }

        if (packet is not null)
        {
            WalkPacket(packet);
        }
    }

    private static void WalkPacket(XmpPacket packet)
    {
        _ = (packet.About, packet.Title, packet.Description, packet.Creators, packet.Subjects, packet.Format, packet.CreateDate, packet.ModifyDate);
        _ = (packet.MetadataDate, packet.CreatorTool, packet.Producer, packet.Keywords, packet.PdfVersion, packet.Trapped, packet.PdfAPart);
        _ = (packet.PdfAConformance, packet.PdfUAPart, packet.DocumentId, packet.InstanceId);
        var pending = new Stack<XmpProperty>(packet.Properties);
        while (pending.TryPop(out XmpProperty? property))
        {
            if ((property.Kind == XmpPropertyKind.Simple) != (property.Value is not null))
            {
                throw new InvalidOperationException($"Only a simple XMP property has a value: {property.Name} is {property.Kind}.");
            }

            foreach (XmpProperty child in property.Items.Concat(property.Fields).Concat(property.Qualifiers))
            {
                pending.Push(child);
            }
        }
    }

    /// <summary>
    /// Reads the input as a date (ISO 32000-2 §7.9.4 and ISO 16684-1 §8.2.1.2), as Latin-1 text; neither parser may throw, a parsed
    /// date keeps the text it was read from, and an offset beyond what <see cref="DateTimeOffset"/> holds keeps the instant in UTC.
    /// </summary>
    private static void PdfDateTarget(ReadOnlySpan<byte> data)
    {
        string text = System.Text.Encoding.Latin1.GetString(data);
        DateParseOutcome outcome = PdfDate.Parse(text, out PdfDate date);
        if ((outcome != DateParseOutcome.Unreadable) != PdfDate.TryParse(text, out _))
        {
            throw new InvalidOperationException("PdfDate.TryParse must agree with the outcome of parsing.");
        }

        if (outcome != DateParseOutcome.Unreadable
            && (date.Text != text || Math.Abs(date.Value.Offset.TotalHours) > 14 || date.UtcOffsetMinutes is < -(23 * 60) - 59 or > (23 * 60) + 59))
        {
            throw new InvalidOperationException($"Date {text} parsed inconsistently: {date.Value:O}, offset {date.UtcOffsetMinutes}.");
        }

        if (XmpDate.TryParse(text, out XmpDate xmp) && xmp.Text != text)
        {
            throw new InvalidOperationException("An XMP date must keep its text.");
        }
    }
}

/// <summary>A processor for the content targets: asks for every event and throws when what it receives is inconsistent.</summary>
internal sealed class CheckingProcessor : ContentProcessor
{
    private int _saves;

    public override void BeginRun(ContentContext context) => _saves = 0;

    public override void SaveState(ContentContext context) => _saves++;

    public override void RestoreState(ContentContext context) => _saves--;

    public override void EndRun(ContentContext context)
    {
        if (_saves != 0 || context.StateDepth != 0)
        {
            throw new InvalidOperationException($"The state stack is unbalanced at the end of the run: {_saves} saves, depth {context.StateDepth}.");
        }
    }

    public override void PaintPath(in PathEvent path, ContentContext context) => Check(path.Path);

    public override void IntersectClip(in ClipEvent clip, ContentContext context)
    {
        Check(clip.Path);
        if (context.State.ClipHandle != clip.Handle || clip.ParentHandle >= clip.Handle)
        {
            throw new InvalidOperationException($"Clip {clip.Handle} (parent {clip.ParentHandle}) is not the state's clip {context.State.ClipHandle}.");
        }

        int steps = 0;
        for (int handle = clip.Handle; handle != 0; handle = context.GetClip(handle).ParentHandle)
        {
            if (++steps > clip.Handle)
            {
                throw new InvalidOperationException($"Clip {clip.Handle} does not chain back to the initial clip.");
            }
        }
    }

    private static void Check(PathView path)
    {
        int points = 0;
        foreach (PathVerb verb in path.Verbs)
        {
            points += verb switch
            {
                PathVerb.MoveTo or PathVerb.LineTo => 1,
                PathVerb.QuadTo => 2,
                PathVerb.CubicTo => 3,
                _ => 0,
            };
        }

        if (points != path.Points.Length || (path.Verbs.Length > 0 && path.Verbs[0] != PathVerb.MoveTo))
        {
            throw new InvalidOperationException($"A path with {path.Verbs.Length} verbs has {path.Points.Length} points, expected {points}, or does not start with a moveto.");
        }
    }
}
