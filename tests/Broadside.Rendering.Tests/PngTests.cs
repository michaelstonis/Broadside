using System.Buffers.Binary;
using Broadside.TestSupport;
using StbImageSharp;

namespace Broadside.Rendering.Tests;

/// <summary>
/// The PNG encoder, checked through an independent decoder (StbImageSharp): every pixel comes back exactly, opaque bitmaps as RGB and
/// others as RGBA whose straight colour premultiplies back to the bitmap's bytes.
/// </summary>
public sealed class PngTests
{
    [Fact]
    public void A_rendered_page_round_trips_its_pixels_exactly_through_an_independent_decoder()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(document.Pages[0], new RenderOptions().WithDpi(150));
        using MemoryStream png = PngSnapshots.Encode(bitmap);

        Assert.Equal(2, ColorType(png));
        AssertRoundTrips(bitmap, PngSnapshots.Decode(png));
    }

    [Fact]
    public void Anti_aliased_edges_on_a_transparent_bitmap_round_trip_through_straight_alpha()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("paths-fill-rules.pdf"));
        using var rasterizer = new SoftwareRasterizer();
        PdfPage page = document.Pages[0];
        var options = new RenderOptions().WithScale(1.37);
        PixelSize size = options.GetPixelSize(page);
        using var bitmap = new Bitmap(size.Width, size.Height);
        rasterizer.Render(page.CreateDisplayList(), bitmap, options.GetDeviceTransform(page));
        using MemoryStream png = PngSnapshots.Encode(bitmap);

        Assert.Equal(6, ColorType(png));
        AssertRoundTrips(bitmap, PngSnapshots.Decode(png));
    }

    [Fact]
    public void Every_premultiplied_channel_and_alpha_pair_round_trips()
    {
        // Column a, row c: alpha a with every channel c ≤ a (premultiplied colour never exceeds its alpha); blue, green and red differ.
        using var bitmap = new Bitmap(256, 256);
        for (int c = 0; c < 256; c++)
        {
            Span<byte> row = bitmap.GetRow(c);
            for (int a = c; a < 256; a++)
            {
                row[a * 4] = (byte)c;
                row[(a * 4) + 1] = (byte)(a - c);
                row[(a * 4) + 2] = (byte)(c / 2);
                row[(a * 4) + 3] = (byte)a;
            }
        }

        using MemoryStream png = PngSnapshots.Encode(bitmap);

        AssertRoundTrips(bitmap, PngSnapshots.Decode(png));
    }

    [Fact]
    public void A_large_image_spans_several_data_chunks()
    {
        // Noise does not compress, so 1024 x 512 x 3 bytes need more than one 1 MiB IDAT chunk.
        using var bitmap = new Bitmap(1024, 512);
        uint state = 95;
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < row.Length; x++)
            {
                // xorshift32: deterministic noise.
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                row[x] = x % 4 == 3 ? (byte)0xFF : (byte)state;
            }
        }

        using MemoryStream png = PngSnapshots.Encode(bitmap);

        Assert.True(Chunks(png).Count(type => type == "IDAT") > 1);
        Assert.Equal(["IHDR", "IDAT", "IEND"], Chunks(png).Distinct());
        AssertRoundTrips(bitmap, PngSnapshots.Decode(png));
    }

    private static void AssertRoundTrips(Bitmap bitmap, ImageResult decoded)
    {
        Assert.Equal((bitmap.Width, bitmap.Height), (decoded.Width, decoded.Height));
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<byte> row = bitmap.GetRow(y);
            for (int x = 0; x < bitmap.Width; x++)
            {
                int i = ((y * bitmap.Width) + x) * 4;
                int alpha = decoded.Data[i + 3];
                var actual = new Rgba(Premultiply(decoded.Data[i], alpha), Premultiply(decoded.Data[i + 1], alpha), Premultiply(decoded.Data[i + 2], alpha), (byte)alpha);
                var expected = new Rgba(row[(x * 4) + 2], row[(x * 4) + 1], row[x * 4], row[(x * 4) + 3]);
                if (actual != expected)
                {
                    Assert.Fail($"Pixel ({x}, {y}) is {expected} in the bitmap and {actual} after the round trip.");
                }
            }
        }
    }

    /// <summary>round(u × a / 255).</summary>
    private static byte Premultiply(int straight, int alpha) => (byte)(((2 * straight * alpha) + 255) / 510);

    private static int ColorType(MemoryStream png) => png.GetBuffer()[8 + 8 + 9];

    private static List<string> Chunks(MemoryStream png)
    {
        var types = new List<string>();
        ReadOnlySpan<byte> data = png.GetBuffer().AsSpan(8, (int)png.Length - 8);
        while (!data.IsEmpty)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data);
            types.Add(System.Text.Encoding.ASCII.GetString(data.Slice(4, 4)));
            data = data[(12 + length)..];
        }

        return types;
    }
}
