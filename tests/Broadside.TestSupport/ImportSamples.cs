using System.Buffers.Binary;
using System.IO.Compression;

namespace Broadside.TestSupport;

/// <summary>How <see cref="ImportSamples.Tiff"/> writes one page.</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Samples">Samples per pixel.</param>
/// <param name="Bits">Bits per sample: 1, 2, 4, 8 or 16.</param>
/// <param name="Photometric">The PhotometricInterpretation.</param>
/// <param name="Data">The samples, chunky, rows byte-aligned, MSB first, 16-bit samples big-endian (the PDF layout).</param>
public sealed record TiffPage(int Width, int Height, int Samples, int Bits, int Photometric, byte[] Data)
{
    /// <summary>Gets the compression: 1, 2, 3, 4, 5 (LZW), 8 (Deflate) or 32773 (PackBits); any other value is written as stated with the data uncompressed.</summary>
    public int Compression { get; init; } = 1;

    /// <summary>Gets the predictor (2: horizontal differencing).</summary>
    public int Predictor { get; init; } = 1;

    /// <summary>Gets the planar configuration.</summary>
    public int Planar { get; init; } = 1;

    /// <summary>Gets the rows per strip; 0 for one strip.</summary>
    public int RowsPerStrip { get; init; }

    /// <summary>Gets the tile size; 0 for strips.</summary>
    public int Tile { get; init; }

    /// <summary>Gets the T4Options (bit 0: two-dimensional, bit 2: fill bits before EOL).</summary>
    public int T4Options { get; init; }

    /// <summary>Gets the ExtraSamples values, or <see langword="null"/>.</summary>
    public int[]? ExtraSamples { get; init; }

    /// <summary>Gets the ColorMap, 3 x 2^Bits values, or <see langword="null"/>.</summary>
    public ushort[]? ColorMap { get; init; }

    /// <summary>Gets the ICC profile, or <see langword="null"/>.</summary>
    public byte[]? IccProfile { get; init; }

    /// <summary>Gets the X and Y resolution in pixels per inch, or <see langword="null"/>.</summary>
    public (int X, int Y)? Resolution { get; init; }

    /// <summary>Gets the Orientation, or 0 for none.</summary>
    public int Orientation { get; init; }

    /// <summary>Gets the NewSubfileType, or 0.</summary>
    public int SubfileType { get; init; }

    /// <summary>Gets a value indicating whether StripByteCounts or TileByteCounts are written as 0 (a repair the reader makes).</summary>
    public bool ZeroByteCounts { get; init; }

    /// <summary>Gets a value indicating whether the byte counts are left out.</summary>
    public bool NoByteCounts { get; init; }
}

/// <summary>
/// Builds TIFF, GIF and BMP files for the image import tests, benchmarks and fuzz seeds of issue #642 (TIFF 6.0; GIF89a; the
/// Windows GDI bitmap formats), and holds the pixel patterns that <c>make_import_images.py</c> in
/// <c>tests/Broadside.Tests/Images/Import</c> writes into its files.
/// </summary>
public static class ImportSamples
{
    /// <summary>Whether the bilevel pattern's pixel is black.</summary>
    public static bool Black(int x, int y) => ((x / 5) + (y / 3)) % 3 == 0 || ((x * x) + (y * y)) % 97 < 20;

    /// <summary>The 8-bit gray pattern.</summary>
    public static int Gray8(int x, int y) => ((x * 4) + (y * 3)) & 255;

    /// <summary>The 8-bit RGB pattern.</summary>
    public static (int R, int G, int B) Rgb8(int x, int y) => (((x * 3) + y) & 255, (y * 5) & 255, (x * y) & 255);

    /// <summary>The smooth 8-bit RGB pattern of the JPEG files.</summary>
    public static (int R, int G, int B) Smooth(int x, int y) => (x * 3, y * 5, (x + y) * 2);

    /// <summary>The 8-bit alpha pattern.</summary>
    public static int Alpha8(int x, int y) => ((x * 7) + (y * 11)) & 255;

