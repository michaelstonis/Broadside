using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <content>
/// Factur-X and ZUGFeRD 2.x e-invoices (issue #630): a PDF/A-3 file (ISO 19005-3) with the UN/CEFACT Cross Industry Invoice XML
/// embedded as an associated file of the document (ISO 32000-2 §14.13.3) and the <c>fx</c> XMP properties described by a PDF/A
/// extension schema (ISO 19005-3 §6.6).
/// </content>
public sealed partial class PdfDocument
{
    /// <summary>The Factur-X XMP namespace, prefix <c>fx</c>, which ZUGFeRD 2.x and XRechnung hybrids share.</summary>
    private const string FacturXNamespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";

    /// <summary>The namespace of the CII D16B root element, <c>rsm:CrossIndustryInvoice</c>.</summary>
    private const string CrossIndustryInvoiceNamespace = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Each profile's <c>fx:ConformanceLevel</c>, default file name and default <c>AFRelationship</c>: Factur-X 1.0 and ZUGFeRD 2.x
    /// attach the XML of the profiles that are not invoices under EN 16931 (MINIMUM, BASIC WL) as <c>Data</c> and the others as
    /// <c>Alternative</c>, under <c>factur-x.xml</c>; ZUGFeRD's XRECHNUNG profile uses <c>xrechnung.xml</c>.
    /// </summary>
    private static readonly Dictionary<PdfInvoiceProfile, (string Level, string FileName, PdfFileRelationship Relationship)> InvoiceProfiles = new()
    {
        [PdfInvoiceProfile.Minimum] = ("MINIMUM", "factur-x.xml", PdfFileRelationship.Data),
        [PdfInvoiceProfile.BasicWl] = ("BASIC WL", "factur-x.xml", PdfFileRelationship.Data),
        [PdfInvoiceProfile.Basic] = ("BASIC", "factur-x.xml", PdfFileRelationship.Alternative),
        [PdfInvoiceProfile.EN16931] = ("EN 16931", "factur-x.xml", PdfFileRelationship.Alternative),
        [PdfInvoiceProfile.Extended] = ("EXTENDED", "factur-x.xml", PdfFileRelationship.Alternative),
        [PdfInvoiceProfile.XRechnung] = ("XRECHNUNG", "xrechnung.xml", PdfFileRelationship.Alternative),
    };

    /// <summary>The file names a hybrid invoice's XML is embedded under, for a file whose packet does not name it.</summary>
    private static readonly string[] InvoiceFileNames = ["factur-x.xml", "xrechnung.xml", "zugferd-invoice.xml"];

    /// <summary>Whether this session attached an invoice, which a save that is not PDF/A-3 reports (<see cref="SavingDiagnostics.InvoiceNotPdfA3"/>).</summary>
    private bool _invoiceAttached;

