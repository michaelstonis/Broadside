using System.Buffers;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The baseline JPEG encoder through the encoder contract: the same bytes as libjpeg-turbo for the same settings, decoding through
/// the DCT filter close to its input, and the parameters it refuses. ISO 32000-2 §7.4.8; ITU-T T.81 Annex A, F.1 and K.
/// </summary>
public sealed class DctEncoderTests
{
    public static TheoryData<string, string, int, DctChromaSubsampling, string?> LibjpegVectors => new()
    {
        // make_encoder_vectors.py: TurboJPEG 3.2.0, integer DCT, Annex K Huffman tables, IJG quality scaling.
        { "encoded-rgb-q90-444", "rgb", 90, DctChromaSubsampling.None, null },
        { "encoded-rgb-q90-422", "rgb", 90, DctChromaSubsampling.Horizontal, null },
        { "encoded-rgb-q90-420", "rgb", 90, DctChromaSubsampling.HorizontalAndVertical, null },
        { "encoded-rgb-q75-420", "rgb", 75, DctChromaSubsampling.HorizontalAndVertical, null },
        { "encoded-rgb-q10-420", "rgb", 10, DctChromaSubsampling.HorizontalAndVertical, null },
        { "encoded-rgb-q100-444", "rgb", 100, DctChromaSubsampling.None, null },
        { "encoded-rgb-untransformed-q90", "rgb", 90, DctChromaSubsampling.HorizontalAndVertical, "<< /ColorTransform 0 >>" },
        { "encoded-gray-q90", "dct-baseline-gray", 90, DctChromaSubsampling.HorizontalAndVertical, null },
        { "encoded-cmyk-q90", "cmyk", 90, DctChromaSubsampling.HorizontalAndVertical, null },
        { "encoded-ycck-q90-420", "cmyk", 90, DctChromaSubsampling.HorizontalAndVertical, "<< /ColorTransform 1 >>" },
    };

    [Theory]
    [MemberData(nameof(LibjpegVectors))]
    public void The_output_equals_libjpeg_turbo_byte_for_byte(string vector, string source, int quality, DctChromaSubsampling subsampling, string? parameters)
    {
        // Integer arithmetic only, so these bytes are what every platform writes.
        DctGolden image = Golden(source);

        byte[] encoded = Encode(new DctEncoder(quality, subsampling), image, parameters);

        Assert.Equal(Jpeg(vector), encoded);
    }

    public static TheoryData<string, DctChromaSubsampling, double> Psnr90 => new()
    {
        // The stated PSNR at quality 90 (ticket #143): 38 dB in every layout, more where the measurement allows it. testorig
        // (227 x 149 RGB, itself decoded from 4:2:0, hence no loss from 4:2:0) measures 43.5 / 44.3 / 45.9 dB at 4:4:4 / 4:2:2 /
        // 4:2:0; its luminance 51.3 dB; the 75 x 43 CMYK picture 52.0 dB untransformed and 40.2 dB as YCCK 4:2:0.
        { "rgb", DctChromaSubsampling.None, 40 },
        { "rgb", DctChromaSubsampling.Horizontal, 40 },
        { "rgb", DctChromaSubsampling.HorizontalAndVertical, 40 },
        { "gray", DctChromaSubsampling.HorizontalAndVertical, 48 },
        { "cmyk", DctChromaSubsampling.HorizontalAndVertical, 48 },
        { "ycck", DctChromaSubsampling.HorizontalAndVertical, 38 },
    };

    [Theory]
    [MemberData(nameof(Psnr90))]
    public void At_quality_90_the_DCT_filter_decodes_the_output_above_the_stated_PSNR(string kind, DctChromaSubsampling subsampling, double minimum)
    {
        (DctGolden image, string? parameters) = kind switch
        {
            "gray" => (Luminance(Golden("testorig")), null),
            "cmyk" => (Golden("cmyk"), null),
            "ycck" => (Golden("cmyk"), "<< /ColorTransform 1 >>"),
            _ => (Golden("testorig"), (string?)null),
        };

        byte[] encoded = Encode(new DctEncoder(90, subsampling), image, parameters);
        (byte[] decoded, FilterContext context) = DecodeBytes(encoded, parameters, PdfReadingMode.Strict);

        Assert.Empty(Codes(context));
        Assert.Equal(image.Samples.Length, decoded.Length);
        double psnr = Psnr(image.Samples, decoded);
        Assert.True(psnr >= minimum, $"PSNR {psnr:F2} dB is below {minimum} dB.");
    }

