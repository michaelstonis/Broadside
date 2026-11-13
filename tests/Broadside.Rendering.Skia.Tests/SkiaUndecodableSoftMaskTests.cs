using Broadside.TestSupport;
using SkiaSharp;

namespace Broadside.Rendering.Skia.Tests;

/// <summary>
/// Issue #515, on Skia: a soft mask (ISO 32000-2 §11.6.5.2) kept encoded whose data cannot be decoded when it is drawn masks
/// nothing, folded into its image's pixels or drawn on its own grid, at the image's resolution or a reduction: the image is painted
/// as it is without the soft mask, as the software rasterizer paints it, and the codec's reason is a diagnostic of the document. An
/// image that cannot be decoded paints nothing (§8.9.5).
/// </summary>
public sealed class SkiaUndecodableSoftMaskTests
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

    [Theory]
    [MemberData(nameof(Pages))]
    public void An_image_whose_soft_mask_cannot_be_decoded_is_painted_unmasked(string page, bool scaled)
    {
        // At an eighth the 256 x 256 image is drawn 64 pixels square: a viewer render reduces it (and a soft mask of its size) by half.
        using PdfDocument masked = PdfDocument.Open(UndecodableSoftMaskPages.Page(page));
        using PdfDocument unmasked = PdfDocument.Open(UndecodableSoftMaskPages.Page(page, masked: false));

        byte[] actual = Render(masked, 0.125, scaled);
        byte[] expected = Render(unmasked, 0.125, scaled);

        Assert.Contains(expected, b => b != byte.MaxValue);
        Assert.Equal(expected, actual);
        Assert.Contains(UndecodableSoftMaskPages.DiagnosticCode(page), masked.Diagnostics.Select(d => d.Code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_image_that_cannot_be_decoded_paints_nothing(bool scaled)
    {
        using PdfDocument document = PdfDocument.Open(UndecodableSoftMaskPages.UndecodableImagePage());

        byte[] actual = Render(document, 0.125, scaled);

        Assert.All(actual, b => Assert.Equal(byte.MaxValue, b));
        Assert.Contains("DctTableMissing", document.Diagnostics.Select(d => d.Code));
    }

    /// <summary>A list render on white, as a viewer's tile is drawn.</summary>
    private static byte[] Render(PdfDocument document, double scale, bool scaled)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var backend = new SkiaBackend { ScaledImageDecode = scaled };
        backend.Render(page.CreateDisplayList(), canvas, options.GetDeviceTransform(page));
        return bitmap.Bytes;
    }
}
