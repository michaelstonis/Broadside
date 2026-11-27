using Broadside.Diagnostics;
using Broadside.Tests.Files;

namespace Broadside.Tests.Writing;

/// <summary>
/// Factur-X and ZUGFeRD 2.x e-invoices (issue #630) saved as PDF/A-3b and PDF/A-3u, validated by veraPDF's profiles of the same
/// flavour: the embedded CII XML as an associated file (ISO 19005-3 §6.8) and the <c>fx</c> XMP properties with their extension
/// schema (§6.6). Skipped when veraPDF is absent, as the other veraPDF tests (<see cref="VeraPdf"/>).
/// </summary>
[Trait("Category", "VeraPdf")]
public sealed class InvoiceVeraPdfTests
{
    [Theory]
    [InlineData(PdfAConformance.PdfA3b, "3b", PdfInvoiceProfile.EN16931)]
    [InlineData(PdfAConformance.PdfA3u, "3u", PdfInvoiceProfile.Minimum)]
    [InlineData(PdfAConformance.PdfA3b, "3b", PdfInvoiceProfile.XRechnung)]
    public void An_e_invoice_saved_as_pdf_a_3_passes_verapdf(PdfAConformance conformance, string flavour, PdfInvoiceProfile profile)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Properties.Title = "Invoice INV-42";
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        document.AttachInvoice(InvoiceAttachmentTests.Invoice(), new PdfInvoiceOptions().WithProfile(profile));

        byte[] saved = PdfA2u3bConformanceTests.Save(document, conformance);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, flavour));
    }

    [Fact]
    public void An_e_invoice_attached_twice_and_updated_incrementally_passes_verapdf()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfA2u3bConformanceTests.DrawRgbSquare(document);
        document.AttachInvoice(InvoiceAttachmentTests.Invoice("first"));
        using PdfDocument reopened = PdfDocument.Open(PdfA2u3bConformanceTests.Save(document, PdfAConformance.PdfA3b));
        reopened.AttachInvoice(InvoiceAttachmentTests.Invoice("second"), new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.Basic));

        byte[] updated = PdfA2u3bConformanceTests.Save(reopened, PdfAConformance.PdfA3b, incremental: true);

        Assert.DoesNotContain(reopened.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(updated, "3b"));
    }
}
