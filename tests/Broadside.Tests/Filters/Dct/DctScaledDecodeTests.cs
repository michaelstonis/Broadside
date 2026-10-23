using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Images;
using static Broadside.Tests.Filters.Dct.DctVectors;

namespace Broadside.Tests.Filters.Dct;

/// <summary>
/// Issue #362: <c>DCTDecode</c> at 1/2, 1/4 and 1/8 through <see cref="ImageFilterContext.Reduction"/>. The reduced IDCTs and the
/// chroma rule are ports of libjpeg-turbo's <c>jidctred.c</c> and <c>jdmaster.c</c>, so every vector at every scale equals
/// <c>djpeg -scale 1/s -dct int -nosmooth</c> (TurboJPEG's raw CMYK for four components) byte for byte; the references are hashes
/// written by <c>make_scaled_references.py</c> (ITU-T T.81 §A.3.3; ISO 32000-2 §7.4.8).
/// </summary>
public sealed class DctScaledDecodeTests
{
    public static TheoryData<string, int> References()
    {
        var data = new TheoryData<string, int>();
        foreach (Reference reference in ReadReferences())
        {
            data.Add(reference.Name, reference.Scale);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(References))]
    public void Every_vector_at_every_scale_equals_libjpeg_turbo_byte_for_byte(string name, int scale)
    {
        Reference reference = ReadReferences().Single(r => r.Name == name && r.Scale == scale);
        int reduction = System.Numerics.BitOperations.Log2((uint)scale);

        using DecodedImage image = Decode(File.ReadAllBytes(Path.Combine(DctVectors.Directory, name)), reduction, out _);

        Assert.Equal((reference.Width, reference.Height, reference.Components, reduction), (image.Width, image.Height, image.Components, image.Reduction));
        Assert.Equal(reference.Sha256, Convert.ToHexStringLower(SHA256.HashData(image.Samples)));
    }

    [Theory]
    [InlineData("progressive-scans", 3, true)]
    [InlineData("arithmetic-progressive-scans", 3, true)]
    [InlineData("progressive-gray-2x2", 3, true)]
    [InlineData("ycck-progressive", 3, false)]
    [InlineData("progressive", 3, false)]
    [InlineData("progressive-scans", 2, false)]
    public void At_an_eighth_a_progressive_image_whose_every_block_reduces_to_its_DC_skips_the_AC_scans(string name, int reduction, bool skips)
    {
        // progressive-scans (4:4:4), its arithmetic twin and the one-component progressive file reduce every component to the DC
        // at 1/8; the 4:2:0 files keep a 2 x 2 chroma IDCT that needs AC coefficients, so they decode every scan, as does 1/4.
        byte[] jpeg = Jpeg(name);
        using DecodedImage reduced = Decode(jpeg, reduction, out FilterContext context);
        using DecodedImage sequential = Decode(Strip(jpeg, acScans: !skips), reduction, out _);

        Assert.Equal(sequential.Samples.ToArray(), reduced.Samples.ToArray());
        Assert.DoesNotContain(context.Diagnostics, static diagnostic => diagnostic.Severity != Diagnostics.DiagnosticSeverity.Information);
    }

    [Fact]
    public void The_header_says_the_codec_reduces_to_an_eighth_and_the_plain_path_stays_whole()
    {
        var filter = new DctDecodeFilter();
        var context = new ImageFilterContext(new FilterContext());

        Assert.True(filter.TryReadHeader(Jpeg("testorig"), context, out ImageHeader header));
        Assert.Equal(3, header.MaxReduction);

        // More than an eighth asks for what the codec can do: an eighth.
        using DecodedImage most = Decode(Jpeg("testorig"), 5, out _);
        Assert.Equal((29, 19, 3), (most.Width, most.Height, most.Reduction));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_truncated_image_counts_its_decoded_rows_at_the_reduced_size(int reduction)
    {
        byte[] jpeg = Jpeg("testorig");
        using DecodedImage whole = Decode(jpeg, 0, out _);
        using DecodedImage cut = Decode(jpeg.AsSpan(0, jpeg.Length / 2).ToArray(), reduction, out _);

        int expected = (int)Math.Ceiling(whole.Height / (double)(1 << reduction));
        Assert.Equal(expected, cut.Height);
        Assert.InRange(cut.DecodedRows, 1, expected - 1);
        Assert.Equal(0, cut.DecodedRows % (16 >> reduction));
    }

    /// <summary>Decodes <paramref name="jpeg"/> through the image facet at <paramref name="reduction"/>.</summary>
    internal static DecodedImage Decode(byte[] jpeg, int reduction, out FilterContext context)
    {
        context = new FilterContext();
        return new DctDecodeFilter().DecodeImage(jpeg, new ImageFilterContext(context) { Reduction = Math.Min(reduction, 15) })!;
    }

    /// <summary>
    /// Returns the JPEG with its progressive AC scans (spectral start above 0) removed, or unchanged: what a decoder that skips them
    /// reads. A scan is the SOS segment and its entropy-coded data up to the next marker that is not RST or a stuffed byte.
    /// </summary>
    private static byte[] Strip(byte[] jpeg, bool acScans)
    {
        if (acScans)
        {
            return jpeg;
        }

        var output = new List<byte>(jpeg.Length);
        int position = 0;
        while (position < jpeg.Length)
        {
            if (jpeg[position] == 0xFF && position + 3 < jpeg.Length && jpeg[position + 1] == 0xDA)
            {
                int length = (jpeg[position + 2] << 8) | jpeg[position + 3];
                int count = jpeg[position + 4];
                int spectralStart = jpeg[position + 5 + (2 * count)];
                int end = position + 2 + length;
                while (end + 1 < jpeg.Length && !(jpeg[end] == 0xFF && jpeg[end + 1] != 0 && jpeg[end + 1] is not (>= 0xD0 and <= 0xD7)))
                {
                    end++;
                }

                if (spectralStart == 0)
                {
                    output.AddRange(jpeg.AsSpan(position, end - position));
                }

                position = end;
                continue;
            }

            output.Add(jpeg[position++]);
        }

        return [.. output];
    }

    private static List<Reference> ReadReferences() =>
        [.. File.ReadAllLines(Path.Combine(DctVectors.Directory, "scaled-references.txt"))
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(static line => line.Split(' '))
            .Select(static parts => new Reference(parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture), parts[5]))];

    private sealed record Reference(string Name, int Scale, int Width, int Height, int Components, string Sha256);
}
