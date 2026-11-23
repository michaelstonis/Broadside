using System.Buffers;
using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// Writes a <see cref="Bitmap"/> as a baseline JPEG image through <see cref="DctEncoder"/>, compositing premultiplied BGRA pixels
/// over opaque white when any pixel is transparent.
/// </summary>
/// <remarks>ITU-T T.81 (ISO/IEC 10918-1) baseline sequential process; ISO 32000-2 §7.4.8.</remarks>
internal static class BitmapJpegEncoder
{
    private static readonly CosName WidthName = new("Width");
    private static readonly CosName HeightName = new("Height");

    public static void Write(Bitmap bitmap, Stream stream, int quality)
    {
        if (bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            throw new ArgumentException("A baseline JPEG image's width and height must be at most 65535 pixels (ITU-T T.81 §B.2.2).", nameof(bitmap));
        }

        int rgbLength = checked(bitmap.Width * bitmap.Height * 3);
        byte[] rgb = ArrayPool<byte>.Shared.Rent(rgbLength);
        try
        {
            int offset = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                ReadOnlySpan<byte> row = bitmap.GetRow(y);
                for (int i = 0; i < row.Length; i += 4)
                {
                    int b = row[i];
                    int g = row[i + 1];
                    int r = row[i + 2];
                    int a = row[i + 3];
                    int backdrop = 255 - a;
                    rgb[offset] = (byte)Math.Min(255, r + backdrop);
                    rgb[offset + 1] = (byte)Math.Min(255, g + backdrop);
                    rgb[offset + 2] = (byte)Math.Min(255, b + backdrop);
                    offset += 3;
                }
            }

            var dictionary = new CosDictionary
            {
                [WidthName] = new CosInteger(bitmap.Width),
                [HeightName] = new CosInteger(bitmap.Height),
            };
            var context = new StreamEncoderContext { StreamDictionary = dictionary };
            var writer = new ArrayBufferWriter<byte>(Math.Max(1024, rgbLength / 4));
            new DctEncoder(quality).Encode(rgb.AsSpan(0, rgbLength), writer, context);
            stream.Write(writer.WrittenSpan);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rgb);
        }
    }
}
