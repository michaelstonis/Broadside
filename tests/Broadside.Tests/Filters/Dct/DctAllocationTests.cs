using System.Buffers;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// The DCT decoder is a hot path and allocates nothing per block, MCU or row (CLAUDE.md, "Code conventions"): once warm, a whole
/// decode through the plain filter path allocates nothing at all (per-image buffers are pooled), and the image path allocates the
/// same few objects whatever the image size. <c>DctBenchmarks</c> is the measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class DctAllocationTests
{
    public static TheoryData<string> Vectors => new()
    {
        "testorig", "sampling-411", "gray-2x2", "multiscan", "restart-blocks", "progressive", "progressive-scans", "arithmetic",
        "arithmetic-progressive-scans", "ycck", "ycck-progressive",
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Decoding_through_the_filter_contract_allocates_nothing_once_warm(string vector)
    {
        byte[] jpeg = Jpeg(vector);
        var filter = new DctDecodeFilter();
        var context = new FilterContext();
        var output = new ArrayBufferWriter<byte>(256 * 1024);

        long allocated = Allocations.Measure(() => Decode(filter, jpeg, output, context));

        Assert.True(output.WrittenCount > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_image_path_allocates_per_image_not_per_row()
    {
        var filter = new DctDecodeFilter();
        byte[] small = Jpeg("sampling-420");
        byte[] large = Jpeg("testorig");
        long forSmall = Allocations.Measure(() => DecodeImage(filter, small));
        long forLarge = Allocations.Measure(() => DecodeImage(filter, large));

        Assert.InRange(forLarge, 0, forSmall);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_reduced_decode_allocates_per_image_not_per_block_or_row(int reduction)
    {
        // Issue #362: the reduced IDCTs work in stack scratch and the strips shrink with the scale, so a decode at 1/2, 1/4 or 1/8
        // allocates what the full decode does, whatever the size: the image objects.
        var filter = new DctDecodeFilter();
        byte[] small = Jpeg("sampling-420");
        byte[] large = Jpeg("progressive");
        long forSmall = Allocations.Measure(() => DecodeImage(filter, small, reduction));
        long forLarge = Allocations.Measure(() => DecodeImage(filter, large, reduction));
        long full = Allocations.Measure(() => DecodeImage(filter, small));

        Assert.InRange(forLarge, 0, forSmall);
        Assert.Equal(full, forSmall);
    }

    public static TheoryData<string, DctChromaSubsampling, string?> EncoderLayouts => new()
    {
        { "testorig", DctChromaSubsampling.HorizontalAndVertical, null },
        { "testorig", DctChromaSubsampling.Horizontal, null },
        { "testorig", DctChromaSubsampling.None, "<< /ColorTransform 0 >>" },
        { "dct-baseline-gray", DctChromaSubsampling.None, null },
        { "cmyk", DctChromaSubsampling.None, null },
        { "cmyk", DctChromaSubsampling.HorizontalAndVertical, "<< /ColorTransform 1 >>" },
    };

    [Theory]
    [MemberData(nameof(EncoderLayouts))]
    public void Encoding_through_the_encoder_contract_allocates_nothing_once_warm(string source, DctChromaSubsampling subsampling, string? parameters)
    {
        // Issue #143: the per-image buffers are pooled, so no allocation per block, MCU or row, nor per image.
        DctGolden image = Golden(source);
        var encoder = new DctEncoder(90, subsampling);
        StreamEncoderContext context = DctEncoderTests.Context(image, parameters);
        var output = new ArrayBufferWriter<byte>(256 * 1024);

        long allocated = Allocations.Measure(() =>
        {
            output.ResetWrittenCount();
            encoder.Encode(image.Samples, output, context);
        });

        Assert.True(output.WrittenCount > 0);
        Assert.Equal(0, allocated);
    }

    private static void Decode(DctDecodeFilter filter, byte[] jpeg, ArrayBufferWriter<byte> output, FilterContext context)
    {
        output.ResetWrittenCount();
        filter.Decode(jpeg, output, context);
    }

    private static void DecodeImage(DctDecodeFilter filter, byte[] jpeg, int reduction = 0)
    {
        var context = new ImageFilterContext(new FilterContext()) { Reduction = reduction };
        using DecodedImage? image = filter.DecodeImage(jpeg, context);
        Assert.NotNull(image);
    }
}
