namespace Broadside;

/// <summary>The common document properties, each taken from the XMP metadata when it has it and from the Info dictionary otherwise.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.3.1 ("The preferred method in PDF 2.0" is the metadata stream), §14.3.3 Table 349 (which XMP property each Info
/// entry corresponds to) and §14.3.4 (when the two disagree, which one a reader uses is its own decision). Each property comes from
/// the readable XMP packet of <see cref="PdfDocument.Metadata"/> when that packet has the property, else from
/// <see cref="PdfDocument.Information"/>. Disagreement is not a deviation and is not reported.
/// </para>
/// <para>Reads both sources on every call. Use <see cref="PdfDocument.Information"/> and <see cref="PdfMetadata.Packet"/> to see each one.</para>
/// </remarks>
public sealed class PdfDocumentProperties
{
    private readonly PdfDocument _document;

    internal PdfDocumentProperties(PdfDocument document) => _document = document;

    /// <summary>Gets the title: <c>dc:title</c> (default language), else <c>Title</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Title => Packet?.Title ?? _document.Information?.Title;

    /// <summary>Gets the author: the items of <c>dc:creator</c> joined by <c>"; "</c>, else <c>Author</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Author => Packet?.Creators is { Count: > 0 } creators ? string.Join("; ", creators) : _document.Information?.Author;

    /// <summary>Gets the subject: <c>dc:description</c> (default language), else <c>Subject</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Subject => Packet?.Description ?? _document.Information?.Subject;

    /// <summary>Gets the keywords: <c>pdf:Keywords</c>, else <c>Keywords</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Keywords => Packet?.Keywords ?? _document.Information?.Keywords;

    /// <summary>Gets the application that created the original document: <c>xmp:CreatorTool</c>, else <c>Creator</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Creator => Packet?.CreatorTool ?? _document.Information?.Creator;

    /// <summary>Gets the application that produced the PDF: <c>pdf:Producer</c>, else <c>Producer</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Producer => Packet?.Producer ?? _document.Information?.Producer;

    /// <summary>Gets the creation date: <c>xmp:CreateDate</c>, else <c>CreationDate</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349. An XMP date without a time zone is its local time with a zero offset (<see cref="XmpDate"/>).</remarks>
    public DateTimeOffset? CreationDate => Packet?.CreateDate?.Value ?? _document.Information?.CreationDate?.Value;

    /// <summary>Gets the modification date: <c>xmp:ModifyDate</c>, else <c>ModDate</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public DateTimeOffset? ModificationDate => Packet?.ModifyDate?.Value ?? _document.Information?.ModificationDate?.Value;

    private XmpPacket? Packet => _document.Metadata?.Packet;
}
