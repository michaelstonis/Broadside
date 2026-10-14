using System.Globalization;
using System.Text;

namespace Broadside.TestSupport;

/// <summary>
/// Synthetic pages of masked images (ISO 32000-2 §8.9.6, §11.6.5) for allocation tests and benchmarks of the rasterizer's mask
/// paths. Linked as source into the benchmark project.
/// </summary>
public static class MaskSamples
{
    /// <summary>
    /// One page painting every mask path of the software rasterizer: stencil masks (an XObject enlarged, reduced, interpolated and
    /// rotated, and an inline one), explicit masks finer than their image and of its size, a 16-bit soft mask with a Decode array at
    /// another size, same-size soft masks with Matte (DeviceRGB and Indexed) and colour key masking.
    /// </summary>
    /// <returns>The file.</returns>
    public static byte[] MaskHeavy()
    {
        const string image = "/Type /XObject /Subtype /Image ";
        var palette = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            palette.Append(CultureInfo.InvariantCulture, $"{i * 17:X2}{255 - (i * 17):X2}{(i * 51) % 256:X2}");
        }

        const string content =
            "1 0 0 rg q 300 0 0 300 20 470 cm /Stencil Do Q 0 0 1 rg q 20 0 0 20 340 740 cm /Stencil Do Q "
            + "0 1 0 rg q 150 0 0 150 380 560 cm /Smooth Do Q q 80 80 -80 80 500 420 cm /Stencil Do Q "
            + "0.5 g q 60 0 0 60 520 700 cm BI /W 8 /H 2 /IM true ID ªU EI Q "
            + "q 200 0 0 200 20 240 cm /Explicit Do Q q 100 0 0 100 240 340 cm /ExplicitSame Do Q "
            + "q 180 0 0 180 360 240 cm /Soft Do Q q 150 0 0 150 20 20 cm /Matte Do Q "
            + "q 150 0 0 150 200 20 cm /Key Do Q q 150 0 0 150 380 20 cm /IndexedMatte Do Q";
        return ContentSamples.OnePage(
            Encoding.Latin1.GetBytes(content),
            "/XObject << /Stencil 5 0 R /Smooth 6 0 R /Explicit 7 0 R /ExplicitSame 9 0 R /Soft 11 0 R /Matte 13 0 R /Key 15 0 R /IndexedMatte 16 0 R >>",
            ContentSamples.Stream(Bits(64, 64, static (x, y) => ((x / 8) + (y / 8)) % 2 == 0), image + "/Width 64 /Height 64 /ImageMask true"),
            ContentSamples.Stream(Bits(64, 64, static (x, y) => (x * x) + (y * y) < 2048), image + "/Width 64 /Height 64 /ImageMask true /Decode [1 0] /Interpolate true"),
            ContentSamples.Stream(Bytes(32, 32, 3, static (x, y, c) => (x * 8) + (y * c)), image + "/Width 32 /Height 32 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask 8 0 R"),
            ContentSamples.Stream(Bits(128, 128, static (x, y) => ((x - 64) * (x - 64)) + ((y - 64) * (y - 64)) > 3600), image + "/Width 128 /Height 128 /ImageMask true"),
            ContentSamples.Stream(Bytes(32, 32, 1, static (x, y, c) => x * y), image + "/Width 32 /Height 32 /ColorSpace /DeviceGray /BitsPerComponent 8 /Mask 10 0 R"),
            ContentSamples.Stream(Bits(32, 32, static (x, y) => x > y), image + "/Width 32 /Height 32 /ImageMask true"),
            ContentSamples.Stream(Bytes(64, 64, 3, static (x, y, c) => (x * 4) ^ (y * (c + 1))), image + "/Width 64 /Height 64 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 12 0 R"),
            ContentSamples.Stream(Bytes(16, 16, 2, static (x, y, c) => c == 0 ? x * 16 : y * 16), image + "/Width 16 /Height 16 /ColorSpace /DeviceGray /BitsPerComponent 16 /Decode [1 0] /Interpolate true"),
            ContentSamples.Stream(Bytes(64, 64, 3, static (x, y, c) => 255 - ((x + y + c) % 64)), image + "/Width 64 /Height 64 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 14 0 R"),
            ContentSamples.Stream(Bytes(64, 64, 1, static (x, y, c) => x * 4), image + "/Width 64 /Height 64 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [1 1 1]"),
            ContentSamples.Stream(Bytes(64, 64, 3, static (x, y, c) => (x + y) * c), image + "/Width 64 /Height 64 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask [0 40 0 80 0 120]"),
            ContentSamples.Stream(Bytes(32, 16, 1, static (x, y, c) => ((x & 15) << 4) | (y & 15)), image + $"/Width 32 /Height 32 /BitsPerComponent 4 /ColorSpace [/Indexed /DeviceRGB 15 <{palette}>] /SMask 17 0 R"),
            ContentSamples.Stream(Bytes(32, 32, 1, static (x, y, c) => y * 8), image + "/Width 32 /Height 32 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte [0 0 0]"));
    }

    /// <summary>Packs a 1-bit image, rows padded to a byte, as a Latin-1 string.</summary>
    private static string Bits(int width, int height, Func<int, int, bool> set)
    {
        var text = new StringBuilder(((width + 7) / 8) * height);
        for (int y = 0; y < height; y++)
        {
            for (int b = 0; b < (width + 7) / 8; b++)
            {
                int value = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    int x = (b * 8) + bit;
                    value |= x < width && set(x, y) ? 0x80 >> bit : 0;
                }

                text.Append((char)value);
            }
        }

        return text.ToString();
    }

    /// <summary>Writes <paramref name="width"/> × <paramref name="height"/> samples of <paramref name="bytes"/> bytes each, as a Latin-1 string.</summary>
    private static string Bytes(int width, int height, int bytes, Func<int, int, int, int> value)
    {
        var text = new StringBuilder(width * height * bytes);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < bytes; c++)
                {
                    text.Append((char)(value(x, y, c) & 0xFF));
                }
            }
        }

        return text.ToString();
    }
}
