using System.Text;
using System.Xml;
using Broadside.Objects;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Writing the Info dictionary and the XMP metadata and keeping them in sync. ISO 32000-2 §14.3; ISO 16684-1.</summary>
public sealed class MetadataWritingTests
{
    /// <summary>The instant every save in these tests happens at, in a +02:00 zone.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 30, 15, TimeSpan.FromHours(2));

    /// <summary>A packet with every serialization form ISO 16684-1 §7.5-7.9 allows, including a URI, qualifiers and a prefix clash.</summary>
    private const string RichPacket =
        "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n"
        + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n"
        + " <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n"
        + "  <rdf:Description rdf:about=\"\" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"\n"
        + "    pdf:Producer=\"Broadside\" xmp:CreateDate=\"2014-09-24T21:23:03+02:00\">\n"
        + "   <xmp:CreatorTool> spaced </xmp:CreatorTool>\n"
        + "  </rdf:Description>\n"
        + "  <rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:ex=\"http://example.com/ns/\" xmlns:dc2=\"http://example.com/dc\">\n"
        + "   <dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">Forms</rdf:li><rdf:li xml:lang=\"de\">Formen</rdf:li></rdf:Alt></dc:title>\n"
        + "   <dc:creator><rdf:Seq><rdf:li>Ada</rdf:li><rdf:li>Grace</rdf:li></rdf:Seq></dc:creator>\n"
        + "   <dc:subject><rdf:Bag><rdf:li>pdf</rdf:li><rdf:li>xmp</rdf:li></rdf:Bag></dc:subject>\n"
        + "   <ex:Resource rdf:parseType=\"Resource\"><ex:Name>Resource form</ex:Name><ex:Count>2</ex:Count></ex:Resource>\n"
        + "   <ex:Nested><rdf:Description ex:Name=\"Attribute field\"><ex:Count>3</ex:Count></rdf:Description></ex:Nested>\n"
        + "   <ex:Short ex:Name=\"Shorthand\"/>\n"
        + "   <ex:Link rdf:resource=\"http://example.com/\"/>\n"
        + "   <ex:WithQualifier><rdf:Description><rdf:value>42</rdf:value><ex:unit>mm</ex:unit></rdf:Description></ex:WithQualifier>\n"
        + "   <ex:Items><rdf:Seq><rdf:li rdf:parseType=\"Resource\"><ex:Name>one</ex:Name></rdf:li><rdf:li xml:lang=\"fr\">deux</rdf:li></rdf:Seq></ex:Items>\n"
        + "   <ex:Empty></ex:Empty>\n"
        + "   <dc2:format>clash</dc2:format>\n"
        + "  </rdf:Description>\n"
        + " </rdf:RDF>\n"
        + "</x:xmpmeta>\n"
        + "<?xpacket end=\"w\"?>";

    private static readonly PdfOptions Options = new PdfOptions().UseTimeProvider(new FixedTimeProvider(Now));

