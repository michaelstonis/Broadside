using System.Globalization;
using System.Text;

namespace Broadside.GoldenImages;

/// <summary>A pixel position, 0 at the top left.</summary>
/// <param name="X">The column.</param>
/// <param name="Y">The row.</param>
public readonly record struct GoldenPixel(int X, int Y);

/// <summary>
/// The perceptual comparison of a render with its golden image under a <see cref="GoldenThreshold"/>: how many pixels differ, how many
/// are tolerated, and a diff image to look at when they do not match.
/// </summary>
/// <remarks>Spec #33 story 13 and Testing Decisions (golden images with per-page thresholds). See <see cref="GoldenThreshold"/> for the metric.</remarks>
public sealed class GoldenComparison
{
    // YIQ weights of Kotsarenko and Ramos (2010); the distance is divided by the white-to-black distance, sqrt(0.5053) × 255.
    private const double YWeight = 0.5053;
    private const double IWeight = 0.299;
    private const double QWeight = 0.1957;

    // Pixel classes in the map the diff image is drawn from.
    private const byte Tolerated = 1;
    private const byte Differs = 2;

    // Floating-point slack: a grey a fifth of the way to black is within a tolerance of 0.2.
    private const double Epsilon = 1e-6;
    private static readonly double WhiteToBlack = Math.Sqrt(YWeight) * 255;

    private readonly GoldenImage _expected;
    private readonly GoldenImage _actual;
    private readonly byte[] _classes;

    private GoldenComparison(GoldenImage expected, GoldenImage actual, GoldenThreshold threshold)
    {
        _expected = expected;
        _actual = actual;
        Threshold = threshold;
        SizesMatch = expected.Width == actual.Width && expected.Height == actual.Height;
        _classes = SizesMatch ? new byte[expected.Width * expected.Height] : [];
    }

    /// <summary>Gets the threshold the comparison applied.</summary>
    public GoldenThreshold Threshold { get; }

    /// <summary>Gets a value indicating whether the two images have the same width and height; images of different sizes never match.</summary>
    public bool SizesMatch { get; }

    /// <summary>Gets the number of pixels whose difference exceeds the tolerance.</summary>
    public int DifferingPixels { get; private set; }

    /// <summary>Gets the number of pixels that differ but within the tolerance.</summary>
    public int TolerantPixels { get; private set; }

    /// <summary>Gets the largest per-pixel difference, from 0 to 1.</summary>
    public double MaxDifference { get; private set; }

    /// <summary>Gets the first pixel, in row order, whose difference exceeds the tolerance, if any.</summary>
    public GoldenPixel? FirstDifference { get; private set; }

    /// <summary>Gets a value indicating whether the render matches: same size and at most <see cref="GoldenThreshold.MaxDifferingPixels"/> differing pixels.</summary>
    public bool Matches => SizesMatch && DifferingPixels <= Threshold.MaxDifferingPixels;

    /// <summary>Compares a render with its golden image.</summary>
    /// <param name="expected">The golden image.</param>
    /// <param name="actual">The render.</param>
    /// <param name="threshold">The threshold.</param>
    /// <returns>The comparison.</returns>
    public static GoldenComparison Compare(GoldenImage expected, GoldenImage actual, GoldenThreshold threshold)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var comparison = new GoldenComparison(expected, actual, threshold);
        if (comparison.SizesMatch)
        {
            comparison.Measure();
        }

