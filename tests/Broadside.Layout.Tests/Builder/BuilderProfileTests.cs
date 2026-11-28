using System.Text;
using Broadside.Diagnostics;
using Broadside.Layout.Flow;
using Broadside.Tests.Writing;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// Declared PDF/UA and PDF/A conformance in the document builder (issue #397, B8): each precondition is a diagnostic without a profile
/// and an error at <c>Create</c> with one, the profile picks the structure namespace and becomes the document's default save options, and
/// attached files are embedded and associated with the document.
/// </summary>
public sealed class BuilderProfileTests
{
    [Fact]
    public void A_missing_language_is_a_diagnostic_without_a_profile_and_an_error_under_PDF_UA()
    {
        using (PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").Content(c => c.Paragraph("x"))))
        {
            Assert.Contains(pdf.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.DocumentLanguageMissing.Name);
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            PdfDocument.Create(doc => doc.WithTitle("t").WithPdfUAConformance(PdfUAConformance.PdfUA1).Content(c => c.Paragraph("x"))));
        Assert.Contains("WithLanguage", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_title_is_a_diagnostic_without_a_profile_and_an_error_under_PDF_UA()
    {
        using (PdfDocument pdf = PdfDocument.Create(doc => doc.WithLanguage("en").Content(c => c.Paragraph("x"))))
        {
            Assert.Contains(pdf.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.DocumentTitleMissing.Name);
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            PdfDocument.Create(doc => doc.WithLanguage("en").WithPdfUAConformance(PdfUAConformance.PdfUA2).Content(c => c.Paragraph("x"))));
        Assert.Contains("WithTitle", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_error_names_every_unmet_precondition_at_once()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PdfDocument.Create(doc => doc.WithPdfUAConformance(PdfUAConformance.PdfUA1).WithTagging(false).Content(c => c.Paragraph("x"))));

        Assert.Contains("WithLanguage", error.Message, StringComparison.Ordinal);
        Assert.Contains("WithTitle", error.Message, StringComparison.Ordinal);
        Assert.Contains("WithTagging(false)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Untagged_output_is_allowed_without_a_profile_and_refused_under_PDF_UA()
    {
        using (PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithTagging(false).Content(c => c.Paragraph("x"))))
        {
            Assert.Null(pdf.StructureTree);
            Assert.Empty(pdf.Diagnostics);
        }

        Assert.Throws<InvalidOperationException>(() =>
            PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithTagging(false).WithPdfUAConformance(PdfUAConformance.PdfUA2).Content(c => c.Paragraph("x"))));
    }

    [Fact]
    public void A_skipped_heading_level_is_a_diagnostic_without_a_profile_and_under_PDF_UA_2_and_an_error_under_PDF_UA_1()
    {
        static void Skipping(ContainerBuilder c)
        {
            c.Heading(1, "Title");
            c.Heading(3, "Too deep");
        }

        using (PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").Content(Skipping)))
        {
            Assert.Contains(pdf.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.HeadingLevelSkipped.Name);
        }

        using (PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithPdfUAConformance(PdfUAConformance.PdfUA2).Content(Skipping)))
        {
            Assert.Contains(pdf.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.HeadingLevelSkipped.Name);
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithPdfUAConformance(PdfUAConformance.PdfUA1).Content(Skipping)));
        Assert.Contains("Too deep", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_attached_file_is_allowed_without_a_profile_and_at_PDF_A_3_and_refused_at_PDF_A_2_and_PDF_A_4()
    {
        static void Attaching(DocumentBuilder doc, PdfAConformance level) =>
            doc.WithTitle("t").WithLanguage("en").WithPdfAConformance(level).AttachFile("data.csv", "a,b\n1,2\n", PdfFileRelationship.Data).Content(c => c.Paragraph("x"));

        using (PdfDocument none = PdfDocument.Create(doc => Attaching(doc, PdfAConformance.None)))
        using (PdfDocument a3 = PdfDocument.Create(doc => Attaching(doc, PdfAConformance.PdfA3u)))
        using (PdfDocument a4f = PdfDocument.Create(doc => Attaching(doc, PdfAConformance.PdfA4f)))
        {
            Assert.Single(none.EmbeddedFiles);
            Assert.Single(a3.EmbeddedFiles);
            Assert.Single(a4f.EmbeddedFiles);
        }

        Assert.Contains("data.csv", Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => Attaching(doc, PdfAConformance.PdfA2u))).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => Attaching(doc, PdfAConformance.PdfA4)));
    }

    [Fact]
    public void PDF_UA_and_PDF_A_on_different_base_versions_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en")
            .WithPdfUAConformance(PdfUAConformance.PdfUA1).WithPdfAConformance(PdfAConformance.PdfA4).Content(c => c.Paragraph("x"))));
        Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en")
            .WithPdfUAConformance(PdfUAConformance.PdfUA2).WithPdfAConformance(PdfAConformance.PdfA2u).Content(c => c.Paragraph("x"))));
    }

    [Fact]
    public void Appending_to_an_untagged_document_is_a_diagnostic_without_a_profile_and_an_error_under_PDF_UA()
    {
        using (PdfDocument untagged = PdfDocument.Create())
        {
            untagged.AppendPages(doc => doc.Content(c => c.Paragraph("Schedule A")));
            Assert.Contains(untagged.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.AppendedToUntaggedDocument.Name);
        }

        using PdfDocument other = PdfDocument.Create();
        Assert.Throws<InvalidOperationException>(() => other.AppendPages(doc => doc
            .WithTitle("t").WithLanguage("en").WithPdfUAConformance(PdfUAConformance.PdfUA1).Content(c => c.Paragraph("Schedule A"))));
    }

    [Theory]
    [InlineData(PdfUAConformance.PdfUA1, PdfAConformance.None, false)]
    [InlineData(PdfUAConformance.None, PdfAConformance.PdfA2u, false)]
    [InlineData(PdfUAConformance.None, PdfAConformance.PdfA3b, false)]
    [InlineData(PdfUAConformance.PdfUA2, PdfAConformance.None, true)]
    [InlineData(PdfUAConformance.None, PdfAConformance.PdfA4, true)]
    public void The_profile_chooses_the_structure_namespace(PdfUAConformance ua, PdfAConformance a, bool pdf20)
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en")
            .WithPdfUAConformance(ua).WithPdfAConformance(a).Content(c => c.Paragraph("x")));

        string bytes = Encoding.Latin1.GetString(Save(pdf));
        Assert.Equal(pdf20, bytes.Contains("http://iso.org/pdf2/ssn", StringComparison.Ordinal));
    }

    [Fact]
    public void The_profile_becomes_the_default_save_options_and_explicit_options_win_with_a_diagnostic()
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("Profiles").WithLanguage("en-GB")
            .WithPdfUAConformance(PdfUAConformance.PdfUA1).WithPdfAConformance(PdfAConformance.PdfA2u).Content(c =>
            {
                c.Heading(1, "Profiles");
                c.Paragraph("Saved as PDF/UA-1 and PDF/A-2u without asking twice.");
            }));

        Assert.Equal(PdfUAConformance.PdfUA1, pdf.DefaultSaveOptions!.PdfUAConformance);
        Assert.Equal(PdfAConformance.PdfA2u, pdf.DefaultSaveOptions.PdfAConformance);
        byte[] saved = Save(pdf);
        Assert.DoesNotContain(pdf.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using (PdfDocument reopened = PdfDocument.Open(saved))
        {
            Assert.Equal(1, reopened.Metadata!.Packet!.PdfUAPart);
            Assert.Equal(2, reopened.Metadata.Packet.PdfAPart);
        }

        FlowBuildingBlockTests.AssertQpdfPasses(saved);

        using PdfDocument other = PdfDocument.Create(Options(), doc => doc.WithTitle("Profiles").WithLanguage("en-GB")
            .WithPdfUAConformance(PdfUAConformance.PdfUA1).Content(c => c.Paragraph("Saved plainly on request.")));
        using var plain = new MemoryStream();
        other.Save(plain, new PdfSaveOptions());
        Assert.Contains(other.Diagnostics, diagnostic => diagnostic.Code == SavingDiagnostics.ConformanceOverridden.Name);
        using PdfDocument plainCopy = PdfDocument.Open(plain.ToArray());
        Assert.Null(plainCopy.Metadata?.Packet?.PdfUAPart);
    }

    [Fact]
    public void An_attached_file_is_embedded_listed_and_associated_with_the_document_and_saves_as_PDF_A_3()
    {
        const string xml = "<?xml version=\"1.0\"?><Invoice><ID>2026-0042</ID></Invoice>";
        static PdfDocument Build() => PdfDocument.Create(Options(), doc => doc
            .WithTitle("Invoice 2026-0042")
            .WithLanguage("en-GB")
            .WithPdfAConformance(PdfAConformance.PdfA3u)
            .AttachFile("factur-x.xml", xml, PdfFileRelationship.Alternative)
            .Content(c => c.Heading(1, "Invoice")));

        using PdfDocument pdf = Build();
        using PdfDocument again = Build();
        byte[] saved = Save(pdf);
        Assert.DoesNotContain(pdf.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfFileSpecification file = Assert.Single(reopened.AssociatedFiles);
        Assert.Equal("factur-x.xml", file.FileName);
        Assert.Equal(PdfFileRelationship.Alternative, file.Relationship);
        Assert.Equal("text/xml", file.EmbeddedFile!.MediaType);
        Assert.Equal(xml, Encoding.UTF8.GetString(file.EmbeddedFile.Decode().Span));
        Assert.Equal("factur-x.xml", Assert.Single(reopened.EmbeddedFiles).Name);
        Assert.Equal(3, reopened.Metadata!.Packet!.PdfAPart);
        Assert.Equal(saved, Save(again));
        FlowBuildingBlockTests.AssertQpdfPasses(saved);
    }

    [Fact]
    public void Declaring_a_profile_refuses_undefined_values_at_the_call()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.WithPdfUAConformance((PdfUAConformance)9)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.WithPdfAConformance((PdfAConformance)99)));
        Assert.Throws<ArgumentException>(() => PdfDocument.Create(doc => doc.AttachFile("", "x", PdfFileRelationship.Data)));
    }

    [Theory]
    [Trait("Category", "VeraPdf")]
    [InlineData(PdfUAConformance.PdfUA1, PdfAConformance.None, "ua1")]
    [InlineData(PdfUAConformance.PdfUA2, PdfAConformance.None, "ua2")]
    [InlineData(PdfUAConformance.None, PdfAConformance.PdfA2u, "2u")]
    [InlineData(PdfUAConformance.None, PdfAConformance.PdfA4, "4")]
    public void The_report_and_the_invoice_pass_verapdf_under_each_profile(PdfUAConformance ua, PdfAConformance a, string flavour)
    {
        var files = new List<byte[]>();
        foreach (PdfDocument document in new[] { BuilderSampleTests.FieldReport(ua, a), BuilderSampleTests.Invoice(ua, a) })
        {
            using (document)
            {
                byte[] saved = Save(document);
                Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
                FlowBuildingBlockTests.AssertQpdfPasses(saved);
                files.Add(saved);
            }
        }

        foreach (byte[] saved in files)
        {
            Assert.Empty(VeraPdf.FailedRules(saved, flavour));
        }
    }
}
