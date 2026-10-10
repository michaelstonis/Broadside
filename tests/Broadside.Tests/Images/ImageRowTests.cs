using Broadside.Graphics;
using Broadside.Images;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// The row helpers and the Decode mapping with spec-derived vectors (ISO 32000-2 §8.9.3, §8.9.5.2 Table 88 and its Note 4, §8.6.6.3,
/// §8.9.6.4, §11.6.5.2), and odd depths decoded through documents.
/// </summary>
public sealed class ImageRowTests
{
    public static TheoryData<int, byte[], int, ushort[]> UnpackVectors => new()
    {
        { 1, [0b1011_0010, 0b1100_0000], 10, [1, 0, 1, 1, 0, 0, 1, 0, 1, 1] },
        { 1, [0b0000_0001], 7, [0, 0, 0, 0, 0, 0, 0] },
        { 2, [0b0001_1011, 0b1110_0000], 6, [0, 1, 2, 3, 3, 2] },
        { 4, [0x12, 0x3F, 0xA0], 5, [1, 2, 3, 15, 10] },
        { 8, [7, 8, 9], 3, [7, 8, 9] },
        { 16, [0x12, 0x34, 0xFF, 0x01, 0x00, 0x80], 3, [0x1234, 0xFF01, 0x0080] },
    };

    [Theory]
    [MemberData(nameof(UnpackVectors))]
    public void Rows_unpack_most_significant_bit_first_and_16_bit_values_big_endian(int bits, byte[] row, int count, ushort[] expected)
    {
        ushort[] wide = new ushort[count];
        ImageRows.Unpack(row, bits, count, wide);
        Assert.Equal(expected, wide);

        if (bits <= 8)
        {
            byte[] narrow = new byte[count];
            ImageRows.Unpack(row, bits, count, narrow);
            Assert.Equal(expected.Select(v => (byte)v), narrow);
        }
    }

    [Fact]
    public void Scaling_to_8_bits_maps_the_ends_of_every_depth_onto_0_and_255()
    {
        byte[] output = new byte[4];
        ImageRows.UnpackScaled([0b0001_1011], 2, 2, 4, output);
        Assert.Equal(new byte[] { 0, 85, 170, 255 }, output);

        ImageRows.UnpackScaled([0x0F, 0xF0], 4, 4, 4, output);
        Assert.Equal(new byte[] { 0, 255, 255, 0 }, output);

        // A 3-bit depth stored in bytes (values 0-7) and a 12-bit depth stored in 16 bits (0-4095).
        ImageRows.UnpackScaled([0, 7, 4, 3], 8, 3, 4, output);
        Assert.Equal(new byte[] { 0, 255, 146, 109 }, output);
        ImageRows.UnpackScaled([0x0F, 0xFF, 0x00, 0x00, 0x08, 0x00, 0x00, 0x01], 16, 12, 4, output);
        Assert.Equal(new byte[] { 255, 0, 128, 0 }, output);
    }

    [Fact]
    public void A_Decode_array_maps_samples_linearly_even_outside_the_component_range()
    {
        // Table 88 Note 4: Decode [0 4.04762] with 4-bit samples maps 0-15 to 0-4.04762 in steps of 0.26984, values clamped at use.
        ImageDecodeMap map = ImageDecodeMap.Create([0, 4.04762], 1, 4);
        Assert.Equal(0f, map.Map(0, 0));
        Assert.Equal(4.04762f, map.Map(0, 15), 4);
        Assert.Equal(0.26984f, map.Map(0, 1), 4);

        float[] clamped = new float[3];
        map.Map(new ushort[] { 0, 4, 15 }, clamped, [new ComponentRange(0, 1)]);
        Assert.Equal([0f, 1f, 1f], clamped);

        // 16-bit samples are computed, not tabulated.
        ImageDecodeMap wide = ImageDecodeMap.Create([1, 0, 0, 2], 2, 16);
        float[] values = new float[4];
        wide.Map(new ushort[] { 0, 0, 65535, 65535 }, values);
        Assert.Equal([1f, 0f, 0f, 2f], values);
        Assert.True(wide.IsInverted);
    }