    [Theory]
    [InlineData("Im0", "rgb", DctChromaSubsampling.HorizontalAndVertical, 40)]
    [InlineData("Im0", "rgb", DctChromaSubsampling.None, double.PositiveInfinity)]
    [InlineData("Im1", "dct-baseline-gray", DctChromaSubsampling.None, double.PositiveInfinity)]
    public void A_saved_PDF_with_an_encoded_image_renders_in_poppler_like_our_own_decode(string xobject, string source, DctChromaSubsampling subsampling, double minimum)
    {
        // dct-baseline.pdf's image replaced by a quality 90 encoding, drawn alone on a page of its size and rendered by pdftoppm at
        // 720 dpi: the centre of each 10 x 10 pixel cell is one sample (at 72 dpi poppler resamples). Poppler decodes with libjpeg,
        // whose 4:2:0 upsampling interpolates where ours replicates (43 dB apart); without subsampling the samples are identical.
        DctGolden image = Golden(source);
        byte[] saved;
        using (PdfDocument document = PdfDocument.Open(Corpus.Path("dct-baseline.pdf"), new PdfOptions().UseStreamEncoder(new DctEncoder(90, subsampling))))
        {
            PdfPage page = document.Pages[0];
            var xobjects = (CosDictionary)document.Resolve(page.Resources![new CosName("XObject")]);
            var stream = (CosStream)document.Resolve(xobjects[new CosName(xobject)]);
            stream.Dictionary[new CosName("Width")] = new CosInteger(image.Width);
            stream.Dictionary[new CosName("Height")] = new CosInteger(image.Height);
            document.EncodeStream(stream, image.Samples);
            page.Dictionary[new CosName("MediaBox")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(image.Width), new CosInteger(image.Height)]);
            var contents = (CosStream)document.Resolve(page.Dictionary[new CosName("Contents")]);
            document.EncodeStream(contents, Encoding.ASCII.GetBytes(FormattableString.Invariant($"q {image.Width} 0 0 {image.Height} 0 0 cm /{xobject} Do Q")));
            using var output = new MemoryStream();
            document.Save(output);
            saved = output.ToArray();
        }

        byte[] ours;
        using (PdfDocument reopened = PdfDocument.Open(saved))
        {
            var xobjects = (CosDictionary)reopened.Resolve(reopened.Pages[0].Resources![new CosName("XObject")]);
            ours = reopened.DecodeStream((CosStream)reopened.Resolve(xobjects[new CosName(xobject)])).ToArray();
            Assert.Empty(reopened.Diagnostics);
        }

