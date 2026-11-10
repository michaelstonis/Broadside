using System.Globalization;
using Broadside.Rendering.Android.Device;

namespace Broadside.Rendering.Android.Tests;

/// <summary>
/// Issue #376: Android's <c>BitmapFactory</c> behind the platform image decoder seam (<see cref="AndroidImageDecoder"/>), against the
/// managed decoder through the verification mode on the device. Images without chroma subsampling (4:4:4, gray) and 4:2:0 images
/// reduced are taken and stay within the tolerance (bit-identical where measured); subsampled chroma at full size, and 4:2:2 reduced,
/// are outside it and decode with the managed codec; four-component data is never offered; an ICC-tagged, Exif-rotated JPEG keeps
/// its own colours and orientation.
/// </summary>
/// <remarks>ISO 32000-2 §7.4.8 (DCTDecode), §8.9.3; ITU-T T.81 A.1.1 (sampling factors).</remarks>
public sealed class AndroidImageDecoderTests(ITestOutputHelper output)
{
    private const string Verified = "PlatformImageVerified";
    private const string Managed = "ImageDecodedManaged";

    public static TheoryData<string, int> Taken => new()
    {
        { "photo-444", 0 }, { "photo-444", 1 }, { "photo-444", 2 }, { "photo-444", 3 },
        { "photo-gray", 0 }, { "photo-gray", 1 }, { "photo-gray", 2 }, { "photo-gray", 3 },
        { "photo-420", 1 }, { "photo-420", 2 }, { "photo-420", 3 },
    };

    [Theory]
    [MemberData(nameof(Taken))]
    public void Images_it_takes_decode_within_the_tolerance_at_the_reduction_asked(string photo, int reduction)
    {
        Report report = Verify(DeviceProtocol.PlatformCase(photo, reduction));

        Assert.Equal([Verified], report.Codes);
        Assert.Contains(FormattableString.Invariant($"at reduction {reduction}:"), report.Messages[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("photo-420")]
    [InlineData("photo-422")]
    public void Subsampled_chroma_at_full_size_is_left_to_the_managed_decoder(string photo) =>
        // libjpeg's fancy upsampling: max 30 and 1.08% of pixels over 2 levels on the 4:2:0 photograph (see the fidelity matrix).
        Assert.Empty(Verify(DeviceProtocol.PlatformCase(photo, 0)).Codes);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_reduced_4_2_2_image_falls_back_to_the_managed_decoder_with_a_diagnostic(int reduction)
    {
        Report report = Verify(DeviceProtocol.PlatformCase("photo-422", reduction));

        Assert.Equal([Managed], report.Codes);
        Assert.Contains("4:2:0", report.Messages[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Corpus_DCT_images_decode_on_the_platform_only_where_they_are_within_the_tolerance()
    {
        // dct-baseline: the 4:2:0 colour image at full size decodes managed, the gray one on the platform; dct-progressive is 4:2:0
        // at full size; dct-cmyk's four-component images are never offered.
        foreach (int reduction in (int[])[0, 2])
        {
            Report baseline = Verify(DeviceProtocol.PlatformCase("dct-baseline.pdf", reduction));
            Assert.Equal([Verified], baseline.Codes);
            Assert.Contains("37 x 21", baseline.Messages[0], StringComparison.Ordinal);
            Assert.Empty(Verify(DeviceProtocol.PlatformCase("dct-progressive.pdf", reduction)).Codes);
            Assert.Empty(Verify(DeviceProtocol.PlatformCase("dct-cmyk.pdf", reduction)).Codes);
        }
    }

    [Fact]
    public void An_Exif_rotated_JPEG_with_a_converting_ICC_profile_keeps_its_own_orientation_and_colours()
    {
        // The profile swaps red and blue, and Exif orientation 6 would turn the 640 x 400 image: either is a mismatch.
        Report report = Verify(DeviceProtocol.PlatformCase(DeviceProtocol.ExifIccPhoto, 0));

        Assert.Equal([Verified], report.Codes);
        Assert.Contains("max 0,", report.Messages[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_ICC_profile_zero_filled_by_the_core_is_not_converted()
    {
        AndroidDevice device = AndroidDevice.Run();
        foreach (string variant in (string[])[DeviceProtocol.ProfileAsIs, DeviceProtocol.ProfileSrgb, DeviceProtocol.ProfileCleared])
        {
            output.WriteLine($"{variant}: max {device.Platform(variant, DeviceProtocol.MaxDifference)}, bitmap {device.Platform(variant, DeviceProtocol.ColorSpaceName)}");
        }

        // What the decoder is handed: the payload zero-filled, decoded with no preferred colour space. inPreferredColorSpace sRGB
        // converts the swapped profile (it is never set).
        Assert.Equal("0", device.Platform(DeviceProtocol.ProfileCleared, DeviceProtocol.MaxDifference));
        Assert.True(int.Parse(device.Platform(DeviceProtocol.ProfileSrgb, DeviceProtocol.MaxDifference), CultureInfo.InvariantCulture) > 24);
    }

    public static TheoryData<string> FidelityCases => ["photo-444", "photo-gray", "photo-420", "edges-444", "edges-420", "edges-420-large"];

    [Theory]
    [MemberData(nameof(FidelityCases))]
    public void BitmapFactory_is_within_the_tolerance_bars_wherever_the_decoder_takes_the_image(string name)
    {
        AndroidDevice device = AndroidDevice.Run();
        for (int reduction = 0; reduction <= 3; reduction++)
        {
            string[] figures = device.Platform(DeviceProtocol.PlatformCase(name, reduction), DeviceProtocol.Fidelity).Split(' ');
            output.WriteLine($"{name} r{reduction}: delivered r{figures[0]}, max {figures[1]}, PSNR {figures[2]} dB, {figures[3]}% over 2 levels");
            bool subsampled = name.Contains("420", StringComparison.Ordinal);
            if (subsampled && reduction == 0)
            {
                continue; // Not taken (CanDecode).
            }

            int delivered = int.Parse(figures[0], CultureInfo.InvariantCulture);
            int max = int.Parse(figures[1], CultureInfo.InvariantCulture);
            double psnr = figures[2] == "inf" ? double.PositiveInfinity : double.Parse(figures[2], CultureInfo.InvariantCulture);
            double over = double.Parse(figures[3], CultureInfo.InvariantCulture);
            Assert.Equal(reduction, delivered);
            Assert.True(
                reduction == 0 ? max <= 24 && psnr >= 50 && over <= 1 : psnr >= 55,
                $"{name} at reduction {reduction}: max {max}, PSNR {psnr} dB, {over}% of pixels over 2 levels");
        }
    }

    [Fact]
    public void Decode_and_first_paint_times_against_the_managed_decoder()
    {
        // Measured, not asserted: an emulator's figures are unreliable; docs/benchmarks/android-image-decoder.md records them.
        foreach ((string key, string value) in AndroidDevice.Run().ResultsStartingWith("time:"))
        {
            output.WriteLine($"{key} = {value} ms");
        }
    }

    private static Report Verify(string name)
    {
        string text = AndroidDevice.Run().Platform(name, DeviceProtocol.Diagnostics);
        string[] lines = text.Split(DeviceProtocol.LineSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Report([.. lines.Select(static line => line.Split('|')[0])], [.. lines.Select(static line => line.Split('|', 2)[1])]);
    }

    private sealed record Report(string[] Codes, string[] Messages);
}
