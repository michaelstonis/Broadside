using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #625: a file saved with the save-time image pass renders within a pixel tolerance of the original through the software
/// rasterizer, and is smaller. ISO 32000-2 §8.9.5.3 (an image reduced to the resolution it is shown at looks the same there).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class ImageOptimizationRenderTests
{
    [Theory]
    [InlineData(1.0, false)]
    [InlineData(2.0, false)]
    [InlineData(1.0, true)]
    public void The_optimized_corpus_file_renders_like_the_original_and_is_smaller(double scale, bool jpeg)
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("image-oversampled.pdf"));
        var optimization = new PdfImageOptimization().WithMaxResolution(150);
        if (jpeg)
        {
            optimization.WithJpegQuality(85).WithRecompressLossless();
        }

        byte[] optimized;
        using (PdfDocument document = PdfDocument.Open(original))
        using (var stream = new MemoryStream())
        {
            document.Save(stream, new PdfSaveOptions().WithImageOptimization(optimization));
            optimized = stream.ToArray();
        }

        Assert.True(optimized.Length < original.Length, $"{optimized.Length} bytes after, {original.Length} before.");
        using PdfDocument before = PdfDocument.Open(original);
        using PdfDocument after = PdfDocument.Open(optimized);
        Assert.Equal(150, after.Pages[0].ExtractImages()[0].Image.Width);

        using Bitmap expected = Render(before, scale);
        using Bitmap actual = Render(after, scale);

        (double psnr, int max) = Compare(expected, actual);
        Assert.True(psnr >= 35, $"PSNR {psnr:F1} dB, max {max}");
    }

    private static Bitmap Render(PdfDocument document, double scale)
    {
        PdfPage page = document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(scale);
        PixelSize size = options.GetPixelSize(page);
        var bitmap = new Bitmap(size.Width, size.Height);
        bitmap.Pixels.Fill(byte.MaxValue);
        using var rasterizer = new SoftwareRasterizer();
        rasterizer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));
        return bitmap;
    }

    private static (double Psnr, int Max) Compare(Bitmap a, Bitmap b)
    {
        ReadOnlySpan<byte> x = a.Pixels;
        ReadOnlySpan<byte> y = b.Pixels;
        double squared = 0;
        int max = 0;
        for (int i = 0; i < x.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                int difference = Math.Abs(x[i + c] - y[i + c]);
                squared += difference * difference;
                max = Math.Max(max, difference);
            }
        }

        double mse = squared / (x.Length / 4 * 3.0);
        return (mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), max);
    }
}
