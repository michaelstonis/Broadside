using System.Buffers.Binary;
using System.IO.Compression;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// The PNG vectors in <c>Png/</c> (provenance in the README there) and their reference pixels, decoded by Go's image/png; and the
/// same pixels read back from a PDF image (base image, soft mask or colour key), for comparison.
/// </summary>
internal static class PngVectors
{
    /// <summary>The directory of the vectors.</summary>
    public static string Directory { get; } = Path.Combine(Corpus.Directory, "..", "Broadside.Tests", "Graphics", "Png");

    /// <summary>Every vector, as <c>pngsuite/name</c> or <c>generated/name</c> without the extension.</summary>
    public static TheoryData<string> All => new(
        new[] { "pngsuite", "generated" }
            .SelectMany(folder => System.IO.Directory.EnumerateFiles(Path.Combine(Directory, folder), "*.png")
                .Select(path => folder + "/" + Path.GetFileNameWithoutExtension(path)))
            .Order(StringComparer.Ordinal));

    /// <summary>The bytes of a vector.</summary>
    public static byte[] Png(string name) => File.ReadAllBytes(Path.Combine(Directory, name + ".png"));

    /// <summary>The reference pixels: straight (not premultiplied) 16-bit R, G, B, A per pixel.</summary>
    public static ushort[] Reference(string name)
    {
        using var gzip = new GZipStream(File.OpenRead(Path.Combine(Directory, name + ".rgba64.gz")), CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        ushort[] pixels = new ushort[bytes.Length / 2];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2 * i));
        }

        return pixels;
    }

    /// <summary>
    /// Reads an image back as straight 16-bit R, G, B, A per pixel: samples scaled from their depth to 16 bits, an Indexed image's
    /// samples looked up in its table, gray repeated in R, G and B; alpha from the soft mask, the colour key, or opaque.
    /// </summary>
    public static ushort[] Read(PdfImage image)
    {
        using DecodedImage decoded = image.Decode() ?? throw new InvalidOperationException("The image does not decode.");
        Assert.Equal(decoded.Height, decoded.DecodedRows);
        int width = decoded.Width;
        int height = decoded.Height;
        int components = decoded.Components;
        int scale = 65535 / ((1 << image.BitsPerComponent) - 1);
        PdfColorSpace space = image.ColorSpace!;
        byte[]? lookup = space is PdfIndexedColorSpace indexed ? indexed.GetLookup().ToArray() : null;
        int colors = lookup is null ? components : ((PdfIndexedColorSpace)space).Base.ComponentCount;
        IReadOnlyList<double>? key = image.ColorKey;
        ushort[] alpha = Alpha(image, width, height);
        ushort[] pixels = new ushort[width * height * 4];
        ushort[] raw = new ushort[width * components];
        for (int y = 0; y < height; y++)
        {
            ImageRows.Unpack(decoded.GetRow(y), decoded.StorageBits, width * components, raw);
            for (int x = 0; x < width; x++)
            {
                Span<ushort> pixel = pixels.AsSpan(((y * width) + x) * 4, 4);
                for (int c = 0; c < 3; c++)
                {
                    int channel = colors == 1 ? 0 : c;
                    pixel[c] = lookup is null
                        ? (ushort)(raw[(x * components) + channel] * scale)
                        : (ushort)(lookup[(raw[x] * colors) + channel] * 0x101);
                }

                bool keyed = key is not null;
                for (int c = 0; keyed && c < components; c++)
                {
                    keyed = raw[(x * components) + c] >= key![2 * c] && raw[(x * components) + c] <= key[(2 * c) + 1];
                }

                pixel[3] = keyed ? (ushort)0 : alpha[(y * width) + x];
            }
        }

        return pixels;
    }

    /// <summary>Asserts that two pixel arrays agree: every alpha, and every colour where the pixel is not fully transparent.</summary>
    public static void AssertSamePixels(ushort[] expected, ushort[] actual, string name)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i += 4)
        {
            bool same = expected[i + 3] == actual[i + 3]
                && (expected[i + 3] == 0 || (expected[i] == actual[i] && expected[i + 1] == actual[i + 1] && expected[i + 2] == actual[i + 2]));
            Assert.True(same, $"{name}: pixel {i / 4} is {Format(actual, i)}, expected {Format(expected, i)}.");
        }
    }

    private static string Format(ushort[] pixels, int i) => $"({pixels[i]}, {pixels[i + 1]}, {pixels[i + 2]}, {pixels[i + 3]})";

    private static ushort[] Alpha(PdfImage image, int width, int height)
    {
        ushort[] alpha = new ushort[width * height];
        if (image.MaskKind != PdfImageMaskKind.Soft)
        {
            Array.Fill(alpha, ushort.MaxValue);
            return alpha;
        }

        PdfImage mask = image.Mask!;
        Assert.Equal((width, height, 1), (mask.Width, mask.Height, mask.ColorSpace!.ComponentCount));
        using DecodedImage decoded = mask.Decode() ?? throw new InvalidOperationException("The soft mask does not decode.");
        int scale = 65535 / ((1 << mask.BitsPerComponent) - 1);
        ushort[] row = new ushort[width];
        for (int y = 0; y < height; y++)
        {
            ImageRows.Unpack(decoded.GetRow(y), decoded.StorageBits, width, row);
            for (int x = 0; x < width; x++)
            {
                alpha[(y * width) + x] = (ushort)(row[x] * scale);
            }
        }

        return alpha;
    }
}
