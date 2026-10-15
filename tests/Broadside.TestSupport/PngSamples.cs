using System.Buffers.Binary;
using System.IO.Compression;

namespace Broadside.TestSupport;

/// <summary>
/// Builds PNG files of any size for the allocation tests and benchmarks of issue #153: 8-bit RGB or RGBA, optionally Adam7
/// interlaced, every row filtered with the filter type <c>row mod 5</c> over a smooth gradient. W3C PNG (Third Edition) §5, §7,
/// §8 and §9.
/// </summary>
public static class PngSamples
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>A PNG file.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="alpha">Whether the image is RGBA (colour type 6) rather than RGB (colour type 2).</param>
    /// <param name="interlaced">Whether the image is Adam7 interlaced.</param>
    /// <returns>The file.</returns>
    public static byte[] Rgb(int width, int height, bool alpha, bool interlaced)
    {
        int channels = alpha ? 4 : 3;
        using var raw = new MemoryStream();
        foreach ((int x0, int y0, int dx, int dy) in interlaced ? Adam7 : [(0, 0, 1, 1)])
        {
            int passWidth = (width - x0 + dx - 1) / dx;
            int passHeight = (height - y0 + dy - 1) / dy;
            if (passWidth <= 0 || passHeight <= 0)
            {
                continue;
            }

            byte[] previous = new byte[passWidth * channels];
            byte[] row = new byte[passWidth * channels];
            for (int y = 0; y < passHeight; y++)
            {
                for (int x = 0; x < passWidth; x++)
                {
                    int px = x0 + (x * dx);
                    int py = y0 + (y * dy);
                    row[(x * channels) + 0] = (byte)(px * 3);
                    row[(x * channels) + 1] = (byte)(py * 5);
                    row[(x * channels) + 2] = (byte)((px + py) * 2);
                    if (alpha)
                    {
                        row[(x * channels) + 3] = (byte)(255 - px);
                    }
                }

                int filter = y % 5;
                raw.WriteByte((byte)filter);
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= channels ? row[i - channels] : 0;
                    int up = previous[i];
                    int upLeft = i >= channels ? previous[i - channels] : 0;
                    int predicted = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        _ => Paeth(left, up, upLeft),
                    };
                    raw.WriteByte((byte)(row[i] - predicted));
                }

                (previous, row) = (row, previous);
            }
        }

        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = (byte)(alpha ? 6 : 2);
        header[12] = (byte)(interlaced ? 1 : 0);
        Chunk(output, "IHDR"u8, header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.Position = 0;
                raw.CopyTo(zlib);
            }

            // Split in two IDAT chunks, as encoders that stream do.
            byte[] data = compressed.ToArray();
            Chunk(output, "IDAT"u8, data.AsSpan(0, data.Length / 2));
            Chunk(output, "IDAT"u8, data.AsSpan(data.Length / 2));
        }

        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(MemoryStream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        output.Write(number);
        output.Write(type);
        output.Write(body);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in body)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFF);
        output.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
