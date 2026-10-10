using Broadside.Images;
using Broadside.TestSupport;
using static Broadside.TestSupport.Jbig2Encoder;

namespace Broadside.Tests.Filters;

/// <summary>
/// JBIG2Decode on symbol dictionaries, text regions, pattern dictionaries, halftone regions and generic refinement regions coded by
/// the test encoder, through the public filter contract: each page decodes to the picture its instances, cells and refinements
/// describe. ISO 32000-2 §7.4.7; ITU-T T.88 §6.3 to §6.7, §7.4.2 to §7.4.7, §8.2, Annexes A to C.
/// </summary>
public sealed class Jbig2SymbolRegionTests
{
    // Symbols of different widths and heights, so a wrong corner, axis or CURS update moves pixels.
    private static readonly bool[][][] Symbols =
    [
        CcittEncoder.SampleBitmap(5, 7, seed: 11),
        CcittEncoder.SampleBitmap(9, 4, seed: 12),
        CcittEncoder.SampleBitmap(3, 7, seed: 13),
        CcittEncoder.SampleBitmap(12, 10, seed: 14),
        CcittEncoder.SampleBitmap(1, 1, seed: 15),
    ];

    private static readonly Jbig2Instance[] Instances =
    [
        new(0, 2, 3), new(1, 9, 1), new(2, 20, 5), new(3, 26, 0), new(0, 40, 2),
        new(4, 3, 14), new(3, 6, 12), new(1, 19, 18), new(2, 30, 13), new(1, 41, 15),
        new(3, -4, 25), new(0, 15, 24), new(2, 44, 26), new(1, 50, 30),
    ];

