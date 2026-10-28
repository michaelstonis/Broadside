using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Pages for the scaled image decode tests of every backend (issues #362, #428): one large image (1024 x 1024, Flate, decoded when the
/// list is built and reduced from that decode) painted into a 512-point square, in the depths and with the masks a backend handles differently, and
/// the page tolerance a reduced render must keep against a full-resolution one.
/// </summary>
public static class ScaledImagePages
{
    /// <summary>The content that paints the image into a 512-point square on the page (the rasterizer tests' placement).</summary>
    public const string Paint = "q 512 0 0 512 50 100 cm /Im0 Do Q";

    /// <summary>Gets the names <see cref="Page"/> knows.</summary>
    public static IReadOnlyList<string> Names { get; } =
        ["gray", "rgb16", "one-bit", "stencil", "soft-mask-same-size", "soft-mask-own-size", "explicit-mask", "colour-key"];

    /// <summary>Returns the page called <paramref name="name"/> (one of <see cref="Names"/>).</summary>
    /// <param name="name">The page.</param>
    /// <returns>The file.</returns>
    public static byte[] Page(string name)
    {
        byte[] gray = Samples(1024, 1024, 1, 8);
        return name switch
        {
            "gray" => Image(gray, "/DeviceGray", 8),
            "rgb16" => Image(Samples(1024, 1024, 3, 16), "/DeviceRGB", 16),
            "one-bit" => Image(Dither(gray, 1024, 1024), "/DeviceGray", 1),
            "stencil" => File("0.8 0.1 0.1 rg " + Paint, Stream(Dither(gray, 1024, 1024), "/Width 1024 /Height 1024 /ImageMask true")),
            "soft-mask-same-size" => File(Paint, Stream(Samples(1024, 1024, 3, 8), "/Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R"), Stream(gray, "/Width 1024 /Height 1024 /ColorSpace /DeviceGray /BitsPerComponent 8")),
            "soft-mask-own-size" => File(Paint, Stream(Samples(1024, 1024, 3, 8), "/Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R"), Stream(Samples(700, 500, 1, 8), "/Width 700 /Height 500 /ColorSpace /DeviceGray /BitsPerComponent 8")),
            "explicit-mask" => File(Paint, Stream(Samples(1024, 1024, 3, 8), "/Width 1024 /Height 1024 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask 6 0 R"), Stream(Dither(Samples(900, 600, 1, 8), 900, 600), "/Width 900 /Height 600 /ImageMask true")),
            "colour-key" => Image(gray, "/DeviceGray", 8, "/Mask [100 140]"),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }

    /// <summary>
    /// Compares two renders of the same size, pixel by pixel over their first three channels: the PSNR, the share of pixels more than
    /// 8 levels off on any channel, and the largest difference.
    /// </summary>
    /// <param name="full">The full-resolution render.</param>
    /// <param name="reduced">The reduced render.</param>
    /// <param name="bytesPerPixel">The bytes per pixel of both (4 for BGRA or RGBA).</param>
    /// <returns>The figures.</returns>
    public static (double Psnr, double Over8, int Max) Compare(ReadOnlySpan<byte> full, ReadOnlySpan<byte> reduced, int bytesPerPixel = 4)
    {
        if (full.Length != reduced.Length)
        {
            throw new ArgumentException("The renders differ in size.", nameof(reduced));
        }

        double squared = 0;
        int over = 0;
        int max = 0;
        int pixels = full.Length / bytesPerPixel;
        for (int i = 0; i < full.Length; i += bytesPerPixel)
        {
            int largest = 0;
            for (int c = 0; c < 3; c++)
            {
                int difference = full[i + c] - reduced[i + c];
                squared += difference * difference;
                largest = Math.Max(largest, Math.Abs(difference));
            }

            over += largest > 8 ? 1 : 0;
            max = Math.Max(max, largest);
        }

        double mse = squared / (pixels * 3.0);
        double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse);
        return (psnr, over / (double)pixels, max);
    }

    /// <summary>Asserts the page tolerance of #362: PSNR at least <paramref name="minimumPsnr"/>, at most <paramref name="maximumOver8"/> of pixels more than 8 levels off.</summary>
    /// <param name="full">The full-resolution render.</param>
    /// <param name="reduced">The reduced render.</param>
    /// <param name="what">What is compared, for the message.</param>
    /// <param name="minimumPsnr">The least PSNR in dB.</param>
    /// <param name="maximumOver8">The largest share of pixels more than 8 levels off.</param>
    public static void AssertWithinTolerance(ReadOnlySpan<byte> full, ReadOnlySpan<byte> reduced, string what, double minimumPsnr = 35, double maximumOver8 = 0.04)
    {
        (double psnr, double over8, _) = Compare(full, reduced);
        if (psnr < minimumPsnr || over8 > maximumOver8)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{what}: PSNR {psnr:F1} dB, {over8:P2} of pixels over 8 levels"));
        }
    }

    private static byte[] Image(byte[] samples, string space, int bits, string extra = "") =>
        File(Paint, Stream(samples, FormattableString.Invariant($"/Width 1024 /Height 1024 /ColorSpace {space} /BitsPerComponent {bits} {extra}")));

    private static byte[] File(string content, params string[] objects) =>
        ContentSamples.OnePage(Encoding.ASCII.GetBytes(content), "/XObject << /Im0 5 0 R >>", objects);

    /// <summary>An image XObject compressed with Flate.</summary>
    private static string Stream(byte[] samples, string entries)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest))
        {
            zlib.Write(samples);
        }

        return ContentSamples.Stream(Encoding.Latin1.GetString(compressed.ToArray()), "/Type /XObject /Subtype /Image /Filter /FlateDecode " + entries);
    }

    private static byte[] Samples(int width, int height, int components, int bits)
    {
        byte[] photo = ReducingImageFilter.Photo(width, height, components);
        if (bits == 8)
        {
            return photo;
        }

        byte[] wide = new byte[photo.Length * 2];
        for (int i = 0; i < photo.Length; i++)
        {
            int value = (photo[i] * 257) ^ (i % 97);
            wide[2 * i] = (byte)(value >> 8);
            wide[(2 * i) + 1] = (byte)value;
        }

        return wide;
    }

    /// <summary>Ordered-dither a gray image to 1 bit per sample (1 = white), rows padded to bytes.</summary>
    private static byte[] Dither(byte[] gray, int width, int height)
    {
        int stride = (width + 7) / 8;
        byte[] bits = new byte[stride * height];
        int[] bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (gray[(y * width) + x] > (bayer[((y & 3) * 4) + (x & 3)] * 16) + 8)
                {
                    bits[(y * stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return bits;
    }
}