    [Fact]
    public void Setting_title_and_author_on_info_dictionary_pdf_writes_both_info_and_xmp_and_rereads_identically()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);

        document.Properties.Title = "Analytical Engine";
        document.Properties.Author = "Charles Babbage";
        using PdfDocument reopened = PdfDocument.Open(Save(document));

        PdfDocumentInformation info = reopened.Information!;
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("Analytical Engine", info.Title);
        Assert.Equal("Analytical Engine", packet.Title);
        Assert.Equal("Charles Babbage", info.Author);
        Assert.Equal(["Charles Babbage"], packet.Creators);

        // The entries the document already had are mirrored into the new packet (Table 349's mapping).
        Assert.Equal("Document information", info.Subject);
        Assert.Equal("Document information", packet.Description);
        Assert.Equal("info, metadata", info.Keywords);
        Assert.Equal("info, metadata", packet.Keywords);
        Assert.Equal("generate.py", info.Creator);
        Assert.Equal("generate.py", packet.CreatorTool);
        Assert.Equal("Broadside corpus", info.Producer);
        Assert.Equal("Broadside corpus", packet.Producer);
        Assert.Equal(PdfTrapped.True, info.Trapped);
        Assert.Equal("True", packet.Trapped);
        var created = new DateTimeOffset(2014, 3, 14, 12, 42, 11, TimeSpan.FromHours(1));
        Assert.Equal(created, info.CreationDate!.Value.Value);
        Assert.Equal(created, packet.CreateDate!.Value.Value);
        Assert.Equal("2014-03-14T12:42:11+01:00", packet.CreateDate.Value.Text);

        // §14.3.4: the save is a modification, dated the same in both.
        Assert.Equal(Now, info.ModificationDate!.Value.Value);
        Assert.Equal(TimeSpan.FromHours(2), info.ModificationDate.Value.Value.Offset);
        Assert.Equal(Now, packet.ModifyDate!.Value.Value);
        Assert.Equal(Now, packet.MetadataDate!.Value.Value);
        Assert.Equal("Corpus", info.GetText("Department"));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_pdf_2_0_save_writes_xmp_and_only_the_info_entries_pdf_2_0_keeps()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.Catalog[new CosName("Version")] = new CosName("2.0");

        document.Properties.Keywords = "upgraded";
        using PdfDocument reopened = PdfDocument.Open(Save(document));

        // §14.3.3: in PDF 2.0 only CreationDate and ModDate stay in Info; the deprecated entries now live in the packet.
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        CosDictionary info = reopened.Information!.Dictionary;
        Assert.Equal(["CreationDate", "Department", "ModDate", "Printed"], info.Keys.Select(key => key.Value).Order(StringComparer.Ordinal));
        Assert.Equal("D:20140314124211+01'00", reopened.Information.CreationDate!.Value.Text);
        Assert.Equal("D:20261012093015+02'00", reopened.Information.ModificationDate!.Value.Text);
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("Broadside – Info", packet.Title);
        Assert.Equal(["Ada Lovelace"], packet.Creators);
        Assert.Equal("Document information", packet.Description);
        Assert.Equal("upgraded", packet.Keywords);
        Assert.Equal("generate.py", packet.CreatorTool);
        Assert.Equal("Broadside corpus", packet.Producer);
        Assert.Equal("True", packet.Trapped);
        Assert.Equal("2014-03-14T12:42:11+01:00", packet.CreateDate!.Value.Text);
        Assert.Equal("2026-10-12T09:30:15+02:00", packet.ModifyDate!.Value.Text);
        Assert.Equal("upgraded", reopened.Properties.Keywords);
        Assert.Equal("Broadside – Info", reopened.Properties.Title);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_created_pdf_2_0_document_is_saved_with_an_xmp_packet_and_no_info_dictionary()
    {
        using PdfDocument document = new PdfEngine(Options).Create();

        document.Properties.Title = "Created";
        document.Properties.Author = "Broadside";
        using PdfDocument reopened = PdfDocument.Open(Save(document));

        Assert.Null(reopened.Information);
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("Created", packet.Title);
        Assert.Equal(["Broadside"], packet.Creators);
        Assert.Equal("application/pdf", packet.Format);

        // §14.3.4: a new document's first save writes its creation date, equal to the modification date.
        Assert.Equal(Now, packet.CreateDate!.Value.Value);
        Assert.Equal(Now, packet.ModifyDate!.Value.Value);
        Assert.Equal(Now, packet.MetadataDate!.Value.Value);
        Assert.Equal(Now, reopened.Properties.CreationDate);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void The_written_packet_is_well_formed_xml_in_its_wrapper_and_rereads_with_no_diagnostics()
    {
        const string title = "Tom & Jerry <\"quoted\"> \r\n line\u0001";
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.Properties.Title = title;

        byte[] saved = Save(document);
        using PdfDocument reopened = PdfDocument.Open(saved);

        PdfMetadata metadata = reopened.Metadata!;
        Assert.Equal(new CosName("Metadata"), metadata.Stream.Dictionary[new CosName("Type")]);
        Assert.Equal(new CosName("XML"), metadata.Stream.Dictionary[new CosName("Subtype")]);
        Assert.False(metadata.Stream.Dictionary.ContainsKey(new CosName("Filter")));
        byte[] data = metadata.Data.ToArray();
        string text = Encoding.UTF8.GetString(data);

        // ISO 16684-1 §7.3: the header's begin attribute is the byte order mark character; the trailer is writable.
        Assert.False(data.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]));
        Assert.StartsWith("<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>", text, StringComparison.Ordinal);
        Assert.EndsWith("<?xpacket end=\"w\"?>", text, StringComparison.Ordinal);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, ConformanceLevel = ConformanceLevel.Document };
        using (var reader = XmlReader.Create(new StringReader(text), settings))
        {
            while (reader.Read())
            {
            }
        }

        // XML 1.0 cannot carry U+0001 at all; everything else, the carriage return included, survives.
        Assert.Equal("Tom & Jerry <\"quoted\"> \r\n line", metadata.Packet!.Title);
        Assert.Equal(title, reopened.Information!.Title);
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void Every_other_property_of_an_existing_packet_survives_a_change()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp-forms.pdf"), Options);
        document.Metadata!.Stream.EncodedData = Encoding.UTF8.GetBytes(RichPacket);
        XmpPacket before = document.Metadata.Packet!;

        document.Properties.Title = "Changed";
        using PdfDocument reopened = PdfDocument.Open(Save(document));

        XmpPacket after = reopened.Metadata!.Packet!;
        Assert.Equal("Changed", after.Title);
        Assert.Equal("Formen", after.GetLanguageAlternative(XmpNamespaces.DublinCore, "title", "de"));
        string[] owned = ["title", "ModifyDate", "MetadataDate"];
        Assert.Equal(
            before.Properties.Where(property => !owned.Contains(property.Name)).Select(Describe),
            after.Properties.Where(property => !owned.Contains(property.Name)).Select(Describe));
        Assert.Equal(before.About, after.About);
        Assert.Equal("Changed", reopened.Information!.Title);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Metadata_set_before_an_incremental_save_is_appended_and_rereads_in_both_places()
    {
        byte[] file = Corpus.Bytes("info-dictionary.pdf");
        using PdfDocument document = PdfDocument.Open(file, Options);
        document.Properties.Subject = "Incremental";

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.True(saved.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("Incremental", reopened.Information!.Subject);
        Assert.Equal("Incremental", reopened.Metadata!.Packet!.Description);
        Assert.Equal(Now, reopened.Information.ModificationDate!.Value.Value);
        Assert.Equal(Now, reopened.Metadata.Packet.ModifyDate!.Value.Value);
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void A_changed_document_is_dated_on_save_and_an_unchanged_one_is_not()
    {
        byte[] file = Corpus.Bytes("info-dictionary.pdf");
        using PdfDocument unchanged = PdfDocument.Open(file, Options);
        using PdfDocument changed = PdfDocument.Open(file, Options);
        changed.Pages[0].Dictionary[new CosName("Rotate")] = new CosInteger(90);

        using PdfDocument reopenedUnchanged = PdfDocument.Open(Save(unchanged));
        using PdfDocument reopenedChanged = PdfDocument.Open(Save(changed));

        Assert.Equal("D:20140924212303Z", reopenedUnchanged.Information!.ModificationDate!.Value.Text);
        Assert.Null(reopenedUnchanged.Metadata);
        Assert.Equal("D:20261012093015+02'00'", reopenedChanged.Information!.ModificationDate!.Value.Text);

        // A document before PDF 2.0 without a metadata stream does not get one only to record the date.
        Assert.Null(reopenedChanged.Metadata);
    }

    [Fact]
    public void Dating_can_be_turned_off_and_a_date_set_explicitly_is_kept()
    {
        byte[] file = Corpus.Bytes("info-dictionary.pdf");
        using PdfDocument off = PdfDocument.Open(file, Options);
        off.Pages[0].Dictionary[new CosName("Rotate")] = new CosInteger(90);
        using PdfDocument explicitly = PdfDocument.Open(file, Options);
        var date = new DateTimeOffset(2020, 2, 29, 23, 59, 58, TimeSpan.FromMinutes(-330));
        explicitly.Properties.ModificationDate = date;

        using PdfDocument reopenedOff = PdfDocument.Open(Save(off, new PdfSaveOptions().WithUpdateModificationDate(false)));
        using PdfDocument reopenedExplicitly = PdfDocument.Open(Save(explicitly));

        Assert.Equal("D:20140924212303Z", reopenedOff.Information!.ModificationDate!.Value.Text);
        Assert.Equal(date, reopenedExplicitly.Information!.ModificationDate!.Value.Value);
        Assert.Equal("D:20200229235958-05'30'", reopenedExplicitly.Information.ModificationDate.Value.Text);
        Assert.Equal("2020-02-29T23:59:58-05:30", reopenedExplicitly.Metadata!.Packet!.ModifyDate!.Value.Text);
        Assert.Equal(Now, reopenedExplicitly.Metadata.Packet.MetadataDate!.Value.Value);
    }

    [Fact]
    public void Setting_an_entry_to_null_removes_it_from_both_places()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"), Options);
        Assert.Equal("Broadside", document.Information!.Title);

        document.Properties.Title = null;
        using PdfDocument reopened = PdfDocument.Open(Save(document));

        Assert.Null(reopened.Information!.Title);
        Assert.Null(reopened.Metadata!.Packet!.Title);
        Assert.Null(reopened.Properties.Title);
    }

    [Fact]
    public void An_unreadable_packet_is_replaced_by_a_readable_one_holding_the_info_entries()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"), Options);
        document.Metadata!.Stream.EncodedData = "<x:xmpmeta><rdf:RDF>"u8.ToArray();
        Assert.Null(document.Metadata.Packet);

        document.Properties.Subject = "Repaired";
        using PdfDocument reopened = PdfDocument.Open(Save(document));

        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("Repaired", packet.Description);
        Assert.Equal("Broadside", packet.Title);
        Assert.Equal("Repaired", reopened.Information!.Subject);
        Assert.Empty(reopened.Diagnostics);
    }

    /// <summary>A structural description of a property, independent of how it was serialized, for comparing packets.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_dated_metadata_stream_of_an_encrypted_save_is_plaintext_only_without_metadata_encryption(bool encryptMetadata)
    {
        // §7.6.3.2: EncryptMetadata false leaves the document-level metadata stream unencrypted; the packet dated by the save
        // (§14.3.4) follows the same rule, both under new encryption and when the encrypted file is changed and saved again.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"), Options);
        document.Properties.Title = "Dated";
        var encryption = new PdfEncryptionOptions("user", "owner").WithEncryptMetadata(encryptMetadata);

        byte[] saved = Save(document, new PdfSaveOptions().WithEncryption(encryption));

        AssertDatedPacket(saved, Now, "Dated", encryptMetadata);

        DateTimeOffset later = Now.AddDays(1);
        using PdfDocument encrypted = new PdfEngine(new PdfOptions().UseTimeProvider(new FixedTimeProvider(later)))
            .Open(saved, new PdfPassword("user"));
        encrypted.Properties.Title = "Dated again";

        byte[] resaved = Save(encrypted);

        AssertDatedPacket(resaved, later, "Dated again", encryptMetadata);
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Aes256, "2.0", "D:20261012093015+02'00")]
    [InlineData(PdfEncryptionAlgorithm.Aes128, "1.7", "D:20261012093015+02'00'")]
    public void A_save_dates_the_modification_in_the_form_of_the_version_it_writes(PdfEncryptionAlgorithm algorithm, string version, string modified)
    {
        // §7.9.4 NOTE 2: the terminating apostrophe is PDF 1.7 and earlier's; AES-256 raises the 1.7 file to 2.0 (§7.6.2, Table 20),
        // AES-128 needs only 1.6 and leaves it at 1.7.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"), Options);
        document.Properties.Title = "Raised";

        byte[] saved = Save(document, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm)));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal(version, reopened.Version.ToString());
        Assert.Equal(modified, reopened.Information!.ModificationDate!.Value.Text);
        Assert.Equal("2026-10-12T09:30:15+02:00", reopened.Metadata!.Packet!.ModifyDate!.Value.Text);
        Assert.Equal(Now, reopened.Information.ModificationDate.Value.Value);
    }

    [Fact]
    public void An_incremental_save_that_raises_to_2_0_dates_the_modification_and_a_set_creation_date_in_the_2_0_form()
    {
        // An associated file is PDF 2.0 (§14.13): the update raises the catalog's Version (§7.7.2, Table 29).
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.Properties.CreationDate = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5));
        document.AddAssociatedFile(document.Catalog, document.CreateEmbeddedFile("a.txt", "a"u8));

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Equal("D:20261012093015+02'00", reopened.Information!.ModificationDate!.Value.Text);
        Assert.Equal("D:20250102030405-05'00", reopened.Information.CreationDate!.Value.Text);
    }

    [Fact]
    public void A_date_set_explicitly_is_written_in_the_2_0_form_when_the_save_raises_to_2_0_without_dating()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.Properties.ModificationDate = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.FromHours(1));

        byte[] saved = Save(document, new PdfSaveOptions().WithUpdateModificationDate(false).WithEncryption(new PdfEncryptionOptions("user", "owner")));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Equal("D:20250102030405+01'00", reopened.Information!.ModificationDate!.Value.Text);
        Assert.Equal("D:20140314124211+01'00", reopened.Information.CreationDate!.Value.Text); // The file's own, kept as it was.
    }

    [Fact]
    public void A_save_of_a_pdf_2_0_document_that_needs_no_raise_dates_it_in_the_2_0_form()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.Catalog[new CosName("Version")] = new CosName("2.0");

        byte[] saved = Save(document, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner")));

        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal("D:20261012093015+02'00", reopened.Information!.ModificationDate!.Value.Text);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void A_save_that_raises_to_2_0_moves_the_deprecated_info_entries_to_a_new_packet(bool incremental, bool dating)
    {
        // info-dictionary.pdf is 1.7 with Title, Author and the other Table 349 entries in Info and no metadata stream. An associated
        // file is PDF 2.0 (§14.13), so the save raises the document, and §14.3.3 then applies: the deprecated entries live in XMP.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.AddAssociatedFile(document.Catalog, document.CreateEmbeddedFile("a.txt", "a"u8));

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(incremental).WithUpdateModificationDate(dating));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        CosDictionary info = reopened.Information!.Dictionary;
        Assert.Equal(["CreationDate", "Department", "ModDate", "Printed"], info.Keys.Select(key => key.Value).Order(StringComparer.Ordinal));
        Assert.Equal("D:20140314124211+01'00", reopened.Information.CreationDate!.Value.Text);
        Assert.Equal(dating ? "D:20261012093015+02'00" : "D:20140924212303Z", reopened.Information.ModificationDate!.Value.Text);
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal("Broadside – Info", packet.Title);
        Assert.Equal(["Ada Lovelace"], packet.Creators);
        Assert.Equal("Document information", packet.Description);
        Assert.Equal("info, metadata", packet.Keywords);
        Assert.Equal("generate.py", packet.CreatorTool);
        Assert.Equal("Broadside corpus", packet.Producer);
        Assert.Equal("True", packet.Trapped);
        Assert.Equal("application/pdf", packet.Format);
        Assert.Equal("2014-03-14T12:42:11+01:00", packet.CreateDate!.Value.Text);
        Assert.Equal(dating ? "2026-10-12T09:30:15+02:00" : "2014-09-24T21:23:03Z", packet.ModifyDate!.Value.Text);
        Assert.Equal(Now, packet.MetadataDate!.Value.Value);
        Assert.Equal("Broadside – Info", reopened.Properties.Title);
        Assert.Equal("Ada Lovelace", reopened.Properties.Author);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_raise_to_2_0_with_no_info_entry_to_move_and_no_dating_adds_no_packet()
    {
        // empty-page.pdf has neither Info nor a metadata stream; a PDF 2.0 document whose metadata nobody writes gets none either.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("empty-page.pdf"), Options);
        document.AddAssociatedFile(document.Catalog, document.CreateEmbeddedFile("a.txt", "a"u8));

        byte[] saved = Save(document, new PdfSaveOptions().WithUpdateModificationDate(false));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Null(reopened.Metadata);
        Assert.Null(reopened.Information);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_save_that_stays_below_2_0_keeps_the_info_entries_and_creates_no_packet(bool incremental)
    {
        // An embedded file without a relationship needs PDF 1.7 at most (§7.11.4, Table 43): the 1.7 document is not raised.
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("info-dictionary.pdf"), Options);
        document.AddEmbeddedFile("a.txt", "a"u8);

        byte[] saved = Save(document, new PdfSaveOptions().WithIncrementalUpdate(incremental));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
        Assert.Null(reopened.Metadata);
        CosDictionary info = reopened.Information!.Dictionary;
        Assert.Equal(
            ["Author", "CreationDate", "Creator", "Department", "Keywords", "ModDate", "Printed", "Producer", "Subject", "Title", "Trapped"],
            info.Keys.Select(key => key.Value).Order(StringComparer.Ordinal));
        Assert.Equal("Broadside – Info", reopened.Information.Title);
        Assert.Equal("Ada Lovelace", reopened.Information.Author);
        Assert.Equal("D:20261012093015+02'00'", reopened.Information.ModificationDate!.Value.Text);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Strict_mode_refusing_the_raise_to_2_0_writes_nothing_and_leaves_the_version()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("metadata-xmp.pdf"), new PdfOptions().UseTimeProvider(new FixedTimeProvider(Now)).UseStrict());
        document.Properties.Title = "Refused";
        using var output = new MemoryStream();

        Diagnostics.DiagnosticException exception = Assert.Throws<Diagnostics.DiagnosticException>(
            () => document.Save(output, new PdfSaveOptions().WithEncryption(new PdfEncryptionOptions("user", "owner"))));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Equal(0, output.Length);
        Assert.Equal(new PdfVersion(1, 7), document.Version);
        Assert.Null(document.CatalogVersion);
        Assert.DoesNotContain(new CosName("Extensions"), document.Catalog.Keys);
    }

    private static void AssertDatedPacket(byte[] file, DateTimeOffset dated, string title, bool encryptMetadata)
    {
        ReadOnlySpan<byte> bytes = file;
        byte[] date = Encoding.ASCII.GetBytes(dated.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(!encryptMetadata, bytes.IndexOf("<?xpacket"u8) >= 0);
        Assert.Equal(!encryptMetadata, bytes.IndexOf(date) >= 0);
        Assert.Equal(!encryptMetadata, bytes.IndexOf(Encoding.UTF8.GetBytes(title)) >= 0);

        using PdfDocument reopened = new PdfEngine().Open(file, new PdfPassword("user"));
        Assert.Equal(encryptMetadata, reopened.Security!.EncryptsMetadata);
        XmpPacket packet = reopened.Metadata!.Packet!;
        Assert.Equal(title, packet.Title);
        Assert.Equal(dated, packet.ModifyDate!.Value.Value);
        Assert.Equal(dated, packet.MetadataDate!.Value.Value);
        Assert.Equal(dated, reopened.Information!.ModificationDate!.Value.Value);
        Assert.Empty(reopened.Diagnostics);
    }

    private static string Describe(XmpProperty property) =>
        $"{property.NamespaceName}|{property.Name}|{property.Kind}|{property.Value}|{property.Language}"
        + $"[{string.Join(",", property.Items.Select(Describe))}]{{{string.Join(",", property.Fields.Select(Describe))}}}({string.Join(",", property.Qualifiers.Select(Describe))})";

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }

    /// <summary>A clock that always reads <paramref name="now"/>, in a zone with its offset.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Fixed", now.Offset, "Fixed", "Fixed");

        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