    /// <summary>The 16-bit RGB pattern.</summary>
    public static (int R, int G, int B) Rgb16(int x, int y) => (((x * 1000) + (y * 7)) & 0xFFFF, ((y * 2000) + x) & 0xFFFF, (x * y * 37) & 0xFFFF);

    /// <summary>The 8-bit CMYK pattern.</summary>
    public static (int C, int M, int Y, int K) Cmyk8(int x, int y) => ((x * 8) & 255, (y * 15) & 255, ((x + y) * 4) & 255, (x * y) & 255);

    /// <summary>The palette index pattern.</summary>
    public static int Index(int x, int y, int entries) => (x + (y * 3)) % entries;

    /// <summary>The color of palette entry <paramref name="i"/>.</summary>
    public static (int R, int G, int B) PaletteRgb(int i) => (i, 255 - i, (i * 7) & 255);

    /// <summary>Packs samples, chunky, rows byte-aligned, MSB first, 16-bit big-endian.</summary>
    public static byte[] Pack(int width, int height, int bits, int samples, Func<int, int, int, int> value)
    {
        int stride = (int)((((long)width * samples * bits) + 7) / 8);
        byte[] data = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int c = 0; c < samples; c++)
                {
                    int v = value(x, y, c);
                    int index = (x * samples) + c;
                    if (bits == 16)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan((y * stride) + (2 * index)), (ushort)v);
                    }
                    else if (bits == 8)
                    {
                        data[(y * stride) + index] = (byte)v;
                    }
                    else
                    {
                        int bit = index * bits;
                        data[(y * stride) + (bit >> 3)] |= (byte)(v << (8 - bits - (bit & 7)));
                    }
                }
            }
        }

        return data;
    }

    /// <summary>A smooth RGB image for benchmarks.</summary>
    public static byte[] RgbGradient(int width, int height) => Pack(width, height, 8, 3, (x, y, c) => c switch
    {
        0 => (x * 255 / Math.Max(width - 1, 1)) & 255,
        1 => (y * 255 / Math.Max(height - 1, 1)) & 255,
        _ => ((x + y) / 4) & 255,
    });

    /// <summary>The bilevel pattern packed as TIFF WhiteIsZero data (1 = black).</summary>
    public static byte[] Bilevel(int width, int height) => Pack(width, height, 1, 1, (x, y, _) => Black(x, y) ? 1 : 0);

    /// <summary>Writes a little-endian (or big-endian) TIFF file of the pages.</summary>
    public static byte[] Tiff(bool bigEndian, params TiffPage[] pages)
    {
        using var file = new TiffWriter(bigEndian);
        file.Header();
        int previousNext = 4;
        foreach (TiffPage page in pages)
        {
            int ifd = file.Page(page);
            file.Patch(previousNext, ifd);
            previousNext = ifd + 2 + (12 * file.LastEntryCount);
        }

        return file.ToArray();
    }

    /// <summary>Writes a little-endian TIFF file of the pages.</summary>
    public static byte[] Tiff(params TiffPage[] pages) => Tiff(false, pages);

    /// <summary>TIFF LZW (TIFF 6.0 §13): MSB-first codes, the width growing one code early, a clear code first and when the table fills.</summary>
    public static byte[] LzwTiff(ReadOnlySpan<byte> data) => Lzw(data, 8, msbFirst: true, earlyChange: true);

    /// <summary>GIF LZW (GIF89a Appendix F): LSB-first codes, a clear code first and when the table fills.</summary>
    public static byte[] LzwGif(ReadOnlySpan<byte> data, int minimumCodeSize) => Lzw(data, minimumCodeSize, msbFirst: false, earlyChange: false);

    /// <summary>PackBits (TIFF 6.0 §9): literal runs and repeats of up to 128 bytes.</summary>
    public static byte[] PackBits(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>();
        int i = 0;
        while (i < data.Length)
        {
            int run = 1;
            while (i + run < data.Length && run < 128 && data[i + run] == data[i])
            {
                run++;
            }

            if (run >= 2)
            {
                output.Add((byte)(1 - run));
                output.Add(data[i]);
                i += run;
                continue;
            }

            int start = i;
            while (i < data.Length && i - start < 128 && (i + 1 >= data.Length || data[i + 1] != data[i]))
            {
                i++;
            }

            if (i == start)
            {
                i++;
            }

            output.Add((byte)(i - start - 1));
            for (int k = start; k < i; k++)
            {
                output.Add(data[k]);
            }
        }

        return [.. output];
    }

    /// <summary>A GIF89a file of 8-bit indices with a global or local table of <paramref name="palette"/> (3 bytes per entry, a power of two of entries).</summary>
    public static byte[] Gif(int width, int height, byte[] indices, byte[] palette, bool interlaced = false, int transparent = -1, bool local = false)
    {
        int entries = palette.Length / 3;
        int bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(entries)));
        int minimum = Math.Max(2, bits);
        byte[] ordered = indices;
        if (interlaced)
        {
            ordered = new byte[indices.Length];
            int stored = 0;
            foreach ((int start, int step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
            {
                for (int y = start; y < height; y += step)
                {
                    indices.AsSpan(y * width, width).CopyTo(ordered.AsSpan(stored++ * width));
                }
            }
        }

        byte[] data = LzwGif(ordered, minimum);
        var output = new MemoryStream();
        output.Write("GIF89a"u8);
        WriteU16(output, width);
        WriteU16(output, height);
        output.WriteByte((byte)((local ? 0 : 0x80) | ((bits - 1) << 4) | (bits - 1)));
        output.WriteByte(0);
        output.WriteByte(0);
        if (!local)
        {
            output.Write(palette);
        }

        if (transparent >= 0)
        {
            output.Write([0x21, 0xF9, 4, 1, 0, 0, (byte)transparent, 0]);
        }

        output.WriteByte(0x2C);
        WriteU16(output, 0);
        WriteU16(output, 0);
        WriteU16(output, width);
        WriteU16(output, height);
        output.WriteByte((byte)((local ? 0x80 | (bits - 1) : 0) | (interlaced ? 0x40 : 0)));
        if (local)
        {
            output.Write(palette);
        }

        output.WriteByte((byte)minimum);
        for (int i = 0; i < data.Length; i += 255)
        {
            int size = Math.Min(255, data.Length - i);
            output.WriteByte((byte)size);
            output.Write(data, i, size);
        }

        output.WriteByte(0);
        output.WriteByte(0x3B);
        return output.ToArray();
    }

    /// <summary>A BITMAPINFOHEADER bitmap of 24-bit pixels, bottom-up, from RGB samples.</summary>
    public static byte[] Bmp24(int width, int height, byte[] rgb)
    {
        int stride = ((width * 24) + 31) / 32 * 4;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int row = (height - 1 - y) * stride;
            for (int x = 0; x < width; x++)
            {
                int source = ((y * width) + x) * 3;
                pixels[row + (3 * x)] = rgb[source + 2];
                pixels[row + (3 * x) + 1] = rgb[source + 1];
                pixels[row + (3 * x) + 2] = rgb[source];
            }
        }

        return Bmp(width, height, 24, 0, [], pixels);
    }

    /// <summary>A BITMAPINFOHEADER bitmap with the given bit depth, compression, color table (BGRX entries) and pixel data.</summary>
    public static byte[] Bmp(int width, int height, int bits, int compression, byte[] table, byte[] pixels)
    {
        int offset = 14 + 40 + table.Length;
        byte[] file = new byte[offset + pixels.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(2), file.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), offset);
        Span<byte> info = file.AsSpan(14);
        BinaryPrimitives.WriteInt32LittleEndian(info, 40);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], height);
        BinaryPrimitives.WriteInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(info[14..], (short)bits);
        BinaryPrimitives.WriteInt32LittleEndian(info[16..], compression);
        BinaryPrimitives.WriteInt32LittleEndian(info[20..], pixels.Length);
        BinaryPrimitives.WriteInt32LittleEndian(info[32..], table.Length / 4);
        table.CopyTo(file, 54);
        pixels.CopyTo(file, offset);
        return file;
    }

    private static void WriteU16(Stream output, int value)
    {
        output.WriteByte((byte)value);
        output.WriteByte((byte)(value >> 8));
    }

    private static byte[] Lzw(ReadOnlySpan<byte> data, int minimum, bool msbFirst, bool earlyChange)
    {
        int clear = 1 << minimum;
        var output = new List<byte>();
        uint buffer = 0;
        int bits = 0;
        int width = minimum + 1;
        var table = new Dictionary<(int Prefix, byte Suffix), int>();
        int next = clear + 2;

        void Emit(int code)
        {
            if (msbFirst)
            {
                buffer = (buffer << width) | (uint)code;
                bits += width;
                while (bits >= 8)
                {
                    output.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }

                buffer &= (1u << bits) - 1;
            }
            else
            {
                buffer |= (uint)code << bits;
                bits += width;
                while (bits >= 8)
                {
                    output.Add((byte)buffer);
                    buffer >>= 8;
                    bits -= 8;
                }
            }
        }

        Emit(clear);
        int current = -1;
        foreach (byte value in data)
        {
            if (current < 0)
            {
                current = value;
                continue;
            }

            if (table.TryGetValue((current, value), out int code))
            {
                current = code;
                continue;
            }

            Emit(current);
            table[(current, value)] = next++;
            int limit = earlyChange ? (1 << width) - 1 : 1 << width;
            if (next > limit && width < 12)
            {
                width++;
            }

            if (next >= (earlyChange ? 4094 : 4096))
            {
                Emit(clear);
                table.Clear();
                next = clear + 2;
                width = minimum + 1;
            }

            current = value;
        }

        if (current >= 0)
        {
            Emit(current);
            next++;
            int limit = earlyChange ? (1 << width) - 1 : 1 << width;
            if (next > limit && width < 12)
            {
                width++;
            }
        }

        Emit(clear + 1);
        if (bits > 0)
        {
            output.Add(msbFirst ? (byte)(buffer << (8 - bits)) : (byte)buffer);
        }

        return [.. output];
    }

    /// <summary>Lays out a TIFF file: header, then per page the strip data, values and directory.</summary>
    private sealed class TiffWriter(bool bigEndian) : IDisposable
    {
        private readonly MemoryStream _output = new();

        public int LastEntryCount { get; private set; }

        public void Header()
        {
            _output.Write(bigEndian ? "MM\0*"u8 : "II*\0"u8);
            U32(0);
        }

        public void Patch(int at, int value)
        {
            long position = _output.Position;
            _output.Position = at;
            U32(value);
            _output.Position = position;
        }

        public byte[] ToArray() => _output.ToArray();

        public void Dispose() => _output.Dispose();

        public int Page(TiffPage page)
        {
            bool tiled = page.Tile > 0;
            int chunkWidth = tiled ? page.Tile : page.Width;
            int chunkHeight = tiled ? page.Tile : page.RowsPerStrip > 0 ? Math.Min(page.RowsPerStrip, page.Height) : page.Height;
            int across = (page.Width + chunkWidth - 1) / chunkWidth;
            int down = (page.Height + chunkHeight - 1) / chunkHeight;
            int planes = page.Planar == 2 ? page.Samples : 1;
            int perPixel = page.Planar == 2 ? 1 : page.Samples;
            var offsets = new List<int>();
            var counts = new List<int>();
            for (int p = 0; p < planes; p++)
            {
                for (int cy = 0; cy < down; cy++)
                {
                    for (int cx = 0; cx < across; cx++)
                    {
                        int rows = tiled ? chunkHeight : Math.Min(chunkHeight, page.Height - (cy * chunkHeight));
                        byte[] raw = Chunk(page, p, cx * chunkWidth, cy * chunkHeight, chunkWidth, rows, perPixel);
                        byte[] encoded = Encode(page, raw, chunkWidth, rows, perPixel);
                        Align();
                        offsets.Add((int)_output.Position);
                        counts.Add(encoded.Length);
                        _output.Write(encoded);
                    }
                }
            }

            var entries = new SortedDictionary<int, (int Type, int[] Values, byte[]? Raw)>
            {
                [256] = (4, [page.Width], null),
                [257] = (4, [page.Height], null),
                [258] = (3, Enumerable.Repeat(page.Bits, page.Samples).ToArray(), null),
                [259] = (3, [page.Compression], null),
                [262] = (3, [page.Photometric], null),
                [277] = (3, [page.Samples], null),
                [284] = (3, [page.Planar], null),
            };
            if (page.SubfileType != 0)
            {
                entries[254] = (4, [page.SubfileType], null);
            }

            if (tiled)
            {
                entries[322] = (3, [page.Tile], null);
                entries[323] = (3, [page.Tile], null);
                entries[324] = (4, [.. offsets], null);
                if (!page.NoByteCounts)
                {
                    entries[325] = (4, page.ZeroByteCounts ? new int[counts.Count] : [.. counts], null);
                }
            }
            else
            {
                entries[273] = (4, [.. offsets], null);
                entries[278] = (4, [chunkHeight], null);
                if (!page.NoByteCounts)
                {
                    entries[279] = (4, page.ZeroByteCounts ? new int[counts.Count] : [.. counts], null);
                }
            }

            if (page.Predictor != 1)
            {
                entries[317] = (3, [page.Predictor], null);
            }

            if (page.T4Options != 0)
            {
                entries[292] = (4, [page.T4Options], null);
            }

            if (page.ExtraSamples is { } extra)
            {
                entries[338] = (3, extra, null);
            }

            if (page.ColorMap is { } map)
            {
                entries[320] = (3, [.. map.Select(v => (int)v)], null);
            }

            if (page.Orientation != 0)
            {
                entries[274] = (3, [page.Orientation], null);
            }

            if (page.Resolution is { } resolution)
            {
                entries[282] = (5, [resolution.X, 1], null);
                entries[283] = (5, [resolution.Y, 1], null);
                entries[296] = (3, [2], null);
            }

            if (page.IccProfile is { } profile)
            {
                entries[34675] = (7, [], profile);
            }

            // Values that do not fit in an entry go before the directory.
            var positions = new Dictionary<int, int>();
            foreach ((int tag, (int type, int[] values, byte[]? raw)) in entries)
            {
                byte[] bytes = Values(type, values, raw);
                if (bytes.Length > 4)
                {
                    Align();
                    positions[tag] = (int)_output.Position;
                    _output.Write(bytes);
                }
            }

            Align();
            int ifd = (int)_output.Position;
            U16(entries.Count);
            foreach ((int tag, (int type, int[] values, byte[]? raw)) in entries)
            {
                byte[] bytes = Values(type, values, raw);
                U16(tag);
                U16(type);
                U32(raw?.Length ?? (type == 5 ? values.Length / 2 : values.Length));
                if (bytes.Length > 4)
                {
                    U32(positions[tag]);
                }
                else
                {
                    _output.Write(bytes);
                    _output.Write(new byte[4 - bytes.Length]);
                }
            }

            U32(0);
            LastEntryCount = entries.Count;
            return ifd;
        }

        private static byte[] Chunk(TiffPage page, int plane, int left, int top, int width, int rows, int perPixel)
        {
            int stride = (int)((((long)width * perPixel * page.Bits) + 7) / 8);
            int sourceStride = (int)((((long)page.Width * page.Samples * page.Bits) + 7) / 8);
            byte[] chunk = new byte[stride * rows];
            for (int y = 0; y < rows && top + y < page.Height; y++)
            {
                for (int x = 0; x < width && left + x < page.Width; x++)
                {
                    for (int c = 0; c < perPixel; c++)
                    {
                        int sample = page.Planar == 2 ? plane : c;
                        int value = Get(page.Data.AsSpan((top + y) * sourceStride, sourceStride), ((left + x) * page.Samples) + sample, page.Bits);
                        Set(chunk.AsSpan(y * stride, stride), (x * perPixel) + c, page.Bits, value);
                    }
                }
            }

            return chunk;
        }

        private byte[] Encode(TiffPage page, byte[] raw, int width, int rows, int perPixel)
        {
            int stride = rows == 0 ? 0 : raw.Length / rows;
            if (page.Bits == 16 && !bigEndian)
            {
                for (int i = 0; i + 1 < raw.Length; i += 2)
                {
                    (raw[i], raw[i + 1]) = (raw[i + 1], raw[i]);
                }
            }

            if (page.Predictor == 2)
            {
                for (int y = 0; y < rows; y++)
                {
                    Span<byte> row = raw.AsSpan(y * stride, stride);
                    if (page.Bits == 8)
                    {
                        for (int i = row.Length - 1; i >= perPixel; i--)
                        {
                            row[i] -= row[i - perPixel];
                        }
                    }
                    else
                    {
                        int step = 2 * perPixel;
                        for (int i = row.Length - 2; i >= step; i -= 2)
                        {
                            ushort value = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(row[i..]) : BinaryPrimitives.ReadUInt16LittleEndian(row[i..]);
                            ushort left = bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(row[(i - step)..]) : BinaryPrimitives.ReadUInt16LittleEndian(row[(i - step)..]);
                            if (bigEndian)
                            {
                                BinaryPrimitives.WriteUInt16BigEndian(row[i..], (ushort)(value - left));
                            }
                            else
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(row[i..], (ushort)(value - left));
                            }
                        }
                    }
                }
            }

            switch (page.Compression)
            {
                case 5:
                    return LzwTiff(raw);
                case 8:
                    using (var compressed = new MemoryStream())
                    {
                        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                        {
                            zlib.Write(raw);
                        }

                        return compressed.ToArray();
                    }

                case 32773:
                    return PackBits(raw);
                case 2 or 3 or 4:
                    bool[][] bitmap = new bool[rows][];
                    for (int y = 0; y < rows; y++)
                    {
                        bitmap[y] = new bool[width];
                        for (int x = 0; x < width; x++)
                        {
                            bitmap[y][x] = Get(raw.AsSpan(y * stride, stride), x, 1) == 1; // libtiff codes 1 bits as black runs, whatever the photometric
                        }
                    }

                    CcittEncoding encoding = page.Compression switch
                    {
                        2 => new CcittEncoding(0, EndOfLine: false, EncodedByteAlign: true, EndOfBlock: false),
                        3 => new CcittEncoding((page.T4Options & 1) != 0 ? 4 : 0, EndOfLine: true, EncodedByteAlign: (page.T4Options & 4) != 0, EndOfBlock: true),
                        _ => new CcittEncoding(-1, EndOfBlock: true),
                    };
                    return CcittEncoder.Encode(bitmap, encoding);
                default:
                    return raw;
            }
        }

        private byte[] Values(int type, int[] values, byte[]? raw)
        {
            if (raw is not null)
            {
                return raw;
            }

            int size = type switch
            {
                3 => 2,
                5 => 4,
                _ => 4,
            };
            byte[] bytes = new byte[values.Length * size];
            for (int i = 0; i < values.Length; i++)
            {
                if (size == 2)
                {
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2 * i), (ushort)values[i]);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 * i), (ushort)values[i]);
                    }
                }
                else if (bigEndian)
                {
                    BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4 * i), values[i]);
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 * i), values[i]);
                }
            }

            return bytes;
        }

        private static int Get(ReadOnlySpan<byte> row, int index, int bits) => bits switch
        {
            8 => row[index],
            16 => (row[2 * index] << 8) | row[(2 * index) + 1],
            _ => (row[(index * bits) >> 3] >> (8 - bits - ((index * bits) & 7))) & ((1 << bits) - 1),
        };

        private static void Set(Span<byte> row, int index, int bits, int value)
        {
            switch (bits)
            {
                case 8:
                    row[index] = (byte)value;
                    break;
                case 16:
                    row[2 * index] = (byte)(value >> 8);
                    row[(2 * index) + 1] = (byte)value;
                    break;
                default:
                    int bit = index * bits;
                    row[bit >> 3] |= (byte)(value << (8 - bits - (bit & 7)));
                    break;
            }
        }

        private void Align()
        {
            if (_output.Position % 2 != 0)
            {
                _output.WriteByte(0);
            }
        }

        private void U16(int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
            }

            _output.Write(bytes);
        }

        private void U32(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (bigEndian)
            {
                BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            }

            _output.Write(bytes);
        }
    }
}
