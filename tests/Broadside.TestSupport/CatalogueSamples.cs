using System.Buffers;
using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;

namespace Broadside.TestSupport;

/// <summary>
/// The catalogue-class page of issue #362, synthesized: a letter-size page filled by one large photograph, DCT-encoded (baseline,
/// 4:4:4 unless asked otherwise, quality 90) in an ICCBased RGB space (the sRGB profile). It stands in for the owner's catalogue
/// cover (2550 x 3150, DCT, ICCBased), which is never committed; the samples are generated, so the file is public domain.
/// </summary>
/// <remarks>
/// The photograph is smooth gradients, a few hard edges and fine texture, like a product photo: what the reduced decode and the
/// rasterizer's box filter both see in real covers. Built once per process and size (encoding 8 megapixels takes about a second).
/// </remarks>
public static class CatalogueSamples
{
    private static readonly Dictionary<(int, int, DctChromaSubsampling), byte[]> Pages = [];
    private static readonly Dictionary<(int, int, DctChromaSubsampling), byte[]> Jpegs = [];
    private static readonly Lock Gate = new();

    /// <summary>Returns the page: a <paramref name="width"/> × <paramref name="height"/> photograph over the whole 612 x 792 page.</summary>
    /// <param name="width">The photograph's width in samples.</param>
    /// <param name="height">The photograph's height in samples.</param>
    /// <param name="subsampling">The chroma subsampling of the JPEG.</param>
    /// <returns>The file.</returns>
    public static byte[] Cover(int width = 2550, int height = 3150, DctChromaSubsampling subsampling = DctChromaSubsampling.None)
    {
        lock (Gate)
        {
            if (!Pages.TryGetValue((width, height, subsampling), out byte[]? file))
            {
                file = Build(width, height, subsampling);
                Pages[(width, height, subsampling)] = file;
            }

            return file;
        }
    }

    /// <summary>Returns the photograph alone as a JPEG: baseline, quality 90, YCbCr with <paramref name="subsampling"/>.</summary>
    /// <param name="width">The width in samples.</param>
    /// <param name="height">The height in samples.</param>
    /// <param name="subsampling">The chroma subsampling.</param>
    /// <returns>The JPEG.</returns>
    public static byte[] Jpeg(int width = 2550, int height = 3150, DctChromaSubsampling subsampling = DctChromaSubsampling.None)
    {
        lock (Gate)
        {
            if (!Jpegs.TryGetValue((width, height, subsampling), out byte[]? jpeg))
            {
                var output = new ArrayBufferWriter<byte>();
                var context = new StreamEncoderContext
                {
                    StreamDictionary = new CosDictionary { [new CosName("Width")] = new CosInteger(width), [new CosName("Height")] = new CosInteger(height) },
                };
                new DctEncoder(90, subsampling).Encode(Photo(width, height), output, context);
                jpeg = output.WrittenSpan.ToArray();
                Jpegs[(width, height, subsampling)] = jpeg;
            }

            return jpeg;
        }
    }

    /// <summary>Returns photo-like RGB samples, <paramref name="width"/> × <paramref name="height"/> × 3 bytes.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The samples.</returns>
    public static byte[] Photo(int width, int height)
    {
        byte[] samples = new byte[width * height * 3];
        uint noise = 2463534242;
        for (int y = 0; y < height; y++)
        {
            double v = y / (double)height;
            for (int x = 0; x < width; x++)
            {
                double u = x / (double)width;
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                double grain = ((noise & 0xFF) - 127.5) / 16;
                double weave = 18 * Math.Sin(x * 0.31) * Math.Sin(y * 0.29);
                bool product = Math.Pow((u - 0.5) / 0.3, 2) + Math.Pow((v - 0.55) / 0.25, 2) < 1;
                int at = ((y * width) + x) * 3;
                if (product)
                {
                    double light = 0.6 + (0.4 * Math.Cos((u - 0.45) * 6)) - (0.3 * v);
                    samples[at] = Clamp((200 * light) + grain);
                    samples[at + 1] = Clamp((70 * light) + (weave / 2) + grain);
                    samples[at + 2] = Clamp((40 * light) + grain);
                }
                else
                {
                    samples[at] = Clamp(225 - (60 * v) + weave + grain);
                    samples[at + 1] = Clamp(215 - (40 * v) + weave + grain);
                    samples[at + 2] = Clamp(200 - (30 * u) + weave + grain);
                }
            }
        }

        return samples;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static byte[] Build(int width, int height, DctChromaSubsampling subsampling)
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStreamEncoder(new DctEncoder(90, subsampling)));
        PdfIccBasedColorSpace srgb = document.CreateIccBasedColorSpace(PdfIccBasedColorSpace.CreateSrgbProfileData());
        PdfImage image = document.CreateImage(Photo(width, height), new PdfImageLayout(width, height, srgb), PdfImageCompression.Dct);
        // A new document starts with one empty letter page.
        PdfPage page = document.Pages.Count > 0 ? document.Pages[0] : document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.DrawImage(image, 0, 0, 612, 792);
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }
}
