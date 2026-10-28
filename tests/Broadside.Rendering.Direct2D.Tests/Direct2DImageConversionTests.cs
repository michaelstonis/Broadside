using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Rendering.Direct2D.Tests;

/// <summary>
/// Issue #428 part 5: the managed half of the Direct2D backend's scaled image decode, which needs no Direct2D device (so these tests
/// also run off Windows): converting reduced samples, reducing masks by their own rule, the reduced grid's geometry and the
/// converted-image cache keyed by the delivered reduction.
/// </summary>
public sealed class Direct2DImageConversionTests
{
    [Fact]
    public void A_reduced_image_converts_its_reduced_samples_with_a_soft_mask_of_the_same_reduction_folded()
    {
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page("soft-mask-same-size"));
        (DisplayImage image, PdfColorConverter converter) = FirstImage(document);
        DisplayImageSamples samples = image.GetSamplesAt(2, TestContext.Current.CancellationToken)!.Value;

        bool folds = ImageConverter.FoldsSoftMask(image, samples, out DisplayImageSamples? soft);
        using PixelImage pixels = ImageConverter.Convert(image, samples, folds, soft, foldMask: false, converter, null)!;
        using PixelImage alpha = ImageConverter.ConvertSoftMask(image.SoftMask!, soft!.Value)!;

        Assert.True(folds);
        Assert.Equal(2, samples.Reduction);
        Assert.Equal((256, 256, 4), (pixels.Width, pixels.Height, pixels.Channels));
        Assert.Equal((256, 256, 1), (alpha.Width, alpha.Height, alpha.Channels));
        for (int i = 0; i < alpha.Data.Length; i++)
        {
            Assert.Equal(alpha.Data[i], pixels.Data[(4 * i) + 3]);
        }
    }

    [Fact]
    public void A_soft_mask_of_its_own_size_is_not_folded_into_a_reduced_image()
    {
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page("soft-mask-own-size"));
        (DisplayImage image, _) = FirstImage(document);

        Assert.False(ImageConverter.FoldsSoftMask(image, image.GetSamplesAt(2, TestContext.Current.CancellationToken)!.Value, out _));
        Assert.False(ImageConverter.FoldsSoftMask(image, image.GetSamplesAt(0, TestContext.Current.CancellationToken)!.Value, out _));
    }

    [Fact]
    public void A_reduced_stencil_is_the_share_of_the_samples_it_covers_that_paint()
    {
        // The 900 x 600 explicit mask at 1/4: 225 x 150 values, each the rounded mean of a 4 x 4 block of the full-resolution mask.
        using PdfDocument document = PdfDocument.Open(ScaledImagePages.Page("explicit-mask"));
        DisplayImageMask mask = FirstImage(document).Image.Mask!;

        using PixelImage full = ImageConverter.ConvertStencil(mask)!;
        using PixelImage reduced = ImageConverter.ConvertStencil(mask, 2)!;

        Assert.Equal((225, 150, 1), (reduced.Width, reduced.Height, reduced.Channels));
        for (int y = 0; y < reduced.Height; y++)
        {
            for (int x = 0; x < reduced.Width; x++)
            {
                int sum = 0;
                for (int sy = 4 * y; sy < (4 * y) + 4; sy++)
                {
                    for (int sx = 4 * x; sx < (4 * x) + 4; sx++)
                    {
                        sum += full.Data[(sy * full.Width) + sx];
                    }
                }

                Assert.Equal((sum + 8) / 16, reduced.Data[(y * reduced.Width) + x]);
            }
        }

        using PixelImage same = ImageConverter.ConvertStencil(mask, 0)!;
        Assert.Equal(full.Data, same.Data);
    }

    [Fact]
    public void A_reduction_that_does_not_divide_the_size_overhangs_the_unit_square_by_the_remainder()
    {
        // 613 x 377 at 1/8: 77 x 48 samples, of which the unit square covers 76.625 x 47.125.
        var grid = new Direct2DBackend.SampleGrid(77, 48, 613, 377, 3);
        var whole = new Direct2DBackend.SampleGrid(613, 377, 613, 377, 0);

        Assert.True(grid.Overhangs);
        Assert.Equal((76.625, 47.125), (grid.OutlineWidth, grid.OutlineHeight));
        PathPoint corner = grid.ToUnitSquare.Transform(76.625, 47.125);
        Assert.Equal(1, corner.X, 12);
        Assert.Equal(0, corner.Y, 12);
        Assert.False(whole.Overhangs);
        Assert.Equal(new Matrix(1.0 / 613, 0, 0, -1.0 / 377, 0, 1), whole.ToUnitSquare);
        Assert.False(new Direct2DBackend.SampleGrid(128, 128, 1024, 1024, 3).Overhangs);
    }

    [Fact]
    public void A_kept_conversion_serves_its_own_and_coarser_reductions_and_a_new_reduction_frees_the_resampling_slots()
    {
        using var cache = new ImageCache();
        ImageCache.Entry entry = cache.Find(new object());
        ColorConversion conversion = default;

        Assert.True(cache.Keep(entry, PixelImage.Copy(new byte[16], 2, 2, 4), 16, conversion, null, delivered: 1));
        Assert.True(entry.Admit(Matrix.Identity, out int slot));

        Assert.False(entry.Matches(conversion, null, 0));
        Assert.True(entry.Matches(conversion, null, 1));
        Assert.True(entry.Matches(conversion, null, 3));

        // A finer conversion replaces it: the resamplings were for the coarser grid.
        Assert.True(entry.Admit(Matrix.CreateScale(2, 2), out int other));
        Assert.NotEqual(slot, other);
        Assert.True(cache.Keep(entry, PixelImage.Copy(new byte[64], 4, 4, 4), 64, conversion, null, delivered: 0));
        Assert.Equal(0, entry.Delivered);
        Assert.True(entry.Admit(Matrix.CreateScale(3, 3), out int first));
        Assert.Equal(0, first);
        Assert.True(entry.Matches(conversion, null, 0));
        Assert.Equal(64, cache.Bytes);
    }

    private static (DisplayImage Image, PdfColorConverter Converter) FirstImage(PdfDocument document)
    {
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        (DisplayImage image, PdfDefaultColorSpaces defaults, ColorConversion conversion) = Assert.Single(recorder.Images);
        return (image, document.GetColorConverter(image.ColorSpace, conversion with { Target = DeviceColorModel.Rgb }, defaults));
    }
}
