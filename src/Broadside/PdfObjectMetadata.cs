using Broadside.Objects;

namespace Broadside;

/// <summary>The kind of object a metadata stream describes.</summary>
/// <remarks>ISO 32000-2 §14.3.2, Table 348, and PDF 2.0 Application Note 003 Tables 1-5 and "Other locations".</remarks>
public enum PdfMetadataLocation
{
    /// <summary>The whole document: the catalog's <c>Metadata</c> (Table 29).</summary>
    Document,

    /// <summary>A page (Table 31).</summary>
    Page,

    /// <summary>An ICC profile stream of an ICCBased colour space (Table 65).</summary>
    IccProfile,

    /// <summary>An image XObject (Table 87).</summary>
    ImageXObject,

    /// <summary>A form XObject (Table 93).</summary>
    FormXObject,

    /// <summary>An article thread (Table 162, PDF 2.0).</summary>
    Thread,

    /// <summary>A document part (Table 409, PDF 2.0).</summary>
    DocumentPart,

    /// <summary>An embedded font program stream (<c>FontFile</c>, <c>FontFile2</c>, <c>FontFile3</c>), not the font or its descriptor (Application Note 003 Table 2).</summary>
    FontProgram,

    /// <summary>A Type 3 font dictionary (Application Note 003, other locations).</summary>
    Type3Font,

    /// <summary>A tiling pattern stream (Application Note 003 Table 3).</summary>
    TilingPattern,

    /// <summary>A shading dictionary or stream, not a shading pattern (Application Note 003 Table 4).</summary>
    Shading,

    /// <summary>A marked-content property list in a <c>Properties</c> resource (§14.3.2).</summary>
    MarkedContent,

    /// <summary>A structure element (§14.7.2, Table 355; Application Note 003 Table 5).</summary>
    StructureElement,

    /// <summary>An embedded file stream (Table 44; Application Note 003, other locations).</summary>
    EmbeddedFile,

    /// <summary>An annotation (Application Note 003, other locations).</summary>
    Annotation,

    /// <summary>An optional content group (Application Note 003, other locations).</summary>
    OptionalContentGroup,

    /// <summary>A 3D artwork stream (Application Note 003, other locations).</summary>
    ThreeD,

    /// <summary>Any other object: found by a deep scan (§14.3.2 NOTE 2).</summary>
    Other,
}

/// <summary>A metadata stream attached to an object, together with that object.</summary>
/// <remarks>
/// ISO 32000-2 §14.3.2 (PDF 1.4), Tables 347 and 348: a stream with <c>Type</c> <c>Metadata</c> and <c>Subtype</c> <c>XML</c> holding
/// an XMP packet (ISO 16684-1), in the <c>Metadata</c> entry of the object it describes. PDF 2.0 Application Note 003.
/// </remarks>
public sealed class PdfObjectMetadata
{
    private readonly PdfDocument _document;

    internal PdfObjectMetadata(PdfDocument document, CosStream stream, CosReference? reference, PdfMetadataLocation location, CosObject owner, CosReference? ownerReference, int? pageIndex)
    {
        _document = document;
        Stream = stream;
        Reference = reference;
        Location = location;
        Owner = owner;
        OwnerReference = ownerReference;
        PageIndex = pageIndex;
    }

    /// <summary>Gets the metadata stream.</summary>
    /// <remarks>ISO 32000-2 §14.3.2, Table 347.</remarks>
    public CosStream Stream { get; }

    /// <summary>Gets the indirect reference to the metadata stream, when it has one.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the kind of object the metadata describes.</summary>
    public PdfMetadataLocation Location { get; }

    /// <summary>Gets the object whose <c>Metadata</c> entry holds the stream: a dictionary or a stream.</summary>
    public CosObject Owner { get; }

    /// <summary>Gets the indirect reference to <see cref="Owner"/>, when it has one.</summary>
    public CosReference? OwnerReference { get; }

    /// <summary>Gets the index of the page the owner was found under, or <see langword="null"/> for document-level objects.</summary>
    public int? PageIndex { get; }

    /// <summary>Returns the XMP packet: the stream data decoded through its filters.</summary>
    /// <returns>The decoded bytes.</returns>
    /// <remarks>ISO 32000-2 §14.3.2. Nothing is cached.</remarks>
    public ReadOnlyMemory<byte> Decode() => _document.DecodeStream(Stream);

    /// <summary>Gets the parsed XMP packet, or <see langword="null"/> when the stream does not hold well-formed XMP (with a diagnostic).</summary>
    /// <remarks>ISO 32000-2 §14.3.2 and ISO 16684-1. Parsed once per stream and document; a changed stream is parsed again.</remarks>
    public XmpPacket? Packet => _document.ReadXmpPacket(Stream, Reference ?? OwnerReference);

    /// <summary>Gets the PDF Declarations the packet makes about the owner object (object-level scope).</summary>
    /// <remarks>PDF Declarations §7: claims at the object level are limited to the data of that object.</remarks>
    public IReadOnlyList<PdfDeclaration> Declarations => PdfDeclaration.Read(Packet);

    /// <inheritdoc/>
    public override string ToString() => Location.ToString();
}
