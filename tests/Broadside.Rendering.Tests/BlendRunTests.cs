#pragma warning disable CA5394 // Deterministic test data, not security.

using Broadside.Graphics;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The row and run paths of <see cref="Compositor"/> in a blend mode other than Normal (ISO 32000-2 §11.3.5, §11.3.6) blend a run of
/// equal pixel pairs once (issue #602). They must give, pixel for pixel, what <see cref="Compositor.Composite"/> gives each pixel
/// alone. The rows mix runs of equal pairs with pairs where only the source or only the backdrop changes, clear sources and clear
/// backdrops, in every blend mode, at full and partial coverage and constant alpha.
/// </summary>
public sealed class BlendRunTests
{
    public static TheoryData<BlendMode, double, int> Cases()
    {
        var data = new TheoryData<BlendMode, double, int>();
        foreach (BlendMode mode in Enum.GetValues<BlendMode>().Where(static m => m != BlendMode.Normal))
        {
            foreach ((double alpha, int coverage) in new[] { (1.0, 255), (1.0, 97), (0.5, 255), (0.5, 1) })
            {
                data.Add(mode, alpha, coverage);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_blended_row_is_each_pixel_blended_alone(BlendMode mode, double alpha, int coverage)
    {
        var compositor = new Compositor(alpha, mode);
        (uint[] source, uint[] backdrop) = Rows(seed: (int)mode * 31 + coverage);
        uint[] expected = new uint[backdrop.Length];
        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] = compositor.Composite(source[i], backdrop[i], coverage);
        }

        uint[] actual = (uint[])backdrop.Clone();
        compositor.CompositeRow(source, actual, coverage);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_blended_fill_is_each_pixel_blended_alone(BlendMode mode, double alpha, int coverage)
    {
        var compositor = new Compositor(alpha, mode);
        (uint[] source, uint[] backdrop) = Rows(seed: (int)mode * 17 + coverage);
        foreach (uint color in new[] { Opaque(source[0]), 0xFF000000u, 0xFFFFFFFFu, 0xFF2080C0u })
        {
            uint[] expected = new uint[backdrop.Length];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = compositor.Composite(color, backdrop[i], coverage);
            }

            uint[] actual = (uint[])backdrop.Clone();
            compositor.Fill(actual, 0, actual.Length, coverage, color);

            Assert.Equal(expected, actual);
        }
    }

    /// <summary>A source and a backdrop row, premultiplied, drawn from a few pixels each so that runs and partial repeats occur.</summary>
    private static (uint[] Source, uint[] Backdrop) Rows(int seed)
    {
        var random = new Random(seed);
        uint[] sources = [.. Enumerable.Range(0, 4).Select(_ => Pixel(random)), 0];
        uint[] backdrops = [.. Enumerable.Range(0, 4).Select(_ => Pixel(random)), 0, 0xFFFFFFFFu];
        var source = new List<uint>();
        var backdrop = new List<uint>();
        while (source.Count < 600)
        {
            uint s = sources[random.Next(sources.Length)];
            uint b = backdrops[random.Next(backdrops.Length)];
            int run = random.Next(1, 24);
            int change = random.Next(3);
            for (int i = 0; i < run; i++)
            {
                // Within a run either nothing changes or, part way, only the source or only the backdrop does.
                bool late = i > run / 2;
                source.Add(late && change == 1 ? sources[(Array.IndexOf(sources, s) + 1) % sources.Length] : s);
                backdrop.Add(late && change == 2 ? backdrops[(Array.IndexOf(backdrops, b) + 1) % backdrops.Length] : b);
            }
        }

        return ([.. source], [.. backdrop]);
    }

    private static uint Pixel(Random random)
    {
        int a = random.Next(2) == 0 ? 255 : random.Next(1, 256);
        int r = random.Next(a + 1);
        int g = random.Next(a + 1);
        int b = random.Next(a + 1);
        return (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    private static uint Opaque(uint pixel) => pixel | 0xFF000000u;
}
