using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
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
    };

    private static readonly CosName ContentsKey = new("Contents");

    /// <summary>
    /// Opens the input as a whole file in lenient mode and reads everything the document model exposes: version, trailer, every
    /// page's boxes, rotation, user unit and resources, the revisions, and the linearization dictionary and hint tables. A <see cref="DiagnosticException"/> is the one documented outcome for a file
    /// whose cross-reference information cannot be read at all (until issue #41 reconstructs it); any other exception is a finding.
    /// Every box must be normalized and every rotation one of 0, 90, 180, 270; every revision must be a non-empty prefix of the input
    /// at its own index.
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

            _ = page.Resources;
            if (page.Dictionary.TryGetValue(ContentsKey, out CosObject? contents) && document.Resolve(contents) is CosStream stream)
            {
                _ = document.DecodeStream(stream);
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
    /// Colors (1 to 4), BitsPerComponent (1, 2, 4, 8, 16) and Columns (1 to 64); the rest is the filter's output to undo. The result
    /// must be whole rows, and no more rows than the input holds.
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
        int columns = 1 + (data[3] % 64);
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

        int row = ((colors * bitsPerComponent * columns) + 7) / 8;
        int rowsIn = predictor >= 10 ? (body.Length + row) / (row + 1) : (body.Length + row - 1) / row;
        bool passedThrough = predictor is 1 or 3;
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

    private static PdfDocument? OpenOrNull(ReadOnlySpan<byte> data)
    {
        try
        {
            return PdfDocument.Open(data.ToArray());
        }
        catch (DiagnosticException)
        {
            return null;
        }
    }

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
}
