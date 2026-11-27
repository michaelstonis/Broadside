using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Stamps are artifacts (ISO 32000-2 §14.8.2.2), so text extraction leaves them out by default and includes them on request (issue
/// #627). The PDF/UA-2 check of a stamped tagged document is <c>Broadside.Layout.Tests.StampPdfUATests</c>.
/// </summary>
public sealed class StampExtractionTests
{
    [Fact]
    public void Extracted_text_leaves_stamps_out_by_default_and_includes_them_with_artifacts()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginText();
            canvas.SetFont(font, 12);
            canvas.MoveText(72, 700);
            canvas.ShowText("Body text");
            canvas.EndText();
        }

        document.Stamp(PdfStamp.FromText("CONFIDENTIAL", font, 48).WithRotation(45).WithOpacity(0.3));
        document.Stamp(PdfStamp.FromText("Under", font, 20).WithLayer(PdfStampLayer.Background).WithVerticalAlignment(PdfStampAlignment.End));
        document.AddHeaderFooter(new PdfHeaderFooter(font, 9) { FooterCenter = "Page {page} of {pages}" });

        using PdfDocument reopened = PdfDocument.Open(Graphics.CanvasTests.Saved(document));
        string plain = reopened.Pages[0].ExtractText().Text;
        string all = reopened.Pages[0].ExtractText(new TextExtractionOptions().WithArtifacts()).Text;

        Assert.Contains("Body text", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("CONFIDENTIAL", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Under", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Page 1", plain, StringComparison.Ordinal);
        Assert.Contains("CONFIDENTIAL", all, StringComparison.Ordinal);
        Assert.Contains("Under", all, StringComparison.Ordinal);
        Assert.Contains("Page 1 of 1", all, StringComparison.Ordinal);
        Assert.Empty(reopened.Diagnostics);
    }
}
