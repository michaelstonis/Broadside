using System.IO.Compression;
using System.Text;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #362, part 4: images whose filters cannot reduce (Flate, raw samples, CCITT, JBIG2) are decoded in full when the list is
/// built and, drawn small by a viewer, reduced from that decode by an exact box average before conversion, when the conversion is
/// affine; 1-bit images and image masks become 8-bit coverage; Indexed images and curved conversions keep today's order (convert,
/// then average); masks are reduced by their own rule. And the colour-order bar: where a codec's reduction is capped for a curved
/// conversion, the page stays within 45 dB (max 40 levels) of converting first.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class RowReductionRenderingTests
{
    // A 1024 x 1024 image over 512 x 512 points: at a quarter scale, 8 samples per device pixel, so 2 per pixel keep a quarter.
    private const string Paint = "q 512 0 0 512 50 100 cm /Im0 Do Q";

    [Theory]
    [InlineData("/DeviceGray", 1, 8)]
    [InlineData("/DeviceRGB", 3, 8)]
    [InlineData("/DeviceGray", 1, 16)]
    public void A_flate_image_drawn_small_is_the_exact_box_average_of_its_full_decode(string space, int components, int bits)
    {
        byte[] samples = Samples(1024, 1024, components, bits);
        using PdfDocument document = Open(Image(samples, 1024, 1024, space, bits));
        DisplayImage image = Image(document);

        DisplayImageSamples reduced = image.GetSamplesAt(2, TestContext.Current.CancellationToken)!.Value;

        Assert.Equal((256, 256, 2, bits), (reduced.Width, reduced.Height, reduced.Reduction, reduced.BitsPerComponent));
        Assert.Equal(BoxAverage(samples, 1024, 1024, components, bits, 4), reduced.Samples.ToArray());
        Assert.Equal(reduced, image.GetSamplesAt(2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_one_bit_image_becomes_8_bit_coverage_with_its_decode_mapping()
    {
        // A 1-bit DeviceGray image with Decode [1 0]: the reduced samples are the share of 1s, scaled to 255, mapped through [1 0].
        byte[] bits = new byte[1024 / 8 * 1024];
        for (int i = 0; i < bits.Length; i++)
        {
            bits[i] = (byte)((i * 2654435761u) >> 13);
        }
        using PdfDocument document = Open(Image(bits, 1024, 1024, "/DeviceGray", 1, "/Decode [1 0]"));
        DisplayImage image = Image(document);

        DisplayImageSamples reduced = image.GetSamplesAt(3, TestContext.Current.CancellationToken)!.Value;

        Assert.Equal((128, 128, 8, 8), (reduced.Width, reduced.Height, reduced.BitsPerComponent, reduced.StorageBits));
        int ones = 0;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                ones += (bits[(y * 128) + (x / 8)] >> (7 - (x % 8))) & 1;
            }
        }

        Assert.Equal((byte)(((ones * 255) + 32) / 64), reduced.Samples[0]);
        Assert.Equal(1f, image.DecodeMapFor(reduced).Map(0, 0));
        Assert.Equal(0f, image.DecodeMapFor(reduced).Map(0, 255));
        Assert.Same(image.DecodeMapFor(reduced), image.DecodeMapFor(reduced));
    }

    [Theory]
    [InlineData("DeviceGray", 1, 3)]
    [InlineData("DeviceRGB", 3, 3)]
    [InlineData("DeviceCMYK", 4, 0)]
    [InlineData("Indexed", 1, 0)]
    [InlineData("Separation", 1, 0)]
    public void An_image_decoded_at_build_reduces_only_through_an_affine_conversion(string family, int components, int expected)
    {
        string space = Space(family);
        using PdfDocument document = Open(Image(Samples(1024, 1024, components, 8), 1024, 1024, space, 8));
        DisplayImage image = Image(document);

        Assert.Equal(expected, image.GetReduction(new Matrix(64, 0, 0, 64, 0, 0), 2, document.GetColorConverter(image.ColorSpace)));
    }

    [Fact]
    public void A_small_image_decoded_at_build_is_not_reduced()
    {
        using PdfDocument document = Open(Image(Samples(200, 200, 1, 8), 200, 200, "/DeviceGray", 8));

        Assert.Equal(0, Image(document).MaxReduction);
    }

    public static TheoryData<string> Pages => new() { "gray", "rgb16", "one-bit", "stencil", "soft-mask-same-size", "soft-mask-own-size", "explicit-mask" };

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_viewer_render_stays_within_the_page_tolerance_of_a_full_resolution_render(string page)
    {
        using PdfDocument document = Open(PageFile(page));

        using Bitmap reduced = Render(document, 0.25, scaled: true);
        using Bitmap full = Render(document, 0.25, scaled: false);

        (double psnr, double over8, _) = Compare(full, reduced);
        Assert.True(psnr >= 35, $"{page}: PSNR {psnr:F1} dB");
        Assert.True(over8 <= 0.04, $"{page}: {over8:P2} of pixels over 8 levels");
    }

    [Theory]
    [InlineData("Tint", 1)]
    [InlineData("CalRGB", 3)]
    [InlineData("Lab", 3)]
    [InlineData("DeviceCMYK", 4)]
    public void A_codec_reduction_capped_for_a_curved_conversion_stays_within_45_dB_of_converting_first(string family, int components)
    {
        string space = Space(family);
        // The reducing codec delivers the exact box average in sample space; the cap leaves at most 1/2 to it and the rest to the
        // rasterizer after conversion. The reference converts every sample first (no reduction at all).
        var codec = new ReducingImageFilter();
        using PdfDocument document = PdfDocument.Open(
            ReducingImageFilter.Page(ReducingImageFilter.Photo(1024, 1024, components), 1024, 1024, space, Paint), new PdfOptions().UseFilter(codec));

        using Bitmap reduced = Render(document, 0.0625, scaled: true);
        using Bitmap full = Render(document, 0.0625, scaled: false);

        Assert.Equal(1, codec.DecodesAt(1));
        (double psnr, _, int max) = Compare(full, reduced);
        Assert.True(psnr >= 45, $"PSNR {psnr:F1} dB");
        Assert.True(max <= 40, $"max {max}");
    }

    [Fact]
    public void Replaying_a_list_whose_reduced_flate_image_is_made_allocates_nothing()
    {
        using PdfDocument document = Open(PageFile("soft-mask-same-size"));
        PdfPage page = document.Pages[0];
        DisplayList list = page.CreateDisplayList();
        RenderOptions options = new RenderOptions().WithScale(0.25);
        using var bitmap = new Bitmap(options.GetPixelSize(page).Width, options.GetPixelSize(page).Height);
        Matrix device = options.GetDeviceTransform(page);
        using var rasterizer = new SoftwareRasterizer { ScaledImageDecode = true };
        Action render = () => rasterizer.Render(list, bitmap, device);

        Assert.Equal(0, Allocations.Measure(render, warmUpCalls: 3));
    }

    private static string Space(string family) => family switch
    {
        "Indexed" => "[/Indexed /DeviceRGB 255 <00>]",
        "Separation" => "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 2 >>]",
        "Tint" => "[/Separation /Spot /DeviceRGB << /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0.2 0.6 0.9] /N 2 >>]",
        "CalRGB" => "[/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [2.2 2.2 2.2] >>]",
        "Lab" => "[/Lab << /WhitePoint [0.9505 1 1.089] /Range [-100 100 -100 100] >>]",
        _ => "/" + family,
    };

    private static PdfDocument Open(byte[] file) => PdfDocument.Open(file, new PdfOptions().WithDisplayListCacheBudget(0));

    private static DisplayImage Image(PdfDocument document)
    {
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return Assert.Single(recorder.Images).Image;
    }

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

    private static byte[] PageFile(string page)
    {
        byte[] gray = Samples(1024, 1024, 1, 8);
        return page switch
        {
            "gray" => Image(gray, 1024, 1024, "/DeviceGray", 8),
            "rgb16" => Image(Samples(1024, 1024, 3, 16), 1024, 1024, "/DeviceRGB", 16),
            "one-bit" => Image(Dither(gray, 1024, 1024), 1024, 1024, "/DeviceGray", 1),
            "stencil" => File("0.8 0.1 0.1 rg " + Paint, Stream(Dither(gray, 1024, 1024), "/Width 1024 /Height 1024 /ImageMask true")),
            "soft-mask-same-size" => File(Paint, Stream(Samples(1024, 1024, 3, 8), "/Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R"), Stream(gray, "/Width 1024 /Height 1024 /ColorSpace /DeviceGray /BitsPerComponent 8")),
            "soft-mask-own-size" => File(Paint, Stream(Samples(1024, 1024, 3, 8), "/Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R"), Stream(Samples(700, 500, 1, 8), "/Width 700 /Height 500 /ColorSpace /DeviceGray /BitsPerComponent 8")),
            _ => File(Paint, Stream(Samples(1024, 1024, 3, 8), "/Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask 6 0 R"), Stream(Dither(Samples(900, 600, 1, 8), 900, 600), "/Width 900 /Height 600 /ImageMask true")),
        };
    }

    private static byte[] Image(byte[] samples, int width, int height, string space, int bits, string extra = "") =>
        File(Paint, Stream(samples, FormattableString.Invariant($"/Width {width} /Height {height} /ColorSpace {space} /BitsPerComponent {bits} {extra}")));

    private static byte[] File(string content, params string[] objects)
    {
        var names = new StringBuilder("/XObject << /Im0 5 0 R >>");
        return ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), names.ToString(), objects);
    }

    /// <summary>An image XObject compressed with Flate.</summary>
    private static string Stream(byte[] samples, string entries)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest))
        {
            zlib.Write(samples);
        }

        return ContentSamples.Stream(Encoding.Latin1.GetString(compressed.ToArray()), "/Type /XObject /Subtype /Image /Filter /FlateDecode " + entries);
    }

    private static byte[] Samples(int width, int height, int components, int bits)
    {
        byte[] photo = ReducingImageFilter.Photo(width, height, components);
        if (bits == 8)
        {
            return photo;
        }

        byte[] wide = new byte[photo.Length * 2];
        for (int i = 0; i < photo.Length; i++)
        {
            int value = (photo[i] * 257) ^ (i % 97);
            wide[2 * i] = (byte)(value >> 8);
            wide[(2 * i) + 1] = (byte)value;
        }

        return wide;
    }

    /// <summary>Ordered-dither a gray image to 1 bit per sample (1 = white), rows padded to bytes.</summary>
    private static byte[] Dither(byte[] gray, int width, int height)
    {
        int stride = (width + 7) / 8;
        byte[] bits = new byte[stride * height];
        int[] bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (gray[(y * width) + x] > (bayer[((y & 3) * 4) + (x & 3)] * 16) + 8)
                {
                    bits[(y * stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return bits;
    }

    /// <summary>The exact box average, rounded half up, of 8- or 16-bit samples (16-bit big-endian).</summary>
    private static byte[] BoxAverage(byte[] samples, int width, int height, int components, int bits, int scale)
    {
        int bytes = bits / 8;
        int reducedWidth = (width + scale - 1) / scale;
        int reducedHeight = (height + scale - 1) / scale;
        byte[] output = new byte[reducedWidth * reducedHeight * components * bytes];
        for (int y = 0; y < reducedHeight; y++)
        {
            for (int x = 0; x < reducedWidth; x++)
            {
                for (int c = 0; c < components; c++)
                {
                    long sum = 0;
                    int count = 0;
                    for (int sy = y * scale; sy < Math.Min(height, (y + 1) * scale); sy++)
                    {
                        for (int sx = x * scale; sx < Math.Min(width, (x + 1) * scale); sx++)
                        {
                            int at = (((sy * width) + sx) * components) + c;
                            sum += bytes == 1 ? samples[at] : (samples[2 * at] << 8) | samples[(2 * at) + 1];
                            count++;
                        }
                    }

                    long value = (sum + (count / 2)) / count;
                    int o = (((y * reducedWidth) + x) * components) + c;
                    if (bytes == 1)
                    {
                        output[o] = (byte)value;
                    }
                    else
                    {
                        output[2 * o] = (byte)(value >> 8);
                        output[(2 * o) + 1] = (byte)value;
                    }
                }
            }
        }

        return output;
    }

    private static (double Psnr, double Over8, int Max) Compare(Bitmap a, Bitmap b)
    {
        ReadOnlySpan<byte> x = a.Pixels;
        ReadOnlySpan<byte> y = b.Pixels;
        double squared = 0;
        int over = 0;
        int max = 0;
        int pixels = x.Length / 4;
        for (int i = 0; i < x.Length; i += 4)
        {
            int largest = 0;
            for (int c = 0; c < 3; c++)
            {
                int difference = Math.Abs(x[i + c] - y[i + c]);
                squared += difference * difference;
                largest = Math.Max(largest, difference);
            }

            max = Math.Max(max, largest);
            over += largest > 8 ? 1 : 0;
        }

        double mse = squared / (pixels * 3.0);
        return (mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), over / (double)pixels, max);
    }
}
