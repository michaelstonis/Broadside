using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>Helpers shared by the filter tests: one-stream files and direct filter calls. Encoders are in <see cref="FilterEncoders"/>.</summary>
internal static class FilterTesting
{
    /// <summary>The stream object number in files built by <see cref="FileWithStream"/>.</summary>
    public static readonly CosReference StreamReference = new(4, 0);

    /// <summary>A one-page file whose object 4 is a stream with the given dictionary entries (besides Length) and data.</summary>
    public static byte[] FileWithStream(string dictionaryEntries, ReadOnlySpan<byte> data, params string[] moreObjects)
    {
        string stream = $"<< /Length {data.Length} {dictionaryEntries} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream";
        return new TestPdf().Build(
            ["<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            stream,
            .. moreObjects]);
    }

    /// <summary>Opens a file built by <see cref="FileWithStream"/> and decodes its stream.</summary>
    public static (byte[] Decoded, PdfDocument Document) Decode(byte[] file, PdfOptions? options = null)
    {
        PdfDocument document = PdfDocument.Open(file, options ?? new PdfOptions());
        var stream = (CosStream)document.Resolve(StreamReference);
        return (document.DecodeStream(stream).ToArray(), document);
    }

    /// <summary>Opens a file built by <see cref="FileWithStream"/>, decodes its stream, and returns the bytes and diagnostic codes.</summary>
    public static (byte[] Decoded, string[] Codes) DecodeWithCodes(byte[] file, PdfOptions? options = null)
    {
        (byte[] decoded, PdfDocument document) = Decode(file, options);
        using (document)
        {
            return (decoded, [.. document.Diagnostics.Select(diagnostic => diagnostic.Code)]);
        }
    }

    /// <summary>Calls a filter directly through its contract with a stand-alone context.</summary>
    public static (byte[] Decoded, string[] Codes) Run(IStreamFilter filter, ReadOnlySpan<byte> encoded, string? parameters = null, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        var context = new FilterContext
        {
            Parameters = parameters is null ? null : (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes(parameters)),
            ReadingMode = mode,
        };
        var output = new ArrayBufferWriter<byte>();
        filter.Decode(encoded.ToArray(), output, context);
        return (output.WrittenSpan.ToArray(), [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)]);
    }
}
