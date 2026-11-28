using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>
/// Saving as PDF/A-4e (issue #650; ISO 19005-4, Annex B): PDF/A-4 that keeps 3D and RichMedia annotations, identified with
/// <c>pdfaid:conformance</c> E. The 3D content is kept as stored, never rendered; a 3D stream that is not U3D or PRC is reported
/// (veraPDF PDF/A-4E rule 6.1.6.1-3). veraPDF validates the output in <see cref="PdfALevelAVeraPdfTests"/>.
/// </summary>
public sealed class PdfA4eConformanceTests
{
    private const string NonConformant = "PdfANonConformant";

    [Fact]
    public void A_3d_annotation_is_kept_as_stored_and_the_file_is_identified_as_4e()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));
        byte[] stored = ThreeDData(document);

        byte[] saved = PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4e);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        using PdfDocument reopened = PdfDocument.Open(saved);
        string packet = PdfA4ConformanceTests.Packet(reopened);
        Assert.Contains("<pdfaid:part>4</pdfaid:part>", packet, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>E</pdfaid:conformance>", packet, StringComparison.Ordinal);
        Assert.Equal(stored, ThreeDData(reopened));
    }

    [Fact]
    public void Plain_pdf_a_4_forbids_the_3d_annotation_that_4e_allows()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("a 3D annotation, which PDF/A forbids (veraPDF rule 6.3.1-1,", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_3d_stream_that_is_neither_u3d_nor_prc_breaks_rule_6_1_6_1_3()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));
        ThreeDStream(document).Dictionary[new CosName("Subtype")] = new CosName("STL");

        PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA4e);

        Diagnostic reported = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant);
        Assert.Contains("veraPDF rule 6.1.6.1-3,", reported.Message, StringComparison.Ordinal);
        Assert.Contains("STL", reported.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Converting_to_4e_keeps_the_3d_annotation_and_converting_to_4_removes_it()
    {
        using PdfDocument engineering = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));

        PdfAConversion kept = engineering.ConvertToPdfA(PdfAConformance.PdfA4e);
        PdfAConversion removed = plain.ConvertToPdfA(PdfAConformance.PdfA4);

        Assert.True(kept.IsConformant);
        Assert.Single(engineering.Pages[0].Annotations);
        Assert.True(removed.IsConformant);
        Assert.Empty(plain.Pages[0].Annotations);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA4e, false)]
    [InlineData(PdfAConformance.PdfA4, true)]
    public void A_goto_3d_view_action_is_allowed_only_in_4e(PdfAConformance conformance, bool reported)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("pdfa4e-3d.pdf"));
        document.Catalog[new CosName("OpenAction")] = new CosDictionary
        {
            [new CosName("S")] = new CosName("GoTo3DView"),
            [new CosName("TA")] = document.Pages[0].Annotations[0].Reference!,
            [new CosName("V")] = new CosName("F"),
        };

        PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.Equal(reported, document.Diagnostics.Any(diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains("GoTo3DView", StringComparison.Ordinal)));
    }

    private static CosStream ThreeDStream(PdfDocument document)
    {
        var annotation = (CosDictionary)document.Resolve(((CosArray)document.Resolve(document.Pages[0].Dictionary[new CosName("Annots")]))[0]);
        return (CosStream)document.Resolve(annotation[new CosName("3DD")]);
    }

    private static byte[] ThreeDData(PdfDocument document) => document.DecodeStream(ThreeDStream(document)).ToArray();
}
