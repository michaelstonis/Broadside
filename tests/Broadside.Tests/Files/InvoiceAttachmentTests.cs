using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Files;

/// <summary>
/// Factur-X and ZUGFeRD 2.x e-invoices (issue #630): the CII XML embedded as the document's associated file (ISO 32000-2 §14.13.3,
/// §7.11.4) with the <c>fx</c> XMP properties and the PDF/A extension schema describing them (ISO 19005-3 §6.6, §6.8).
/// </summary>
public sealed class InvoiceAttachmentTests
{
    private const string FacturX = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";
    private const string Extension = "http://www.aiim.org/pdfa/ns/extension/";
    private const string Schema = "http://www.aiim.org/pdfa/ns/schema#";

    private static readonly DateTimeOffset Modified = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    internal static byte[] Invoice(string id = "INV-42") => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\""
        + " xmlns:ram=\"urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100\">\n"
        + "  <rsm:ExchangedDocument><ram:ID>" + id + "</ram:ID><ram:TypeCode>380</ram:TypeCode></rsm:ExchangedDocument>\n"
        + "</rsm:CrossIndustryInvoice>\n");

    [Fact]
    public void An_attached_invoice_saved_as_pdf_a_3b_has_the_fx_properties_and_their_extension_schema()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        document.AttachInvoice(Invoice(), new PdfInvoiceOptions().WithModificationDate(Modified));

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3b);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("3", packet.GetText(XmpNamespaces.PdfAIdentification, "part"));
        Assert.Equal("INVOICE", packet.GetText(FacturX, "DocumentType"));
        Assert.Equal("factur-x.xml", packet.GetText(FacturX, "DocumentFileName"));
        Assert.Equal("1.0", packet.GetText(FacturX, "Version"));
        Assert.Equal("EN 16931", packet.GetText(FacturX, "ConformanceLevel"));
        XmpProperty schema = Assert.Single(SchemasOf(packet), item => item.GetField(Schema, "namespaceURI")?.Value == FacturX);
        Assert.Equal("fx", schema.GetField(Schema, "prefix")?.Value);
        XmpProperty properties = schema.GetField(Schema, "property")!;
        Assert.Equal(XmpPropertyKind.OrderedArray, properties.Kind);
        Assert.Equal(
            ["DocumentFileName", "DocumentType", "Version", "ConformanceLevel"],
            properties.Items.Select(item => item.GetField("http://www.aiim.org/pdfa/ns/property#", "name")!.Value));
        Assert.Contains("xmlns:fx=\"urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#\"", Encoding.UTF8.GetString(reopened.Metadata!.Data.Span), StringComparison.Ordinal);
    }

    [Fact]
    public void The_embedded_files_tree_and_the_catalog_af_point_at_one_file_specification()
    {
        using PdfDocument document = PdfDocument.Create();
        document.AttachInvoice(Invoice(), new PdfInvoiceOptions().WithModificationDate(Modified));

        using PdfDocument reopened = PdfDocument.Open(PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3b));

        PdfEmbeddedFileEntry entry = Assert.Single(reopened.EmbeddedFiles);
        PdfFileSpecification associated = Assert.Single(reopened.AssociatedFiles);
        Assert.Equal("factur-x.xml", entry.Name);
        Assert.NotNull(associated.Reference);
        Assert.Same(entry.File.Dictionary, associated.Dictionary);
        Assert.Equal(PdfFileRelationship.Alternative, associated.Relationship);
        PdfEmbeddedFile embedded = associated.EmbeddedFile!;
        Assert.Equal("text/xml", embedded.MediaType);
        Assert.Equal(Modified, embedded.Parameters!.ModificationDate!.Value.Value);
        Assert.Equal(Invoice(), embedded.Decode().ToArray());
    }

    [Theory]
    [InlineData(PdfInvoiceProfile.Minimum, "MINIMUM", "factur-x.xml", PdfFileRelationship.Data)]
    [InlineData(PdfInvoiceProfile.BasicWl, "BASIC WL", "factur-x.xml", PdfFileRelationship.Data)]
    [InlineData(PdfInvoiceProfile.Basic, "BASIC", "factur-x.xml", PdfFileRelationship.Alternative)]
    [InlineData(PdfInvoiceProfile.EN16931, "EN 16931", "factur-x.xml", PdfFileRelationship.Alternative)]
    [InlineData(PdfInvoiceProfile.Extended, "EXTENDED", "factur-x.xml", PdfFileRelationship.Alternative)]
    [InlineData(PdfInvoiceProfile.XRechnung, "XRECHNUNG", "xrechnung.xml", PdfFileRelationship.Alternative)]
    public void Each_profile_has_its_conformance_level_file_name_and_relationship(PdfInvoiceProfile profile, string level, string fileName, PdfFileRelationship relationship)
    {
        using PdfDocument document = PdfDocument.Create();

        PdfEmbeddedFileEntry entry = document.AttachInvoice(Invoice(), new PdfInvoiceOptions().WithProfile(profile));

        Assert.Equal(fileName, entry.Name);
        Assert.Equal(relationship, entry.File.Relationship);
        Assert.Equal(level, document.Metadata!.Packet!.GetText(FacturX, "ConformanceLevel"));
        Assert.Equal(fileName, document.Metadata!.Packet!.GetText(FacturX, "DocumentFileName"));
        PdfInvoiceAttachment invoice = document.ExtractInvoice()!;
        Assert.Equal(profile, invoice.Profile);
    }

    [Fact]
    public void Options_override_the_profile_file_name_relationship_and_version()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfEmbeddedFileEntry entry = document.AttachInvoice(
            Invoice(),
            new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.Minimum).WithFileName("zugferd-invoice.xml").WithRelationship(PdfFileRelationship.Source).WithVersion("2p0"));

        Assert.Equal("zugferd-invoice.xml", entry.Name);
        Assert.Equal(PdfFileRelationship.Source, entry.File.Relationship);
        PdfInvoiceAttachment invoice = document.ExtractInvoice()!;
        Assert.Equal("2p0", invoice.Version);
        Assert.Equal("zugferd-invoice.xml", invoice.File.Name);
    }

    [Fact]
    public void Attaching_twice_replaces_the_first_invoice_its_af_entry_and_its_metadata()
    {
        using PdfDocument document = PdfDocument.Create();
        document.AttachInvoice(Invoice("first"), new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.XRechnung));
        document.AttachInvoice(Invoice("second"), new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.Basic));

        using PdfDocument reopened = PdfDocument.Open(PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3b));

        PdfEmbeddedFileEntry entry = Assert.Single(reopened.EmbeddedFiles);
        PdfFileSpecification associated = Assert.Single(reopened.AssociatedFiles);
        Assert.Same(entry.File.Dictionary, associated.Dictionary);
        Assert.Equal("factur-x.xml", entry.Name);
        Assert.Equal(Invoice("second"), entry.File.EmbeddedFile!.Decode().ToArray());
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("BASIC", packet.GetText(FacturX, "ConformanceLevel"));
        Assert.Equal("factur-x.xml", packet.GetText(FacturX, "DocumentFileName"));
        Assert.Single(SchemasOf(packet), item => item.GetField(Schema, "namespaceURI")?.Value == FacturX);
    }

    [Fact]
    public void Extract_invoice_round_trips_the_file_profile_and_version()
    {
        using PdfDocument document = PdfDocument.Create();
        document.AttachInvoice(Invoice(), new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.Extended));

        using PdfDocument reopened = PdfDocument.Open(PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3u));
        PdfInvoiceAttachment? invoice = reopened.ExtractInvoice();

        Assert.NotNull(invoice);
        Assert.Equal(PdfInvoiceProfile.Extended, invoice.Profile);
        Assert.Equal("1.0", invoice.Version);
        Assert.Equal("factur-x.xml", invoice.File.Name);
        Assert.Equal(Invoice(), invoice.GetXml().ToArray());
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Kind == DocumentDiagnostics.InvoiceMetadataInvalid);
    }

    [Fact]
    public void The_pdfuaid_and_fx_extension_schemas_coexist_in_either_order()
    {
        // The invoice first, then a PDF/UA-1 save that adds the pdfuaid schema to the packet.
        using PdfDocument first = AccessibleDocument();
        first.AttachInvoice(Invoice());
        using PdfDocument invoiceFirst = PdfDocument.Open(SaveArchivalAndAccessible(first));

        // A PDF/UA-1 and PDF/A-3b file first, then the invoice attached to it and saved again.
        using PdfDocument second = AccessibleDocument();
        using PdfDocument accessible = PdfDocument.Open(SaveArchivalAndAccessible(second));
        accessible.AttachInvoice(Invoice());
        using PdfDocument accessibleFirst = PdfDocument.Open(SaveArchivalAndAccessible(accessible));

        foreach (PdfDocument document in new[] { invoiceFirst, accessibleFirst })
        {
            string[] namespaces = [.. SchemasOf(document.Metadata!.Packet!).Select(item => item.GetField(Schema, "namespaceURI")!.Value!)];
            Assert.Equal(2, namespaces.Length);
            Assert.Contains(FacturX, namespaces);
            Assert.Contains(XmpNamespaces.PdfUAIdentification, namespaces);
            Assert.Equal("1", document.Metadata!.Packet!.GetText(XmpNamespaces.PdfUAIdentification, "part"));
            Assert.NotNull(document.ExtractInvoice()?.Profile);
        }
    }

    [Fact]
    public void A_save_that_is_not_pdf_a_3_writes_the_invoice_and_warns()
    {
        using PdfDocument document = PdfDocument.Create();
        document.AttachInvoice(Invoice());

        using var stream = new MemoryStream();
        document.Save(stream);

        Diagnostic warning = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Kind == SavingDiagnostics.InvoiceNotPdfA3);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        using PdfDocument reopened = PdfDocument.Open(stream.ToArray());
        Assert.Equal(PdfInvoiceProfile.EN16931, reopened.ExtractInvoice()?.Profile);
    }

    [Fact]
    public void A_pdf_a_2_save_warns_and_strict_mode_refuses_it_before_anything_changes()
    {
        using PdfDocument lenient = PdfDocument.Create();
        lenient.AttachInvoice(Invoice());
        PdfA2u3bConformanceTests.Save(lenient, PdfAConformance.PdfA2b);
        Assert.Contains(lenient.Diagnostics, diagnostic => diagnostic.Kind == SavingDiagnostics.InvoiceNotPdfA3 && diagnostic.Message.Contains("PDF/A-2b", StringComparison.Ordinal));

        using PdfDocument strict = PdfDocument.Create(new PdfOptions().UseStrict());
        PdfA2u3bConformanceTests.DrawRgbSquare(strict);
        strict.AttachInvoice(Invoice());
        string before = CosSnapshot.Of(strict);
        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => strict.Save(new MemoryStream()));
        Assert.Equal(SavingDiagnostics.InvoiceNotPdfA3, refused.Diagnostic.Kind);
        Assert.Equal(before, CosSnapshot.Of(strict));
    }

    [Fact]
    public void A_pdf_a_3_save_records_no_invoice_warning()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        document.AttachInvoice(Invoice());

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3b);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind == SavingDiagnostics.InvoiceNotPdfA3);
    }

    [Theory]
    [InlineData("<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\">", "not well-formed")]
    [InlineData("<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"/>", "root element")]
    [InlineData("<rsm:CrossIndustryDocument xmlns:rsm=\"urn:ferd:CrossIndustryDocument:invoice:1p0\"/>", "root element")]
    [InlineData("<CrossIndustryInvoice/>", "root element")]
    [InlineData("<!DOCTYPE x><rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\"/>", "not well-formed")]
    [InlineData("<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\"/><second/>", "not well-formed")]
    [InlineData("<rsm:CrossIndustryInvoice xmlns:rsm=\"urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100\"/>trailing", "not well-formed")]
    [InlineData("", "not well-formed")]
    public void Xml_that_is_not_a_well_formed_cii_invoice_is_refused_and_nothing_is_attached(string xml, string reason)
    {
        using PdfDocument document = PdfDocument.Create();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => document.AttachInvoice(Encoding.UTF8.GetBytes(xml)));

        Assert.Contains(reason, refused.Message, StringComparison.Ordinal);
        Assert.Equal("invoiceXml", refused.ParamName);
        Assert.Empty(document.EmbeddedFiles);
        Assert.Null(document.ExtractInvoice());
    }

    [Fact]
    public void A_byte_order_mark_comments_and_processing_instructions_around_the_root_are_accepted()
    {
        byte[] xml = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n<!-- generated -->\n"), .. Invoice().AsSpan(Invoice().AsSpan().IndexOf((byte)'\n') + 1), .. "<?done?>\n"u8];
        using PdfDocument document = PdfDocument.Create();

        PdfEmbeddedFileEntry entry = document.AttachInvoice(xml);

        Assert.Equal(xml, entry.File.EmbeddedFile!.Decode().ToArray());
    }

    [Fact]
    public void Invalid_utf_8_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => document.AttachInvoice([.. "<a>"u8, 0xC3, 0x28, .. "</a>"u8]));

        Assert.Contains("UTF-8", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_corpus_invoice_reads_back_with_its_profile_and_no_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("factur-x-minimal.pdf"));

        PdfInvoiceAttachment? invoice = document.ExtractInvoice();

        Assert.NotNull(invoice);
        Assert.Equal(PdfInvoiceProfile.Minimum, invoice.Profile);
        Assert.Equal("1.0", invoice.Version);
        Assert.Equal(PdfFileRelationship.Data, invoice.File.File.Relationship);
        Assert.StartsWith("<?xml", Encoding.UTF8.GetString(invoice.GetXml().Span), StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_malformed_fx_schema_returns_the_file_without_a_profile_and_records_information()
    {
        byte[] file = Corpus.Bytes("factur-x-minimal.pdf");
        string text = Encoding.Latin1.GetString(file).Replace(">MINIMUM<", ">MINIMAL<", StringComparison.Ordinal);
        using PdfDocument document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        PdfInvoiceAttachment? invoice = document.ExtractInvoice();

        Assert.NotNull(invoice);
        Assert.Null(invoice.Profile);
        Assert.Equal("factur-x.xml", invoice.File.Name);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Kind == DocumentDiagnostics.InvoiceMetadataInvalid);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains("MINIMAL", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_named_like_an_invoice_without_fx_metadata_is_returned_without_a_profile()
    {
        using PdfDocument document = PdfDocument.Create();
        document.AddEmbeddedFile("factur-x.xml", Invoice());

        PdfInvoiceAttachment? invoice = document.ExtractInvoice();

        Assert.NotNull(invoice);
        Assert.Null(invoice.Profile);
        Assert.Null(invoice.Version);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Kind == DocumentDiagnostics.InvoiceMetadataInvalid);
    }

    [Fact]
    public void A_document_without_an_invoice_extracts_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("embedded-files.pdf"));

        Assert.Null(document.ExtractInvoice());
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind == DocumentDiagnostics.InvoiceMetadataInvalid);
    }

    [Fact]
    public void Options_validate_their_values_and_chain()
    {
        var options = new PdfInvoiceOptions();

        Assert.Same(options, options.WithProfile(PdfInvoiceProfile.Basic).WithFileName(null).WithVersion("1.0").WithRelationship(null).WithModificationDate(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Profile = (PdfInvoiceProfile)42);
        Assert.Throws<ArgumentException>(() => options.FileName = string.Empty);
        Assert.Throws<ArgumentException>(() => options.Version = string.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Relationship = PdfFileRelationship.Other);
    }

    private static IReadOnlyList<XmpProperty> SchemasOf(XmpPacket packet) => packet.GetProperty(Extension, "schemas")?.Items ?? [];

    private static PdfDocument AccessibleDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Invoice 42";
        document.Catalog[new CosName("Lang")] = new CosString("en-US"u8.ToArray());
        var root = new CosDictionary { [new CosName("Type")] = new CosName("StructTreeRoot") };
        CosReference rootReference = document.AddObject(root);
        root[new CosName("K")] = document.AddObject(new CosDictionary
        {
            [new CosName("Type")] = new CosName("StructElem"),
            [new CosName("S")] = new CosName("Document"),
            [new CosName("P")] = rootReference,
        });
        document.Catalog[new CosName("StructTreeRoot")] = rootReference;
        return document;
    }

    private static byte[] SaveArchivalAndAccessible(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA3b).WithPdfUAConformance(PdfUAConformance.PdfUA1));
        return stream.ToArray();
    }
}
