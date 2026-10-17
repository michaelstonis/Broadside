using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Broadside.Rendering;

/// <summary>
/// Writes a <see cref="Bitmap"/> as PNG: signature, IHDR, IDAT, IEND (PNG specification, third edition, §5 and §11.2), 8-bit
/// truecolour (colour type 2) when every pixel is opaque, else truecolour with alpha (colour type 6), never interlaced.
/// </summary>
/// <remarks>
/// Rows are filtered with the per-row heuristic the PNG specification recommends for truecolour (§12.7: the filter type with the
/// smallest sum of absolute signed bytes) and compressed with zlib (§10, RFC 1950/1951) through the BCL's <see cref="ZLibStream"/>.
/// Chunk CRCs are CRC-32 over type and data (§5.5, ISO 3309). Premultiplied colour is un-premultiplied with rounding.
/// </remarks>
internal static class PngEncoder
{
    private const int MaxChunk = 1 << 20;

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(Bitmap bitmap, Stream stream)
    {
        bool opaque = IsOpaque(bitmap);
        int channels = opaque ? 3 : 4;
        int rowBytes = bitmap.Width * channels;

        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, bitmap.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], bitmap.Height);
        header[8] = 8;                          // bit depth
        header[9] = (byte)(opaque ? 2 : 6);     // colour type: truecolour, or truecolour with alpha
        header[10] = 0;                         // compression method 0 (deflate)
        header[11] = 0;                         // filter method 0 (adaptive, five types)
        header[12] = 0;                         // no interlace
        WriteChunk(stream, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        byte[] previous = ArrayPool<byte>.Shared.Rent(rowBytes);
        byte[] current = ArrayPool<byte>.Shared.Rent(rowBytes);
        byte[] candidates = ArrayPool<byte>.Shared.Rent(5 * (rowBytes + 1));
        try
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                Array.Clear(previous, 0, rowBytes);
                for (int y = 0; y < bitmap.Height; y++)
                {
                    Unpremultiply(bitmap.GetRow(y), current.AsSpan(0, rowBytes), channels);
                    zlib.Write(Filter(current.AsSpan(0, rowBytes), previous.AsSpan(0, rowBytes), channels, candidates.AsSpan(0, 5 * (rowBytes + 1))));
                    (previous, current) = (current, previous);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(previous);
            ArrayPool<byte>.Shared.Return(current);
            ArrayPool<byte>.Shared.Return(candidates);
        }

        ReadOnlySpan<byte> data = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
        do
        {
            int length = Math.Min(data.Length, MaxChunk);
            WriteChunk(stream, "IDAT"u8, data[..length]);
            data = data[length..];
        }
        while (!data.IsEmpty);

        WriteChunk(stream, "IEND"u8, []);
    }

    private static bool IsOpaque(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<byte> row = bitmap.GetRow(y);
            for (int x = 3; x < row.Length; x += 4)
            {
                if (row[x] != 0xFF)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Converts one row of premultiplied BGRA to RGB or straight RGBA.</summary>
    private static void Unpremultiply(ReadOnlySpan<byte> bgra, Span<byte> output, int channels)
    {
        int o = 0;
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int alpha = bgra[i + 3];
            if (channels == 3)
            {
                output[o] = bgra[i + 2];
                output[o + 1] = bgra[i + 1];
                output[o + 2] = bgra[i];
                o += 3;
                continue;
            }

            if (alpha == 0)
            {
                output.Slice(o, 4).Clear();
            }
            else
            {
                output[o] = Straight(bgra[i + 2], alpha);
                output[o + 1] = Straight(bgra[i + 1], alpha);
                output[o + 2] = Straight(bgra[i], alpha);
                output[o + 3] = (byte)alpha;
            }

            o += 4;
        }
    }

    /// <summary>round(c × 255 / a), clamped for a channel that exceeds its alpha.</summary>
    private static byte Straight(int channel, int alpha) => (byte)Math.Min(255, ((2 * channel * 255) + alpha) / (2 * alpha));

    /// <summary>Filters one row with each of the five filter types and returns the filter byte and row of the cheapest (PNG §9.2, §12.7).</summary>
    private static ReadOnlySpan<byte> Filter(ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, int bpp, Span<byte> candidates)
    {
        int size = row.Length + 1;
        Span<byte> none = candidates[..size];
        Span<byte> sub = candidates.Slice(size, size);
        Span<byte> up = candidates.Slice(2 * size, size);
        Span<byte> average = candidates.Slice(3 * size, size);
        Span<byte> paeth = candidates.Slice(4 * size, size);
        none[0] = 0;
        sub[0] = 1;
        up[0] = 2;
        average[0] = 3;
        paeth[0] = 4;
        long sumNone = 0, sumSub = 0, sumUp = 0, sumAverage = 0, sumPaeth = 0;
        for (int i = 0; i < row.Length; i++)
        {
            int value = row[i];
            int a = i >= bpp ? row[i - bpp] : 0;
            int b = above[i];
            int c = i >= bpp ? above[i - bpp] : 0;
            sumNone += Cost(none[i + 1] = (byte)value);
            sumSub += Cost(sub[i + 1] = (byte)(value - a));
            sumUp += Cost(up[i + 1] = (byte)(value - b));
            sumAverage += Cost(average[i + 1] = (byte)(value - ((a + b) >> 1)));
            sumPaeth += Cost(paeth[i + 1] = (byte)(value - Paeth(a, b, c)));
        }

        // Ties go to the simpler filter, in type order.
        (long best, int chosen) = (sumNone, 0);
        if (sumSub < best)
        {
            (best, chosen) = (sumSub, 1);
        }

        if (sumUp < best)
        {
            (best, chosen) = (sumUp, 2);
        }

        if (sumAverage < best)
        {
            (best, chosen) = (sumAverage, 3);
        }

        if (sumPaeth < best)
        {
            chosen = 4;
        }

        return candidates.Slice(chosen * size, size);
    }

    /// <summary>The heuristic cost of a filtered byte: its magnitude as a signed byte.</summary>
    private static int Cost(byte value) => Math.Abs((int)(sbyte)value);

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        stream.Write(word);
        stream.Write(type);
        stream.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        stream.Write(word);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
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
