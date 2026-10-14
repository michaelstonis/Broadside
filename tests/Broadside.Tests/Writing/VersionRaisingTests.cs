using System.Text;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Structure;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving raises the version to the highest minimum the written features need, or throws in strict mode naming the feature.
/// ISO 32000-2 §7.5.2, §7.7.2 Table 29 (<c>Version</c>), §7.12; ADR 0003.
/// </summary>
public sealed class VersionRaisingTests
{
    private static PdfSaveOptions Options => new PdfSaveOptions().WithUpdateModificationDate(false);

    [Fact]
    public void Object_streams_raise_a_pdf_1_4_corpus_file_to_1_5()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jbig2-generic.pdf"));
        Assert.Equal(new PdfVersion(1, 4), document.Version);

        byte[] saved = Save(document, Options.WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams));

        Assert.Equal("1.5", HeaderVersion(saved));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 5), reopened.Version);
        Assert.Empty(reopened.Diagnostics);
        Diagnostic raised = Assert.Single(document.Diagnostics);
        Assert.Equal("VersionRaised", raised.Code);
        Assert.Equal(DiagnosticSeverity.Information, raised.Severity);
        Assert.Contains("object streams", raised.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_refuses_object_streams_in_a_pdf_1_4_corpus_file_naming_the_feature_before_writing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("jbig2-generic.pdf"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(
            () => document.Save(output, Options.WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams)));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Contains("object streams", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1.5", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(PdfCrossReferenceLayout.Stream, "a cross-reference stream")]
    [InlineData(PdfCrossReferenceLayout.StreamWithObjectStreams, "object streams")]
    public void Each_cross_reference_layout_needing_pdf_1_5_raises_a_pdf_1_4_file(PdfCrossReferenceLayout layout, string feature)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: "%PDF-1.4"));

        byte[] saved = Save(document, Options.WithCrossReferenceLayout(layout));

        Assert.Equal("1.5", HeaderVersion(saved));
        Assert.Contains(feature, Assert.Single(document.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flate_compression_on_save_raises_a_pdf_1_1_file_to_1_2_and_no_compression_keeps_1_1()
    {
        using PdfDocument compressed = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: "%PDF-1.1"));
        compressed.AddObject(new CosStream(new CosDictionary(), new byte[1000]));
        using PdfDocument uncompressed = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: "%PDF-1.1"));
        uncompressed.AddObject(new CosStream(new CosDictionary(), new byte[1000]));

        byte[] withCompression = Save(compressed, Options);
        byte[] withoutCompression = Save(uncompressed, Options.WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal("1.2", HeaderVersion(withCompression));
        Assert.Contains("FlateDecode", Assert.Single(compressed.Diagnostics).Message, StringComparison.Ordinal);
        Assert.Equal("1.1", HeaderVersion(withoutCompression));
        Assert.Empty(uncompressed.Diagnostics);
    }

    [Fact]
    public void Encoding_a_stream_with_flate_raises_a_pdf_1_1_document_to_1_2()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: "%PDF-1.1"));
        var stream = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("FlateDecode") }, ReadOnlyMemory<byte>.Empty);
        document.AddObject(stream);
        document.EncodeStream(stream, new byte[100]);

        byte[] saved = Save(document, Options.WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal("1.2", HeaderVersion(saved));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 2), reopened.Version);
    }

    [Theory]
    [InlineData(PdfStreamCompression.None, "1.1", "linearization")]
    [InlineData(PdfStreamCompression.ChangedStreams, "1.2", "FlateDecode")]
    public void Linearizing_a_pdf_1_0_file_raises_it_to_1_1_and_a_compressed_hint_stream_to_1_2(PdfStreamCompression compression, string version, string named)
    {
        // Annex F, F.3.2: linearization applies to PDF 1.1 or later; the hint stream is FlateDecode-compressed (§7.4.4, PDF 1.2)
        // unless compression is off.
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.0"));

        byte[] saved = Save(document, Options.WithLinearize(true).WithStreamCompression(compression));

        Assert.Equal(version, HeaderVersion(saved));
        Diagnostic raised = Assert.Single(document.Diagnostics);
        Assert.Equal("VersionRaised", raised.Code);
        Assert.Contains(named, raised.Message, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.True(reopened.IsLinearized);
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check-linearization");
        Assert.True(exitCode == 0 && output.Contains("no linearization errors", StringComparison.Ordinal), output);
    }

    [Fact]
    public void Strict_mode_refuses_linearizing_a_pdf_1_0_file_before_writing()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.0"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(
            () => document.Save(output, Options.WithLinearize(true).WithStreamCompression(PdfStreamCompression.None)));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Contains("linearization", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_linearized_incremental_update_is_refused_with_an_argument_exception_before_any_version_check()
    {
        // The refusal is the first check of the save (issue #300's check step), ahead of the strict-mode version refusal.
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, LaterExtension, header: "%PDF-1.4"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        Assert.Throws<ArgumentException>(() => document.Save(output, Options.WithIncrementalUpdate(true).WithLinearize(true)));

        Assert.Equal(0, output.Length);
        Assert.Empty(document.Diagnostics);
        Assert.Null(document.CatalogVersion);
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4, "%PDF-1.1", 1, 4, "RC4")]
    [InlineData(PdfEncryptionAlgorithm.Aes128, "%PDF-1.4", 1, 6, "AES-128")]
    [InlineData(PdfEncryptionAlgorithm.Aes256, "%PDF-1.7", 2, 0, "AES-256")]
    [InlineData(PdfEncryptionAlgorithm.Aes256Gcm, "%PDF-1.7", 2, 0, "AES-GCM")]
    public void New_encryption_raises_the_version_to_its_algorithms(PdfEncryptionAlgorithm algorithm, string header, int major, int minor, string feature)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: header));

        byte[] saved = Save(document, Options.WithEncryption(new PdfEncryptionOptions("user", "owner").WithAlgorithm(algorithm)));

        var expected = new PdfVersion(major, minor);
        Assert.Equal(expected.ToString(), HeaderVersion(saved));
        Diagnostic raised = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
        Assert.Contains(feature, raised.Message, StringComparison.Ordinal);
        using PdfDocument reopened = new PdfEngine().Open(saved, new PdfPassword("user"));
        Assert.Equal(expected, reopened.Version);
    }

    [Fact]
    public void Strict_mode_refuses_aes_256_encryption_of_a_pdf_1_7_document()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("empty-page.pdf"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(
            () => document.Save(output, Options.WithEncryption(new PdfEncryptionOptions("user", "owner"))));

        Assert.Contains("AES-256", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Keeping_aes_gcm_encryption_declares_the_iso_ts_32003_extension_beside_the_others()
    {
        // The corpus file with its declaration changed to another level: the document uses ISO/TS 32003 without declaring it.
        using PdfDocument document = PdfDocument.Open(UndeclaredGcm());
        Assert.Equal("EncryptionExtensionMissing", Assert.Single(document.Diagnostics).Code);

        byte[] saved = Save(document, Options);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        PdfDeveloperExtension[] iso = [.. reopened.Extensions.Where(extension => extension.Prefix.Value == "ISO_")];
        Assert.Equal([32009, 32003], iso.Select(extension => extension.ExtensionLevel ?? 0));
        PdfDeveloperExtension added = iso[1];
        Assert.Equal(new PdfVersion(2, 0), added.BaseVersion);
        Assert.Equal(":2023", added.ExtensionRevision);
        Assert.Equal(new Uri("https://www.iso.org/standard/45876.html"), added.Url);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_single_developer_extensions_dictionary_becomes_an_array_when_an_iso_extension_is_added()
    {
        using PdfDocument document = PdfDocument.Open(UndeclaredGcm());
        var extensions = (CosDictionary)document.Catalog[new CosName("Extensions")];
        var iso = new CosName("ISO_");
        extensions[iso] = ((CosArray)extensions[iso])[0];
        extensions[new CosName("ADBE")] = new CosDictionary { [new CosName("BaseVersion")] = new CosName("1.7"), [new CosName("ExtensionLevel")] = new CosInteger(3) };

        using PdfDocument reopened = PdfDocument.Open(Save(document, Options));

        Assert.Equal(
            ["ISO_ 32009", "ISO_ 32003", "ADBE 3"],
            reopened.Extensions.Select(extension => $"{extension.Prefix.Value} {extension.ExtensionLevel}"));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_developer_extension_later_than_the_document_is_reported_when_read_and_raises_the_version_on_save()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, LaterExtension, header: "%PDF-1.4"));

        PdfDeveloperExtension extension = Assert.Single(document.Extensions);
        Diagnostic later = Assert.Single(document.Diagnostics);
        Assert.Equal("DeveloperExtensionBaseVersionLater", later.Code);
        Assert.Equal(DiagnosticSeverity.Warning, later.Severity);
        Assert.Equal(new PdfVersion(1, 6), extension.BaseVersion);

        byte[] saved = Save(document, Options);

        Assert.Equal("1.6", HeaderVersion(saved));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Single(reopened.Extensions);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_developer_extension_later_than_the_document_throws_in_strict_mode()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, LaterExtension, header: "%PDF-1.4"), new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Extensions);

        Assert.Equal("DeveloperExtensionBaseVersionLater", exception.Diagnostic.Code);
    }

    [Fact]
    public void An_incremental_save_that_needs_1_6_writes_the_catalog_version_and_leaves_the_header_bytes_untouched()
    {
        byte[] file = Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", LaterExtension, header: "%PDF-1.4");
        using PdfDocument document = PdfDocument.Open(file);

        byte[] saved = Save(document, Options.WithIncrementalUpdate(true));

        Assert.True(saved.AsSpan(0, file.Length).SequenceEqual(file), "The original bytes changed.");
        Assert.Equal("1.4", HeaderVersion(saved));
        Assert.Equal(new PdfVersion(1, 6), document.Version);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 4), reopened.HeaderVersion);
        Assert.Equal(new PdfVersion(1, 6), reopened.CatalogVersion);
        Assert.Equal(new PdfVersion(1, 6), reopened.Version);
        Assert.Single(reopened.Extensions);
        Assert.Empty(reopened.Diagnostics);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void Strict_mode_refuses_an_incremental_save_that_needs_a_later_version_before_writing()
    {
        // The extension is not read, so opening in strict mode succeeds; the save finds it.
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, LaterExtension, header: "%PDF-1.4"), new PdfOptions().UseStrict());
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Save(output, Options.WithIncrementalUpdate(true)));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Contains("XYZ_ developer extension", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
        Assert.Null(document.CatalogVersion);
    }

    public static TheoryData<PdfCrossReferenceLayout, bool> RoundTrips => new()
    {
        { PdfCrossReferenceLayout.Table, false },
        { PdfCrossReferenceLayout.Stream, false },
        { PdfCrossReferenceLayout.StreamWithObjectStreams, false },
        { PdfCrossReferenceLayout.Table, true },
    };

    [Theory]
    [MemberData(nameof(RoundTrips))]
    public void Catalog_version_extensions_keeps_its_extensions_on_round_trip(PdfCrossReferenceLayout layout, bool incremental)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("catalog-version-extensions.pdf"));
        string[] before = [.. document.Extensions.Select(Describe)];
        document.Catalog[new CosName("Lang")] = new CosString("en"u8);

        byte[] saved = Save(document, Options.WithCrossReferenceLayout(layout).WithIncrementalUpdate(incremental));

        Assert.Equal(["ADBE 1.7 3 - -", "ISO_ 2.0 32001 https://www.iso.org/standard/45874.html :2022"], before);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(before, reopened.Extensions.Select(Describe));
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void A_document_whose_catalog_version_covers_the_features_is_not_raised()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, "/Version /1.7", header: "%PDF-1.4"));

        byte[] saved = Save(document, Options.WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams));

        Assert.Empty(document.Diagnostics);
        Assert.Equal("1.5", HeaderVersion(saved));
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 7), reopened.Version);
    }

    [Fact]
    public void A_metadata_property_set_below_1_4_writes_no_metadata_stream_and_keeps_the_version()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, header: "%PDF-1.3"));
        document.Properties.Title = "Below 1.4";

        byte[] saved = Save(document, Options);

        Assert.Equal("1.3", HeaderVersion(saved));
        Assert.Empty(document.Diagnostics);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata);
        Assert.Equal("Below 1.4", reopened.Properties.Title);
    }

    public static TheoryData<string, int, int, string> AttachmentFeatures => new()
    {
        { "embedded file stream", 1, 7, "an embedded file stream (ISO 32000-2 §7.11.4) needs PDF 1.3" },
        { "unicode file name", 1, 7, "Unicode file name (UF) (ISO 32000-2 §7.11.3, Table 43) needs PDF 1.7" },
        { "embedded files tree", 1, 7, "the EmbeddedFiles name tree (ISO 32000-2 §7.7.4, Table 32) needs PDF 1.4" },
        { "description", 1, 7, "description (Desc) (ISO 32000-2 §7.11.3, Table 43) needs PDF 1.6" },
        { "relationship", 2, 0, "associated files (AF, AFRelationship) (ISO 32000-2 §14.13) needs PDF 2.0" },
        { "associated file", 2, 0, "associated files (AF, AFRelationship) (ISO 32000-2 §14.13) needs PDF 2.0" },
    };

    [Theory]
    [MemberData(nameof(AttachmentFeatures))]
    public void Each_attachment_feature_raises_a_pdf_1_2_document_to_its_minimum(string scenario, int major, int minor, string named)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.2"));
        byte[] contents = "attached"u8.ToArray();
        switch (scenario)
        {
            case "embedded file stream" or "unicode file name":
                document.CreateEmbeddedFile("a.txt", contents);
                break;
            case "embedded files tree":
                document.AddEmbeddedFile("a.txt", contents);
                break;
            case "description":
                document.CreateEmbeddedFile("a.txt", contents, new PdfEmbeddedFileOptions().WithDescription("A file"));
                break;
            case "relationship":
                document.CreateEmbeddedFile("a.txt", contents, new PdfEmbeddedFileOptions().WithRelationship(PdfFileRelationship.Data));
                break;
            default:
                document.AddAssociatedFile(document.Catalog, document.CreateEmbeddedFile("a.txt", contents));
                break;
        }

        byte[] saved = Save(document, Options);

        var expected = new PdfVersion(major, minor);
        Assert.Equal(expected.ToString(), HeaderVersion(saved));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(expected, reopened.Version);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
    }

    public static TheoryData<string, string, int, int, string> NavigationFeatures => new()
    {
        { "page labels", "1.2", 1, 3, "page labels (PageLabels) (ISO 32000-2 §12.4.2) needs PDF 1.3" },
        { "outline colour", "1.3", 1, 4, "colour or style flags (C, F) (ISO 32000-2 §12.3.3, Table 151) needs PDF 1.4" },
        { "outline flags", "1.3", 1, 4, "colour or style flags (C, F) (ISO 32000-2 §12.3.3, Table 151) needs PDF 1.4" },
        { "FitB destination", "1.0", 1, 1, "a FitB, FitBH or FitBV destination (ISO 32000-2 §12.3.2.2, Table 149) needs PDF 1.1" },
        { "named destination", "1.1", 1, 2, "the Dests name tree (destinations named by strings) (ISO 32000-2 §12.3.2.4) needs PDF 1.2" },
    };

    [Theory]
    [MemberData(nameof(NavigationFeatures))]
    public void Each_navigation_feature_raises_a_document_below_its_minimum(string scenario, string header, int major, int minor, string named)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-" + header));
        PdfPage page = document.Pages[0];
        switch (scenario)
        {
            case "page labels":
                document.CreatePageLabels().SetRange(0, PdfPageLabelStyle.LowercaseRoman);
                break;
            case "outline colour":
                document.CreateOutline().Add("Red").Color = new PdfRgbColor(1, 0, 0);
                break;
            case "outline flags":
                document.CreateOutline().Add("Bold").Flags = PdfOutlineItemFlags.Bold;
                break;
            case "FitB destination":
                document.CreateOutline().Add("Box").Destination = document.CreateDestination(page, PdfDestinationView.FitB);
                break;
            default:
                document.SetNamedDestination("start", document.CreateDestination(page, PdfDestinationView.Fit));
                break;
        }

        byte[] saved = Save(document, Options);

        var expected = new PdfVersion(major, minor);
        Assert.Equal(expected.ToString(), HeaderVersion(saved));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(expected, reopened.Version);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
    }

    [Fact]
    public void A_structure_element_in_the_pdf_2_0_namespace_raises_a_pdf_1_7_document_to_2_0_and_one_in_the_default_namespace_does_not()
    {
        using PdfDocument namespaced = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]"));
        namespaced.CreateStructureTree().AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf20));
        using PdfDocument plain = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]"));
        plain.CreateStructureTree().AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf17));

        byte[] raised = Save(namespaced, Options);
        byte[] kept = Save(plain, Options);

        Assert.Equal("2.0", HeaderVersion(raised));
        Assert.Contains(
            "structure namespaces or other PDF 2.0 structure entries (Namespaces, NS, Ref, AF, Phoneme, PronunciationLexicon) (ISO 32000-2 §14.7.4, Table 356; Tables 354 and 355) needs PDF 2.0",
            Assert.Single(namespaced.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message,
            StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(raised);
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
        Assert.Equal(PdfStructureNamespace.Pdf20, Assert.Single(reopened.StructureTree!.Elements).Namespace);
        Assert.Equal("1.7", HeaderVersion(kept));
        Assert.DoesNotContain(plain.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
    }

    [Fact]
    public void Strict_mode_refuses_a_structure_namespace_in_a_pdf_1_7_document_before_writing()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]"), new PdfOptions().UseStrict());
        document.CreateStructureTree().AddElement(new PdfStructureType("Document", PdfStructureNamespace.Pdf20));
        using var output = new MemoryStream();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Save(output, Options));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Contains("structure namespaces", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, output.Length);
    }

    public static TheoryData<string, string, int, int, string> GraphicsFeatures => new()
    {
        { "JBIG2 encoding", "1.3", 1, 4, "JBIG2Decode compression (ISO 32000-2 §7.4.7, Table 6) needs PDF 1.4" },
        { "JPX encoding", "1.4", 1, 5, "JPXDecode compression (ISO 32000-2 §7.4.9, Table 6) needs PDF 1.5" },
        { "transparent annotation flattened", "1.3", 1, 4, "transparency parameters (CA, ca, BM, AIS, TK) (ISO 32000-2 §11.6.3 and §11.6.4.4, Table 57) needs PDF 1.4" },
        { "transparent annotation flattened", "1.3", 1, 4, "a transparency group (Group) (ISO 32000-2 §11.6.6; Tables 31 and 93) needs PDF 1.4" },
        { "optional annotation flattened", "1.4", 1, 5, "optional content (OCG, OCMD, OC, OCProperties) (ISO 32000-2 §8.11; Table 29) needs PDF 1.5" },
        { "soft mask copied", "1.3", 1, 4, "a soft mask (SMask) (ISO 32000-2 §11.6.5; Tables 57 and 87) needs PDF 1.4" },
        { "annotation language copied", "1.7", 2, 0, "PDF 2.0 annotation entries (ca, BM, Lang, CA of a non-markup annotation, Projection and RichMedia types) (ISO 32000-2 §12.5.2, Table 166; §12.5.6.1, Table 171) needs PDF 2.0" },
    };

    /// <summary>
    /// Each transparency, filter, optional content and annotation feature (issue #299) raises a document below its minimum, through
    /// the API that writes it: <see cref="PdfDocument.EncodeStream"/> with a registered encoder, <see cref="PdfDocument.Flatten()"/>
    /// (whose graphics state, transparency group and <c>OC</c> sequence carry an annotation's opacity and optional content), and, for
    /// the features no API creates, a page copy (<see cref="PdfObjectCopier"/>).
    /// </summary>
    public static TheoryData<string, string, int, int, string> FormFeatures => new()
    {
        { "text field", "1.1", 1, 2, "an interactive form (AcroForm) with fields and widget annotations (ISO 32000-2 §12.7.3; §7.7.2, Table 29) needs PDF 1.2" },
        { "signature field", "1.2", 1, 3, "signature fields (FT Sig, SigFlags) (ISO 32000-2 §12.7.5.5; §12.7.3, Table 224) needs PDF 1.3" },
        { "alternate name", "1.2", 1, 3, "a field's alternative name (TU) (ISO 32000-2 §12.7.4.1, Table 226) needs PDF 1.3" },
        { "do not spell check", "1.3", 1, 4, "the field flags FileSelect, MultiSelect, DoNotSpellCheck or DoNotScroll" },
        { "multiple selection", "1.3", 1, 4, "the field flags FileSelect, MultiSelect, DoNotSpellCheck or DoNotScroll" },
        { "comb", "1.4", 1, 5, "the field flags Comb, RichText, RadiosInUnison or CommitOnSelChange" },
        { "radios in unison", "1.4", 1, 5, "the field flags Comb, RichText, RadiosInUnison or CommitOnSelChange" },
    };

    [Theory]
    [MemberData(nameof(FormFeatures))]
    public void Each_form_field_feature_raises_a_document_below_its_minimum(string scenario, string header, int major, int minor, string named)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-" + header));
        PdfPage page = document.Pages[0];
        var rect = new PdfRectangle(72, 700, 272, 724);
        PdfAcroForm form = document.CreateAcroForm();
        switch (scenario)
        {
            case "text field":
                form.AddTextField("name", page, rect);
                break;
            case "signature field":
                form.AddSignatureField("approval", page, rect);
                break;
            case "alternate name":
                form.AddTextField("name", page, rect, new PdfFieldOptions().WithAlternateName("Name"));
                break;
            case "do not spell check":
                form.AddTextField("name", page, rect, new PdfFieldOptions().WithFlags(PdfFieldFlags.DoNotSpellCheck));
                break;
            case "multiple selection":
                form.AddListBox("pick", page, rect, [new PdfChoiceOption("A")], new PdfFieldOptions().WithFlags(PdfFieldFlags.MultiSelect));
                break;
            case "comb":
                form.AddTextField("code", page, rect, new PdfFieldOptions().WithFlags(PdfFieldFlags.Comb).WithMaxLength(6));
                break;
            default:
                form.AddRadioButtons("pick", [new PdfRadioButton(page, rect, "A")], new PdfFieldOptions().WithFlags(PdfFieldFlags.RadiosInUnison));
                break;
        }

        byte[] saved = Save(document, Options.WithStreamCompression(PdfStreamCompression.None));

        var expected = new PdfVersion(major, minor);
        Assert.Equal(expected.ToString(), HeaderVersion(saved));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(expected, reopened.Version);
        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning);
    }

    [Theory]
    [MemberData(nameof(GraphicsFeatures))]
    public void Each_graphics_feature_raises_a_document_below_its_minimum(string scenario, string header, int major, int minor, string named)
    {
        using PdfDocument document = OpenForGraphicsFeature(scenario, header);
        UseGraphicsFeature(document, scenario);

        byte[] saved = Save(document, Options.WithStreamCompression(PdfStreamCompression.None));

        var expected = new PdfVersion(major, minor);
        Assert.Equal(expected.ToString(), HeaderVersion(saved));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(expected, reopened.Version);
    }

    [Theory]
    [MemberData(nameof(GraphicsFeatures))]
    public void Each_graphics_feature_keeps_a_document_already_at_its_minimum(string scenario, string header, int major, int minor, string named)
    {
        _ = (header, named);
        string version = new PdfVersion(major, minor).ToString();
        using PdfDocument document = OpenForGraphicsFeature(scenario, version);
        UseGraphicsFeature(document, scenario);

        byte[] saved = Save(document, Options.WithStreamCompression(PdfStreamCompression.None));

        Assert.Equal(version, HeaderVersion(saved));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised");
    }

    /// <summary>
    /// The document a <see cref="GraphicsFeatures"/> scenario starts from: for a flattening, a page with one square annotation drawn
    /// at half opacity, or in an optional content group; for an encoding, the encoder registered; otherwise one empty page.
    /// </summary>
    private static PdfDocument OpenForGraphicsFeature(string scenario, string header)
    {
        const string appearance = "<< /Type /XObject /Subtype /Form /BBox [0 0 100 50] /Resources << >> /Length 24 >>\nstream\n0 0 1 rg 0 0 100 50 re f\nendstream";
        switch (scenario)
        {
            case "JBIG2 encoding" or "JPX encoding":
                PdfOptions options = new PdfOptions().UseStreamEncoder(new CopyingEncoder(FilterFor(scenario)));
                return PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-" + header), options);
            case "transparent annotation flattened":
                return PdfDocument.Open(new Document.TestPdf { Header = "%PDF-" + header }.Build(
                    "<< /Type /Catalog /Pages 2 0 R >>",
                    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
                    "<< /Type /Annot /Subtype /Square /Rect [72 600 172 650] /CA 0.5 /AP << /N 5 0 R >> >>",
                    appearance));
            case "optional annotation flattened":
                return PdfDocument.Open(new Document.TestPdf { Header = "%PDF-" + header }.Build(
                    "<< /Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [6 0 R] /D << >> >> >>",
                    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
                    "<< /Type /Annot /Subtype /Square /Rect [72 600 172 650] /OC 6 0 R /AP << /N 5 0 R >> >>",
                    appearance,
                    "<< /Type /OCG /Name (Layer) >>"));
            default:
                return PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-" + header));
        }
    }

    /// <summary>Writes the scenario's feature into <paramref name="document"/> through the API that writes it.</summary>
    private static void UseGraphicsFeature(PdfDocument document, string scenario)
    {
        switch (scenario)
        {
            case "JBIG2 encoding" or "JPX encoding":
                var stream = new CosStream(new CosDictionary { [new CosName("Filter")] = FilterFor(scenario) }, ReadOnlyMemory<byte>.Empty);
                document.AddObject(stream);
                document.EncodeStream(stream, new byte[8]);
                break;
            case "transparent annotation flattened" or "optional annotation flattened":
                Assert.True(document.Flatten(new PdfFlattenOptions().WithAnnotations(true)));
                break;
            default:
                // No API creates a soft mask or a PDF 2.0 annotation entry; a page copy from a PDF 2.0 document carries them.
                using (PdfDocument source = PdfDocument.Open(Document.TestPdf.OnePage(
                    scenario == "soft mask copied"
                        ? "/MediaBox [0 0 612 792] /Resources << /ExtGState << /G0 << /Type /ExtGState /SMask /None >> >> >>"
                        : "/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Lang (en-GB) >>]",
                    header: "%PDF-2.0")))
                {
                    document.Pages.Add(new PdfObjectCopier(source, document).CopyPage(source.Pages[0]));
                }

                break;
        }
    }

    private static CosName FilterFor(string scenario) => new(scenario == "JPX encoding" ? "JPXDecode" : "JBIG2Decode");

    /// <summary>An encoder for a filter the library has no encoder for, which writes the data as it is.</summary>
    private sealed class CopyingEncoder(CosName name) : Broadside.Filters.IStreamEncoder
    {
        public CosName Name => name;

        public void Encode(ReadOnlySpan<byte> data, System.Buffers.IBufferWriter<byte> output, Broadside.Filters.StreamEncoderContext context) =>
            System.Buffers.BuffersExtensions.Write(output, data);
    }

    /// <summary>A catalog entry declaring a developer extension of PDF 1.6.</summary>
    private const string LaterExtension = "/Extensions << /XYZ_ << /BaseVersion /1.6 /ExtensionLevel 1 >> >>";

    /// <summary>encrypted-aes-gcm.pdf with its ISO/TS 32003 declaration changed to level 32009: AES-GCM used but not declared.</summary>
    private static byte[] UndeclaredGcm() => Encoding.Latin1.GetBytes(
        Encoding.Latin1.GetString(Corpus.Bytes("encrypted-aes-gcm.pdf")).Replace("/ExtensionLevel 32003", "/ExtensionLevel 32009", StringComparison.Ordinal));

    private static string Describe(PdfDeveloperExtension extension) =>
        $"{extension.Prefix.Value} {extension.BaseVersion} {extension.ExtensionLevel} {extension.Url?.ToString() ?? "-"} {extension.ExtensionRevision ?? "-"}";

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }

    /// <summary>The version the header states, read from the bytes (§7.5.2).</summary>
    private static string HeaderVersion(byte[] file)
    {
        string text = Encoding.Latin1.GetString(file, 0, 16);
        Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
        return text[5..8];
    }
}
