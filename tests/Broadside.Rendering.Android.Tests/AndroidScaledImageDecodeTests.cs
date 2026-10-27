using Broadside.Rendering.Android.Device;
using Broadside.TestSupport;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// Issue #428 (the Android Canvas half of #362): a viewer's Android renders convert a large image drawn small at the reduction that
/// keeps two samples per device pixel, each mask at its own, and stay within the page tolerance of the same backend converting every
/// sample (PSNR ≥ 35 dB, at most 4% of pixels more than 8 levels off); page renders, print and export keep converting every sample.
/// The device renders both and reports the figures (<see cref="DeviceProtocol.ScaledArgument"/>).
/// </summary>
/// <remarks>ISO 32000-2 §8.9.4 (the image unit square), §8.9.5.3, §8.9.6 and §11.6.5.2 (masks).</remarks>
public sealed class AndroidScaledImageDecodeTests
{
    private const double MinimumPsnr = 35;
    private const double MaximumOver8 = 0.04;

    [Theory]
    [InlineData(390, 1)]
    [InlineData(300, 2)]
    [InlineData(150, 3)]
    public void The_catalogue_cover_at_phone_widths_converts_a_reduction_within_the_page_tolerance(int width, int reduction)
    {
        AndroidDevice device = AndroidDevice.Run();
        string name = DeviceProtocol.CoverCase(width);

        Assert.Equal(2550L * 3150, (long)device.ScaledNumber(name, DeviceProtocol.FullPixels));
        Assert.Equal((long)Reduce(2550, reduction) * Reduce(3150, reduction), (long)device.ScaledNumber(name, DeviceProtocol.ReducedPixels));
        AssertWithinTolerance(device, name);
    }

    [Fact]
    public void The_converted_pixels_kept_are_the_reduced_size()
    {
        AndroidDevice device = AndroidDevice.Run();
        string name = DeviceProtocol.CoverCase(150);

        // The 319 x 394 conversion and its resampling to the 150-pixel-wide device, never the 2550 x 3150 image.
        long kept = (long)device.ScaledNumber(name, DeviceProtocol.KeptBytes);
        Assert.InRange(kept, 319L * 394 * 4, (319L * 394 * 4) + (160L * 200 * 4));
    }

    public static TheoryData<string> DctFiles => [.. DeviceProtocol.ScaledDctFiles];

    [Theory]
    [MemberData(nameof(DctFiles))]
    public void Corpus_DCT_pages_at_a_quarter_scale_stay_within_the_page_tolerance(string file) =>
        AssertWithinTolerance(AndroidDevice.Run(), file);

    public static TheoryData<string> Pages => [.. ScaledImagePages.Names];

    [Theory]
    [MemberData(nameof(Pages))]
    public void Large_images_and_their_masks_at_a_quarter_scale_stay_within_the_page_tolerance(string page)
    {
        // Flate images decoded at build, reduced from that decode: depths, a stencil, soft masks of the image's size and another, an
        // explicit mask of another size, and a colour key (never reduced).
        AndroidDevice device = AndroidDevice.Run();
        long reduced = (long)device.ScaledNumber(page, DeviceProtocol.ReducedPixels);
        long full = (long)device.ScaledNumber(page, DeviceProtocol.FullPixels);

        AssertWithinTolerance(device, page);
        if (page == "colour-key")
        {
            Assert.Equal(full, reduced);
        }
        else
        {
            // The 1024 x 1024 image is drawn 128 pixels square: a quarter keeps two samples per pixel.
            Assert.True(reduced + (1024L * 1024 * 3 / 4) <= full, $"{page}: {reduced} pixels converted, {full} in full");
        }
    }

    [Fact]
    public void A_curved_colour_conversion_caps_the_codec_reduction_at_a_half()
    {
        AndroidDevice device = AndroidDevice.Run();

        Assert.Equal(512L * 512, (long)device.ScaledNumber(DeviceProtocol.CurvedCase, DeviceProtocol.ReducedPixels));
        double psnr = device.ScaledNumber(DeviceProtocol.CurvedCase, DeviceProtocol.Psnr);
        double max = device.ScaledNumber(DeviceProtocol.CurvedCase, DeviceProtocol.MaxDifference);
        Assert.True(psnr >= 45, $"PSNR {psnr:F1} dB");
        Assert.True(max <= 40, $"max {max}");
    }

    [Fact]
    public void A_render_at_one_to_one_never_reduces() =>
        Assert.Equal("yes", AndroidDevice.Run().Scaled(DeviceProtocol.OneToOneCase, DeviceProtocol.Identical));

    [Fact]
    public void Page_renders_convert_every_sample_unless_a_viewer_asks()
    {
        // Viewer-only: RenderOptions.ScaledImageDecode is off by default, as are a new backend's list renders.
        AndroidDevice device = AndroidDevice.Run();

        Assert.Equal("False False 2", device.Scaled(DeviceProtocol.PageCase, DeviceProtocol.Defaults));
        Assert.Equal(2550L * 3150, (long)device.ScaledNumber(DeviceProtocol.PageCase, DeviceProtocol.FullPixels));
        Assert.Equal(638L * 788, (long)device.ScaledNumber(DeviceProtocol.PageCase, DeviceProtocol.ReducedPixels));
    }

    [Fact]
    public void One_sample_per_pixel_reduces_one_step_further()
    {
        AndroidDevice device = AndroidDevice.Run();

        Assert.Equal(638L * 788, (long)device.ScaledNumber(DeviceProtocol.OneSampleCase, DeviceProtocol.ReducedPixels));
        Assert.Equal(1275L * 1575, (long)device.ScaledNumber(DeviceProtocol.OneSampleCase, DeviceProtocol.FullPixels));
        Assert.Equal("yes", device.Scaled(DeviceProtocol.OneSampleCase, DeviceProtocol.Identical));
    }

    [Fact]
    public void A_zoom_in_converts_the_new_reduction_once_for_every_tile_and_a_zoom_out_converts_nothing()
    {
        AndroidDevice device = AndroidDevice.Run();

        Assert.Equal(2, (int)device.ScaledNumber(DeviceProtocol.ZoomCase, DeviceProtocol.Images));
        Assert.Equal((319L * 394) + (1275L * 1575), (long)device.ScaledNumber(DeviceProtocol.ZoomCase, DeviceProtocol.ReducedPixels));
    }

    [Fact]
    public void Replaying_a_list_whose_image_is_reduced_allocates_nothing()
    {
        AndroidDevice device = AndroidDevice.Run();
        if (!device.IsOptimizedBuild)
        {
            Assert.Skip("The device app is a Debug build: Mono runs it without JIT optimizations, which allocate in shared generic code; build it with -c Release.");
        }

        Assert.Equal(0, (long)device.ScaledNumber(DeviceProtocol.AllocationCase, DeviceProtocol.Allocated));
    }

    private static int Reduce(int size, int reduction) => (size + (1 << reduction) - 1) >> reduction;

    private static void AssertWithinTolerance(AndroidDevice device, string name)
    {
        double psnr = device.ScaledNumber(name, DeviceProtocol.Psnr);
        double over8 = device.ScaledNumber(name, DeviceProtocol.Over8);
        Assert.True(psnr >= MinimumPsnr && over8 <= MaximumOver8, $"{name}: PSNR {psnr:F1} dB, {over8:P2} of pixels over 8 levels");
    }
}
