using System.IO.Compression;
using System.Text;
using BenchmarkDotNet.Attributes;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Issue #362, part 4: a letter page holding one large Flate image, decoded in full when its list is built (Flate cannot reduce),
/// shown at a phone's width (390 pixels) as a viewer shows it (<c>Scaled</c>: reduced from the full decode by a box average before
/// conversion, a 1-bit scan to 8-bit coverage) or as a page render does. <c>Image</c>: a 2550 x 3300 8-bit gray photograph, or a
/// 5100 x 6600 1-bit scan (600 dpi). <see cref="Replay"/> renders the page again from a kept list whose reduction is made: the
/// converting and painting alone, which allocates nothing.
/// </summary>
[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet releases them in [GlobalCleanup].")]
public class ScaledScanBenchmarks
{
    private PdfDocument? _document;
    private DisplayList? _list;
    private Bitmap? _bitmap;
    private SoftwareRasterizer? _rasterizer;
    private Matrix _device;

    /// <summary>The image: an 8-bit gray photograph, or a 1-bit scan.</summary>
    [Params("gray", "scan")]
    public string Image { get; set; } = "gray";

    /// <summary>Whether the image is reduced when drawn small, as viewers do.</summary>
    [Params(false, true)]
    public bool Scaled { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = PdfDocument.Open(Page(Image == "scan"));
        PdfPage page = _document.Pages[0];
        RenderOptions options = new RenderOptions().WithScale(390 / page.CropBox.Width);
        PixelSize size = options.GetPixelSize(page);
        _device = options.GetDeviceTransform(page);
        _bitmap = new Bitmap(size.Width, size.Height);
        _rasterizer = new SoftwareRasterizer { ScaledImageDecode = Scaled };
        _list = page.CreateDisplayList();
        Replay();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rasterizer?.Dispose();
        _bitmap?.Dispose();
        _document?.Dispose();
    }

    /// <summary>Renders the page from a kept list.</summary>
    [Benchmark]
    public int Replay()
    {
        _rasterizer!.Render(_list!, _bitmap!, _device);
        return _bitmap!.Pixels[(_bitmap.Height / 2 * _bitmap.Stride) + 2];
    }

    private static byte[] Page(bool scan)
    {
        int width = scan ? 5100 : 2550;
        int height = scan ? 6600 : 3300;
        byte[] photo = CatalogueSamples.Photo(width, height);
        byte[] samples;
        if (scan)
        {
            int stride = (width + 7) / 8;
            samples = new byte[stride * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (photo[((y * width) + x) * 3] > 128 + (((x ^ y) & 7) * 8))
                    {
                        samples[(y * stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }
        }
        else
        {
            samples = new byte[width * height];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = photo[3 * i];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest))
        {
            zlib.Write(samples);
        }

        string image = ContentSamples.Stream(
            Encoding.Latin1.GetString(compressed.ToArray()),
            FormattableString.Invariant($"/Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceGray /BitsPerComponent {(scan ? 1 : 8)} /Filter /FlateDecode"));
        return ContentSamples.OnePage("q 612 0 0 792 0 0 cm /Im0 Do Q"u8.ToArray(), "/XObject << /Im0 5 0 R >>", image);
    }
}