    [Fact]
    public void Indexed_samples_round_half_up_and_clamp_to_the_table()
    {
        // Decode [0 7.5] on 2-bit samples: 0, 2.5, 5, 7.5 -> indices 0, 3, 5, 8 clamped to hival 6.
        ImageDecodeMap map = ImageDecodeMap.Create([0, 7.5], 1, 2);
        byte[] indices = new byte[4];
        map.MapToIndices(new ushort[] { 0, 1, 2, 3 }, indices, 6);
        Assert.Equal(new byte[] { 0, 3, 5, 6 }, indices);
    }

    [Fact]
    public void A_colour_key_compares_raw_values_with_real_ranges_inclusively()
    {
        byte[] coverage = new byte[3];
        ImageRows.ColorKey(new ushort[] { 10, 2, 11, 2, 10, 3 }, 2, [9.5, 10.5, 1, 2], coverage);
        Assert.Equal(new byte[] { 0, 255, 255 }, coverage);

        ImageRows.ColorKey(new ushort[] { 0x1234, 0xFFFF }, 1, [0x1234, 0x1234], coverage);
        Assert.Equal(new byte[] { 0, 255 }, coverage[..2]);
    }

    [Fact]
    public void Unpremultiplying_inverts_the_pre_blend_clamps_and_gives_the_matte_where_alpha_is_0()
    {
        float[] colours = [0.6f, 0.2f, 0.9f, 0.9f];
        ImageRows.Unpremultiply(colours, 2, [0.5f, 0f], [1f, 0f], [new ComponentRange(0, 1), new ComponentRange(0, 1)]);

        // c = m + (c' - m) / a: 1 + (0.6 - 1) / 0.5 = 0.2; 0 + 0.2 / 0.5 = 0.4; alpha 0: the matte (1, 0).
        Assert.Equal([0.2f, 0.4f, 1f, 0f], colours, (a, b) => Math.Abs(a - b) < 1e-6);

        float[] overflow = [0.9f];
        ImageRows.Unpremultiply(overflow, 1, [0.1f], [0f], [new ComponentRange(0, 1)]);
        Assert.Equal(1f, overflow[0]);
    }

    [Theory]
    [InlineData(0, 4, 16, 2)]
    [InlineData(3, 4, 16, 14)]
    [InlineData(0, 16, 4, 0)]
    [InlineData(15, 16, 4, 3)]
    [InlineData(1, 2, 3, 2)]
    public void A_mask_is_sampled_at_the_centre_of_each_image_sample(int index, int size, int maskSize, int expected) =>
        Assert.Equal(expected, ImageRows.MaskIndex(index, size, maskSize));

    [Fact]
    public void Undersized_spans_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.Unpack([0xFF], 1, 9, new byte[9]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.Unpack([0xFF, 0xFF], 1, 9, new byte[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageRows.Unpack([0xFF], 3, 2, new byte[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageDecodeMap.Create([0, 1], 2, 8));
    }

    [Fact]
    public void Odd_depths_are_repacked_into_the_next_wider_storage_with_their_values()
    {
        // 3-bit gray, 3 samples (5, 2, 7) = 101 010 11|1 + padding; 12-bit gray, 2 samples (0xABC, 0x123).
        using PdfDocument three = PdfDocument.Open(OneImage("/Width 3 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 3", Bytes(0b1010_1011, 0b1000_0000)));
        PdfImage image = three.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;
        Assert.Equal((3, 8, 3), (decoded.BitsPerComponent, decoded.StorageBits, decoded.Stride));
        Assert.Equal(new byte[] { 5, 2, 7 }, decoded.Samples.ToArray());
        Assert.Equal(1f, image.CreateDecodeMap(decoded).Map(0, 7));
        Assert.Equal(["ImageBitsPerComponentInvalid"], Codes(three));

        using PdfDocument twelve = PdfDocument.Open(OneImage("/Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 12", Bytes(0xAB, 0xC1, 0x23)));
        using DecodedImage wide = twelve.Pages[0].GetImage("Im0")!.Decode()!;
        Assert.Equal((12, 16, 4), (wide.BitsPerComponent, wide.StorageBits, wide.Stride));
        Assert.Equal(new ushort[] { 0xABC, 0x123 }, Row(wide, 0));
    }
}
