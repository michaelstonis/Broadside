using System.Buffers;
using System.IO.Compression;
using System.Text;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters.Dct;

/// <summary>A reference image decoded by libjpeg-turbo: its size, components and interleaved 8-bit samples.</summary>
internal sealed record DctGolden(int Width, int Height, int Components, byte[] Samples);

/// <summary>
/// The JPEG vectors and libjpeg-turbo goldens next to this file (provenance in README.md there), and the comparisons the tests
/// make against them.
/// </summary>
internal static class DctVectors
{
    /// <summary>The directory of the vectors.</summary>
    public static string Directory { get; } = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Filters", "Dct");

    /// <summary>The bytes of a vector.</summary>
    public static byte[] Jpeg(string name) => File.ReadAllBytes(Path.Combine(Directory, name + ".jpg"));

    /// <summary>The golden of a vector: <c>djpeg -dct int -nosmooth -pnm</c>, gzipped.</summary>
    public static DctGolden Golden(string name)
    {
        string path = Path.Combine(Directory, name + ".ppm.gz");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory, name + ".pgm.gz");
        }

        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        return ParsePnm(buffer.ToArray());
    }

    /// <summary>Parses a binary PGM (P5) or PPM (P6) with maxval 255.</summary>
    public static DctGolden ParsePnm(byte[] data)
    {
        int position = 0;
        string magic = Token(data, ref position);
        int width = int.Parse(Token(data, ref position), System.Globalization.CultureInfo.InvariantCulture);
        int height = int.Parse(Token(data, ref position), System.Globalization.CultureInfo.InvariantCulture);
        int maxValue = int.Parse(Token(data, ref position), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(255, maxValue);
        position++;
        int components = magic == "P5" ? 1 : 3;
        return new DctGolden(width, height, components, data.AsSpan(position, width * height * components).ToArray());
    }

    /// <summary>Decodes through <see cref="IStreamFilter.Decode"/> with a stand-alone context.</summary>
    public static (byte[] Samples, FilterContext Context) DecodeBytes(byte[] jpeg, string? parameters = null, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        var context = new FilterContext
        {
            Parameters = parameters is null ? null : (CosDictionary)CosObject.Parse(Encoding.Latin1.GetBytes(parameters)),
            ReadingMode = mode,
        };
        var output = new ArrayBufferWriter<byte>();
        new DctDecodeFilter().Decode(jpeg, output, context);
        return (output.WrittenSpan.ToArray(), context);
    }

    /// <summary>The diagnostic codes of a context.</summary>
    public static string[] Codes(FilterContext context) => [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)];

    /// <summary>Asserts every sample is within ±1 of the golden's.</summary>
    public static void AssertWithinOne(DctGolden golden, ReadOnlySpan<byte> samples)
    {
        Assert.Equal(golden.Samples.Length, samples.Length);
        int worst = 0;
        int at = -1;
        for (int i = 0; i < samples.Length; i++)
        {
            int difference = Math.Abs(samples[i] - golden.Samples[i]);
            if (difference > worst)
            {
                worst = difference;
                at = i;
            }
        }

        Assert.True(worst <= 1, $"Sample {at} differs from the reference by {worst}.");
    }

    private static string Token(byte[] data, ref int position)
    {
        while (data[position] is (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
        {
            position++;
        }

        int start = position;
        while (data[position] is not ((byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t'))
        {
            position++;
        }

        return Encoding.ASCII.GetString(data, start, position - start);
    }
}
