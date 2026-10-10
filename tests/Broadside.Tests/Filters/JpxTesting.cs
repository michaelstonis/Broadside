using System.Buffers;
using Broadside.Filters;
using Broadside.Images;

namespace Broadside.Tests.Filters;

/// <summary>Calls the JPXDecode filter through its public contract with a stand-alone context (ISO 32000-2 §7.4.9).</summary>
internal static class JpxTesting
{
    /// <summary>Decodes <paramref name="encoded"/> through <see cref="IImageFilter.DecodeImage"/>; fails when nothing is decoded.</summary>
    public static DecodedImage DecodeImage(byte[] encoded, out string[] codes, int colorComponents = 0, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        DecodedImage? image = TryDecodeImage(encoded, out codes, colorComponents, mode);
        Assert.NotNull(image);
        return image;
    }

    /// <summary>Decodes <paramref name="encoded"/> through <see cref="IImageFilter.DecodeImage"/>.</summary>
    public static DecodedImage? TryDecodeImage(byte[] encoded, out string[] codes, int colorComponents = 0, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        var filter = new JpxDecodeFilter();
        var context = new FilterContext { ReadingMode = mode };
        var imageContext = new ImageFilterContext(context) { ColorComponents = colorComponents };
        DecodedImage? image = filter.DecodeImage(encoded, imageContext);
        codes = [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)];
        return image;
    }

    /// <summary>Decodes <paramref name="encoded"/> through the plain <see cref="IStreamFilter.Decode"/> path.</summary>
    public static (byte[] Decoded, string[] Codes) Decode(byte[] encoded, PdfReadingMode mode = PdfReadingMode.Lenient)
    {
        var context = new FilterContext { ReadingMode = mode };
        var output = new ArrayBufferWriter<byte>();
        new JpxDecodeFilter().Decode(encoded, output, context);
        return (output.WrittenSpan.ToArray(), [.. context.Diagnostics.Select(diagnostic => diagnostic.Code)]);
    }

    /// <summary>Unpacks every sample of an image into raw values, row by row, component by component.</summary>
    public static int[] Raw(DecodedImage image)
    {
        int count = image.Width * image.Components;
        int[] values = new int[count * image.Height];
        ushort[] row = new ushort[count];
        for (int y = 0; y < image.Height; y++)
        {
            ImageRows.Unpack(image.GetRow(y), image.StorageBits, count, row);
            for (int i = 0; i < count; i++)
            {
                values[(y * count) + i] = row[i];
            }
        }

        return values;
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> is within a peak absolute error and a mean squared error of <paramref name="expected"/> in
    /// every component (the measures of ITU-T T.803 / ISO/IEC 15444-4 the conformance tests use).
    /// </summary>
    public static void AssertWithinTolerance(int[] expected, int[] actual, int components, int peak, double meanSquare)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int c = 0; c < components; c++)
        {
            int worst = 0;
            double sum = 0;
            int count = 0;
            for (int i = c; i < expected.Length; i += components)
            {
                int difference = Math.Abs(expected[i] - actual[i]);
                worst = Math.Max(worst, difference);
                sum += difference * difference;
                count++;
            }

            Assert.True(worst <= peak && sum / count <= meanSquare, $"component {c}: peak error {worst}, mean squared error {sum / count:F3}");
        }
    }
}
