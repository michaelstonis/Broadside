using Broadside.Images;
using Broadside.Tests.Filters.Dct;

namespace Broadside.Tests.Images;

/// <summary>
/// Issue #372: the one rule that decides which images may go to a platform image decoder (ISO 32000-2 §7.4.8 Table 13, §8.9.5.1
/// Table 87; ITU-T T.81 Annex B), one case per hazard measured in the image decode research report, and the byte hygiene before the
/// data is handed over.
/// </summary>
public sealed class PlatformImageEligibilityTests
{
    [Theory]
    [InlineData("encoded-rgb-q90-420", 75, 43, 3, false)]
    [InlineData("encoded-rgb-q90-444", 75, 43, 3, false)]
    [InlineData("encoded-gray-q90", 37, 21, 1, false)]
    [InlineData("progressive-scans", 75, 43, 3, true)]
    public void Baseline_and_progressive_8_bit_gray_and_YCbCr_are_eligible(string name, int width, int height, int components, bool progressive)
    {
        Assert.True(PlatformImageEligibility.TryCreateRequest(DctVectors.Jpeg(name), -1, width, height, out PlatformImageRequest request, out string? reason), reason);

        Assert.Equal((width, height, components, 8, progressive, false), (request.Width, request.Height, request.Components, request.BitsPerComponent, request.Progressive, request.ArithmeticCoding));
        Assert.Equal(components == 3, request.ColorTransform);
        Assert.Equal("DCTDecode", request.Filter.Value);
    }

    [Theory]
    [InlineData("encoded-rgb-q90-420", true)]
    [InlineData("encoded-rgb-q90-422", true)]
    [InlineData("encoded-rgb-q90-444", false)]
    [InlineData("encoded-gray-q90", false)]
    public void Subsampled_chroma_is_reported(string name, bool subsampled)
    {
        byte[] jpeg = DctVectors.Jpeg(name);
        int width = name.Contains("gray", StringComparison.Ordinal) ? 37 : 75;
        int height = name.Contains("gray", StringComparison.Ordinal) ? 21 : 43;

        Assert.True(PlatformImageEligibility.TryCreateRequest(jpeg, -1, width, height, out PlatformImageRequest request, out _));
        Assert.Equal(subsampled, request.ChromaSubsampled);
    }

    [Fact]
    public void Arithmetic_coding_is_eligible_and_flagged_so_only_a_decoder_that_declares_it_is_offered_it()
    {
        Assert.True(PlatformImageEligibility.TryCreateRequest(DctVectors.Jpeg("arithmetic"), -1, 75, 43, out PlatformImageRequest request, out _));

        Assert.True(request.ArithmeticCoding);
    }

    [Theory]
    [InlineData("cmyk", 75, 43, -1, "4 components")]
    [InlineData("encoded-ycck-q90-420", 75, 43, -1, "4 components")]
    [InlineData("precision12", 75, 43, -1, "12 bits")]
    [InlineData("dnl", 75, 43, -1, "DNL")]
    [InlineData("encoded-rgb-q90-420", 76, 43, -1, "image dictionary says 76 x 43")]
    [InlineData("encoded-rgb-q90-420", 75, 43, 0, "ColorTransform is 0")]
    [InlineData("encoded-rgb-untransformed-q90", 75, 43, -1, "APP14 transform is 0")]
    public void Each_hazard_keeps_the_image_managed(string name, int width, int height, int colorTransform, string expected)
    {
        Assert.False(PlatformImageEligibility.TryCreateRequest(DctVectors.Jpeg(name), colorTransform, width, height, out _, out string? reason));

        Assert.Contains(expected, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Components_named_R_G_B_without_APP14_stay_managed()
    {
        byte[] jpeg = Rename(DctVectors.Jpeg("encoded-rgb-q90-420"), "RGB"u8);

        Assert.False(PlatformImageEligibility.TryCreateRequest(jpeg, -1, 75, 43, out _, out string? reason));
        Assert.Contains("R, G, B", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Exif_and_ICC_segments_are_reported_and_zero_filled_on_request_with_every_offset_unchanged()
    {
        byte[] jpeg = WithSegments(DctVectors.Jpeg("encoded-rgb-q90-420"));
        Assert.True(PlatformImageEligibility.TryCreateRequest(jpeg, -1, 75, 43, out PlatformImageRequest request, out _));
        Assert.True(request.HasExif);
        Assert.True(request.HasIccProfile);

        byte[] exifOnly = (byte[])jpeg.Clone();
        PlatformImageEligibility.Sanitize(exifOnly, exif: true, icc: false);
        Assert.Equal(jpeg.Length, exifOnly.Length);
        Assert.All(ExifPayload(exifOnly), b => Assert.Equal(0, b));
        Assert.Equal(IccPayload(jpeg), IccPayload(exifOnly));
        Assert.True(exifOnly.AsSpan().IndexOf("Exif\0\0"u8) > 0, "the segment keeps its identifier");

        byte[] both = (byte[])jpeg.Clone();
        PlatformImageEligibility.Sanitize(both, exif: true, icc: true);
        Assert.All(IccPayload(both), b => Assert.Equal(0, b));
        Assert.Equal(jpeg.AsSpan(OriginalStart).ToArray(), both.AsSpan(OriginalStart).ToArray());
        Assert.True(PlatformImageEligibility.TryCreateRequest(both, -1, 75, 43, out _, out _), "still a well-formed JPEG");
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x30 })]
    public void Malformed_data_is_never_eligible_and_never_throws(byte[] data)
    {
        Assert.False(PlatformImageEligibility.TryCreateRequest(data, -1, 1, 1, out _, out string? reason));
        Assert.NotNull(reason);
        PlatformImageEligibility.Sanitize(data, exif: true, icc: true);
    }

    /// <summary>Inserts an APP1 Exif segment (orientation 6) and an APP2 ICC profile segment after the SOI marker.</summary>
    internal static byte[] WithSegments(byte[] jpeg)
    {
        byte[] exif = [.. "Exif\0\0MM\0*\0\0\0\b\0\u0001\u0001\u0012\0\u0003\0\0\0\u0001\0\u0006\0\0\0\0\0\0"u8];
        byte[] icc = [.. "ICC_PROFILE\0\u0001\u0001"u8, .. Enumerable.Range(1, 128).Select(i => (byte)i)];
        return [0xFF, 0xD8, .. Segment(0xE1, exif), .. Segment(0xE2, icc), .. jpeg.AsSpan(2)];

        static byte[] Segment(byte marker, byte[] payload) =>
            [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];
    }

    /// <summary>The Exif payload after its identifier, in a JPEG made by <see cref="WithSegments"/>.</summary>
    internal static byte[] ExifPayload(byte[] jpeg) => jpeg.AsSpan(2 + 4 + 6, ExifLength - 6).ToArray();

    /// <summary>The ICC payload after its identifier and sequence numbers, in a JPEG made by <see cref="WithSegments"/>.</summary>
    internal static byte[] IccPayload(byte[] jpeg) => jpeg.AsSpan(2 + 4 + ExifLength + 4 + 14, 128).ToArray();

    private const int ExifLength = 32;

    // Where the original JPEG's segments resume after the SOI and the two inserted segments.
    private const int OriginalStart = 2 + 4 + ExifLength + 4 + 14 + 128;

    private static byte[] Rename(byte[] jpeg, ReadOnlySpan<byte> ids)
    {
        byte[] copy = (byte[])jpeg.Clone();
        int sof = copy.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        for (int c = 0; c < 3; c++)
        {
            copy[sof + 4 + 6 + (3 * c)] = ids[c];
        }

        return copy;
    }
}