    public static TheoryData<int, bool, int, int, int> Layouts
    {
        get
        {
            var data = new TheoryData<int, bool, int, int, int>();
            foreach (int corner in new[] { 0, 1, 2, 3 })
            {
                foreach (bool transposed in new[] { false, true })
                {
                    // Corner, transposed, LOGSBSTRIPS, SBDSOFFSET, SBCOMBOP.
                    data.Add(corner, transposed, (corner + (transposed ? 1 : 0)) % 4, corner - 2, corner % 4);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void An_arithmetic_text_region_places_every_instance_by_its_reference_corner_and_axis(int corner, bool transposed, int logStrips, int dsOffset, int combination)
    {
        var options = new Jbig2TextOptions { Corner = corner, Transposed = transposed, LogStrips = logStrips, DsOffset = dsOffset, CombinationOperator = combination };
        byte[] dictionary = SymbolDictionary(Symbols, 0, out int[] order);
        bool[][][] ordered = [.. order.Select(i => Symbols[i])];
        Jbig2Instance[] instances = [.. Instances.Select(i => i with { Symbol = Array.IndexOf(order, i.Symbol) })];
        byte[] page = [
            .. Segment(0, 48, 1, PageInformation(60, 40)),
            .. Segment(1, 0, 1, [], dictionary),
            .. Segment(2, 6, 1, [1], TextRegion(56, 36, 2, 3, 0, ordered, instances, options)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 60, 40, out string[] codes);

        bool[][] region = Blank(56, 36, 0);
        foreach (Jbig2Instance instance in instances)
        {
            Draw(region, ordered[instance.Symbol], instance.X, instance.Y, combination);
        }

        Assert.Equal(PackPdf(Compose(Blank(60, 40, 0), region, 2, 3, 0), 60), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Refined_instances_are_decoded_against_their_symbols_with_the_floor_of_half_the_size_change(bool huffman, bool template1)
    {
        // Negative RDW and RDH: GRREFERENCEDX = floor(RDW / 2) + RDX (T.88 Table 12).
        bool[][] grown = CcittEncoder.SampleBitmap(14, 12, seed: 21);
        bool[][] shrunk = CcittEncoder.SampleBitmap(7, 2, seed: 22);
        var options = new Jbig2TextOptions { Refine = true, RefinementTemplate = template1 ? 1 : 0, Huffman = huffman };
        byte[] dictionary = huffman ? HuffmanSymbolDictionary(Symbols, mmr: true, out int[] order) : SymbolDictionary(Symbols, 1, out order);
        bool[][][] ordered = [.. order.Select(i => Symbols[i])];
        int id1 = Array.IndexOf(order, 1);
        int id3 = Array.IndexOf(order, 3);
        Jbig2Instance[] instances =
        [
            new(id3, 1, 1), new(id1, 15, 2, Refined: shrunk, RefinementX: 1, RefinementY: -1), new(id3, 25, 0, Refined: grown, RefinementX: -2, RefinementY: 3),
            new(Array.IndexOf(order, 0), 3, 15),
        ];
        byte[] page = [
            .. Segment(0, 48, 1, PageInformation(48, 24)),
            .. Segment(1, 53, 0, TableB1Segment),
            .. Segment(2, 0, 1, [], dictionary),
            .. Segment(3, 7, 1, huffman ? [1, 2] : [2], TextRegion(48, 24, 0, 0, 0, ordered, instances, options)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 48, 24, out string[] codes);

        bool[][] expected = Blank(48, 24, 0);
        foreach (Jbig2Instance instance in instances)
        {
            Draw(expected, instance.Refined ?? ordered[instance.Symbol], instance.X, instance.Y, 0);
        }

        Assert.Equal(PackPdf(expected, 48), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public void A_Huffman_dictionary_and_text_region_with_a_custom_code_table_decode(bool mmr, int corner)
    {
        var options = new Jbig2TextOptions { Huffman = true, Corner = corner, LogStrips = 1, DefaultPixel = 1, CombinationOperator = 2 };
        byte[] dictionary = HuffmanSymbolDictionary(Symbols, mmr, out int[] order);
        bool[][][] ordered = [.. order.Select(i => Symbols[i])];
        Jbig2Instance[] instances = [.. Instances.Where(i => i.Y >= 0).Select(i => i with { Symbol = Array.IndexOf(order, i.Symbol) })];
        byte[] globals = [.. Segment(0, 53, 0, TableB1Segment), .. Segment(1, 0, 0, [], dictionary)];
        byte[] page = [
            .. Segment(2, 48, 1, PageInformation(60, 40)),
            .. Segment(3, 6, 1, [0, 1], TextRegion(60, 40, 0, 0, 0, ordered, instances, options)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 60, 40, out string[] codes, globals);

        bool[][] expected = Blank(60, 40, 1);
        foreach (Jbig2Instance instance in instances)
        {
            Draw(expected, ordered[instance.Symbol], instance.X, instance.Y, 2);
        }

        Assert.Equal(PackPdf(expected, 60), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void A_dictionary_reusing_the_retained_statistics_of_the_one_it_refers_to_decodes()
    {
        byte[] contexts = new byte[65536];
        bool[][][] first = [.. Symbols[..3]];
        bool[][][] second = [.. Symbols[3..]];
        byte[] one = SymbolDictionary(first, 0, out int[] firstOrder, retain: true, contexts: contexts);
        byte[] two = SymbolDictionary(second, 0, out int[] secondOrder, contexts: contexts, contextUsed: true, inputCount: first.Length);
        bool[][][] all = [.. firstOrder.Select(i => first[i]), .. secondOrder.Select(i => second[i])];
        Jbig2Instance[] instances = [new(0, 1, 1), new(1, 8, 1), new(2, 20, 1), new(3, 1, 10), new(4, 20, 10)];
        byte[] page = [
            .. Segment(0, 48, 1, PageInformation(32, 24)),
            .. Segment(1, 0, 1, [], one),
            .. Segment(2, 0, 1, [1], two),
            .. Segment(3, 6, 1, [1, 2], TextRegion(32, 24, 0, 0, 0, all, instances, new Jbig2TextOptions())),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 32, 24, out string[] codes);

        bool[][] expected = Blank(32, 24, 0);
        foreach (Jbig2Instance instance in instances)
        {
            Draw(expected, all[instance.Symbol], instance.X, instance.Y, 0);
        }

        Assert.Equal(PackPdf(expected, 32), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    public static TheoryData<int, bool, bool, int> Halftones => new()
    {
        // HTEMPLATE, HMMR, HENABLESKIP, HCOMBOP.
        { 0, false, false, 0 }, { 1, false, true, 0 }, { 2, false, false, 2 }, { 3, false, true, 4 }, { 0, true, false, 1 },
    };

    [Theory]
    [MemberData(nameof(Halftones))]
    public void A_halftone_region_draws_each_grid_cell_with_the_pattern_its_gray_value_selects(int template, bool mmr, bool skip, int combination)
    {
        const int cell = 4;
        bool[][][] patterns = [.. Enumerable.Range(0, 11).Select(i => CcittEncoder.SampleBitmap(cell, cell, seed: 40 + i))];
        int[][] gray = [.. Enumerable.Range(0, 9).Select(m => Enumerable.Range(0, 12).Select(n => ((m * 7) + (n * 3)) % 11).ToArray())];
        (int gridX, int gridY) = (-3, 2);
        byte[] page = [
            .. Segment(0, 48, 1, PageInformation(40, 36, defaultPixel: 1)),
            .. Segment(1, 16, 1, PatternDictionary(patterns, cell, template, mmr)),
            .. Segment(2, 22, 1, [1], HalftoneRegion(40, 36, 0, gray, patterns.Length, cell, gridX, gridY, template, mmr, combination, skip)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 36, out string[] codes);

        bool[][] region = Blank(40, 36, 0);
        for (int m = 0; m < gray.Length; m++)
        {
            for (int n = 0; n < gray[m].Length; n++)
            {
                Draw(region, patterns[gray[m][n]], gridX + (n * cell), gridY + (m * cell), combination);
            }
        }

        Assert.Equal(PackPdf(Compose(Blank(40, 36, 1), region, 0, 0, 0), 40), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void An_intermediate_region_is_refined_through_its_auxiliary_buffer_and_then_drawn(int template, bool typicalPrediction)
    {
        bool[][] rough = CcittEncoder.SampleBitmap(30, 20, seed: 51);
        bool[][] refined = [.. rough.Select((row, y) => row.Select((pixel, x) => (x + y) % 9 == 0 ? !pixel : pixel).ToArray())];
        var at = new Jbig2RefinementAt(-2, -1, 1, 1);
        byte[] page = [
            .. Segment(0, 48, 1, PageInformation(40, 30, operatorOverridden: true)),
            .. Segment(1, 36, 1, GenericRegion(rough, 5, 4, 0, mmr: false)),
            .. Segment(2, 40, 1, [1], RefinementRegion(refined, rough, 5, 4, 0, template, typicalPrediction, at)),
            .. Segment(3, 42, 1, [2], RefinementRegion(rough, refined, 5, 4, 2, template, typicalPrediction)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 30, out string[] codes);

        // The page is white; the last refinement turns the refined buffer back into the rough bitmap and XORs it onto the page.
        Assert.Equal(PackPdf(Compose(Blank(40, 30, 0), rough, 5, 4, 2), 40), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void A_refinement_region_without_a_referred_region_refines_the_page_as_composed_so_far()
    {
        bool[][] background = CcittEncoder.SampleBitmap(40, 30, seed: 61);
        bool[][] window = [.. background.Skip(6).Take(15).Select(row => row.Skip(10).Take(25).ToArray())];
        bool[][] target = [.. window.Select(row => row.Select(pixel => !pixel).ToArray())];
        byte[] page = [
            .. Segment(0, 48, 1, PageInformation(40, 30, operatorOverridden: true)),
            .. Segment(1, 38, 1, GenericRegion(background, 0, 0, 0, mmr: true)),
            .. Segment(2, 42, 1, RefinementRegion(target, window, 10, 6, 4, 0, typicalPrediction: true)),
        ];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 40, 30, out string[] codes);

        Assert.Equal(PackPdf(Compose(background, target, 10, 6, 4), 40), image.Samples.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void A_symbol_ID_beyond_the_symbols_is_reported_and_drawn_as_nothing()
    {
        // Three symbols give SBSYMCODELEN 2, so the ID 3 can be coded although no fourth symbol exists.
        bool[][][] symbols = [.. Symbols[..3]];
        byte[] dictionary = SymbolDictionary(symbols, 0, out int[] order);
        bool[][][] ordered = [.. order.Select(i => symbols[i])];
        byte[] region = TextRegion(30, 10, 0, 0, 0, [.. ordered, Symbols[3]], [new(0, 0, 0), new(1, 8, 0), new(3, 20, 0)], new Jbig2TextOptions());
        byte[] page = [.. Segment(0, 48, 1, PageInformation(30, 10)), .. Segment(1, 0, 1, [], dictionary), .. Segment(2, 6, 1, [1], region)];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 30, 10, out string[] codes);

        bool[][] expected = Blank(30, 10, 0);
        Draw(expected, ordered[0], 0, 0, 0);
        Draw(expected, ordered[1], 8, 0, 0);
        Assert.Equal(PackPdf(expected, 30), image.Samples.ToArray());
        Assert.Equal(["Jbig2SymbolIdOutOfRange"], codes);
    }

    [Fact]
    public void A_text_region_whose_dictionary_is_missing_draws_nothing_and_says_so()
    {
        byte[] region = TextRegion(30, 10, 0, 0, 0, Symbols, [new(0, 0, 0)], new Jbig2TextOptions());
        byte[] page = [.. Segment(0, 48, 1, PageInformation(30, 10)), .. Segment(2, 6, 1, [1], region)];

        using DecodedImage image = Jbig2Testing.DecodeImage(page, 30, 10, out string[] codes);

        Assert.Equal(PackPdf(Blank(30, 10, 0), 30), image.Samples.ToArray());
        Assert.Contains("Jbig2ReferredSegmentMissing", codes);
    }

    private static bool[][] Blank(int width, int height, int pixel) => [.. Enumerable.Range(0, height).Select(_ => Enumerable.Repeat(pixel != 0, width).ToArray())];

    /// <summary>T.88 §4.3: OR, AND, XOR, XNOR, REPLACE of a bitmap drawn with its top left at (x, y), clipped.</summary>
    private static void Draw(bool[][] target, bool[][] bitmap, int x, int y, int op)
    {
        for (int j = 0; j < bitmap.Length; j++)
        {
            for (int i = 0; i < bitmap[j].Length; i++)
            {
                int tx = x + i;
                int ty = y + j;
                if (ty < 0 || ty >= target.Length || tx < 0 || tx >= target[ty].Length)
                {
                    continue;
                }

                bool d = target[ty][tx];
                bool s = bitmap[j][i];
                target[ty][tx] = op switch { 0 => d | s, 1 => d & s, 2 => d ^ s, 3 => !(d ^ s), _ => s };
            }
        }
    }

    private static bool[][] Compose(bool[][] page, bool[][] region, int x, int y, int op)
    {
        bool[][] result = [.. page.Select(row => row.ToArray())];
        Draw(result, region, x, y, op);
        return result;
    }
}
