using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.StructureTree;

/// <summary>
/// Replacing text updates the replacement text that describes it (issue #641): an <c>ActualText</c> on the enclosing marked content
/// or on the structure element has the old text replaced; an entry that does not hold it is removed and recorded. ISO 32000-2
/// §14.9.3 and §14.9.4.
/// </summary>
public sealed class ReplacementTextUpdateTests
{
    [Fact]
    public void The_ActualText_of_the_enclosing_marked_content_is_updated()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Name: ) Tj /Span << /ActualText (Smith) >> BDC (Smith) Tj EMC ET");
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(TextReplaceTests.Single(page, "Smith"), "Jones");

        Assert.Equal(1, result.Replaced);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Name: Jones", TextReplaceTests.Text(page));
        string content = RedactionSamples.Content(document, page);
        Assert.DoesNotContain("Smith", content, StringComparison.Ordinal);
        Assert.Contains("/ActualText", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marked_content_entry_without_the_old_text_is_removed_with_a_diagnostic()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td /Span << /Alt (A greeting) /Lang (en) >> BDC (Hello) Tj EMC ET");
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(TextReplaceTests.Single(page, "Hello"), "Bye");

        Assert.Equal(TextEditDiagnostics.TextEditReplacementTextRemoved.Name, Assert.Single(result.Diagnostics).Code);
        string content = RedactionSamples.Content(document, page);
        Assert.DoesNotContain("/Alt", content, StringComparison.Ordinal);
        Assert.Contains("/Lang", content, StringComparison.Ordinal);
        Assert.Equal("Bye", TextReplaceTests.Text(page));
    }

    [Fact]
    public void The_ActualText_of_the_structure_element_is_updated()
    {
        using PdfDocument document = PdfDocument.Open(Tagged("/ActualText (Hello World)"));
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(TextReplaceTests.Single(page, "World"), "There");

        Assert.Equal(1, result.Replaced);
        Assert.Empty(result.Diagnostics);
        PdfStructureElement element = Paragraph(document);
        Assert.Equal("Hello There", element.ActualText);
        Assert.Equal("Hello There", TextReplaceTests.Text(page));
    }

    [Fact]
    public void A_structure_element_entry_without_the_old_text_is_removed_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Tagged("/Alt (A greeting) /E (Hello Earth)"));
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(TextReplaceTests.Single(page, "World"), "There");

        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == TextEditDiagnostics.TextEditReplacementTextRemoved.Name));
        PdfStructureElement element = Paragraph(document);
        Assert.Null(element.AlternateDescription);
        Assert.Null(element.Expansion);
        Assert.Equal("Hello There", TextReplaceTests.Text(page));
    }

    [Fact]
    public void Updating_can_be_turned_off()
    {
        using PdfDocument document = PdfDocument.Open(Tagged("/Alt (Hello World)"));
        PdfPage page = document.Pages[0];

        PdfTextEditResult result = page.ReplaceText(TextReplaceTests.Single(page, "World"), "There", new PdfTextReplaceOptions().WithUpdateReplacementText(false));

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Hello World", Paragraph(document).AlternateDescription);
    }

    private static PdfStructureElement Paragraph(PdfDocument document) =>
        document.StructureTree!.Elements.Single(static element => element.StructureType?.Value == "P");

    /// <summary>A tagged page whose <c>P</c> element, with <paramref name="entries"/>, owns MCID 0: "Hello World" in Helvetica.</summary>
    private static byte[] Tagged(string entries) => ContentSamples.File(
    [
        "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 6 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /StructParents 0 >>",
        ContentSamples.Stream("/P <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (Hello World) Tj ET EMC"),
        RedactionSamples.Helvetica,
        "<< /Type /StructTreeRoot /K [7 0 R] /ParentTree << /Nums [0 [8 0 R]] >> >>",
        "<< /Type /StructElem /S /Document /P 6 0 R /K [8 0 R] >>",
        $"<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K 0 {entries} >>",
    ]);
}
