using System.Globalization;
using Broadside.Filters;
using Broadside.Images;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>A difference of two decodes, as the platform decoder seam's verification mode measures it.</summary>
internal readonly record struct Difference(int Max, double Psnr, double OverTwoLevels)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"max {Max}, {Psnr:0.0} dB, {OverTwoLevels * 100:0.###}% over 2 levels");
}

/// <summary>The JPEG vectors, requests made from a JPEG's frame header, managed decodes and comparisons, for the ImageIO decoder's tests.</summary>
internal static class ImageIOTesting
{
    /// <summary>Gets the directory of the DCT vectors (tests/Broadside.Tests/Filters/Dct, provenance in the README there).</summary>
    public static string VectorDirectory { get; } = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Filters", "Dct");

    /// <summary>The bytes of a vector.</summary>
    public static byte[] Vector(string name) => File.ReadAllBytes(Path.Combine(VectorDirectory, name + ".jpg"));

    /// <summary>
    /// A request from the frame header (ITU-T T.81 B.2.2) for 8-bit data of 1 or 3 components, or <see langword="null"/>; not the
    /// core's full eligibility rule, which the document tests go through.
    /// </summary>
    public static PlatformImageRequest? Request(byte[] jpeg, int reduction)
    {
        int i = 2;
        while (i + 4 <= jpeg.Length && jpeg[i] == 0xFF)
        {
            int marker = jpeg[i + 1];
            int length = (jpeg[i + 2] << 8) | jpeg[i + 3];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                int p = i + 4;
                int precision = jpeg[p];
                int height = (jpeg[p + 1] << 8) | jpeg[p + 2];
                int width = (jpeg[p + 3] << 8) | jpeg[p + 4];
                int components = jpeg[p + 5];
                bool subsampled = false;
                for (int c = 1; c < components; c++)
                {
                    subsampled |= jpeg[p + 6 + (c * 3) + 1] != jpeg[p + 6 + 1];
                }

                if (precision != 8 || components is not (1 or 3) || height == 0 || marker is 0xC3 or 0xC7 or 0xCB or 0xCF or 0xC5 or 0xC6 or 0xCD or 0xCE)
                {
                    return null;
                }

                return new PlatformImageRequest
                {
                    Filter = new CosName("DCTDecode"),
                    Width = width,
                    Height = height,
                    Components = components,
                    BitsPerComponent = 8,
                    Progressive = marker is 0xC2 or 0xCA,
                    ArithmeticCoding = marker >= 0xC9,
                    ColorTransform = components == 3,
                    ChromaSubsampled = subsampled,
                    Reduction = reduction,
                };
            }

            i += 2 + length;
        }

        return null;
    }

    /// <summary>
    /// Box-averages a full-size decode by 2^<paramref name="reduction"/> (each reduced sample the mean of the samples it covers, rounded
    /// half up, as the core's reducer of images decoded at full size) and disposes it; for measuring ImageIO decoded at full size and
    /// reduced afterwards.
    /// </summary>
    public static DecodedImage? Average(DecodedImage? full, int reduction)
    {
        if (full is null || reduction == 0)
        {
            return full;
        }

        using (full)
        {
            int scale = 1 << reduction;
            int components = full.Components;
            var request = new PlatformImageRequest { Width = full.Width, Height = full.Height, Components = components, BitsPerComponent = 8, Reduction = reduction };
            using DecodedImageBuilder builder = request.CreateImage(reduction);
            int[] sums = new int[builder.Width * components];
            for (int y = 0; y < builder.Height; y++)
            {
                Array.Clear(sums);
                int rows = Math.Min(full.Height, (y + 1) * scale) - (y * scale);
                for (int sy = y * scale; sy < (y * scale) + rows; sy++)
                {
                    ReadOnlySpan<byte> source = full.GetRow(sy);
                    for (int x = 0; x < full.Width; x++)
                    {
                        for (int c = 0; c < components; c++)
                        {
                            sums[((x >> reduction) * components) + c] += source[(x * components) + c];
                        }
                    }
                }

                Span<byte> row = builder.GetRow(y);
                for (int x = 0; x < builder.Width; x++)
                {
                    int count = (Math.Min(full.Width, (x + 1) * scale) - (x * scale)) * rows;
                    for (int c = 0; c < components; c++)
                    {
                        row[(x * components) + c] = (byte)((sums[(x * components) + c] + (count / 2)) / count);
                    }
                }
            }

            return builder.Build();
        }
    }

    /// <summary>The managed decoder's samples at the request's reduction.</summary>
    public static DecodedImage? Managed(byte[] jpeg, in PlatformImageRequest request) =>
        new DctDecodeFilter().DecodeImage(jpeg, new ImageFilterContext(new FilterContext())
        {
            Width = request.Width,
            Height = request.Height,
            BitsPerComponent = 8,
            ColorComponents = request.Components,
            Reduction = request.Reduction,
        });

    /// <summary>Measures <paramref name="a"/> against <paramref name="b"/> (same size and components, 8-bit).</summary>
    public static Difference Compare(DecodedImage a, DecodedImage b)
    {
        Assert.Equal((b.Width, b.Height, b.Components), (a.Width, a.Height, a.Components));
        int components = a.Components;
        int max = 0;
        long over = 0;
        double squares = 0;
        for (int y = 0; y < a.Height; y++)
        {
            ReadOnlySpan<byte> rowA = a.GetRow(y)[..(a.Width * components)];
            ReadOnlySpan<byte> rowB = b.GetRow(y)[..(a.Width * components)];
            for (int x = 0; x < rowA.Length; x += components)
            {
                int pixel = 0;
                for (int c = 0; c < components; c++)
                {
                    int d = Math.Abs(rowA[x + c] - rowB[x + c]);
                    squares += d * d;
                    pixel = Math.Max(pixel, d);
                }

                max = Math.Max(max, pixel);
                over += pixel > 2 ? 1 : 0;
            }
        }

        long pixels = (long)a.Width * a.Height;
        double mse = squares / (pixels * components);
        return new Difference(max, mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 / mse), over / (double)pixels);
    }
}
