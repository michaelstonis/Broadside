using Broadside.Filters;
using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// JBIG2 decoding is a codec hot path: a decode allocates for the stream (segment lists, the reporter, the image object) and nothing
/// per segment header, row or pixel (CLAUDE.md, "Code conventions"; ISO 32000-2 §7.4.7). <c>Jbig2Benchmarks</c> in
/// bench/Broadside.Benchmarks is the measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class Jbig2AllocationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_taller_page_costs_no_more_than_a_short_one(bool mmr)
    {
        long forShort = Measure(rows: 16, mmr);
        long forTall = Measure(rows: 1_600, mmr);

        Assert.InRange(forTall, 0, forShort);
        Assert.InRange(forShort, 1, 4 * 1024);
    }

    [Fact]
    public void More_segments_cost_nothing_per_segment_header()
    {
        long forOne = Measure(rows: 64, mmr: false, comments: 1);
        long forMany = Measure(rows: 64, mmr: false, comments: 12);

        Assert.InRange(forMany, 0, forOne);
    }

    private static long Measure(int rows, bool mmr, int comments = 0)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(600, rows);
        var segments = new List<byte>();
        segments.AddRange(Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(600, (uint)rows)));
        for (int i = 0; i < comments; i++)
        {
            segments.AddRange(Jbig2Encoder.Segment((uint)(1 + i), 62, 1, Jbig2Encoder.Comment("Note", "x")));
        }

        segments.AddRange(Jbig2Encoder.Segment(100, 39, 1, Jbig2Encoder.GenericRegion(bitmap, 0, 0, 0, mmr, 0, typicalPrediction: !mmr)));
        byte[] page = [.. segments];
        var filter = new Jbig2DecodeFilter();
        var context = new ImageFilterContext(new FilterContext()) { Width = 600, Height = rows, BitsPerComponent = 1, ColorComponents = 1 };
        Action decode = () =>
        {
            using DecodedImage image = filter.DecodeImage(page, context)!;
        };

        return Allocations.Measure(decode);
    }
}