    /// <summary>
    /// Attaches a Factur-X or ZUGFeRD 2.x e-invoice: embeds <paramref name="invoiceXml"/>, lists it in the <c>EmbeddedFiles</c> name
    /// tree, associates it with the document and writes the <c>fx</c> XMP properties with the extension schema that describes them.
    /// Save with <see cref="PdfSaveOptions.WithPdfAConformance"/> <see cref="PdfAConformance.PdfA3b"/> or
    /// <see cref="PdfAConformance.PdfA3u"/> to write a valid hybrid invoice.
    /// </summary>
    /// <param name="invoiceXml">The CII XML, UTF-8 encoded; copied. Not validated against the CII schema.</param>
    /// <param name="options">The profile, file name, version, relationship and date; <see langword="null"/> for the defaults.</param>
    /// <returns>The new entry of the <c>EmbeddedFiles</c> tree.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="invoiceXml"/> is not well-formed UTF-8 XML, or its root element is not <c>rsm:CrossIndustryInvoice</c>
    /// (namespace <c>urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100</c>).
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.13.3 (associated files of the document), §7.11.4 (embedded file streams) and §14.3.2 (metadata streams);
    /// ISO 19005-3 §6.8 (embedded files: MIME type, <c>AFRelationship</c>, <c>ModDate</c>, listed in <c>AF</c>) and §6.6 (extension
    /// schemas). The file is embedded with media type <c>text/xml</c>, the relationship and modification date of
    /// <paramref name="options"/>, added to the tree with <see cref="AddEmbeddedFile(string, PdfFileSpecification)"/> and to the
    /// catalog's <c>AF</c> with <see cref="AddAssociatedFile"/>: one file specification for both.
    /// </para>
    /// <para>
    /// The packet gets <c>fx:DocumentType</c> <c>INVOICE</c>, <c>fx:DocumentFileName</c>, <c>fx:Version</c> and
    /// <c>fx:ConformanceLevel</c> in the namespace <c>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</c>, and a
    /// <c>pdfaExtension:schemas</c> entry describing them, merged with the schemas the packet already has (such as the
    /// <c>pdfuaid</c> one of a PDF/UA save), never replacing them. They are written to the packet now, as the document's other
    /// metadata edits are, so the next save carries them; a save that is not a conformant PDF/A-3 save writes them too and records
    /// <see cref="SavingDiagnostics.InvoiceNotPdfA3"/>.
    /// </para>
    /// <para>
    /// Attaching again replaces the invoice: the previous file leaves the tree and the catalog's <c>AF</c>, and the <c>fx</c>
    /// properties and schema are rewritten. The XML is checked to be well-formed with Broadside's own reader (no <c>System.Xml</c>);
    /// a document type declaration is refused. ZUGFeRD 1.0 and UBL invoices are not supported. A mutation: no read may run
    /// concurrently with it.
    /// </para>
    /// </remarks>
    public PdfEmbeddedFileEntry AttachInvoice(ReadOnlySpan<byte> invoiceXml, PdfInvoiceOptions? options = null)
    {
        options ??= new PdfInvoiceOptions();
        CheckInvoiceXml(invoiceXml);
        (string level, string defaultFileName, PdfFileRelationship defaultRelationship) = InvoiceProfiles[options.Profile];
        string fileName = options.FileName ?? defaultFileName;

        PdfEmbeddedFileOptions fileOptions = new PdfEmbeddedFileOptions()
            .WithMediaType("text/xml")
            .WithRelationship(options.Relationship ?? defaultRelationship)
            .WithModificationDate(options.ModificationDate ?? TimeProvider.GetLocalNow());
        PdfFileSpecification? file = null;
        if (FindInvoice(report: false) is { } previous)
        {
            _ = RemoveEmbeddedFile(previous.File.Name);
            if (ReusableInvoiceObjects(previous.File.File) is { } reused)
            {
                // The previous invoice's objects take the new one, keeping their numbers: an incremental update cannot drop them, and
                // an unreferenced embedded file left in the file breaks ISO 19005-3 §6.8 (veraPDF rule 6.8-4).
                FillEmbeddedFile(reused.Specification, reused.Stream, reused.StreamReference, fileName, invoiceXml, fileOptions);
                file = new PdfFileSpecification(this, reused.Specification, reused.Reference);
            }
            else
            {
                _ = RemoveAssociatedFile(Catalog, previous.File.File);
            }
        }

        file ??= CreateEmbeddedFile(fileName, invoiceXml, fileOptions);
        PdfEmbeddedFileEntry entry = AddEmbeddedFile(fileName, file);
        AddAssociatedFile(Catalog, file);

        var writer = new DocumentMetadataWriter(this)
            .SetProperty(FacturXNamespace, "DocumentType", "INVOICE")
            .SetProperty(FacturXNamespace, "DocumentFileName", fileName)
            .SetProperty(FacturXNamespace, "Version", options.Version)
            .SetProperty(FacturXNamespace, "ConformanceLevel", level)
            .SetProperty(FacturXExtensionSchemas());

        // A packet needs PDF 1.4; the associated file already makes the save raise an older document's version.
        writer.Commit(Now(), createPacket: true, layout: Version < new PdfVersion(1, 4) ? PdfA2Version : null);
        _invoiceAttached = true;
        return entry;
    }

    /// <summary>
    /// Reads the document's Factur-X or ZUGFeRD 2.x e-invoice back: the embedded file <c>fx:DocumentFileName</c> names, with the
    /// profile and version the XMP packet declares.
    /// </summary>
    /// <returns>The invoice, or <see langword="null"/> when the document embeds none.</returns>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.13.3 and §7.11.4; ISO 19005-3 §6.6. The file is the <c>EmbeddedFiles</c> entry whose key or file name is
    /// <c>fx:DocumentFileName</c>. Without that property (or when no entry has the name), an entry named <c>factur-x.xml</c>,
    /// <c>xrechnung.xml</c> or <c>zugferd-invoice.xml</c> is taken.
    /// </para>
    /// <para>
    /// Lenient: missing or incomplete <c>fx</c> properties, a conformance level that names no known profile, or <c>fx</c>
    /// properties that no <c>pdfaExtension:schemas</c> entry describes are recorded as one
    /// <see cref="DocumentDiagnostics.InvoiceMetadataInvalid"/> Information entry, and what they do not say is
    /// <see langword="null"/>. A read: safe to run concurrently with other reads.
    /// </para>
    /// </remarks>
    public PdfInvoiceAttachment? ExtractInvoice() => FindInvoice(report: true);