        const int scale = 10;
        string[] arguments = image.Components == 1 ? ["-r", "720", "-gray"] : ["-r", "720"];
        (int exitCode, byte[] rendered, string error) = Writing.ExternalTool.RunBinary("pdftoppm", saved, arguments);
        Assert.True(exitCode == 0, error);
        DctGolden render = ParsePnm(rendered);
        Assert.Equal((image.Width * scale, image.Height * scale, image.Components), (render.Width, render.Height, render.Components));
        byte[] poppler = new byte[image.Samples.Length];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int centre = ((((y * scale) + (scale / 2)) * render.Width) + (x * scale) + (scale / 2)) * image.Components;
                render.Samples.AsSpan(centre, image.Components).CopyTo(poppler.AsSpan(((y * image.Width) + x) * image.Components));
            }
        }

        double psnr = Psnr(ours, poppler);
        Assert.True(psnr >= minimum, $"Poppler's rendering differs from our decode: PSNR {psnr:F2} dB.");
        Assert.True(Psnr(image.Samples, poppler) >= 38, "Poppler's rendering is far from the encoded picture.");
    }

    [Fact]
    public void The_defaults_are_libjpegs_quality_75_with_chroma_halved_both_ways()
    {
        var encoder = new DctEncoder();

        Assert.Equal(75, encoder.Quality);
        Assert.Equal(DctEncoder.DefaultQuality, encoder.Quality);
        Assert.Equal(DctChromaSubsampling.HorizontalAndVertical, encoder.ChromaSubsampling);
        Assert.Equal("DCTDecode", encoder.Name.Value);
        Assert.Equal(Jpeg("encoded-rgb-q75-420"), Encode(encoder, Golden("rgb"), null));
    }

    [Fact]
    public void Encoding_the_same_samples_twice_gives_the_same_bytes()
    {
        DctGolden image = Golden("testorig");

        byte[] first = Encode(new DctEncoder(85), image, null);
        byte[] second = Encode(new DctEncoder(85), image, null);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(9, 17)]
    [InlineData(16, 16)]
    [InlineData(33, 2)]
    public void Images_of_any_size_decode_to_their_size_in_every_layout(int width, int height)
    {
        foreach (int components in new[] { 1, 3, 4 })
        {
            foreach (DctChromaSubsampling subsampling in Enum.GetValues<DctChromaSubsampling>())
            {
                byte[] samples = new byte[width * height * components];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = (byte)((i * 37) + (i / 7));
                }

                var image = new DctGolden(width, height, components, samples);
                string? parameters = components == 4 ? "<< /ColorTransform 1 >>" : null;
                byte[] decoded = DecodeBytes(Encode(new DctEncoder(95, subsampling), image, parameters), parameters, PdfReadingMode.Strict).Samples;

                Assert.Equal(samples.Length, decoded.Length);
            }
        }
    }

    public static TheoryData<string, int> Unencodable => new()
    {
        { "<< /Height 2 >>", 12 },
        { "<< /Width 2 >>", 12 },
        { "<< /Width 0 /Height 2 >>", 0 },
        { "<< /Width 65536 /Height 1 >>", 65536 },
        { "<< /Width 2 /Height 2 >>", 0 },
        { "<< /Width 2 /Height 2 >>", 8 },
        { "<< /Width 2 /Height 2 >>", 9 },
        { "<< /Width 2 /Height 2 /BitsPerComponent 16 >>", 24 },
        { "<< /Width 2 /Height 2 /ColorSpace /DeviceGray >>", 12 },
        { "<< /Width 2 /Height 2 /ColorSpace /DeviceRGB >>", 16 },
        { "<< /Width 2 /Height 2 /ColorSpace /DeviceCMYK >>", 4 },
        { "<< /Width 2 /Height 2 /ImageMask true >>", 4 },
    };

    [Theory]
    [MemberData(nameof(Unencodable))]
    public void Samples_the_dictionary_does_not_describe_are_rejected(string dictionary, int length)
    {
        var context = new StreamEncoderContext { StreamDictionary = (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes(dictionary)) };

        Assert.Throws<ArgumentException>(() => new DctEncoder().Encode(new byte[length], new ArrayBufferWriter<byte>(), context));
    }

    [Theory]
    [InlineData("<< /ColorTransform 2 >>")]
    [InlineData("<< /ColorTransform /Yes >>")]
    public void A_colour_transform_other_than_0_or_1_is_rejected(string parameters)
    {
        var context = new StreamEncoderContext
        {
            StreamDictionary = (CosDictionary)CosObject.Parse("<< /Width 2 /Height 2 >>"u8.ToArray()),
            Parameters = (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes(parameters)),
        };

        Assert.Throws<ArgumentException>(() => new DctEncoder().Encode(new byte[12], new ArrayBufferWriter<byte>(), context));
    }

    [Fact]
    public void Quality_outside_1_to_100_and_an_unknown_subsampling_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DctEncoder(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DctEncoder(101));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DctEncoder(90, (DctChromaSubsampling)3));
    }

    [Fact]
    public void A_null_output_or_context_is_rejected()
    {
        var encoder = new DctEncoder();

        Assert.Throws<ArgumentNullException>(() => encoder.Encode([1], null!, new StreamEncoderContext()));
        Assert.Throws<ArgumentNullException>(() => encoder.Encode([1], new ArrayBufferWriter<byte>(), null!));
    }

    /// <summary>Encodes an image through the contract with a stand-alone context.</summary>
    internal static byte[] Encode(IStreamEncoder encoder, DctGolden image, string? parameters)
    {
        var output = new ArrayBufferWriter<byte>();
        encoder.Encode(image.Samples, output, Context(image, parameters));
        return output.WrittenSpan.ToArray();
    }

    /// <summary>A stand-alone context for an image: its Width, Height, BitsPerComponent and device colour space.</summary>
    internal static StreamEncoderContext Context(DctGolden image, string? parameters) => new()
    {
        StreamDictionary = ImageDictionary(image),
        Parameters = parameters is null ? null : (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes(parameters)),
    };

    /// <summary>The image dictionary entries of an 8-bit device image.</summary>
    internal static CosDictionary ImageDictionary(DctGolden image) => new()
    {
        [new CosName("Width")] = new CosInteger(image.Width),
        [new CosName("Height")] = new CosInteger(image.Height),
        [new CosName("BitsPerComponent")] = new CosInteger(8),
        [new CosName("ColorSpace")] = new CosName(image.Components switch { 1 => "DeviceGray", 3 => "DeviceRGB", _ => "DeviceCMYK" }),
    };

    /// <summary>Peak signal-to-noise ratio of 8-bit samples, in dB.</summary>
    internal static double Psnr(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        double sum = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            double difference = expected[i] - actual[i];
            sum += difference * difference;
        }

        double mse = sum / expected.Length;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
    }

    /// <summary>A gray picture: the Rec. 601 luminance of an RGB one.</summary>
    private static DctGolden Luminance(DctGolden rgb)
    {
        byte[] gray = new byte[rgb.Width * rgb.Height];
        for (int i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)(((299 * rgb.Samples[3 * i]) + (587 * rgb.Samples[(3 * i) + 1]) + (114 * rgb.Samples[(3 * i) + 2]) + 500) / 1000);
        }

        return new DctGolden(rgb.Width, rgb.Height, 1, gray);
    }
}
