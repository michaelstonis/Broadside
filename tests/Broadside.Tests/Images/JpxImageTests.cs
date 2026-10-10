using System.Text;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Images;

/// <summary>
/// JPEG 2000 images read through the document API: the JP2 colour boxes (palette, component mapping, channel definitions, colour
/// specifications), the opacity channel with <c>SMaskInData</c>, and the PDF rule that the image dictionary's ColorSpace overrides the
/// JP2 one (ISO 32000-2 §7.4.9 and Table 87; ITU-T T.800 I.5.3). Inputs are lossless OpenJPEG vectors wrapped in JP2 boxes built
/// for each case, so the expected samples follow from the vector's source and the boxes' definitions.
/// </summary>
public sealed class JpxImageTests
{
    private static readonly int[][] Ramp = [.. Enumerable.Range(0, 256).Select(i => new[] { i, 255 - i, (i * 7) & 255 })];

    [Fact]
    public void Without_a_colour_space_a_palette_maps_each_index_to_its_entry()
    {
        JpxVector gray = JpxSamples.Vector("Rlcp1Layer");
        byte[] file = JpxEditing.Jp2(gray.Data, JpxEditing.Color(16), JpxEditing.Palette(Ramp), JpxEditing.Mapping((0, 1, 0), (0, 1, 1), (0, 1, 2)));

        (PdfImage image, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal((3, 8, ImageColorModel.Rgb), (decoded.Components, decoded.BitsPerComponent, decoded.ColorModel));
            Assert.Equal(gray.Expected().SelectMany(i => Ramp[i]), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.Same(PdfDeviceRgbColorSpace.Instance, image.ResolveColorSpace(decoded));
            Assert.Empty(ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void A_palette_index_past_the_last_entry_takes_the_last_entry_with_a_diagnostic()
    {
        JpxVector gray = JpxSamples.Vector("Rlcp1Layer");
        int[][] short64 = [.. Ramp.Take(64)];
        byte[] file = JpxEditing.Jp2(gray.Data, JpxEditing.Color(16), JpxEditing.Palette(short64), JpxEditing.Mapping((0, 1, 0), (0, 1, 1), (0, 1, 2)));

        (_, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(gray.Expected().SelectMany(i => short64[Math.Min(i, 63)]), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.Equal(["JpxPaletteIndexOutOfRange"], ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void With_an_Indexed_colour_space_the_codestream_indices_are_the_samples_and_the_palette_box_is_ignored()
    {
        JpxVector gray = JpxSamples.Vector("Rlcp1Layer");
        byte[] file = JpxEditing.Jp2(gray.Data, JpxEditing.Color(16), JpxEditing.Palette(Ramp), JpxEditing.Mapping((0, 1, 0), (0, 1, 1), (0, 1, 2)));
        string lookup = Convert.ToHexString([.. Ramp.SelectMany(e => e.Select(v => (byte)v))]);

        (_, DecodedImage decoded, PdfDocument document) = Decode(file, $"/ColorSpace [/Indexed /DeviceRGB 255 <{lookup}>] /BitsPerComponent 8");
        using (document)
        using (decoded)
        {
            Assert.Equal(1, decoded.Components);
            Assert.Equal(gray.Expected(), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.Empty(ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void Channel_definitions_put_the_colour_channels_in_association_order()
    {
        JpxVector rgb = JpxSamples.Vector("Lrcp3LayersRct");
        byte[] file = JpxEditing.Jp2(rgb.Data, JpxEditing.Color(16), JpxEditing.Definitions((0, 0, 3), (1, 0, 2), (2, 0, 1)));

        (_, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(Reversed(rgb.Expected(), 3), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.Empty(ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void With_SMaskInData_1_the_opacity_channel_becomes_the_alpha_plane()
    {
        JpxVector rgba = JpxSamples.Vector("Rgba");
        byte[] file = JpxEditing.Jp2(rgba.Data, JpxEditing.Color(16), JpxEditing.Definitions((0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 1, 0)));

        (PdfImage image, DecodedImage decoded, PdfDocument document) = Decode(file, "/SMaskInData 1");
        using (document)
        using (decoded)
        {
            int[] expected = rgba.Expected();
            Assert.Equal((3, ImageColorModel.Rgb, PdfImageMaskKind.SoftInData), (decoded.Components, decoded.ColorModel, image.MaskKind));
            Assert.Equal(expected.Where((_, i) => i % 4 != 3), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.NotNull(decoded.Alpha);
            Assert.False(decoded.AlphaPremultiplied);
            Assert.Equal(expected.Where((_, i) => i % 4 == 3), ImageTesting.Raw(decoded.Alpha!).Select(v => (int)v));
            Assert.Empty(ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void Without_SMaskInData_the_opacity_channel_is_ignored()
    {
        JpxVector rgba = JpxSamples.Vector("Rgba");
        byte[] file = JpxEditing.Jp2(rgba.Data, JpxEditing.Color(16), JpxEditing.Definitions((0, 0, 1), (1, 0, 2), (2, 0, 3), (3, 1, 0)));

        (_, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(3, decoded.Components);
            Assert.Null(decoded.Alpha);
            Assert.Equal(rgba.Expected().Where((_, i) => i % 4 != 3), ImageTesting.Raw(decoded).Select(v => (int)v));
        }
    }

    [Fact]
    public void A_raw_codestream_with_a_colour_space_and_SMaskInData_2_takes_the_channel_after_the_colours_as_premultiplied_alpha()
    {
        JpxVector rgba = JpxSamples.Vector("Rgba");

        (_, DecodedImage decoded, PdfDocument document) = Decode(rgba.Data, "/ColorSpace /DeviceRGB /SMaskInData 2");
        using (document)
        using (decoded)
        {
            int[] expected = rgba.Expected();
            Assert.Equal(3, decoded.Components);
            Assert.Equal(expected.Where((_, i) => i % 4 != 3), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.True(decoded.AlphaPremultiplied);
            Assert.Equal(expected.Where((_, i) => i % 4 == 3), ImageTesting.Raw(decoded.Alpha!).Select(v => (int)v));
            Assert.Equal(["JpxCodestreamRaw"], ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void The_colour_space_of_the_image_dictionary_overrides_the_JP2_colour_specification()
    {
        JpxVector rgb = JpxSamples.Vector("Lrcp3LayersRct");
        byte[] file = JpxEditing.Jp2(rgb.Data, JpxEditing.Color(18));

        (PdfImage image, DecodedImage decoded, PdfDocument document) = Decode(file, "/ColorSpace /DeviceRGB /BitsPerComponent 8");
        using (document)
        using (decoded)
        {
            Assert.Equal(rgb.Expected(), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.Equal(ImageColorModel.Unknown, decoded.ColorModel);
            Assert.Same(PdfDeviceRgbColorSpace.Instance, image.ResolveColorSpace(decoded));
        }
    }

    [Fact]
    public void Without_a_colour_space_sYCC_samples_are_converted_to_RGB()
    {
        JpxVector ycc = JpxSamples.Vector("Lrcp3LayersRct");
        byte[] file = JpxEditing.Jp2(ycc.Data, JpxEditing.Color(18));

        (_, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            // IEC 61966-2-1 Annex G (sYCC), as OpenJPEG's sycc_to_rgb: chroma offset by 128, products truncated toward zero.
            int[] source = ycc.Expected();
            int[] expected = new int[source.Length];
            for (int i = 0; i < source.Length; i += 3)
            {
                int y = source[i];
                int cb = source[i + 1] - 128;
                int cr = source[i + 2] - 128;
                expected[i] = Math.Clamp(y + (int)(1.402f * cr), 0, 255);
                expected[i + 1] = Math.Clamp(y - (int)((0.344f * cb) + (0.714f * cr)), 0, 255);
                expected[i + 2] = Math.Clamp(y + (int)(1.772f * cb), 0, 255);
            }

            Assert.Equal(ImageColorModel.Rgb, decoded.ColorModel);
            Assert.Equal(expected, ImageTesting.Raw(decoded).Select(v => (int)v));
        }
    }

    [Fact]
    public void The_colour_specification_of_highest_precedence_wins()
    {
        JpxVector rgb = JpxSamples.Vector("Lrcp3LayersRct");
        byte[] file = JpxEditing.Jp2(rgb.Data, JpxEditing.Color(18, precedence: 0), JpxEditing.Color(16, precedence: 1));

        (_, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(rgb.Expected(), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.Empty(ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void An_unusable_ICC_profile_gives_way_to_the_next_colour_specification_with_information()
    {
        JpxVector rgb = JpxSamples.Vector("Lrcp3LayersRct");
        byte[] file = JpxEditing.Jp2(rgb.Data, JpxEditing.Color(Encoding.ASCII.GetBytes("not a profile"), precedence: 2), JpxEditing.Color(16));

        (PdfImage image, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(rgb.Expected(), ImageTesting.Raw(decoded).Select(v => (int)v));
            Assert.True(decoded.IccProfile.IsEmpty);
            Assert.Same(PdfDeviceRgbColorSpace.Instance, image.ResolveColorSpace(decoded));
            Assert.Equal(["JpxColorSpecificationUnsupported"], ImageTesting.Codes(document));
        }
    }

    [Fact]
    public void An_ICC_profile_is_handed_on_and_becomes_an_ICCBased_colour_space()
    {
        JpxVector rgb = JpxSamples.Vector("Lrcp3LayersRct");
        byte[] profile = JpxEditing.IccHeader("RGB ");
        byte[] file = JpxEditing.Jp2(rgb.Data, JpxEditing.Color(profile));

        (PdfImage image, DecodedImage decoded, PdfDocument document) = Decode(file, string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(profile, decoded.IccProfile.ToArray());
            Assert.Equal(ImageColorModel.Rgb, decoded.ColorModel);
            PdfIccBasedColorSpace space = Assert.IsType<PdfIccBasedColorSpace>(image.ResolveColorSpace(decoded));
            Assert.Equal(3, space.ComponentCount);
            Assert.Equal(rgb.Expected(), ImageTesting.Raw(decoded).Select(v => (int)v));
        }
    }

    [Fact]
    public void A_greyscale_colour_specification_gives_the_gray_colour_model()
    {
        JpxVector gray = JpxSamples.Vector("NoDecomposition");

        (PdfImage image, DecodedImage decoded, PdfDocument document) = Decode(JpxEditing.Jp2(gray.Data, JpxEditing.Color(17)), string.Empty);
        using (document)
        using (decoded)
        {
            Assert.Equal(ImageColorModel.Gray, decoded.ColorModel);
            Assert.Same(PdfDeviceGrayColorSpace.Instance, image.ResolveColorSpace(decoded));
        }
    }

    private static (PdfImage Image, DecodedImage Decoded, PdfDocument Document) Decode(byte[] data, string entries)
    {
        int siz = data.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xFF, 0x4F, 0xFF, 0x51]) + 4;
        uint width = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(siz + 4));
        uint height = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(siz + 8));
        PdfDocument document = PdfDocument.Open(ImageTesting.OneImage($"/Width {width} /Height {height} /Filter /JPXDecode {entries}", Encoding.Latin1.GetString(data)));
        PdfImage image = document.Pages[0].GetImage("Im0")!;
        DecodedImage? decoded = image.Decode();
        Assert.NotNull(decoded);
        return (image, decoded, document);
    }

    private static IEnumerable<int> Reversed(int[] samples, int components) =>
        samples.Select((_, i) => samples[(i - (i % components)) + (components - 1 - (i % components))]);
}
