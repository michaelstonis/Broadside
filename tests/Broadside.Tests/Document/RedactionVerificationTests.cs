using Broadside.Graphics;
using Broadside.Tests.Content;
using Broadside.TestSupport;

namespace Broadside.Tests.Document;

/// <summary>
/// Redacted content is gone from the saved bytes, not covered (issue #624): after a save with
/// <see cref="PdfRedactionResult.CreateSaveOptions"/> and a reopen, text and image extraction over each area return nothing, and no
/// stream of the file, decoded, holds the redacted text.
/// </summary>
public sealed class RedactionVerificationTests
{
    private static readonly PdfRectangle SecretBox = new(70, 690, 106, 715);

    [Fact]
    public void Text_removed_by_a_redact_annotation_is_gone_from_the_saved_file()
    {
        using PdfDocument document = RedactionSamples.Open(
            "BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET BT /F1 12 Tf 3 Tr 72 650 Td (Secret) Tj ET BT /F1 12 Tf 72 600 Td [(Sec) -10 (ret)] TJ ET");
        PdfPage page = document.Pages[0];
        page.AddRedactAnnotation(new PdfRectangle(70, 590, 106, 715));

        PdfRedactionResult result = document.Redact();
        byte[] saved = RedactionSamples.Save(document, result);

        RedactionSamples.AssertGone(saved, "Secret");
        RedactionSamples.AssertGone(saved, "Sec");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(string.Empty, RedactionSamples.TextIn(reopened.Pages[0], new PdfRectangle(70, 590, 106, 715)));
        Assert.Equal("Public", reopened.Pages[0].ExtractText().Text.Trim());
        Assert.Empty(reopened.Pages[0].Annotations);
    }

    [Fact]
    public void An_image_under_an_area_is_gone_from_the_saved_file()
    {
        using PdfDocument document = RedactionSamples.Open(
            "q 30 0 0 20 72 690 cm /Im0 Do Q",
            "/XObject << /Im0 6 0 R >>",
            ContentSamples.Stream("SecretPixels", "/Type /XObject /Subtype /Image /Width 12 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8"));
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([Quad.FromRectangle(Matrix.Identity, SecretBox.Left, SecretBox.Bottom, SecretBox.Right, SecretBox.Top)]);
        byte[] saved = RedactionSamples.Save(document, result);

        RedactionSamples.AssertGone(saved, "SecretPixels");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Empty(reopened.Pages[0].ExtractImages());
    }

    [Fact]
    public void The_original_content_stream_is_not_written_by_a_full_save()
    {
        using PdfDocument document = RedactionSamples.Open("BT /F1 12 Tf 72 700 Td (Secret Public) Tj ET");
        PdfPage page = document.Pages[0];

        PdfRedactionResult result = page.Redact([Quad.FromRectangle(Matrix.Identity, SecretBox.Left, SecretBox.Bottom, SecretBox.Right, SecretBox.Top)]);
        byte[] saved = RedactionSamples.Save(document, result);

        RedactionSamples.AssertGone(saved, "Secret");
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(string.Empty, RedactionSamples.TextIn(reopened.Pages[0], SecretBox));
        Assert.False(result.CreateSaveOptions().IncrementalUpdate);
        Assert.True(result.CreateSaveOptions().RemoveUnreachableObjects);
    }
}