    /// <summary>Finds the invoice, as <see cref="ExtractInvoice"/>; <paramref name="report"/> says whether to record what is wrong with its metadata.</summary>
    private PdfInvoiceAttachment? FindInvoice(bool report)
    {
        XmpPacket? packet = Metadata?.Packet;
        string? declared = packet?.GetText(FacturXNamespace, "DocumentFileName");
        IReadOnlyList<PdfEmbeddedFileEntry> files = EmbeddedFiles;
        PdfEmbeddedFileEntry? entry = declared is null ? null : files.FirstOrDefault(file => file.Name == declared || file.File.FileName == declared);
        List<string> problems = [];
        if (entry is null)
        {
            entry = files.FirstOrDefault(file => InvoiceFileNames.Contains(file.Name, StringComparer.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            problems.Add(declared is null ? "no fx:DocumentFileName names the invoice file" : $"no embedded file is named '{declared}', as fx:DocumentFileName says");
        }

        string? level = packet?.GetText(FacturXNamespace, "ConformanceLevel");
        PdfInvoiceProfile? profile = ParseInvoiceProfile(level);
        if (profile is null)
        {
            problems.Add(level is null ? "no fx:ConformanceLevel declares the profile" : $"the fx:ConformanceLevel '{level}' names no known profile");
        }

        string? version = packet?.GetText(FacturXNamespace, "Version");
        if (version is null)
        {
            problems.Add("no fx:Version declares the version");
        }

        if (packet is not null && declared is not null && !DescribesFacturX(packet))
        {
            problems.Add("no pdfaExtension:schemas entry describes the fx namespace");
        }

        if (report && problems.Count > 0)
        {
            _diagnostics.Report(
                DocumentDiagnostics.InvoiceMetadataInvalid,
                DiagnosticSeverity.Information,
                $"The e-invoice '{entry.Name}' is read, but its XMP metadata is not complete: {string.Join("; ", problems)} (Factur-X 1.0 and ZUGFeRD 2.x, ISO 19005-3 clause 6.6).",
                objectReference: entry.File.Reference);
        }

        return new PdfInvoiceAttachment(entry, profile, version);
    }

    /// <summary>
    /// The previous invoice's file specification and embedded file stream, when both are indirect objects of this document and the
    /// specification is in the catalog's <c>AF</c> by reference, so <see cref="FillEmbeddedFile"/> can write the new invoice into them.
    /// </summary>
    private (CosDictionary Specification, CosReference Reference, CosStream Stream, CosReference StreamReference)? ReusableInvoiceObjects(PdfFileSpecification previous)
    {
        if (previous.Dictionary is not { } specification
            || Resolve(Catalog.GetValueOrDefault(FileAndLayerNames.AF)) is not CosArray associated
            || associated.OfType<CosReference>().FirstOrDefault(item => ReferenceEquals(Resolve(item), specification)) is not { } reference
            || Resolve(specification.GetValueOrDefault(FileAndLayerNames.EF)) is not CosDictionary embedded
            || (embedded.GetValueOrDefault(FileAndLayerNames.UF) ?? embedded.GetValueOrDefault(FileAndLayerNames.F)) is not CosReference streamReference
            || Resolve(streamReference) is not CosStream stream)
        {
            return null;
        }

        return (specification, reference, stream, streamReference);
    }

    /// <summary>Records, before a save changes anything, that an invoice attached in this session is saved without a conformant PDF/A-3 identification.</summary>
    /// <exception cref="DiagnosticException">In strict mode.</exception>
    private void ReportInvoiceSave(PdfSaveOptions options, bool archival)
    {
        if (!_invoiceAttached || (archival && options.PdfAConformance.Part() == 3))
        {
            return;
        }

        _diagnostics.Report(
            SavingDiagnostics.InvoiceNotPdfA3,
            DiagnosticSeverity.Warning,
            options.PdfAConformance.Part() == 3
                ? $"The attached e-invoice is saved without the {options.PdfAConformance.Describe()} identification, since the document cannot conform (see the PdfANonConformant diagnostics): the file is not a valid Factur-X or ZUGFeRD invoice."
                : $"The attached e-invoice is saved as {options.PdfAConformance.Describe()}: a Factur-X or ZUGFeRD invoice is a PDF/A-3 file, so save with PdfAConformance.PdfA3b or PdfA3u.");
    }

    private static PdfInvoiceProfile? ParseInvoiceProfile(string? level) =>
        level?.Trim().ToUpperInvariant() switch
        {
            "MINIMUM" => PdfInvoiceProfile.Minimum,
            "BASIC WL" => PdfInvoiceProfile.BasicWl,
            "BASIC" => PdfInvoiceProfile.Basic,
            "EN 16931" or "COMFORT" => PdfInvoiceProfile.EN16931,
            "EXTENDED" => PdfInvoiceProfile.Extended,
            "XRECHNUNG" => PdfInvoiceProfile.XRechnung,
            _ => null,
        };

    /// <summary>Checks that the XML is well-formed UTF-8 with a CII root element, without <c>System.Xml</c>.</summary>
    private static void CheckInvoiceXml(ReadOnlySpan<byte> invoiceXml)
    {
        ReadOnlySpan<byte> xml = invoiceXml.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? invoiceXml[3..] : invoiceXml;

        string text;
        try
        {
            text = StrictUtf8.GetString(xml);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException($"The invoice XML is not UTF-8 ({exception.Message}).", nameof(invoiceXml), exception);
        }

        if (XmpXmlReader.ReadRootName(text, out string? error) is not { } root)
        {
            throw new ArgumentException($"The invoice XML is not well-formed: {error}.", nameof(invoiceXml));
        }

        if (root.LocalName != "CrossIndustryInvoice" || root.NamespaceName != CrossIndustryInvoiceNamespace)
        {
            throw new ArgumentException(
                $"The invoice XML's root element is {{{root.NamespaceName}}}{root.LocalName}, not rsm:CrossIndustryInvoice in {CrossIndustryInvoiceNamespace}: Factur-X and ZUGFeRD 2.x embed a CII invoice (ZUGFeRD 1.0 and UBL are not supported).",
                nameof(invoiceXml));
        }
    }

    /// <summary>Whether the packet's extension schemas describe the <c>fx</c> namespace.</summary>
    private static bool DescribesFacturX(XmpPacket packet) =>
        packet.GetProperty(PdfAExtensionNamespace, "schemas") is { Kind: XmpPropertyKind.UnorderedArray } schemas
        && schemas.Items.Any(item => item.GetField(PdfASchemaNamespace, "namespaceURI")?.Value == FacturXNamespace);

    /// <summary>
    /// The packet's <c>pdfaExtension:schemas</c> with the Factur-X schema in place of any earlier description of its namespace,
    /// every other schema kept in order (ISO 19005-3 §6.6, as ISO 19005-2 §6.6.2.3.2: <c>pdfaSchema:schema</c>,
    /// <c>namespaceURI</c>, <c>prefix</c> and a <c>property</c> sequence of <c>name</c>, <c>valueType</c>, <c>category</c>,
    /// <c>description</c>). The descriptions are those of the Factur-X 1.0 specification's sample schema.
    /// </summary>
    private XmpProperty FacturXExtensionSchemas()
    {
        XmpProperty? existing = Metadata?.Packet?.GetProperty(PdfAExtensionNamespace, "schemas");
        List<XmpProperty> items = existing is { Kind: XmpPropertyKind.UnorderedArray }
            ? [.. existing.Items.Where(item => item.GetField(PdfASchemaNamespace, "namespaceURI")?.Value != FacturXNamespace)]
            : [];
        items.Add(Structure(
            XmpProperty.Simple(PdfASchemaNamespace, "schema", "Factur-X PDFA Extension Schema"),
            XmpProperty.Simple(PdfASchemaNamespace, "namespaceURI", FacturXNamespace),
            XmpProperty.Simple(PdfASchemaNamespace, "prefix", "fx"),
            XmpProperty.Array(PdfASchemaNamespace, "property", XmpPropertyKind.OrderedArray, [
                Property("DocumentFileName", "The name of the embedded XML document"),
                Property("DocumentType", "The type of the hybrid document in capital letters, e.g. INVOICE or ORDER"),
                Property("Version", "The actual version of the standard applying to the embedded XML document"),
                Property("ConformanceLevel", "The conformance level of the embedded XML document"),
            ])));
        return XmpProperty.Array(PdfAExtensionNamespace, "schemas", XmpPropertyKind.UnorderedArray, items);

        static XmpProperty Property(string name, string description) => Structure(
            XmpProperty.Simple(PdfAPropertyNamespace, "name", name),
            XmpProperty.Simple(PdfAPropertyNamespace, "valueType", "Text"),
            XmpProperty.Simple(PdfAPropertyNamespace, "category", "external"),
            XmpProperty.Simple(PdfAPropertyNamespace, "description", description));
    }
}
