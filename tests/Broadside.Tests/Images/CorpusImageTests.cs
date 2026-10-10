using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// The image corpus files (tests/Corpus/README.md) read through <see cref="PdfPage.GetImage"/>: dictionary values, sample bytes in
/// the §8.9.3 layout, Decode mapping and masks. Expected values come from the samples each file was generated from and from poppler's
/// rendering of it (recorded in the README).
/// </summary>
public sealed class CorpusImageTests
{
    [Fact]
    public void A_1_bit_image_keeps_its_row_padding_out_of_the_samples_and_reports_Interpolate_and_Intent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-1bpc.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((10, 3, 1, 1, 1, 2), (decoded.Width, decoded.Height, decoded.Components, decoded.BitsPerComponent, decoded.StorageBits, decoded.Stride));
        Assert.Equal(new byte[] { 0x80, 0x7F, 0x55, 0x7F, 0xF8, 0x3F }, decoded.Samples.ToArray());
        Assert.Equal(
            new ushort[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 },
            Raw(decoded));
        Assert.Equal(3, decoded.DecodedRows);
        Assert.True(image.Interpolate);
        Assert.Equal(RenderingIntent.Perceptual, image.Intent);
        Assert.Same(PdfDeviceGrayColorSpace.Instance, image.ColorSpace);
        Assert.Equal([0.0, 1.0], image.DecodeArray);
        Assert.Equal(PdfImageMaskKind.None, image.MaskKind);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_2_bit_image_scales_to_the_gray_levels_poppler_shows()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-2bpc.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.Equal(new byte[] { 0x1B, 0x3F, 0xF9, 0x7F }, decoded.Samples.ToArray());
        byte[] gray = new byte[5];
        ImageRows.UnpackScaled(decoded.GetRow(0), decoded.StorageBits, decoded.BitsPerComponent, 5, gray);
        Assert.Equal(new byte[] { 0, 85, 170, 255, 0 }, gray);
        ImageRows.UnpackScaled(decoded.GetRow(1), decoded.StorageBits, decoded.BitsPerComponent, 5, gray);
        Assert.Equal(new byte[] { 255, 255, 170, 85, 85 }, gray);
        Assert.False(image.Interpolate);
        Assert.Null(image.Intent);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_4_bit_Indexed_image_maps_through_the_default_Decode_to_its_palette()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-4bpc-indexed.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        PdfIndexedColorSpace space = Assert.IsType<PdfIndexedColorSpace>(image.ColorSpace);
        Assert.Equal(15, space.HighValue);
        Assert.Equal([0.0, 15.0], image.DecodeArray);
        Assert.Equal(new byte[] { 0x05, 0xFF, 0x91, 0xEF }, decoded.Samples.ToArray());
        byte[] indices = new byte[6];
        image.CreateDecodeMap(decoded).MapToIndices(Raw(decoded), indices, space.HighValue);
        Assert.Equal(new byte[] { 0, 5, 15, 9, 1, 14 }, indices);

        byte[] rgb = new byte[18];
        document.GetColorConverter(space).Convert(indices, rgb, 6);
        Assert.Equal(new byte[] { 0, 255, 0, 85, 170, 255, 255, 0, 253, 153, 102, 203, 17, 238, 51, 238, 17, 202 }, rgb);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_16_bit_image_keeps_its_big_endian_samples()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-16bpc.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.Equal((16, 16, 18), (decoded.BitsPerComponent, decoded.StorageBits, decoded.Stride));
        Assert.Equal(new ushort[] { 0x12FF, 0xFF12, 0x0001, 0x8000, 0x7FFF, 0xFFFF, 0x0000, 0x1234, 0xABCD }, Row(decoded, 0));
        Assert.Equal(new ushort[] { 0xFFFF, 0x0000, 0x00FF, 0xFF00, 0x4321, 0x8001, 0x0F0F, 0xF0F0, 0x5555 }, Row(decoded, 1));

        // Reduced to 8 bits the first row is poppler's (18,255,0), (128,127,255), (0,18,171) within rounding.
        byte[] reduced = new byte[9];
        ImageRows.UnpackScaled(decoded.GetRow(0), 16, 16, 9, reduced);
        Assert.Equal(new byte[] { 19, 254, 0, 128, 127, 255, 0, 18, 171 }, reduced);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_inverted_Decode_array_reverses_the_ramp()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-decode-inverted.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.Equal([1.0, 0.0], image.DecodeArray);
        float[] values = new float[4];
        image.CreateDecodeMap(decoded).Map(decoded.Samples, values);
        Assert.Equal([1f, 2f / 3, 1f / 3, 0f], values, (a, b) => Math.Abs(a - b) < 1e-6);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_stencil_mask_with_Decode_1_0_paints_where_its_samples_are_1()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-stencil-mask.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.True(image.IsStencil);
        Assert.Equal(PdfImageMaskKind.Stencil, image.MaskKind);
        Assert.Null(image.ColorSpace);
        Assert.Equal(1, image.BitsPerComponent);
        Assert.Equal([1.0, 0.0], image.DecodeArray);
        ImageDecodeMap map = image.CreateDecodeMap(decoded);
        Assert.True(map.IsInverted);