        return comparison;
    }

    /// <summary>The perceptual difference of two colours, from 0 (equal) to 1 (white against black).</summary>
    /// <param name="r1">The first colour's red.</param>
    /// <param name="g1">The first colour's green.</param>
    /// <param name="b1">The first colour's blue.</param>
    /// <param name="r2">The second colour's red.</param>
    /// <param name="g2">The second colour's green.</param>
    /// <param name="b2">The second colour's blue.</param>
    /// <returns>The difference.</returns>
    public static double Difference(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        double dr = r1 - r2;
        double dg = g1 - g2;
        double db = b1 - b2;
        double y = (0.29889531 * dr) + (0.58662247 * dg) + (0.11448223 * db);
        double i = (0.59597799 * dr) - (0.27417610 * dg) - (0.32180189 * db);
        double q = (0.21147017 * dr) - (0.52261711 * dg) + (0.31114694 * db);
        return Math.Min(1, Math.Sqrt((YWeight * y * y) + (IWeight * i * i) + (QWeight * q * q)) / WhiteToBlack);
    }

    /// <summary>Describes the outcome in one line, for a test failure message.</summary>
    /// <returns>The description.</returns>
    public string Describe()
    {
        if (!SizesMatch)
        {
            return $"The render is {_actual.Width} x {_actual.Height} pixels; the golden image is {_expected.Width} x {_expected.Height}.";
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{DifferingPixels} pixels differ beyond their tolerance ({Threshold}); ");
        text.Append(CultureInfo.InvariantCulture, $"{TolerantPixels} more differ within it; the largest difference is {MaxDifference:0.###}");
        if (FirstDifference is GoldenPixel first)
        {
            text.Append(CultureInfo.InvariantCulture, $"; the first differing pixel is ({first.X}, {first.Y})");
        }

        return text.Append('.').ToString();
    }

    /// <summary>
    /// Draws the comparison: pixels beyond the tolerance red, tolerated differences amber, and the golden image faded to a light grey
    /// everywhere else. Images of different sizes draw on the larger canvas, with the area only one of them covers magenta.
    /// </summary>
    /// <returns>The diff image.</returns>
    public GoldenImage CreateDiffImage()
    {
        int width = Math.Max(_expected.Width, _actual.Width);
        int height = Math.Max(_expected.Height, _actual.Height);
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte R, byte G, byte B) colour;
                if (x >= _expected.Width || y >= _expected.Height || x >= _actual.Width || y >= _actual.Height)
                {
                    colour = (255, 0, 255);
                }
                else if (SizesMatch && _classes[(y * width) + x] == Differs)
                {
                    colour = (255, 0, 0);
                }
                else if (SizesMatch && _classes[(y * width) + x] == Tolerated)
                {
                    colour = (255, 176, 0);
                }
                else
                {
                    (byte r, byte g, byte b) = _expected[x, y];
                    double luma = (0.29889531 * r) + (0.58662247 * g) + (0.11448223 * b);
                    byte faded = (byte)Math.Round(255 - ((255 - Math.Min(luma, 255)) / 4));
                    colour = (faded, faded, faded);
                }

                int offset = ((y * width) + x) * 3;
                rgb[offset] = colour.R;
                rgb[offset + 1] = colour.G;
                rgb[offset + 2] = colour.B;
            }
        }

        return GoldenImage.FromRgb(width, height, rgb);
    }

    private void Measure()
    {
        ReadOnlySpan<byte> expected = _expected.Rgb;
        ReadOnlySpan<byte> actual = _actual.Rgb;
        int width = _expected.Width;
        for (int p = 0; p < _classes.Length; p++)
        {
            int o = p * 3;
            if (expected[o] == actual[o] && expected[o + 1] == actual[o + 1] && expected[o + 2] == actual[o + 2])
            {
                continue;
            }

            double difference = Difference(expected[o], expected[o + 1], expected[o + 2], actual[o], actual[o + 1], actual[o + 2]);
            MaxDifference = Math.Max(MaxDifference, difference);
            double tolerance = IsEdge(expected, width, _expected.Height, p % width, p / width) ? Threshold.EdgeTolerance : Threshold.FlatTolerance;
            if (difference > tolerance + Epsilon)
            {
                _classes[p] = Differs;
                DifferingPixels++;
                FirstDifference ??= new GoldenPixel(p % width, p / width);
            }
            else
            {
                _classes[p] = Tolerated;
                TolerantPixels++;
            }
        }
    }

    /// <summary>Whether a pixel of the golden image differs from one of its eight neighbours by more than the flat tolerance.</summary>
    private bool IsEdge(ReadOnlySpan<byte> rgb, int width, int height, int x, int y)
    {
        int o = ((y * width) + x) * 3;
        for (int ny = Math.Max(y - 1, 0); ny <= Math.Min(y + 1, height - 1); ny++)
        {
            for (int nx = Math.Max(x - 1, 0); nx <= Math.Min(x + 1, width - 1); nx++)
            {
                int n = ((ny * width) + nx) * 3;
                if (n != o && Difference(rgb[o], rgb[o + 1], rgb[o + 2], rgb[n], rgb[n + 1], rgb[n + 2]) > Threshold.FlatTolerance + Epsilon)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
