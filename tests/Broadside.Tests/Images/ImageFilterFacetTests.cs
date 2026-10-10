using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using static Broadside.Tests.Images.ImageTesting;

namespace Broadside.Tests.Images;

/// <summary>
/// The image-codec facet (<see cref="IImageFilter"/>) and the decoded-image buffer as the contract later codecs fill, exercised with
/// a fake codec registered through the options: what the codec reports wins over the dictionary where §7.4.9 says so.
/// </summary>
public class ImageFilterFacetTests
{
    [Fact]
    public void The_codec_size_wins_over_the_dictionary_with_a_diagnostic()
    {
        var codec = new FakeImageFilter("JPXDecode") { Make = context => Gray(context, 2, 3, 7) };
        using PdfDocument document = Open(codec, "/Width 4 /Height 4 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /JPXDecode");
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;
        Assert.Equal((2, 3), (decoded.Width, decoded.Height));
        Assert.All(decoded.Samples.ToArray(), value => Assert.Equal(7, value));
        Assert.Equal(["ImageDimensionMismatch"], Codes(document));
        Assert.Equal((4, 4, 8, 1, false, false), (codec.Context!.Width, codec.Context.Height, codec.Context.BitsPerComponent, codec.Context.ColorComponents, codec.Context.IsMask, codec.Context.WantsAlpha));
    }