        string[] painted = [.. Enumerable.Range(0, 8).Select(y =>
        {
            byte[] coverage = new byte[8];
            ImageRows.Stencil(decoded.GetRow(y), 8, map.IsInverted, coverage);
            return string.Concat(coverage.Select(c => c == 255 ? '#' : '.'));
        })];
        Assert.Equal(["...#....", "...##...", "######..", "#######.", "######..", "...##...", "...#....", "........"], painted);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_explicit_mask_is_an_image_mask_at_its_own_resolution()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-explicit-mask.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Equal(PdfImageMaskKind.Explicit, image.MaskKind);
        PdfImage mask = image.Mask!;
        Assert.Equal((16, 16), (mask.Width, mask.Height));
        Assert.True(mask.IsStencil);
        using DecodedImage samples = image.Decode()!;
        using DecodedImage maskSamples = mask.Decode()!;
        Assert.Equal((16, 16, 2), (maskSamples.Width, maskSamples.Height, maskSamples.Stride));

        // The disc of radius 7 about (7.5, 7.5): sample 0 (inside) shows the image. Sampled at each image pixel's centre, the four
        // corner pixels fall outside it, as poppler renders them white.
        bool[] shown = new bool[16];
        byte[] coverage = new byte[16];
        for (int y = 0; y < 4; y++)
        {
            ImageRows.Stencil(maskSamples.GetRow(ImageRows.MaskIndex(y, 4, 16)), 16, mask.CreateDecodeMap(maskSamples).IsInverted, coverage);
            for (int x = 0; x < 4; x++)
            {
                shown[(4 * y) + x] = coverage[ImageRows.MaskIndex(x, 4, 16)] == 255;
            }
        }

        Assert.Equal([false, true, true, false, true, true, true, true, true, true, true, true, false, true, true, false], shown);
        Assert.Equal(new ushort[] { 0, 0, 255, 60, 0, 239, 120, 0, 223, 180, 0, 207 }, Row(samples, 0));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_colour_key_mask_masks_the_samples_whose_raw_values_are_all_in_range()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-color-key-mask.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage decoded = image.Decode()!;

        Assert.Equal(PdfImageMaskKind.ColorKey, image.MaskKind);
        Assert.Equal([250.0, 255.0, 0.0, 5.0, 0.0, 5.0], image.ColorKey!);
        Assert.Null(image.Mask);
        byte[] coverage = new byte[4];
        var rows = new List<byte[]>();
        for (int y = 0; y < 4; y++)
        {
            ImageRows.ColorKey(Row(decoded, y), 3, [.. image.ColorKey!], coverage);
            rows.Add([.. coverage]);
        }

        Assert.Equal(
            [[0, 0, 255, 255], [255, 0, 255, 0], [255, 255, 255, 0], [255, 255, 255, 0]],
            rows);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_soft_mask_of_another_size_decodes_as_gray_at_its_own_size()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-smask.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Equal(PdfImageMaskKind.Soft, image.MaskKind);
        Assert.Null(image.Matte);
        PdfImage softMask = image.Mask!;
        Assert.Same(PdfDeviceGrayColorSpace.Instance, softMask.ColorSpace);
        using DecodedImage alpha = softMask.Decode()!;
        Assert.Equal((2, 8, 1), (alpha.Width, alpha.Height, alpha.Components));
        Assert.Equal(new byte[] { 0, 16, 32, 48, 64, 80, 96, 112, 128, 144, 160, 176, 192, 208, 224, 240 }, alpha.Samples.ToArray());
        using DecodedImage colour = image.Decode()!;
        Assert.Equal(new ushort[] { 0, 60, 191, 60, 60, 175, 120, 60, 159, 180, 60, 143 }, Row(colour, 1));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Matte_pre_blending_is_undone_in_the_image_colour_space()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-smask-matte.pdf"));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        using DecodedImage colour = image.Decode()!;
        using DecodedImage alpha = image.Mask!.Decode()!;

        Assert.Equal([1.0, 1.0, 1.0], image.Matte!);
        float[] colours = new float[12];
        image.CreateDecodeMap(colour).Map(Raw(colour), colours);
        float[] opacity = new float[4];
        image.Mask!.CreateDecodeMap(alpha).Map(Raw(alpha), opacity);
        ComponentRange[] ranges = [.. Enumerable.Range(0, 3).Select(image.ColorSpace!.GetComponentRange)];
        ImageRows.Unpremultiply(colours, 3, opacity, [.. image.Matte!.Select(m => (float)m)], ranges);

        // The colours before pre-blending (generate.py MATTE_ORIGINAL), to within 8-bit rounding of c'; alpha 0 gives the matte.
        float[] expected = [1, 1, 1, 0, 128, 255, 200, 100, 50, 40, 80, 160];
        for (int i = 3; i < 12; i++)
        {
            expected[i] /= 255f;
        }

        Assert.Equal(expected, colours, (a, b) => Math.Abs(a - b) <= 3 / 255f);
        Assert.Empty(document.Diagnostics);
    }
}
