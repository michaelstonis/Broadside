namespace Broadside;

/// <summary>
/// An e-invoice read back from a document by <see cref="PdfDocument.ExtractInvoice"/>: the embedded CII XML file and what the
/// document's XMP packet declares of it.
/// </summary>
/// <remarks>
/// Factur-X 1.0 and ZUGFeRD 2.x: the file is the associated file of ISO 32000-2 §14.13.3 the <c>fx:DocumentFileName</c> property
/// names; the profile and version are <c>fx:ConformanceLevel</c> and <c>fx:Version</c> (ISO 19005-3 §6.6 extension schema). A view:
/// <see cref="File"/> is live, the declared values a snapshot of the packet.
/// </remarks>
public sealed class PdfInvoiceAttachment
{
    internal PdfInvoiceAttachment(PdfEmbeddedFileEntry file, PdfInvoiceProfile? profile, string? version)
    {
        File = file;
        Profile = profile;
        Version = version;
    }

    /// <summary>Gets the embedded file's entry in the document's <c>EmbeddedFiles</c> name tree.</summary>
    public PdfEmbeddedFileEntry File { get; }

    /// <summary>Gets the profile <c>fx:ConformanceLevel</c> declares, or <see langword="null"/> when it is missing or not one Broadside knows.</summary>
    public PdfInvoiceProfile? Profile { get; }

    /// <summary>Gets <c>fx:Version</c>, or <see langword="null"/> when the packet has none.</summary>
    public string? Version { get; }

    /// <summary>Decodes the embedded XML.</summary>
    /// <returns>The file's bytes; empty when the specification embeds no stream.</returns>
    /// <remarks>ISO 32000-2 §7.11.4.</remarks>
    public ReadOnlyMemory<byte> GetXml() => File.File.EmbeddedFile?.Decode() ?? ReadOnlyMemory<byte>.Empty;
}
