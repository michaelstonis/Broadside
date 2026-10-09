using System.Text;

namespace Broadside.Tests.Document;

/// <summary>The XMP packet reader (ISO 16684-1 §7) over bytes, without a PDF: the seam #76 and the fuzz target use.</summary>
public class XmpPacketTests
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

    private static readonly string Forms =
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"Broadside\">\n"
        + " <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n"
        + "  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"\n"
        + "    pdf:Producer=\"Broadside generate.py\" xmp:CreateDate=\"2014-09-24T21:23:03+02:00\">\n"
        + "   <pdf:Keywords>one, two</pdf:Keywords>\n"
        + "   <xmp:CreatorTool> spaced </xmp:CreatorTool>\n"
        + "  </rdf:Description>\n"
        + "  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:ex=\"http://example.com/ns/\">\n"
        + "   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Forms</rdf:li><rdf:li xml:lang=\"de\">Formen</rdf:li></rdf:Alt></dc:title>\n"
        + "   <dc:creator><rdf:Seq><rdf:li>Ada</rdf:li><rdf:li>Grace</rdf:li></rdf:Seq></dc:creator>\n"
        + "   <dc:subject><rdf:Bag><rdf:li>pdf</rdf:li><rdf:li>xmp</rdf:li></rdf:Bag></dc:subject>\n"
        + "   <ex:Resource rdf:parseType=\"Resource\"><ex:Name>Resource form</ex:Name><ex:Count>2</ex:Count></ex:Resource>\n"
        + "   <ex:Nested><rdf:Description ex:Name=\"Attribute field\"><ex:Count>3</ex:Count></rdf:Description></ex:Nested>\n"
        + "   <ex:Short ex:Name=\"Shorthand\"/>\n"
        + "   <ex:Link rdf:resource=\"http://example.com/\"/>\n"
        + "   <ex:WithQualifier><rdf:Description><rdf:value>42</rdf:value><ex:unit>mm</ex:unit></rdf:Description></ex:WithQualifier>\n"
        + "  </rdf:Description>\n"
        + "  <rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\"\n"
        + "    xmlns:xmpMM=\"http://ns.adobe.com/xap/1.0/mm/\" pdfaid:part=\"2\" pdfaid:conformance=\"B\" pdfuaid:part=\"1\">\n"
        + "   <xmpMM:DocumentID>uuid:0b2c7c5e-0000-4000-8000-000000000001</xmpMM:DocumentID>\n"
        + "   <xmpMM:InstanceID>uuid:0b2c7c5e-0000-4000-8000-000000000002</xmpMM:InstanceID>\n"
        + "  </rdf:Description>\n"
        + " </rdf:RDF>\n"
        + "</x:xmpmeta>\n"
        + new string(' ', 2048)
        + "<?xpacket end=\"w\"?>";

    [Fact]
    public void A_packet_reads_every_property_form_into_one_model()
    {
        Assert.True(XmpPacket.TryParse(Encoding.UTF8.GetBytes(Forms), out XmpPacket? packet));

        Assert.Equal(string.Empty, packet.About);
        XmpProperty producer = packet.GetProperty(XmpNamespaces.Pdf, "Producer")!;
        Assert.Equal(XmpPropertyKind.Simple, producer.Kind);
        Assert.Equal("Broadside generate.py", producer.Value);
        Assert.Equal(XmpNamespaces.Pdf, producer.NamespaceName);
        Assert.Equal("Producer", producer.Name);
        Assert.Equal(" spaced ", packet.GetProperty(XmpNamespaces.Xmp, "CreatorTool")!.Value);

        XmpProperty title = packet.GetProperty(XmpNamespaces.DublinCore, "title")!;
        Assert.Equal(XmpPropertyKind.Alternative, title.Kind);
        Assert.Equal(["x-default", "de"], title.Items.Select(item => item.Language));
        Assert.Equal(["Forms", "Formen"], title.Items.Select(item => item.Value));
        Assert.Equal(XmpPropertyKind.OrderedArray, packet.GetProperty(XmpNamespaces.DublinCore, "creator")!.Kind);
        Assert.Equal(XmpPropertyKind.UnorderedArray, packet.GetProperty(XmpNamespaces.DublinCore, "subject")!.Kind);

        const string ex = "http://example.com/ns/";
        XmpProperty resource = packet.GetProperty(ex, "Resource")!;
        Assert.Equal(XmpPropertyKind.Structure, resource.Kind);
        Assert.Equal("Resource form", resource.GetField(ex, "Name")!.Value);
        Assert.Equal("2", resource.GetField(ex, "Count")!.Value);
        XmpProperty nested = packet.GetProperty(ex, "Nested")!;
        Assert.Equal(XmpPropertyKind.Structure, nested.Kind);
        Assert.Equal(["Attribute field", "3"], nested.Fields.Select(field => field.Value));
        Assert.Equal("Shorthand", packet.GetProperty(ex, "Short")!.GetField(ex, "Name")!.Value);
        Assert.Equal("http://example.com/", packet.GetProperty(ex, "Link")!.Value);

        XmpProperty qualified = packet.GetProperty(ex, "WithQualifier")!;
        Assert.Equal(XmpPropertyKind.Simple, qualified.Kind);
        Assert.Equal("42", qualified.Value);
        Assert.Equal("mm", Assert.Single(qualified.Qualifiers).Value);
        Assert.Null(packet.GetProperty(ex, "Missing"));
        Assert.Equal(17, packet.Properties.Count);
    }

    [Fact]
    public void The_common_properties_are_typed()
    {
        Assert.True(XmpPacket.TryParse(Encoding.UTF8.GetBytes(Forms), out XmpPacket? packet));

        Assert.Equal("Forms", packet.Title);
        Assert.Equal("Formen", packet.GetLanguageAlternative(XmpNamespaces.DublinCore, "title", "de"));
        Assert.Equal("Forms", packet.GetLanguageAlternative(XmpNamespaces.DublinCore, "title", "fr"));
        Assert.Equal(["Ada", "Grace"], packet.Creators);
        Assert.Equal(["pdf", "xmp"], packet.Subjects);
        Assert.Null(packet.Description);
        Assert.Equal("Broadside generate.py", packet.Producer);
        Assert.Equal("one, two", packet.Keywords);
        Assert.Equal(" spaced ", packet.CreatorTool);
        Assert.Equal(new DateTimeOffset(2014, 9, 24, 21, 23, 3, TimeSpan.FromHours(2)), packet.CreateDate!.Value.Value);
        Assert.Null(packet.ModifyDate);
        Assert.Equal(2, packet.PdfAPart);
        Assert.Equal("B", packet.PdfAConformance);
        Assert.Equal(1, packet.PdfUAPart);
        Assert.Equal("uuid:0b2c7c5e-0000-4000-8000-000000000001", packet.DocumentId);
        Assert.Equal("uuid:0b2c7c5e-0000-4000-8000-000000000002", packet.InstanceId);
    }

    [Theory]
    [InlineData("utf-16BE")]
    [InlineData("utf-16LE")]
    [InlineData("utf-32BE")]
    [InlineData("utf-32LE")]
    [InlineData("utf-8")]
    public void A_packet_in_any_unicode_encoding_with_its_byte_order_mark_is_read(string encodingName)
    {
        Encoding encoding = encodingName switch
        {
            "utf-16BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            "utf-16LE" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf-32BE" => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
            "utf-32LE" => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        };
        byte[] bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(Title("Ünïcödé"))];

        Assert.True(XmpPacket.TryParse(bytes, out XmpPacket? packet));
        Assert.Equal("Ünïcödé", packet.Title);
    }

    [Fact]
    public void A_utf_16_packet_without_a_byte_order_mark_is_recognized_by_its_first_character()
    {
        Assert.True(XmpPacket.TryParse(Encoding.BigEndianUnicode.GetBytes(Title("big")), out XmpPacket? big));
        Assert.True(XmpPacket.TryParse(Encoding.Unicode.GetBytes(Title("little")), out XmpPacket? little));

        Assert.Equal("big", big.Title);
        Assert.Equal("little", little.Title);
    }

    [Fact]
    public void Padding_and_bytes_after_the_rdf_element_are_never_read()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes(Title("padded")), .. new byte[64], .. "<unclosed"u8];

        Assert.True(XmpPacket.TryParse(bytes, out XmpPacket? packet));
        Assert.Equal("padded", packet.Title);
    }

    [Fact]
    public void Bytes_before_the_first_markup_are_skipped()
    {
        Assert.True(XmpPacket.TryParse([.. "\x00junk \r\n"u8, .. Encoding.UTF8.GetBytes(Title("junk"))], out XmpPacket? packet));

        Assert.Equal("junk", packet.Title);
    }

    [Fact]
    public void Neither_the_packet_wrapper_nor_the_xmpmeta_element_is_required()
    {
        string bare = "<rdf:RDF xmlns:rdf=\"" + Rdf + "\"><rdf:Description xmlns:DC=\"http://purl.org/dc/elements/1.1/\">"
            + "<DC:title><rdf:Alt><rdf:li xml:lang=\"en\">bare</rdf:li></rdf:Alt></DC:title></rdf:Description></rdf:RDF>";

        Assert.True(XmpPacket.TryParse(Encoding.UTF8.GetBytes(bare), out XmpPacket? packet));
        Assert.Equal("bare", packet.Title);
        Assert.Null(packet.About);
    }

    [Fact]
    public void A_property_given_twice_keeps_its_first_value()
    {
        string twice = "<rdf:RDF xmlns:rdf=\"" + Rdf + "\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">"
            + "<rdf:Description pdf:Producer=\"first\"/><rdf:Description><pdf:Producer>second</pdf:Producer></rdf:Description></rdf:RDF>";

        Assert.True(XmpPacket.TryParse(Encoding.UTF8.GetBytes(twice), out XmpPacket? packet));
        Assert.Equal("first", packet.Producer);
        Assert.Single(packet.Properties);
    }

    [Fact]
    public void Predefined_and_character_references_are_text()
    {
        Assert.True(XmpPacket.TryParse(Encoding.UTF8.GetBytes(Title("A &amp; B &#x263A; &lt;")), out XmpPacket? packet));

        Assert.Equal("A & B ☺ <", packet.Title);
    }

    public static TheoryData<string> DocumentTypeDeclarations => new()
    {
        "<!DOCTYPE lolz [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">]>",
        "<!DOCTYPE x SYSTEM \"file:///etc/passwd\">",
    };

    [Theory]
    [MemberData(nameof(DocumentTypeDeclarations))]
    public void A_document_type_declaration_is_refused_so_entities_never_expand(string doctype)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(doctype + Title("&lol2;"));

        Assert.False(XmpPacket.TryParse(bytes, out XmpPacket? packet));
        Assert.Null(packet);
    }

    [Theory]
    [InlineData("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description a=\"1\" a=\"2\"/></rdf:RDF>")]
    [InlineData("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description>")]
    [InlineData("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"></x:xmpmeta>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    public void Malformed_xml_or_a_packet_without_an_rdf_element_does_not_parse(string text)
    {
        Assert.False(XmpPacket.TryParse(Encoding.UTF8.GetBytes(text), out _));
    }

    private static string Title(string title) =>
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"" + Rdf
        + "\"><rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">"
        + title + "</rdf:li></rdf:Alt></dc:title></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>";
}
