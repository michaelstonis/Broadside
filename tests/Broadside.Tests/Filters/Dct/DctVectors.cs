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

    /// <summary>
    /// The golden of a vector: <c>djpeg -dct int -nosmooth -pnm</c>, or for CMYK and YCCK a PAM of TurboJPEG's raw CMYK samples,
    /// gzipped; 12-bit samples reduced to 8 bits as the filter delivers them.
    /// </summary>
    public static DctGolden Golden(string name)
    {
        string path = Path.Combine(Directory, name + ".ppm.gz");
        foreach (string kind in (string[])[".pgm.gz", ".pam.gz"])
        {
            if (!File.Exists(path))
            {
                path = Path.Combine(Directory, name + kind);
            }
        }

        using var gzip = new GZipStream(File.OpenRead(path), CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        return ParsePnm(buffer.ToArray());
    }

    /// <summary>
    /// Parses a binary PGM (P5), PPM (P6) or four-channel PAM (P7, the CMYK goldens) with maxval 255, or 4095 (12-bit samples,
    /// two bytes each, big-endian), which are reduced to 8 bits by (255 v + 2047) / 4095 as ISO 32000-2 Table 87 has the filter do.
    /// </summary>
    public static DctGolden ParsePnm(byte[] data)
    {
        int position = 0;
        string magic = Token(data, ref position);
        int width, height, maxValue, components;
        if (magic == "P7")
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (string key = Token(data, ref position); key != "ENDHDR"; key = Token(data, ref position))
            {
                fields[key] = Token(data, ref position);
            }

            (width, height, components, maxValue) = (Number(fields["WIDTH"]), Number(fields["HEIGHT"]), Number(fields["DEPTH"]), Number(fields["MAXVAL"]));
        }
        else
        {
            width = Number(Token(data, ref position));
            height = Number(Token(data, ref position));
            maxValue = Number(Token(data, ref position));
            components = magic == "P5" ? 1 : 3;
        }

        position++;
        int count = width * height * components;
        if (maxValue == 255)
        {
            return new DctGolden(width, height, components, data.AsSpan(position, count).ToArray());
        }

        Assert.Equal(4095, maxValue);
        byte[] samples = new byte[count];
        for (int i = 0; i < count; i++)
        {
            int value = (data[position + (2 * i)] << 8) | data[position + (2 * i) + 1];
            samples[i] = (byte)(((255 * value) + 2047) / 4095);
        }

        return new DctGolden(width, height, components, samples);
    }

    private static int Number(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

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
