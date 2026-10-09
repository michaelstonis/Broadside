using Broadside.Diagnostics;
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
        ["hint-tables"] = HintTables,
    };

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
