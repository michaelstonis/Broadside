using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.TestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// A tagged document stamped on its existing pages (issue #627) still passes the PDF/UA-2 save checker: stamps and headers and
/// footers are artifacts (ISO 32000-2 §14.8.2.2), drawn in an embedded font, so they add no finding.
/// </summary>
public sealed class StampPdfUATests
{
    [Fact]
    public void A_stamped_tagged_document_saves_as_pdf_ua_2_with_nothing_new_to_report()
    {
        string[] unstamped = Findings(stamp: false);
        string[] stamped = Findings(stamp: true);

        Assert.Equal(unstamped, stamped);
    }

    private static string[] Findings(bool stamp)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("tagged-structure.pdf"));
        if (stamp)
        {
            PdfCanvasFont font = document.EmbedFont(Standard14FontResolver.GetProgram(Standard14Font.Helvetica));
            document.Stamp(PdfStamp.FromText("DRAFT", font, 60).WithOpacity(0.2).WithRotation(30));
            document.AddHeaderFooter(new PdfHeaderFooter(font, 9) { FooterRight = "{page}/{pages}" });
        }

        int before = document.Diagnostics.Count;
        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA2));
        return [.. document.Diagnostics.Skip(before).Select(found => $"{found.Code}: {found.Message}").Order(StringComparer.Ordinal)];
    }
}
