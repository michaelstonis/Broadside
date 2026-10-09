using Broadside.Objects;

namespace Broadside;

/// <summary>The document's metadata stream: a live view over the catalog's <c>Metadata</c> entry and the XMP packet it holds.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.3.2, Tables 347 and 348: a stream with <c>Type</c> <c>Metadata</c> and <c>Subtype</c> <c>XML</c> holding XMP
/// (ISO 16684-1); in PDF 2.0 the preferred place for document metadata. A stream without the right <c>Type</c> or <c>Subtype</c> is
/// still read, with a <c>MetadataStreamTypeInvalid</c> diagnostic.
/// </para>
/// <para>
/// <see cref="Packet"/> is parsed on first use and kept: an immutable snapshot of the stream. When the stream is changed (its data
/// replaced or its dictionary edited), the next read parses it again. A packet that cannot be read is <see langword="null"/> with an
/// <c>XmpMalformed</c> or <c>XmpDtdProhibited</c> diagnostic; the Info dictionary stays readable.
/// </para>
/// </remarks>
public sealed class PdfMetadata
{
    private readonly PdfDocument _document;
    private readonly CosReference? _reference;

    internal PdfMetadata(PdfDocument document, CosStream stream, CosReference? reference)
    {
        _document = document;
        _reference = reference;
        Stream = stream;
    }

    /// <summary>Gets the metadata stream.</summary>
    /// <remarks>ISO 32000-2 §14.3.2, Table 347.</remarks>
    public CosStream Stream { get; }

    /// <summary>Gets the stream's data, decoded through its filters: the XMP packet's bytes. Decoded on every call.</summary>
    /// <remarks>ISO 32000-2 §14.3.2 and §7.4.</remarks>
    public ReadOnlyMemory<byte> Data => _document.DecodeStream(Stream);

    /// <summary>Gets the XMP packet the stream holds, or <see langword="null"/> when it cannot be read.</summary>
    /// <remarks>ISO 32000-2 §14.3.2; ISO 16684-1 §7. See <see cref="XmpPacket"/> for what is accepted.</remarks>
    public XmpPacket? Packet => _document.ReadXmpPacket(Stream, _reference);
}
