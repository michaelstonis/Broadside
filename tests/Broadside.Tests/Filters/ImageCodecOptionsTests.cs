using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The image codecs as part of the options (issue #544): <see cref="PdfOptions()"/> registers all four as before;
/// <see cref="PdfOptions.CreateWithoutImageCodecs"/> registers none, and a stream that needs one is reported with
/// <see cref="FilterDiagnostics.FilterCodecNotRegistered"/> and left encoded. ISO 32000-2 §7.4.1 Table 6, §7.4.6 to §7.4.9.
/// </summary>
public sealed class ImageCodecOptionsTests
{
    public static TheoryData<string, string> CodecFiles => new()
    {
        { "dct-baseline.pdf", "DCTDecode" },
        { "ccitt-g4.pdf", "CCITTFaxDecode" },
        { "jbig2-generic.pdf", "JBIG2Decode" },
        { "jpx-lossless.pdf", "JPXDecode" },
    };

    [Theory]
    [MemberData(nameof(CodecFiles))]
    public void The_default_options_decode_every_image_codec(string file, string codec)
    {
        _ = codec;
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, file), new PdfOptions().UseStrict());

        using DecodedImage? decoded = FirstImage(document).Decode();

        Assert.NotNull(decoded);
        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [MemberData(nameof(CodecFiles))]
    public void Without_image_codecs_an_image_is_not_decoded_and_its_codec_is_reported_once_even_in_strict_mode(string file, string codec)
    {
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, file), PdfOptions.CreateWithoutImageCodecs().UseStrict());

        using DecodedImage? decoded = FirstImage(document).Decode();

        Assert.Null(decoded);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, static d => d.Code == "FilterCodecNotRegistered");
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Contains($"/{codec}", diagnostic.Message, StringComparison.Ordinal);
        Assert.Same(FilterDiagnostics.FilterCodecNotRegistered, diagnostic.Kind);
    }

    [Fact]
    public void Without_image_codecs_a_stream_is_left_encoded_from_the_missing_codec_on()
    {
        // ASCIIHex runs; the DCT stage is not registered, so the bytes it would have decoded are returned as they are.
        byte[] file = FilterTesting.FileWithStream("/Filter [/ASCIIHexDecode /DCTDecode]", "FFD8FFE0>"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, PdfOptions.CreateWithoutImageCodecs());

        Assert.Equal<byte>([0xFF, 0xD8, 0xFF, 0xE0], decoded);
        Assert.Equal(["FilterCodecNotRegistered"], codes);
    }

    [Fact]
    public void Without_image_codecs_the_general_filters_still_decode()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /ASCIIHexDecode", "414243>"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, PdfOptions.CreateWithoutImageCodecs());

        Assert.Equal("ABC"u8.ToArray(), decoded);
        Assert.Empty(codes);
    }

    [Theory]
    [MemberData(nameof(CodecFiles))]
    public void UseImageCodecs_registers_them_again(string file, string codec)
    {
        _ = codec;
        using PdfDocument document = PdfDocument.Open(Path.Combine(Corpus.Directory, file), PdfOptions.CreateWithoutImageCodecs().UseImageCodecs().UseStrict());

        using DecodedImage? decoded = FirstImage(document).Decode();

        Assert.NotNull(decoded);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void One_codec_can_be_registered_alone_with_UseFilter()
    {
        PdfOptions options = PdfOptions.CreateWithoutImageCodecs().UseFilter(new DctDecodeFilter());

        using (PdfDocument jpeg = PdfDocument.Open(Path.Combine(Corpus.Directory, "dct-baseline.pdf"), options))
        using (DecodedImage? decoded = FirstImage(jpeg).Decode())
        {
            Assert.NotNull(decoded);
            Assert.Empty(jpeg.Diagnostics);
        }

        using PdfDocument jpx = PdfDocument.Open(Path.Combine(Corpus.Directory, "jpx-lossless.pdf"), options);
        using DecodedImage? missing = FirstImage(jpx).Decode();
        Assert.Null(missing);
        Assert.Contains(jpx.Diagnostics, static d => d.Code == "FilterCodecNotRegistered");
    }

    [Fact]
    public void Options_that_register_exactly_the_codecs_or_nothing_share_one_registry()
    {
        Assert.Same(ImageCodecRegistry.All, new PdfOptions().CreateFilterRegistry());
        Assert.Same(ImageCodecRegistry.All, PdfOptions.CreateWithoutImageCodecs().UseImageCodecs().CreateFilterRegistry());
        Assert.Same(FilterRegistry.WithoutImageCodecs, PdfOptions.CreateWithoutImageCodecs().CreateFilterRegistry());
        Assert.NotSame(ImageCodecRegistry.All, new PdfOptions().UseFilter(new DctDecodeFilter()).CreateFilterRegistry());
    }

    [Fact]
    public void A_filter_registered_before_UseImageCodecs_is_replaced_by_the_managed_codec_of_its_name()
    {
        var replacement = new NamedFilter(FilterNames.DctDecode);

        FilterRegistry before = PdfOptions.CreateWithoutImageCodecs().UseFilter(replacement).UseImageCodecs().CreateFilterRegistry();
        FilterRegistry after = PdfOptions.CreateWithoutImageCodecs().UseImageCodecs().UseFilter(replacement).CreateFilterRegistry();

        Assert.True(before.TryGet(FilterNames.DctDecode, out IStreamFilter? first));
        Assert.IsType<DctDecodeFilter>(first);
        Assert.True(after.TryGet(FilterNames.DctDecode, out IStreamFilter? second));
        Assert.Same(replacement, second);
    }

    private static PdfImage FirstImage(PdfDocument document) => document.Pages[0].GetImage("Im0")!;

    private sealed class NamedFilter(Broadside.Objects.CosName name) : IStreamFilter
    {
        public Broadside.Objects.CosName Name => name;

        public void Decode(ReadOnlyMemory<byte> encoded, System.Buffers.IBufferWriter<byte> output, FilterContext context) => System.Buffers.BuffersExtensions.Write(output, encoded.Span);
    }
}
