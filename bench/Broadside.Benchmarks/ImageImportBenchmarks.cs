using BenchmarkDotNet.Attributes;
using Broadside.Images.Import;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The TIFF, GIF and BMP readers of issue #642 over 512 x 512 images built by <see cref="ImportSamples"/>: TIFF LZW with the
/// horizontal predictor (RGB), Deflate (RGB, 16 strips), PackBits (gray), CCITT Group 4 in 16-row strips (decoded, since the
/// strips cannot be joined), planar RGBA, and GIF and 24-bit BMP. Decoding strips, undoing the predictor and splitting planes are a
/// hot path: <c>Allocated</c> must not grow with the height; <c>Broadside.Tests.Images.ImageImportAllocationTests</c> fails the
/// build otherwise.
/// </summary>
[MemoryDiagnoser]
public class ImageImportBenchmarks
{
    private byte[] _file = [];

    /// <summary>The input: <c>tiff-lzw</c>, <c>tiff-deflate</c>, <c>tiff-packbits</c>, <c>tiff-g4</c>, <c>tiff-planar</c>, <c>gif</c> or <c>bmp</c>.</summary>
    [Params("tiff-lzw", "tiff-deflate", "tiff-packbits", "tiff-g4", "tiff-planar", "gif", "bmp")]
    public string Input { get; set; } = "tiff-lzw";

    [GlobalSetup]
    public void Setup()
    {
        const int size = 512;
        byte[] rgb = ImportSamples.RgbGradient(size, size);
        _file = Input switch
        {
            "tiff-lzw" => ImportSamples.Tiff(new TiffPage(size, size, 3, 8, 2, rgb) { Compression = 5, Predictor = 2 }),
            "tiff-deflate" => ImportSamples.Tiff(new TiffPage(size, size, 3, 8, 2, rgb) { Compression = 8, RowsPerStrip = 32 }),
            "tiff-packbits" => ImportSamples.Tiff(new TiffPage(size, size, 1, 8, 1, ImportSamples.Pack(size, size, 8, 1, (x, y, _) => (x / 8) + (y / 8))) { Compression = 32773 }),
            "tiff-g4" => ImportSamples.Tiff(new TiffPage(size, size, 1, 1, 0, ImportSamples.Bilevel(size, size)) { Compression = 4, RowsPerStrip = 16 }),
            "tiff-planar" => ImportSamples.Tiff(new TiffPage(size, size, 4, 8, 2, ImportSamples.Pack(size, size, 8, 4, (x, y, c) => (x + (y * c)) & 255)) { Compression = 5, Planar = 2, ExtraSamples = [2] }),
            "gif" => ImportSamples.Gif(size, size, [.. rgb.Where((_, i) => i % 3 == 0)], [.. Enumerable.Range(0, 768).Select(i => (byte)i)]),
            _ => ImportSamples.Bmp24(size, size, rgb),
        };
        if (Read() != size)
        {
            throw new InvalidOperationException($"{Input} did not read.");
        }
    }

    [Benchmark]
    public int Read()
    {
        if (Input == "gif")
        {
            using ImportedImage gif = GifReader.Read(_file, long.MaxValue);
            return gif.Height;
        }

        if (Input == "bmp")
        {
            using ImportedImage bmp = BmpReader.Read(_file, long.MaxValue);
            return bmp.Height;
        }

        List<long> pages = TiffReader.ListPages(_file, out bool little);
        using ImportedImage tiff = TiffReader.ReadPage(_file, pages[0], little, long.MaxValue);
        return tiff.Height;
    }
}
