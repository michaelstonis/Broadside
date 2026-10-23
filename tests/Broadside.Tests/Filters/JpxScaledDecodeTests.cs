using System.Globalization;
using Broadside.Filters;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// Issue #362, part 3: <c>JPXDecode</c> drops resolution levels (ITU-T T.800 B.5) when asked for a reduction, skipping tier-1 for
/// the code-blocks of the dropped sub-bands and their inverse DWT. Every vector at every reduction equals <c>opj_decompress -r d</c>
/// (exactly for the reversible 5/3 filter, within the existing tolerance for the 9/7), in every progression order (B.12.1: the
/// resolution-major orders stop after the last kept resolution, the others read the dropped packets through), with POC, tiles,
/// tile-parts, precincts, ROI and every code-block style. References: <c>jpx-scaled-references.txt</c>, written by
/// <c>make_jpx_scaled_references.py</c>.
/// </summary>
public sealed class JpxScaledDecodeTests
{
    public static TheoryData<string, int> References()
    {
        var data = new TheoryData<string, int>();
        foreach (Reference reference in ReadReferences())
        {
            data.Add(reference.Name, reference.Levels);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(References))]
    public void Every_vector_at_every_reduction_equals_OpenJPEG(string name, int levels)
    {
        Reference reference = ReadReferences().Single(r => r.Name == name && r.Levels == levels);
        JpxVector vector = JpxSamples.Vector(name);

        using DecodedImage image = Decode(vector.Data, levels, out string[] codes);

        Assert.Equal((reference.Width, reference.Height, reference.Components, levels), (image.Width, image.Height, image.Components, image.Reduction));
        int[] actual = JpxTesting.Raw(image);
        if (vector.IsLossless)
        {
            Assert.Equal(reference.Samples, actual);
        }
        else
        {
            JpxTesting.AssertWithinTolerance(reference.Samples, actual, reference.Components, peak: 1, meanSquare: 0.1);
        }

        Assert.Empty(codes);
    }

    [Theory]
    [InlineData("Medium", 3)]
    [InlineData("Rlcp3Layers", 2)]
    [InlineData("Gray4", 0)]
    [InlineData("NoDecomposition", 0)]
    [InlineData("Tiled", 0)]
    [InlineData("RpclTiledOffsets", 0)]
    public void The_header_offers_as_many_levels_as_the_main_header_gives_every_component_and_the_origin_allows(string name, int expected)
    {
        // Tiled and RpclTiledOffsets put the image origin at (3, 5), which no reduction keeps on its grid.
        var context = new ImageFilterContext(new FilterContext());

        Assert.True(new JpxDecodeFilter().TryReadHeader(JpxSamples.Vector(name).Data, context, out ImageHeader header));
        Assert.Equal(expected, header.MaxReduction);
    }

    [Fact]
    public void Every_vector_decoded_at_the_reduction_its_header_offers_delivers_at_most_that_at_the_reduced_size()
    {
        // The header reads the main header only; a tile-part header with fewer levels makes the decode deliver less.
        foreach (JpxVector vector in JpxSamples.Vectors)
        {
            var context = new ImageFilterContext(new FilterContext());
            if (!new JpxDecodeFilter().TryReadHeader(vector.Data, context, out ImageHeader header) || header.MaxReduction == 0)
            {
                continue;
            }

            DecodedImage? image = new JpxDecodeFilter().DecodeImage(vector.Data, new ImageFilterContext(new FilterContext()) { Reduction = header.MaxReduction });
            using (image)
            {
                Assert.NotNull(image);
                Assert.InRange(image.Reduction, 0, header.MaxReduction);
                Assert.Equal((ImageGeometry.Reduce(header.Width, image.Reduction), ImageGeometry.Reduce(header.Height, image.Reduction)), (image.Width, image.Height));
            }
        }
    }

    [Fact]
    public void Asking_for_more_levels_than_the_codestream_has_delivers_what_it_has()
    {
        using DecodedImage image = Decode(JpxSamples.Vector("Rlcp3Layers").Data, 5, out _);

        Assert.Equal((5, 4, 2), (image.Width, image.Height, image.Reduction));
    }

    private static DecodedImage Decode(byte[] data, int reduction, out string[] codes)
    {
        var context = new FilterContext();
        DecodedImage? image = new JpxDecodeFilter().DecodeImage(data, new ImageFilterContext(context) { Reduction = reduction });
        codes = JpxTesting.Codes(context);
        Assert.NotNull(image);
        return image;
    }

    private static List<Reference> ReadReferences() =>
        [.. File.ReadAllLines(Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Filters", "jpx-scaled-references.txt"))
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(static line => line.Split(' '))
            .Select(static parts => new Reference(
                parts[0],
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture),
                int.Parse(parts[3], CultureInfo.InvariantCulture),
                int.Parse(parts[4], CultureInfo.InvariantCulture),
                Unpack(Convert.FromBase64String(parts[5]))))];

    private static int[] Unpack(byte[] packed)
    {
        int[] values = new int[packed.Length / 2];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (packed[2 * i] << 8) | packed[(2 * i) + 1];
        }

        return values;
    }

    private sealed record Reference(string Name, int Levels, int Width, int Height, int Components, int[] Samples);
}
