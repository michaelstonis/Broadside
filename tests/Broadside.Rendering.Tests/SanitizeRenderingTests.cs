using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Sanitizing optional content (issue #648) on <c>optional-content.pdf</c>, rendered by the software rasterizer before and after
/// each mode and compared with reviewed snapshots: removing hidden layers, or flattening the visible ones, leaves the default view
/// pixel for pixel. ISO 32000-2 §8.11.2 to §8.11.4.
/// </summary>
public sealed class SanitizeRenderingTests
{
    public static TheoryData<string> Modes => new("Original", nameof(PdfSanitizeOptionalContent.Keep), nameof(PdfSanitizeOptionalContent.RemoveHidden), nameof(PdfSanitizeOptionalContent.FlattenVisible));

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Optional_content_pdf_renders_its_default_view_under_each_mode(string mode)
    {
        using var rasterizer = new SoftwareRasterizer();
        using PdfDocument original = await PdfDocument.OpenAsync(Corpus.Bytes("optional-content.pdf"), TestContext.Current.CancellationToken);
        using Bitmap expected = rasterizer.Render(original.Pages[0]);

        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes("optional-content.pdf"), TestContext.Current.CancellationToken);
        byte[] bytes = Corpus.Bytes("optional-content.pdf");
        if (mode != "Original")
        {
            PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithOptionalContent(Enum.Parse<PdfSanitizeOptionalContent>(mode)));
            using var output = new MemoryStream();
            await document.SaveAsync(output, result.CreateSaveOptions(), TestContext.Current.CancellationToken);
            bytes = output.ToArray();
        }

        using PdfDocument reopened = await PdfDocument.OpenAsync(bytes, TestContext.Current.CancellationToken);
        using Bitmap bitmap = rasterizer.Render(reopened.Pages[0]);
        using MemoryStream png = PngSnapshots.Encode(bitmap);
        using MemoryStream expectedPng = PngSnapshots.Encode(expected);
        Assert.Equal(PngSnapshots.Decode(expectedPng).Data, PngSnapshots.Decode(png).Data);
        png.Position = 0;
        await Verify(png, "png").UseParameters(mode);
    }
}
