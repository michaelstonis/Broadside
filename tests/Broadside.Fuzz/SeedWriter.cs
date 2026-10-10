using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Fuzz;

/// <summary>
/// Writes the seed inputs of one target for libFuzzer or AFL (<c>--seeds</c>). Targets that read whole files get the PDF files as
/// they are; targets that read a stream body, decoded structure data or a token behind control bytes get those parts, cut out of
/// the files with the library itself and prefixed with the control bytes that select the file's own parameters, so the fuzzer starts
/// from inputs that reach deep into the code. The sources are <c>tests/Corpus/*.pdf</c> plus every PDF under the extra directories
/// (a fixed real-world subset in CI). Seeds are named by their SHA-256, so duplicates collapse, and none is longer than
/// <see cref="MaxSeedLength"/> (libFuzzer's <c>-max_len</c> in the scheduled workflow).
/// </summary>
internal static class SeedWriter
{
    /// <summary>The longest seed written, and the longest input the scheduled workflow lets libFuzzer generate.</summary>
    public const int MaxSeedLength = 256 * 1024;

    private const string OwnerPassword = "owner";
    private const int MaxObjectNumber = 100_000;

    private static readonly CosName Type = new("Type");
    private static readonly CosName Filter = new("Filter");
    private static readonly CosName DecodeParms = new("DecodeParms");
    private static readonly CosName XRef = new("XRef");
    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName W = new("W");
    private static readonly CosName N = new("N");
    private static readonly CosName First = new("First");
    private static readonly CosName S = new("S");
    private static readonly CosName Size = new("Size");
    private static readonly CosName Predictor = new("Predictor");
    private static readonly CosName Colors = new("Colors");
    private static readonly CosName BitsPerComponent = new("BitsPerComponent");
    private static readonly CosName Columns = new("Columns");
    private static readonly CosName EarlyChange = new("EarlyChange");
    private static readonly CosName AuthCode = new("AuthCode");
    private static readonly CosName Mac = new("MAC");
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName Contents = new("Contents");

    private static readonly Dictionary<string, string> FilterTargets = new(StringComparer.Ordinal)
    {
        ["filter-asciihex"] = "ASCIIHexDecode",
        ["filter-ascii85"] = "ASCII85Decode",
        ["filter-flate"] = "FlateDecode",
        ["filter-runlength"] = "RunLengthDecode",
        ["filter-dct"] = "DCTDecode",
    };

    /// <summary>Writes the seeds of <paramref name="target"/> into <paramref name="output"/>.</summary>
    /// <returns>The number of distinct seeds written.</returns>
    public static int Write(string target, string output, IEnumerable<string> extraDirectories)
    {
        Directory.CreateDirectory(output);
        List<string> sources = [.. CorpusLocator.CorpusFiles()];
        foreach (string directory in extraDirectories)
        {
            List<string> found = [.. Directory.EnumerateFiles(directory, "*.pdf", SearchOption.AllDirectories)];
            found.Sort(StringComparer.Ordinal);
            sources.AddRange(found);
        }

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (string source in sources)
        {
            if (new FileInfo(source).Length > MaxSeedLength)
            {
                continue;
            }

            byte[] file = File.ReadAllBytes(source);
            foreach (byte[] seed in SeedsOf(target, file))
            {
                if (seed.Length is > 0 and <= MaxSeedLength)
                {
                    string name = Convert.ToHexStringLower(SHA256.HashData(seed));
                    if (written.Add(name))
                    {
                        File.WriteAllBytes(Path.Combine(output, name), seed);
                    }
                }
            }
        }

        return written.Count;
    }

    private static IEnumerable<byte[]> SeedsOf(string target, byte[] file) => target switch
    {
        "filter-lzw" => Streams(file).Where(stream => FirstFilter(stream.Value) == "LZWDecode").Select(stream => Lzw(stream.Value)),
        "filter-predictor" => Streams(file).Select(stream => PredictorSeed(stream.Value)).OfType<byte[]>(),
        "xref-stream" => Streams(file).Select(stream => XrefStreamSeed(stream.Document, stream.Value)).OfType<byte[]>(),
        "object-stream" => Streams(file).Select(stream => ObjectStreamSeed(stream.Document, stream.Value)).OfType<byte[]>(),
        "hint-tables" => Streams(file).Select(stream => HintSeed(stream.Document, stream.Value)).OfType<byte[]>(),
        "mac-token" => MacTokens(file),
        "content-lexer" or "content-interpreter" or "content-objects" => ContentStreams(file),
        "xmp" => Streams(file).Where(stream => Metadata.Equals(stream.Value.Dictionary.TryGetValue(Type, out CosObject? type) ? type : null))
            .Select(stream => stream.Document.DecodeStream(stream.Value).ToArray()),
        "decrypt" => EncryptedBodies(file),
        "filter-jbig2" => Streams(file).Select(stream => Jbig2Seed(stream.Document, stream.Value)).OfType<byte[]>(),
        _ when FilterTargets.TryGetValue(target, out string? filter) =>
            Streams(file).Where(stream => FirstFilter(stream.Value) == filter).Select(stream => stream.Value.EncodedData.ToArray()),
        _ => [file],
    };

