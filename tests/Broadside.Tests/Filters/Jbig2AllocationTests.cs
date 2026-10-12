using Broadside.Filters;
using Broadside.Images;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// JBIG2 decoding is a codec hot path: a decode allocates for the stream (segment lists, the reporter, the image object), per
/// dictionary and per symbol, and nothing per segment header, row, pixel, symbol instance or halftone grid cell (CLAUDE.md, "Code conventions"; ISO 32000-2 §7.4.7). <c>Jbig2Benchmarks</c> in
/// bench/Broadside.Benchmarks is the measuring half.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class Jbig2AllocationTests
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void More_symbol_instances_cost_nothing_per_instance(bool huffman)
    {
        long forFew = MeasureText(instances: 20, huffman);
        long forMany = MeasureText(instances: 400, huffman);

        Assert.InRange(forMany, 0, forFew);
    }

    [Fact]
    public void A_larger_halftone_grid_costs_nothing_per_cell()
    {
        long forSmall = MeasureHalftone(cells: 8);
        long forLarge = MeasureHalftone(cells: 64);

        Assert.InRange(forLarge, 0, forSmall);
    }

    private static long MeasureText(int instances, bool huffman)
    {
        bool[][][] symbols = [.. Enumerable.Range(0, 6).Select(i => CcittEncoder.SampleBitmap(6 + i, 8 + (i % 3), seed: 70 + i))];
        byte[] dictionary = huffman ? Jbig2Encoder.HuffmanSymbolDictionary(symbols, mmr: true, out int[] order) : Jbig2Encoder.SymbolDictionary(symbols, 0, out order);
        bool[][][] ordered = [.. order.Select(i => symbols[i])];
        Jbig2Instance[] placed = [.. Enumerable.Range(0, instances).Select(i => new Jbig2Instance(i % 6, (i % 40) * 15, (i / 40) * 12, Refined: i % 7 == 3 ? CcittEncoder.SampleBitmap(9, 9, seed: i) : null))];
        var options = new Jbig2TextOptions { Huffman = huffman, Refine = true };
        byte[] page = [
            .. Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(600, 128)),
            .. Jbig2Encoder.Segment(1, 53, 0, Jbig2Encoder.TableB1Segment),
            .. Jbig2Encoder.Segment(2, 0, 1, [], dictionary),
            .. Jbig2Encoder.Segment(3, 6, 1, huffman ? [1, 2] : [2], Jbig2Encoder.TextRegion(600, 128, 0, 0, 0, ordered, placed, options)),
        ];
        return MeasureDecode(page, 600, 128);
    }

    private static long MeasureHalftone(int cells)
    {
        bool[][][] patterns = [.. Enumerable.Range(0, 16).Select(i => CcittEncoder.SampleBitmap(4, 4, seed: 90 + i))];
        int[][] gray = [.. Enumerable.Range(0, cells).Select(m => Enumerable.Range(0, cells).Select(n => (m + (3 * n)) % 16).ToArray())];
        byte[] page = [
            .. Jbig2Encoder.Segment(0, 48, 1, Jbig2Encoder.PageInformation(256, 256)),
            .. Jbig2Encoder.Segment(1, 16, 1, Jbig2Encoder.PatternDictionary(patterns, 4, 0, mmr: false)),
            .. Jbig2Encoder.Segment(2, 22, 1, [1], Jbig2Encoder.HalftoneRegion(256, 256, 0, gray, 16, 4, 0, 0, 0, mmr: false)),
        ];
        return MeasureDecode(page, 256, 256);
    }

    private static long MeasureDecode(byte[] page, int width, int height)
    {
        var filter = new Jbig2DecodeFilter();
        var context = new ImageFilterContext(new FilterContext()) { Width = width, Height = height, BitsPerComponent = 1, ColorComponents = 1 };
        Action decode = () =>
        {
            using DecodedImage image = filter.DecodeImage(page, context)!;
        };

        return Allocations.Measure(decode);
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