    [Theory]
    [InlineData(1, ImageColorModel.Unknown, PdfColorSpaceFamily.DeviceGray)]
    [InlineData(3, ImageColorModel.Unknown, PdfColorSpaceFamily.DeviceRgb)]
    [InlineData(4, ImageColorModel.Unknown, PdfColorSpaceFamily.DeviceCmyk)]
    [InlineData(3, ImageColorModel.Rgb, PdfColorSpaceFamily.DeviceRgb)]
    [InlineData(1, ImageColorModel.Gray, PdfColorSpaceFamily.DeviceGray)]
    public void A_JPEG_2000_image_without_a_colour_space_takes_the_codec_colour_model_or_its_component_count(int components, ImageColorModel model, PdfColorSpaceFamily family)
    {
        var codec = new FakeImageFilter("JPXDecode")
        {
            Make = context =>
            {
                context.TryCreateImage(2, 1, components, 8, out DecodedImageBuilder? builder);
                builder!.ColorModel = model;
                builder.Samples.Fill(255);
                return builder.Build();
            },
        };
        using PdfDocument document = Open(codec, "/Width 2 /Height 1 /Filter /JPXDecode /Decode [1 0 1 0 1 0 1 0]");
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Null(image.ColorSpace);
        Assert.Equal(0, image.BitsPerComponent);
        Assert.Empty(image.DecodeArray);
        using DecodedImage decoded = image.Decode()!;
        Assert.Equal(family, image.ResolveColorSpace(decoded)!.Family);
        Assert.Equal(1f, image.CreateDecodeMap(decoded).Map(0, 255));
        Assert.Equal(0, codec.Context!.ColorComponents);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void SMaskInData_asks_the_codec_for_its_opacity_channel_and_selects_it_as_the_mask(int smaskInData, bool premultiplied)
    {
        var codec = new FakeImageFilter("JPXDecode")
        {
            Make = context =>
            {
                context.TryCreateImage(2, 1, 3, 8, out DecodedImageBuilder? builder);
                if (context.WantsAlpha)
                {
                    builder!.TryCreateAlpha(8, premultiplied, out DecodedImageBuilder alpha);
                    alpha.Samples[0] = 0x40;
                    alpha.Samples[1] = 0xC0;
                }

                return builder!.Build();
            },
        };
        using PdfDocument document = Open(codec, $"/Width 2 /Height 1 /ColorSpace /DeviceRGB /Filter /JPXDecode /SMaskInData {smaskInData}");
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Equal(PdfImageMaskKind.SoftInData, image.MaskKind);
        Assert.Equal(smaskInData, image.SoftMaskInData);
        using DecodedImage decoded = image.Decode()!;
        Assert.True(codec.Context!.WantsAlpha);
        Assert.Equal(new byte[] { 0x40, 0xC0 }, decoded.Alpha!.Samples.ToArray());
        Assert.Equal(premultiplied, decoded.AlphaPremultiplied);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Without_SMaskInData_the_codec_is_not_asked_for_opacity_and_the_image_is_not_masked()
    {
        var codec = new FakeImageFilter("JPXDecode") { Make = context => Gray(context, 2, 1, 0) };
        using PdfDocument document = Open(codec, "/Width 2 /Height 1 /ColorSpace /DeviceGray /Filter /JPXDecode");
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;
        Assert.False(codec.Context!.WantsAlpha);
        Assert.Equal(PdfImageMaskKind.None, image.MaskKind);
        Assert.Null(decoded.Alpha);
    }

    [Fact]
    public void Samples_a_codec_leaves_inverted_are_folded_into_the_decode_mapping()
    {
        var stencilCodec = new FakeImageFilter("JBIG2Decode")
        {
            Make = context =>
            {
                context.TryCreateImage(8, 1, 1, 1, out DecodedImageBuilder? builder);
                builder!.Samples[0] = 0b1100_0000;
                builder.SamplesInverted = true;
                return builder.Build();
            },
        };
        using PdfDocument stencilDocument = Open(stencilCodec, "/Width 8 /Height 1 /ImageMask true /Filter /JBIG2Decode");
        PdfImage stencil = stencilDocument.Pages[0].GetImage("Im0")!;
        using DecodedImage stencilSamples = stencil.Decode()!;

        // JBIG2 1 = black = paint: with the inversion folded in, the two 1 bits paint although Decode is [0 1].
        Assert.Equal([0.0, 1.0], stencil.DecodeArray);
        Assert.True(stencilCodec.Context!.IsMask);
        ImageDecodeMap map = stencil.CreateDecodeMap(stencilSamples);
        Assert.True(map.IsInverted);
        byte[] coverage = new byte[8];
        ImageRows.Stencil(stencilSamples.GetRow(0), 8, map.IsInverted, coverage);
        Assert.Equal(new byte[] { 255, 255, 0, 0, 0, 0, 0, 0 }, coverage);

        var grayCodec = new FakeImageFilter("JBIG2Decode")
        {
            Make = context =>
            {
                context.TryCreateImage(1, 1, 1, 1, out DecodedImageBuilder? builder);
                builder!.SamplesInverted = true;
                return builder.Build();
            },
        };
        using PdfDocument grayDocument = Open(grayCodec, "/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /JBIG2Decode");
        PdfImage gray = grayDocument.Pages[0].GetImage("Im0")!;
        using DecodedImage graySamples = gray.Decode()!;
        Assert.Equal(1f, gray.CreateDecodeMap(graySamples).Map(0, 0));
    }

    [Fact]
    public void A_header_larger_than_the_limit_is_rejected_before_the_codec_decodes_anything()
    {
        var codec = new FakeImageFilter("JPXDecode") { Header = new ImageHeader(100_000, 100_000, 3, 8), Make = context => Gray(context, 1, 1, 0) };
        using PdfDocument document = Open(codec, "/Width 1 /Height 1 /ColorSpace /DeviceRGB /Filter /JPXDecode", new PdfOptions().WithMaxImagePixels(1_000_000));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Null(image.Decode());
        Assert.Equal(0, codec.DecodeCalls);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("ImageTooLarge", DiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
    }

    [Fact]
    public void A_dictionary_size_larger_than_the_limit_is_rejected_before_any_filter_runs()
    {
        var codec = new FakeImageFilter("JPXDecode") { Make = context => Gray(context, 1, 1, 0) };
        using PdfDocument document = Open(codec, "/Width 2147483647 /Height 2147483647 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /JPXDecode");
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Null(image.Decode());
        Assert.Equal(0, codec.DecodeCalls);
        Assert.Equal(["ImageTooLarge"], Codes(document));
    }

    [Fact]
    public void Creating_an_image_over_the_limits_reports_and_rents_nothing()
    {
        var context = new ImageFilterContext(new FilterContext()) { MaxPixels = 15 };

        bool created16 = context.TryCreateImage(4, 4, 1, 8, out DecodedImageBuilder? rejected);
        rejected?.Dispose();
        Assert.False(created16);
        Assert.Null(rejected);
        Assert.Equal("ImageTooLarge", Assert.Single(context.Filter.Diagnostics).Code);
        Assert.True(context.TryCreateImage(3, 5, 1, 8, out DecodedImageBuilder? created));
        using DecodedImageBuilder image = created;
        Assert.Equal(15, image.Samples.Length);
    }

    [Fact]
    public void The_codec_gets_the_bytes_of_the_filters_before_it_and_the_plain_path_stays_bytes_to_bytes()
    {
        byte[] flated = Broadside.TestSupport.FilterEncoders.Zlib("JPX!"u8);
        var codec = new FakeImageFilter("JPXDecode") { Make = context => Gray(context, 1, 1, 9) };
        using PdfDocument document = PdfDocument.Open(
            OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter [/FlateDecode /JPXDecode] /DecodeParms [null << /Marker 5 >>]", Bytes(flated)),
            new PdfOptions().UseFilter(codec));
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        using DecodedImage decoded = image.Decode()!;
        Assert.Equal("JPX!"u8.ToArray(), codec.Received);
        Assert.Equal(new CosInteger(5), codec.Context!.Filter.Parameters![new CosName("Marker")]);
        Assert.Equal(0, codec.StreamDecodeCalls);

        Assert.Equal("bytes"u8.ToArray(), document.DecodeStream(image.Stream!).ToArray());
        Assert.Equal(1, codec.StreamDecodeCalls);
    }

    [Fact]
    public void A_codec_that_throws_becomes_a_diagnostic_and_no_image()
    {
        var codec = new FakeImageFilter("JPXDecode") { Make = _ => throw new InvalidDataException("broken codestream") };
        using PdfDocument document = Open(codec, "/Width 1 /Height 1 /ColorSpace /DeviceGray /Filter /JPXDecode");

        Assert.Null(document.Pages[0].GetImage("Im0")!.Decode());
        Assert.Equal(["FilterFailed"], Codes(document));
    }

    [Fact]
    public void A_codec_with_another_component_count_than_the_colour_space_gives_no_image()
    {
        var codec = new FakeImageFilter("JPXDecode") { Make = context => Gray(context, 1, 1, 0) };
        using PdfDocument document = Open(codec, "/Width 1 /Height 1 /ColorSpace /DeviceRGB /Filter /JPXDecode");

        Assert.Null(document.Pages[0].GetImage("Im0")!.Decode());
        Assert.Equal(["ImageComponentMismatch"], Codes(document));
    }

    [Fact]
    public void An_image_filter_the_engine_does_not_know_is_an_error_and_the_image_is_not_decoded()
    {
        // DCT, CCITT, JBIG2 and JPX all have managed defaults now (issues #61-#68), so no standard image codec is unregistered; an
        // unknown name stops the chain with FilterUnsupported as an error in the file.
        byte[] file = OneImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /NotARealDecode", "data");
        using PdfDocument document = PdfDocument.Open(file);
        PdfImage image = document.Pages[0].GetImage("Im0")!;

        Assert.Null(image.Decode());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("FilterUnsupported", DiagnosticSeverity.Error), (diagnostic.Code, diagnostic.Severity));
    }

    private static PdfDocument Open(FakeImageFilter codec, string entries, PdfOptions? options = null) =>
        PdfDocument.Open(OneImage(entries, "codestream"), (options ?? new PdfOptions()).UseFilter(codec));

    private static DecodedImage Gray(ImageFilterContext context, int width, int height, byte value)
    {
        Assert.True(context.TryCreateImage(width, height, 1, 8, out DecodedImageBuilder? created));
        using DecodedImageBuilder builder = created;
        builder.Samples.Fill(value);
        return builder.Build();
    }

    /// <summary>A codec whose header and output the test decides; it records what the image layer passed it.</summary>
    private sealed class FakeImageFilter(string name) : IImageFilter
    {
        public CosName Name { get; } = new(name);

        public ImageHeader? Header { get; init; }

        public required Func<ImageFilterContext, DecodedImage?> Make { get; init; }

        public ImageFilterContext? Context { get; private set; }

        public byte[]? Received { get; private set; }

        public int DecodeCalls { get; private set; }

        public int StreamDecodeCalls { get; private set; }

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            StreamDecodeCalls++;
            output.Write("bytes"u8);
        }

        public bool TryReadHeader(ReadOnlySpan<byte> encoded, ImageFilterContext context, out ImageHeader header)
        {
            header = Header ?? default;
            return Header is not null;
        }

        public DecodedImage? DecodeImage(ReadOnlyMemory<byte> encoded, ImageFilterContext context)
        {
            DecodeCalls++;
            Context = context;
            Received = encoded.ToArray();
            return Make(context);
        }
    }
}
