using System.Buffers;
using Broadside.Objects;
using Broadside.Security.Cryptography;

namespace Broadside;

/// <summary>An embedded file stream: the contents of a file stored inside the document. A live view over the stream.</summary>
/// <remarks>
/// ISO 32000-2 §7.11.4 (PDF 1.3), Table 44. In a file encrypted with an <c>EFF</c> crypt filter the stream is decrypted with that
/// filter (§7.6.2, Table 20). <c>Subtype</c> and <c>Params</c> are required when the file is an associated file (§14.13.2).
/// </remarks>
public sealed class PdfEmbeddedFile
{
    private readonly PdfDocument _document;

    internal PdfEmbeddedFile(PdfDocument document, CosStream stream, CosReference? reference)
    {
        _document = document;
        Stream = stream;
        Reference = reference;
    }

    /// <summary>Gets the embedded file stream.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1.</remarks>
    public CosStream Stream { get; }

    /// <summary>Gets the indirect reference to the stream, when it was reached through one.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the media type (<c>Subtype</c>, an RFC 2046 MIME type such as <c>text/plain</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 44. The name's <c>#2F</c> escapes are already decoded.</remarks>
    public string? Subtype => ViewReading.Name(_document, Stream.Dictionary, FileAndLayerNames.Subtype)?.Value;

    /// <summary>Gets <see cref="Subtype"/>, or <c>application/octet-stream</c> when the file has none.</summary>
    /// <remarks>ISO 32000-2 §14.13.2: an unknown type is treated as <c>application/octet-stream</c>.</remarks>
    public string MediaType => Subtype ?? "application/octet-stream";

    /// <summary>Gets the embedded file parameters (<c>Params</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.4.1, Tables 44 and 45.</remarks>
    public PdfEmbeddedFileParameters? Parameters =>
        ViewReading.Get(_document, Stream.Dictionary, FileAndLayerNames.Params) is CosDictionary parameters ? new PdfEmbeddedFileParameters(_document, parameters, Reference) : null;

    /// <summary>Returns the file's contents: the stream data decoded through its filters.</summary>
    /// <returns>The decoded bytes.</returns>
    /// <remarks>ISO 32000-2 §7.11.4.1 and §7.4. As <see cref="PdfDocument.DecodeStream(CosStream)"/>; nothing is cached.</remarks>
    public ReadOnlyMemory<byte> Decode() => _document.DecodeStream(Stream);

    /// <summary>Writes the file's contents, decoded through its filters, into <paramref name="output"/>.</summary>
    /// <param name="output">Where to write.</param>
    /// <remarks>ISO 32000-2 §7.11.4.1 and §7.4.</remarks>
    public void Decode(IBufferWriter<byte> output) => _document.DecodeStream(Stream, output);

    /// <summary>Compares the MD5 digest of the decoded contents with <see cref="PdfEmbeddedFileParameters.CheckSum"/>.</summary>
    /// <returns>Whether the checksum is absent, matches or differs. A difference is a result, not a diagnostic.</returns>
    /// <remarks>ISO 32000-2 §7.11.4.1, Table 45. Uses a managed MD5 where the platform has none.</remarks>
    public PdfCheckSumStatus VerifyCheckSum()
    {
        if (Parameters?.CheckSum is not { Length: Md5.HashSize } expected)
        {
            return PdfCheckSumStatus.Absent;
        }

        Span<byte> actual = stackalloc byte[Md5.HashSize];
        Md5.HashData(Decode().Span, actual);
        return actual.SequenceEqual(expected.Span) ? PdfCheckSumStatus.Matches : PdfCheckSumStatus.DoesNotMatch;
    }
}
