namespace Broadside;

/// <summary>
/// The common document properties, each read from the XMP metadata when it has it and from the Info dictionary otherwise, and written
/// to both in sync.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.3.1 ("The preferred method in PDF 2.0" is the metadata stream), §14.3.3 Table 349 (which XMP property each Info
/// entry corresponds to) and §14.3.4 (when the two disagree, which one a reader uses is its own decision). Each property comes from
/// the readable XMP packet of <see cref="PdfDocument.Metadata"/> when that packet has the property, else from
/// <see cref="PdfDocument.Information"/>. Disagreement is not a deviation and is not reported.
/// </para>
/// <para>Reads both sources on every call. Use <see cref="PdfDocument.Information"/> and <see cref="PdfMetadata.Packet"/> to see each one.</para>
/// <para>
/// Setting a property writes it at once to both sources (§14.3.4: the two "are fully equivalent"); <see langword="null"/> removes it
/// from both. The XMP packet is written when the document has a metadata stream, is PDF 2.0, or is PDF 1.4 or later (a new stream is
/// added to the catalog); every other property of an existing packet is kept, and <c>xmp:MetadataDate</c> becomes the engine's
/// clock (<see cref="PdfOptions.UseTimeProvider"/>). The Info dictionary is written before PDF 2.0, added to the trailer when absent;
/// in a PDF 2.0 document it keeps only <c>CreationDate</c>, <c>ModDate</c> and keys Table 349 does not define, and the deprecated
/// entries it held move to the packet (§14.3.3). Entries only one source has are copied to the other; entries the two disagree on
/// and that are not being set are left as they are. Dates are written to the second in the offset they carry. Saving a changed
/// document then dates the modification (<see cref="PdfSaveOptions.UpdateModificationDate"/>).
/// </para>
/// <para>
/// Setting is a mutation: no read may run concurrently with it. In strict mode, a deviation found while reading the current values
/// throws <see cref="Diagnostics.DiagnosticException"/>. Characters XML cannot carry (most C0 controls) are left out of the packet.
/// </para>
/// </remarks>
public sealed class PdfDocumentProperties
{
    private readonly PdfDocument _document;

    internal PdfDocumentProperties(PdfDocument document) => _document = document;

    /// <summary>Gets or sets the title: <c>dc:title</c> (default language), else <c>Title</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349. Setting replaces the <c>x-default</c> item of <c>dc:title</c> and keeps the other languages.</remarks>
    public string? Title
    {
        get => Packet?.Title ?? _document.Information?.Title;
        set => _document.WriteMetadata(MetadataEntry.Title, value);
    }

    /// <summary>Gets or sets the author: the items of <c>dc:creator</c> joined by <c>"; "</c>, else <c>Author</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349. Setting writes <c>dc:creator</c> as an ordered array of one item, the text as given.</remarks>
    public string? Author
    {
        get => Packet?.Creators is { Count: > 0 } creators ? string.Join("; ", creators) : _document.Information?.Author;
        set => _document.WriteMetadata(MetadataEntry.Author, value);
    }

    /// <summary>Gets or sets the subject: <c>dc:description</c> (default language), else <c>Subject</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349. Setting replaces the <c>x-default</c> item and keeps the other languages.</remarks>
    public string? Subject
    {
        get => Packet?.Description ?? _document.Information?.Subject;
        set => _document.WriteMetadata(MetadataEntry.Subject, value);
    }

    /// <summary>Gets or sets the keywords: <c>pdf:Keywords</c>, else <c>Keywords</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Keywords
    {
        get => Packet?.Keywords ?? _document.Information?.Keywords;
        set => _document.WriteMetadata(MetadataEntry.Keywords, value);
    }

    /// <summary>Gets or sets the application that created the original document: <c>xmp:CreatorTool</c>, else <c>Creator</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Creator
    {
        get => Packet?.CreatorTool ?? _document.Information?.Creator;
        set => _document.WriteMetadata(MetadataEntry.Creator, value);
    }

    /// <summary>Gets or sets the application that produced the PDF: <c>pdf:Producer</c>, else <c>Producer</c>.</summary>
    /// <remarks>ISO 32000-2 §14.3.3, Table 349.</remarks>
    public string? Producer
    {
        get => Packet?.Producer ?? _document.Information?.Producer;
        set => _document.WriteMetadata(MetadataEntry.Producer, value);
    }

    /// <summary>Gets or sets the creation date: <c>xmp:CreateDate</c>, else <c>CreationDate</c>.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.3.3, Table 349, §7.9.4, §14.3.4. An XMP date without a time zone is its local time with a zero offset
    /// (<see cref="XmpDate"/>). A set date is written to the second, in its offset.
    /// </remarks>
    public DateTimeOffset? CreationDate
    {
        get => Packet?.CreateDate?.Value ?? _document.Information?.CreationDate?.Value;
        set => _document.WriteMetadata(MetadataEntry.CreationDate, value is { } date ? MetadataDate.FromInstant(date) : null);
    }

    /// <summary>Gets or sets the modification date: <c>xmp:ModifyDate</c>, else <c>ModDate</c>.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.3.3, Table 349, §7.9.4, §14.3.4. A set date is written to the second, in its offset, and the next save keeps
    /// it instead of dating the modification itself (<see cref="PdfSaveOptions.UpdateModificationDate"/>).
    /// </remarks>
    public DateTimeOffset? ModificationDate
    {
        get => Packet?.ModifyDate?.Value ?? _document.Information?.ModificationDate?.Value;
        set => _document.WriteMetadata(MetadataEntry.ModificationDate, value is { } date ? MetadataDate.FromInstant(date) : null);
    }

    private XmpPacket? Packet => _document.Metadata?.Packet;
}