    /// <summary>
    /// A <c>filter-jbig2</c> input from a JBIG2Decode image: Width - 1 and Height - 1 (big-endian 16-bit, kept below 512), the
    /// globals length, the decoded JBIG2Globals stream, then the page stream as the filters before JBIG2Decode leave it.
    /// </summary>
    private static byte[]? Jbig2Seed(PdfDocument document, CosStream stream)
    {
        CosObject? filter = document.Resolve(stream.Dictionary.TryGetValue(Filter, out CosObject? value) ? value : null);
        bool last = filter switch
        {
            CosName name => name.Value == "JBIG2Decode",
            CosArray { Count: > 0 } array => document.Resolve(array[^1]) is CosName name && name.Value == "JBIG2Decode",
            _ => false,
        };
        if (!last || filter is CosArray { Count: > 1 })
        {
            return null;
        }

        int Dimension(string key) => document.Resolve(stream.Dictionary.TryGetValue(new CosName(key), out CosObject? v) ? v : null) is CosInteger { Value: > 0 } i
            ? (int)Math.Min(511, i.Value - 1)
            : 0;
        byte[] globals = [];
        CosObject? parameters = document.Resolve(stream.Dictionary.TryGetValue(DecodeParms, out CosObject? p) ? p : null);
        if (parameters is CosDictionary dictionary && document.Resolve(dictionary.TryGetValue(new CosName("JBIG2Globals"), out CosObject? g) ? g : null) is CosStream globalsStream)
        {
            globals = document.DecodeStream(globalsStream).ToArray();
        }

        byte[] header = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)Dimension("Width"));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)Dimension("Height"));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)globals.Length);
        return [.. header, .. globals, .. stream.EncodedData.Span];
    }

    /// <summary>Every stream object of the file, numbers 1 to <c>Size</c> - 1, generation 0; nothing when the file does not open.</summary>
    private static IEnumerable<(PdfDocument Document, CosStream Value)> Streams(byte[] file)
    {
        using PdfDocument? document = Open(file);
        if (document is null)
        {
            yield break;
        }

        long size = document.Trailer.TryGetValue(Size, out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
        for (int number = 1; number < Math.Min(size, MaxObjectNumber); number++)
        {
            if (document.Resolve(new CosReference(number, 0)) is CosStream stream)
            {
                yield return (document, stream);
            }
        }
    }

    private static PdfDocument? Open(byte[] file)
    {
        try
        {
            return new PdfEngine().Open(file, new PdfPassword(OwnerPassword));
        }
        catch (Exception exception) when (exception is DiagnosticException or PdfPasswordException or PdfEncryptionNotSupportedException or PdfCertificateException)
        {
            return null;
        }
    }

    private static string? FirstFilter(CosStream stream) => stream.Dictionary.TryGetValue(Filter, out CosObject? filter)
        ? filter switch
        {
            CosName name => name.Value,
            CosArray { Count: > 0 } array when array[0] is CosName name => name.Value,
            _ => null,
        }
        : null;

    private static CosDictionary? Parameters(CosStream stream) => stream.Dictionary.TryGetValue(DecodeParms, out CosObject? parameters)
        ? parameters switch
        {
            CosDictionary dictionary => dictionary,
            CosArray { Count: > 0 } array => array[0] as CosDictionary,
            _ => null,
        }
        : null;

    private static int Integer(CosDictionary? dictionary, CosName key, int fallback) =>
        dictionary is not null && dictionary.TryGetValue(key, out CosObject? value) && value is CosInteger integer
            ? (int)Math.Clamp(integer.Value, int.MinValue, int.MaxValue)
            : fallback;

    /// <summary><c>filter-lzw</c>: the EarlyChange byte, then the raw body.</summary>
    private static byte[] Lzw(CosStream stream) => [(byte)(Integer(Parameters(stream), EarlyChange, 1) & 1), .. stream.EncodedData.Span];

    /// <summary>
    /// <c>filter-predictor</c>: the four bytes that select the stream's own Predictor, Colors, BitsPerComponent and Columns, then
    /// the output of its Flate stage before the predictor is undone.
    /// </summary>
    private static byte[]? PredictorSeed(CosStream stream)
    {
        CosDictionary? parameters = Parameters(stream);
        int predictor = Integer(parameters, Predictor, 1);
        if (FirstFilter(stream) != "FlateDecode" || predictor <= 1)
        {
            return null;
        }

        int predictorIndex = Array.IndexOf([1, 2, 10, 11, 12, 13, 14, 15, 3], predictor);
        int depthIndex = Array.IndexOf([1, 2, 4, 8, 16], Integer(parameters, BitsPerComponent, 8));
        var body = new ArrayBufferWriter<byte>();
        new FlateDecodeFilter().Decode(stream.EncodedData, body, new FilterContext());

        return
        [
            (byte)Math.Max(predictorIndex, 0),
            (byte)((Integer(parameters, Colors, 1) - 1) & 3),
            (byte)(depthIndex < 0 ? 3 : depthIndex),
            (byte)((Integer(parameters, Columns, 1) - 1) & 63),
            .. body.WrittenSpan,
        ];
    }

    /// <summary><c>xref-stream</c>: the three widths of <c>W</c>, the default-Index selector, then the decoded data.</summary>
    private static byte[]? XrefStreamSeed(PdfDocument document, CosStream stream)
    {
        if (!XRef.Equals(stream.Dictionary.TryGetValue(Type, out CosObject? type) ? type : null)
            || !stream.Dictionary.TryGetValue(W, out CosObject? widths) || widths is not CosArray { Count: 3 } array
            || array.Any(width => width is not CosInteger { Value: >= 0 and <= 9 }))
        {
            return null;
        }

        return [.. array.Select(width => (byte)((CosInteger)width).Value), 0, .. document.DecodeStream(stream).Span];
    }

    /// <summary><c>object-stream</c>: <c>N</c> and <c>First</c> (when both fit a byte), then the decoded data.</summary>
    private static byte[]? ObjectStreamSeed(PdfDocument document, CosStream stream)
    {
        int count = Integer(stream.Dictionary, N, -1);
        int first = Integer(stream.Dictionary, First, -1);
        if (!ObjStm.Equals(stream.Dictionary.TryGetValue(Type, out CosObject? type) ? type : null) || count is < 0 or > 255 || first is < 0 or > 255)
        {
            return null;
        }

        return [(byte)count, (byte)first, .. document.DecodeStream(stream).Span];
    }

    /// <summary><c>hint-tables</c>: in a linearized file, the page count, the shared object table position, then the hint data.</summary>
    private static byte[]? HintSeed(PdfDocument document, CosStream stream)
    {
        int shared = Integer(stream.Dictionary, S, -1);
        if (document.Linearization is null || shared is < 0 or > ushort.MaxValue || stream.Dictionary.ContainsKey(Type))
        {
            return null;
        }

        byte[] seed = [(byte)((document.Pages.Count - 1) & 15), 0, 0, .. document.DecodeStream(stream).Span];
        BinaryPrimitives.WriteUInt16BigEndian(seed.AsSpan(1), (ushort)shared);
        return seed;
    }

    /// <summary><c>content-lexer</c> and <c>content-interpreter</c>: every page's content streams, decoded.</summary>
    private static IEnumerable<byte[]> ContentStreams(byte[] file)
    {
        using PdfDocument? document = Open(file);
        if (document is null)
        {
            yield break;
        }

        foreach (PdfPage page in document.Pages)
        {
            CosObject contents = document.Resolve(page.Dictionary.TryGetValue(Contents, out CosObject? value) ? value : null);
            IEnumerable<CosObject> items = contents is CosArray array ? array : [contents];
            foreach (CosObject item in items)
            {
                if (document.Resolve(item) is CosStream stream)
                {
                    yield return document.DecodeStream(stream).ToArray();
                }
            }
        }
    }

    /// <summary><c>mac-token</c>: the DER token of a standalone PDF MAC (ISO/TS 32004).</summary>
    private static IEnumerable<byte[]> MacTokens(byte[] file)
    {
        using PdfDocument? document = Open(file);
        if (document?.Trailer.TryGetValue(AuthCode, out CosObject? authCode) == true
            && document.Resolve(authCode) is CosDictionary dictionary
            && dictionary.TryGetValue(Mac, out CosObject? mac) && document.Resolve(mac) is CosString token)
        {
            yield return token.Bytes.ToArray();
        }
    }

    /// <summary>
    /// <c>decrypt</c>: the raw (still encrypted) stream bodies of an encrypted file, each behind every crypt filter selector byte,
    /// cut from the file bytes between <c>stream</c> and <c>endstream</c> because the document model only shows plaintext.
    /// </summary>
    private static IEnumerable<byte[]> EncryptedBodies(byte[] file)
    {
        if (file.AsSpan().IndexOf("/Encrypt"u8) < 0)
        {
            yield break;
        }

        int position = 0;
        while (true)
        {
            int keyword = file.AsSpan(position).IndexOf("stream"u8);
            if (keyword < 0)
            {
                yield break;
            }

            int start = position + keyword + "stream".Length;
            start += file.AsSpan(start).StartsWith("\r\n"u8) ? 2 : file.AsSpan(start).StartsWith("\n"u8) ? 1 : 0;
            int end = file.AsSpan(start).IndexOf("endstream"u8);
            if (end < 0)
            {
                yield break;
            }

            byte[] body = file.AsSpan(start, end).TrimEnd("\r\n"u8).ToArray();
            for (byte selector = 0; selector < 4 && body.Length > 0; selector++)
            {
                yield return [selector, .. body];
            }

            position = start + end + "endstream".Length;
        }
    }

    /// <summary>Formats a count for the console.</summary>
    public static string Describe(string target, int count, string output) =>
        string.Create(CultureInfo.InvariantCulture, $"seeds: target '{target}', {count} seeds written to {output}");
}
