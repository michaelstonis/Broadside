using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #515: a soft mask (ISO 32000-2 §11.6.5.2) kept encoded whose data cannot be decoded when it is drawn masks nothing, so the
/// image is painted unmasked, as when the soft mask could not be decoded when the list was built (before #514); the codec's reason
/// is a diagnostic of the document. An image that cannot be decoded paints nothing (§8.9.5). The other backends' suites assert the
/// same on their own renders.
/// </summary>
public sealed class UndecodableSoftMaskTests
{
    public static TheoryData<string, bool> Pages()
    {
        var data = new TheoryData<string, bool>();
        foreach (string page in UndecodableSoftMaskPages.Names)
        {
            data.Add(page, false);
            data.Add(page, true);
        }

        return data;
    }

    public static TheoryData<string> Names => [.. UndecodableSoftMaskPages.Names];

    [Theory]
    [MemberData(nameof(Names))]
    public void The_soft_mask_is_kept_encoded_and_fails_only_when_drawn(string page)
    {
        using PdfDocument document = PdfDocument.Open(UndecodableSoftMaskPages.Page(page));
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        DisplayImage mask = Assert.Single(recorder.Images).Image.SoftMask!;
        string code = UndecodableSoftMaskPages.DiagnosticCode(page);

        Assert.True(mask.MaxReduction > 0, $"{page}: the soft mask is decoded at build");
        Assert.DoesNotContain(code, document.Diagnostics.Select(d => d.Code));
        Assert.Null(mask.GetSamplesAt(0, TestContext.Current.CancellationToken));
        Assert.Contains(code, document.Diagnostics.Select(d => d.Code));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void An_image_whose_soft_mask_cannot_be_decoded_is_painted_unmasked(string page, bool scaled)
    {
        // At an eighth the 256 x 256 image is drawn 64 pixels square: a viewer render reduces it (and a soft mask of its size) by half.
        using PdfDocument masked = PdfDocument.Open(UndecodableSoftMaskPages.Page(page));
        using PdfDocument unmasked = PdfDocument.Open(UndecodableSoftMaskPages.Page(page, masked: false));

        using Bitmap actual = Render(masked, 0.125, scaled);
        using Bitmap expected = Render(unmasked, 0.125, scaled);

        Assert.True(expected.Pixels.ToArray().Any(b => b != byte.MaxValue), "The image is painted.");
        Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        Assert.Contains(UndecodableSoftMaskPages.DiagnosticCode(page), masked.Diagnostics.Select(d => d.Code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_image_that_cannot_be_decoded_paints_nothing(bool scaled)
    {
        using PdfDocument document = PdfDocument.Open(UndecodableSoftMaskPages.UndecodableImagePage());

        using Bitmap actual = Render(document, 0.125, scaled);

        Assert.All(actual.Pixels.ToArray(), b => Assert.Equal(byte.MaxValue, b));
        Assert.Contains("DctTableMissing", document.Diagnostics.Select(d => d.Code));
    }

    /// <summary>A list render on white, as a viewer's tile is drawn.</summary>
    private static Bitmap Render(PdfDocument document, double scale, bool scaled)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        var bitmap = new Bitmap(size.Width, size.Height);
        bitmap.Pixels.Fill(byte.MaxValue);
        using var rasterizer = new SoftwareRasterizer { ScaledImageDecode = scaled };
        rasterizer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));
        return bitmap;
    }
}
