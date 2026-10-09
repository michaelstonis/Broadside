using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>The Info dictionary, the document's XMP metadata and the file identifier. ISO 32000-2 §14.3, §14.4, §7.9.4.</summary>
public class DocumentInformationTests
{
    [Fact]
    public void Metadata_xmp_pdf_yields_its_title_from_the_info_dictionary_and_from_xmp()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("metadata-xmp.pdf"));

        Assert.Equal("Broadside", document.Information!.Title);
        Assert.Equal("Broadside", document.Metadata!.Packet!.Title);
        Assert.Equal("Broadside", document.Properties.Title);
        Assert.Equal(new CosName("Metadata"), document.Metadata.Stream.Dictionary[new CosName("Type")]);
        Assert.StartsWith("<?xpacket", Encoding.UTF8.GetString(document.Metadata.Data.Span), StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Hybrid_xref_pdf_reads_its_info_dictionary_from_an_object_stream()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("hybrid-xref.pdf"));

        Assert.Equal("hybrid", document.Information!.Title);
        Assert.Null(document.Metadata);
        Assert.Equal("hybrid", document.Properties.Title);
    }

    [Fact]
    public void Info_dictionary_pdf_types_every_table_349_entry()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("info-dictionary.pdf"));
        PdfDocumentInformation info = document.Information!;

        Assert.Equal("Broadside – Info", info.Title);
        Assert.Equal("Ada Lovelace", info.Author);
        Assert.Equal("Document information", info.Subject);
        Assert.Equal("info, metadata", info.Keywords);
        Assert.Equal("generate.py", info.Creator);
        Assert.Equal("Broadside corpus", info.Producer);
        Assert.Equal(new DateTimeOffset(2014, 3, 14, 12, 42, 11, TimeSpan.FromHours(1)), info.CreationDate!.Value.Value);
        Assert.Equal("D:20140314124211+01'00", info.CreationDate.Value.Text);
        Assert.Equal(new DateTimeOffset(2014, 9, 24, 21, 23, 3, TimeSpan.Zero), info.ModificationDate!.Value.Value);
        Assert.Equal(PdfTrapped.True, info.Trapped);
        Assert.Equal("Corpus", info.GetText("Department"));
        Assert.Equal(new DateTimeOffset(1998, 12, 23, 19, 52, 0, TimeSpan.FromHours(-8)), info.GetDate("Printed")!.Value.Value);
        Assert.Null(info.GetText("Missing"));
        Assert.Null(info.GetDate("Missing"));
        Assert.Same(document.Resolve(document.Trailer[new CosName("Info")]), info.Dictionary);

        Assert.Equal("Broadside – Info", document.Properties.Title);
        Assert.Equal("Ada Lovelace", document.Properties.Author);
        Assert.Equal("Document information", document.Properties.Subject);
        Assert.Equal("info, metadata", document.Properties.Keywords);
        Assert.Equal("generate.py", document.Properties.Creator);
        Assert.Equal("Broadside corpus", document.Properties.Producer);
        Assert.Equal(info.CreationDate.Value.Value, document.Properties.CreationDate);
        Assert.Equal(info.ModificationDate.Value.Value, document.Properties.ModificationDate);
        Assert.Empty(document.Diagnostics);
        Assert.False(info.Dictionary.IsDirty);
    }

    [Fact]
    public void Metadata_xmp_forms_pdf_types_the_common_properties_and_the_properties_prefer_xmp()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("metadata-xmp-forms.pdf"));
        XmpPacket packet = document.Metadata!.Packet!;

        Assert.Equal("XMP forms", packet.Title);
        Assert.Equal("XMP-Formen", packet.GetLanguageAlternative(XmpNamespaces.DublinCore, "title", "de"));
        Assert.Equal(["Ada Lovelace", "Grace Hopper"], packet.Creators);
        Assert.Equal(["pdf", "xmp"], packet.Subjects);
        Assert.Equal("Broadside corpus", packet.Producer);
        Assert.Equal("xmp, forms", packet.Keywords);
        Assert.Equal(2, packet.PdfAPart);
        Assert.Equal("B", packet.PdfAConformance);
        Assert.Equal(1, packet.PdfUAPart);
        Assert.Equal("uuid:6b1f3c2e-69a0-4c6b-9d1e-000000000069", packet.DocumentId);
        Assert.Equal("uuid:6b1f3c2e-69a0-4c6b-9d1e-000000000070", packet.InstanceId);
        Assert.Equal("parseType", packet.GetProperty("http://example.com/broadside/", "Resource")!.GetField("http://example.com/broadside/", "Name")!.Value);

        Assert.Null(document.Information);
        Assert.Equal("XMP forms", document.Properties.Title);
        Assert.Equal("Ada Lovelace; Grace Hopper", document.Properties.Author);
        Assert.Equal(new DateTimeOffset(2014, 9, 24, 21, 23, 3, TimeSpan.FromHours(2)), document.Properties.CreationDate);
        Assert.Null(document.Properties.ModificationDate);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_properties_take_each_field_from_xmp_when_the_packet_has_it_and_from_info_otherwise()
    {
        byte[] file = WithMetadata(Title("from xmp"), "/Title (from info) /Author (info author) /CreationDate (D:2001)");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("from xmp", document.Properties.Title);
        Assert.Equal("info author", document.Properties.Author);
        Assert.Equal(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero), document.Properties.CreationDate);
    }

    [Fact]
    public void A_malformed_packet_leaves_the_info_dictionary_readable_and_is_reported_once()
    {
        byte[] file = WithMetadata("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description>", "/Title (still here)");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Null(document.Metadata!.Packet);
        Assert.Null(document.Metadata.Packet);
        Assert.Equal("still here", document.Properties.Title);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("XmpMalformed", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(new CosReference(4, 0), diagnostic.ObjectReference);
    }

    [Fact]
    public void A_packet_with_a_document_type_declaration_is_refused()
    {
        byte[] file = WithMetadata("<!DOCTYPE x [<!ENTITY a \"aaaa\">]>" + Title("&a;"), info: null);
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Null(document.Metadata!.Packet);
        Assert.Equal("XmpDtdProhibited", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Junk_before_the_packet_and_a_duplicate_property_are_read_with_warnings()
    {
        string twice = "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">"
            + "<rdf:Description pdf:Producer=\"first\"/><rdf:Description pdf:Producer=\"second\"/></rdf:RDF>";
        using PdfDocument document = PdfDocument.Open(WithMetadata("junk" + twice, info: null));

        Assert.Equal("first", document.Metadata!.Packet!.Producer);
        Assert.Equal(["XmpLeadingJunk", "XmpPropertyDuplicate"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void A_metadata_stream_without_its_type_and_subtype_is_read_with_a_warning()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            Stream(string.Empty, Title("untyped")));
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal("untyped", document.Metadata!.Packet!.Title);
        Assert.Equal("MetadataStreamTypeInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_metadata_entry_that_is_not_a_stream_is_ignored_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage("/MediaBox [0 0 612 792]", "/Metadata << /Type /Metadata >>"));

        Assert.Null(document.Metadata);
        Assert.Equal("MetadataStreamInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void The_parsed_packet_is_a_snapshot_until_the_stream_changes()
    {
        using PdfDocument document = PdfDocument.Open(WithMetadata(Title("before"), info: null));
        XmpPacket first = document.Metadata!.Packet!;

        Assert.Same(first, document.Metadata.Packet);
        document.Metadata.Stream.EncodedData = Encoding.UTF8.GetBytes(Title("after"));

        Assert.Equal("after", document.Metadata.Packet!.Title);
        Assert.Equal("before", first.Title);
    }

    [Fact]
    public void Info_values_that_are_not_text_strings_are_read_with_a_warning()
    {
        using PdfDocument document = PdfDocument.Open(WithInfo("/Title /NameTitle /Author 42 /Subject [1] /Trapped true"));
        PdfDocumentInformation info = document.Information!;

        Assert.Equal("NameTitle", info.Title);
        Assert.Equal("42", info.Author);
        Assert.Null(info.Subject);
        Assert.Equal(PdfTrapped.True, info.Trapped);
        Assert.Equal(["InfoValueNotTextString", "InfoTrappedInvalid"], document.Diagnostics.Select(d => d.Code).Distinct());
        Assert.All(document.Diagnostics, d => Assert.Equal(new CosReference(4, 0), d.ObjectReference));
    }

    [Theory]
    [InlineData("/Trapped /False", PdfTrapped.False)]
    [InlineData("/Trapped /Unknown", PdfTrapped.Unknown)]
    [InlineData("", PdfTrapped.Unknown)]
    public void Trapped_is_a_name_that_defaults_to_unknown(string entry, PdfTrapped expected)
    {
        using PdfDocument document = PdfDocument.Open(WithInfo(entry));

        Assert.Equal(expected, document.Information!.Trapped);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("(20140924212303Z)", "DateInvalid", true)]
    [InlineData("(D:20140924212303+0100)", "DateInvalid", true)]
    [InlineData("(Monday, March 3, 1997)", "DateUnreadable", false)]
    [InlineData("(D:191001223195200-08'00')", "DateUnreadable", false)]
    public void A_date_that_deviates_is_read_with_a_warning_and_one_that_cannot_be_read_keeps_its_text(string value, string code, bool readable)
    {
        using PdfDocument document = PdfDocument.Open(WithInfo("/ModDate " + value));
        PdfDocumentInformation info = document.Information!;

        Assert.Equal(readable, info.ModificationDate.HasValue);
        Assert.Equal(value[1..^1], info.GetText("ModDate"));
        Assert.Equal(code, Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_utf_16_date_string_is_decoded_before_it_is_parsed()
    {
        string hex = Convert.ToHexString([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("D:2014")]);
        using PdfDocument document = PdfDocument.Open(WithInfo($"/CreationDate <{hex}>"));

        Assert.Equal(2014, document.Information!.CreationDate!.Value.Value.Year);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Strict_mode_throws_from_the_member_that_reads_a_deviating_value()
    {
        byte[] file = WithInfo("/Title /NameTitle");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());

        PdfDocumentInformation info = document.Information!;
        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => info.Title);
        Assert.Equal("InfoValueNotTextString", exception.Diagnostic.Code);
    }

    [Fact]
    public void An_info_entry_that_is_not_a_dictionary_is_ignored_with_a_warning()
    {
        byte[] file = new TestPdf { TrailerEntries = "/Info 4 0 R" }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            "(not a dictionary)");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Null(document.Information);
        Assert.Equal("InfoDictionaryInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Catalog_version_extensions_pdf_has_two_different_file_identifiers()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("catalog-version-extensions.pdf"));
        PdfFileIdentifier id = document.FileIdentifier!;

        Assert.Equal(16, id.Permanent.Length);
        Assert.Equal(16, id.Changing.Length);
        Assert.False(id.Permanent.Span.SequenceEqual(id.Changing.Span));
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("encrypted-rc4-40.pdf")]
    [InlineData("encrypted-aes-256.pdf")]
    public void An_encrypted_file_has_its_identifier_unencrypted(string fileName)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(fileName));
        PdfFileIdentifier id = document.FileIdentifier!;

        Assert.True(id.Permanent.Span.SequenceEqual(id.Changing.Span));
        Assert.Equal(16, id.Permanent.Length);
        CosString raw = Assert.IsType<CosString>(((CosArray)document.Trailer[new CosName("ID")])[0]);
        Assert.True(raw.Bytes.SequenceEqual(id.Permanent.Span));
    }

    [Fact]
    public void A_file_without_an_identifier_has_none()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"));

        Assert.Null(document.FileIdentifier);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("/ID [<0123> <0123>]", true)]
    [InlineData("/ID [<00112233445566778899AABBCCDDEEFF>]", false)]
    [InlineData("/ID (one)", false)]
    public void A_malformed_identifier_is_reported(string entry, bool exposed)
    {
        byte[] file = new TestPdf { TrailerEntries = entry }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal(exposed, document.FileIdentifier is not null);
        Assert.Equal("FileIdentifierInvalid", Assert.Single(document.Diagnostics).Code);
    }

    private static byte[] WithInfo(string entries) =>
        new TestPdf { TrailerEntries = "/Info 4 0 R" }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            $"<< {entries} >>");

    private static byte[] WithMetadata(string packet, string? info)
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            Stream("/Type /Metadata /Subtype /XML", packet),
            .. info is null ? Array.Empty<string>() : [$"<< {info} >>"],
        ];
        return new TestPdf { TrailerEntries = info is null ? string.Empty : "/Info 5 0 R" }.Build(objects);
    }

    private static string Stream(string entries, string data) =>
        $"<< {entries} /Length {Encoding.UTF8.GetByteCount(data)} >>\nstream\n{data}\nendstream";

    private static string Title(string title) =>
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
        + "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">"
        + title + "</rdf:li></rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta>";
}
