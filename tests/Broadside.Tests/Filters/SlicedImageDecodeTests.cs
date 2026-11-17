using Broadside.Filters;
using Broadside.Images;
using Broadside.Tests.Filters.Dct;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Issue #365: the DCT and JPEG 2000 codecs decode in slices (<see cref="ISlicedImageFilter"/>). A decode that pauses at every resume
/// point (a past deadline) gives the samples, the reduction and the diagnostics of one whole decode, for every scan layout the codecs
/// handle: baseline and progressive, Huffman and arithmetic, restart intervals, subsampling, 12-bit, a DNL height, CMYK and YCCK.
/// </summary>
public sealed class SlicedImageDecodeTests
{
    public static TheoryData<string, int> JpegFiles => new()
    {
        { "testorig", 0 },
        { "testorig", 2 },
        { "progressive", 0 },
        { "progressive-scans", 1 },
        { "progressive-restart", 0 },
        { "restart-blocks", 0 },
        { "arithmetic", 0 },
        { "arithmetic-progressive-scans", 0 },
        { "multiscan", 0 },
        { "sampling-420", 1 },
        { "sampling-411", 0 },
        { "precision12", 0 },
        { "dnl", 0 },
        { "ycck-progressive", 0 },
        { "cmyk", 3 },
    };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(JpegFiles))]
    public void A_DCT_decode_paused_at_every_MCU_row_equals_a_whole_decode(string name, int reduction)
    {
        byte[] jpeg = DctVectors.Jpeg(name);
        (DecodedImage? whole, string[] wholeCodes) = Whole(new DctDecodeFilter(), jpeg, reduction);
        (DecodedImage? sliced, string[] slicedCodes, int slices) = Sliced(new DctDecodeFilter(), jpeg, reduction);
        using (whole)
        using (sliced)
        {
            AssertSame(whole, sliced);
            Assert.Equal(wholeCodes, slicedCodes);
            Assert.True(slices > 1, $"{name} decoded in {slices} slice.");
        }
    }

    [Fact]
    public void A_truncated_DCT_decode_in_slices_reports_and_keeps_what_a_whole_decode_keeps()
    {
        byte[] jpeg = DctVectors.Jpeg("testorig");
        byte[] truncated = jpeg[..(jpeg.Length * 2 / 3)];
        (DecodedImage? whole, string[] wholeCodes) = Whole(new DctDecodeFilter(), truncated, 0);
        (DecodedImage? sliced, string[] slicedCodes, _) = Sliced(new DctDecodeFilter(), truncated, 0);
        using (whole)
        using (sliced)
        {
            AssertSame(whole, sliced);
            Assert.Equal(wholeCodes, slicedCodes);
            Assert.Equal(whole!.DecodedRows, sliced!.DecodedRows);
        }
    }

    [Theory]
    [InlineData("Medium", 0)]
    [InlineData("Medium", 1)]
    [InlineData("Tiled", 0)]
    [InlineData("TileParts", 0)]
    [InlineData("PrecinctsAndSmallBlocks", 0)]
    [InlineData("IrreversibleIctTiled", 0)]
    [InlineData("IrreversiblePrecinctsSopEph", 1)]
    [InlineData("AllStyles", 0)]
    [InlineData("Poc", 0)]
    [InlineData("Rgba", 0)]
    [InlineData("Roi", 0)]
    public void A_JPEG_2000_decode_paused_at_every_code_block_equals_a_whole_decode(string name, int reduction)
    {
        byte[] codestream = JpxSamples.Vector(name).Data;
        (DecodedImage? whole, string[] wholeCodes) = Whole(new JpxDecodeFilter(), codestream, reduction);
        (DecodedImage? sliced, string[] slicedCodes, int slices) = Sliced(new JpxDecodeFilter(), codestream, reduction);
        using (whole)
        using (sliced)
        {
            AssertSame(whole, sliced);
            Assert.Equal(wholeCodes, slicedCodes);
            Assert.True(slices > 1, $"{name} decoded in {slices} slice.");
        }
    }

    [Fact]
    public void A_cancelled_slice_throws_and_a_disposed_job_holds_nothing()
    {
        ImageDecodeJob job = ((ISlicedImageFilter)new DctDecodeFilter()).BeginDecodeImage(DctVectors.Jpeg("testorig"), Context(0, out _));
        Assert.False(job.Continue(0, Token));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => job.Continue(long.MaxValue, cancelled.Token));
        job.Dispose();
        Assert.True(job.Continue(long.MaxValue, Token));
        Assert.Null(job.Result);
    }

    private static (DecodedImage? Image, string[] Codes) Whole(IImageFilter filter, byte[] data, int reduction)
    {
        ImageFilterContext context = Context(reduction, out FilterContext filterContext);
        DecodedImage? image = filter.DecodeImage(data, context);
        return (image, Codes(filterContext));
    }

    private static (DecodedImage? Image, string[] Codes, int Slices) Sliced(IImageFilter filter, byte[] data, int reduction)
    {
        ImageFilterContext context = Context(reduction, out FilterContext filterContext);
        using ImageDecodeJob job = ((ISlicedImageFilter)filter).BeginDecodeImage(data, context);
        int slices = 1;

        // A deadline of 0 has always passed: the codec pauses at every resume point after the first unit of work.
        while (!job.Continue(0, Token))
        {
            slices++;
        }

        return (job.Result, Codes(filterContext), slices);
    }

    private static ImageFilterContext Context(int reduction, out FilterContext filterContext)
    {
        filterContext = new FilterContext();
        return new ImageFilterContext(filterContext) { Reduction = reduction };
    }

    private static string[] Codes(FilterContext context) => [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)];

    private static void AssertSame(DecodedImage? expected, DecodedImage? actual)
    {
        Assert.Equal(expected is null, actual is null);
        if (expected is null || actual is null)
        {
            return;
        }

        Assert.Equal(expected.Geometry, actual.Geometry);
        Assert.Equal(expected.Reduction, actual.Reduction);
        Assert.Equal(expected.ColorModel, actual.ColorModel);
        Assert.True(expected.Samples.SequenceEqual(actual.Samples));
        Assert.Equal(expected.Alpha is null, actual.Alpha is null);
        if (expected.Alpha is { } alpha)
        {
            Assert.True(alpha.Samples.SequenceEqual(actual.Alpha!.Samples));
        }
    }
}
