using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Broadside.Images.Import;

/// <summary>
/// Reads a Windows bitmap file (BMP: BITMAPFILEHEADER with a BITMAPCOREHEADER, BITMAPINFOHEADER, BITMAPV2/V3/V4/V5HEADER or OS/2
/// 2.x header) into an <see cref="ImportedImage"/>: 1, 4 and 8 bits through the color table, 16 and 32 bits through the bit
/// fields, 24 bits, BI_RLE8 and BI_RLE4, bottom-up and top-down rows, an alpha mask, the resolution and an embedded ICC profile.
/// </summary>
/// <remarks>
/// Sources: the Windows GDI reference (BITMAPINFOHEADER, BITMAPV4HEADER, BITMAPV5HEADER, "Bitmap Compression"). 16-bit pixels
/// without bit fields are 5-5-5; 32-bit pixels without bit fields are BGR with an unused byte, as Windows reads them. An alpha
/// mask (BI_BITFIELDS with a V3 or later header, or BI_ALPHABITFIELDS) gives an alpha plane unless every pixel is opaque. Pixels
/// an RLE delta or early end skips are index 0. Malformed data throws <see cref="InvalidDataException"/>, a JPEG or PNG
/// payload (BI_JPEG, BI_PNG) or a Huffman or RLE24 OS/2 bitmap <see cref="NotSupportedException"/>. Decoding allocates nothing per row.
/// <para>ISO 32000-2 §8.9.5 (the image XObject it becomes), §8.6.6.3 (Indexed) and §11.6.5.2 (the soft mask).</para>
/// </remarks>
internal static class BmpReader
{
    /// <summary>Returns whether <paramref name="data"/> starts with a BMP file header.</summary>
    public static bool IsBmp(ReadOnlySpan<byte> data) =>
        data.Length >= 18 && data[0] == 'B' && data[1] == 'M' && BinaryPrimitives.ReadUInt32LittleEndian(data[14..]) is 12 or 16 or 40 or 52 or 56 or 64 or 108 or 124;

    /// <summary>Reads the bitmap.</summary>
    /// <exception cref="InvalidDataException">The file is malformed or larger than <paramref name="maxPixels"/>.</exception>
    /// <exception cref="NotSupportedException">The file holds JPEG, PNG, Huffman or RLE24 data.</exception>
    public static ImportedImage Read(ReadOnlySpan<byte> bmp, long maxPixels)
    {
        if (!IsBmp(bmp))
        {
            throw new InvalidDataException("The data does not start with a BMP file header: BM and a known header size.");
        }

        long pixelOffset = BinaryPrimitives.ReadUInt32LittleEndian(bmp[10..]);
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bmp[14..]);
        if (14 + headerSize > bmp.Length)
        {
            throw new InvalidDataException("The bitmap header runs past the end of the file.");
        }

        ReadOnlySpan<byte> header = bmp.Slice(14, headerSize);
        bool core = headerSize == 12;
        long width = core ? BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) : BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        long height = core ? BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) : BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        int bits = core ? BinaryPrimitives.ReadUInt16LittleEndian(header[10..]) : BinaryPrimitives.ReadUInt16LittleEndian(header[14..]);
        int compression = core || headerSize < 20 ? 0 : (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        bool topDown = height < 0;
        height = Math.Abs(height);
        if (width < 1 || height < 1)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The bitmap is {width} x {height} pixels; both shall be at least 1."));
        }

        if (width * height > Math.Min(maxPixels, Array.MaxLength))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The {width} x {height} bitmap has more pixels than the {maxPixels} allowed (PdfOptions.MaxImagePixels)."));
        }

        if (headerSize == 64 && compression is 3 or 4)
        {
            throw new NotSupportedException("OS/2 Huffman 1D and RLE24 bitmaps are not supported.");
        }

        if (compression is 4 or 5)
        {
            throw new NotSupportedException("Bitmaps that hold JPEG or PNG data (BI_JPEG, BI_PNG) are not supported; import the JPEG or PNG itself.");
        }

        if (compression is not (0 or 1 or 2 or 3 or 6))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The bitmap compression {compression} is not BI_RGB, BI_RLE8, BI_RLE4 or BI_BITFIELDS."));
        }

        if (bits is not (1 or 4 or 8 or 16 or 24 or 32)
            || (compression == 1 && bits != 8) || (compression == 2 && bits != 4) || (compression is 3 or 6 && bits is not (16 or 32)))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"A {bits}-bit bitmap with compression {compression} is not a valid combination."));
        }

        var layout = new Layout((int)width, (int)height, bits, topDown);
        ImportedImage image = bits <= 8
            ? ReadIndexed(bmp, header, headerSize, layout, compression, pixelOffset)
            : ReadTrueColor(bmp, header, headerSize, layout, compression, pixelOffset);
        try
        {
            (image.DpiX, image.DpiY, _) = ReadPlacement(bmp);

            if (headerSize >= 124 && BinaryPrimitives.ReadUInt32LittleEndian(header[56..]) == 0x4D424544)
            {
                // PROFILE_EMBEDDED ('MBED'): bV5ProfileData is an offset from the start of the header.
                long offset = 14L + BinaryPrimitives.ReadUInt32LittleEndian(header[112..]);
                long size = BinaryPrimitives.ReadUInt32LittleEndian(header[116..]);
                if (size >= 128 && offset + size <= bmp.Length)
                {
                    image.IccProfile = bmp.Slice((int)offset, (int)size).ToArray();
                }
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>The bitmap's resolution in pixels per inch (from pixels per metre; 0 when it states none). Never throws.</summary>
    public static ImportedPlacement ReadPlacement(ReadOnlySpan<byte> bmp)
    {
        if (!IsBmp(bmp))
        {
            return new ImportedPlacement(0, 0, 1);
        }

        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(bmp[14..]);
        if (headerSize < 32 || 14 + 32 > bmp.Length)
        {
            return new ImportedPlacement(0, 0, 1);
        }

        double x = BinaryPrimitives.ReadInt32LittleEndian(bmp[(14 + 24)..]) * 0.0254;
        double y = BinaryPrimitives.ReadInt32LittleEndian(bmp[(14 + 28)..]) * 0.0254;
        return x > 0 && y > 0 ? new ImportedPlacement(x, y, 1) : new ImportedPlacement(0, 0, 1);
    }

    private static ImportedImage ReadIndexed(ReadOnlySpan<byte> bmp, ReadOnlySpan<byte> header, int headerSize, Layout layout, int compression, long pixelOffset)
    {
        bool core = headerSize == 12;
        int entrySize = core ? 3 : 4;
        long used = core || headerSize < 36 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        int entries = used is > 0 and <= 256 ? (int)used : 1 << layout.Bits;
        int tableStart = 14 + headerSize + (compression is 3 && headerSize == 40 ? 12 : 0);
        int available = pixelOffset > tableStart ? (int)(Math.Min(bmp.Length, pixelOffset) - tableStart) / entrySize : 0;
        entries = Math.Min(entries, available);
        if (entries < 1)
        {
            throw new InvalidDataException("The bitmap has no color table.");
        }

        byte[] palette = new byte[3 * Math.Max(entries, 1 << layout.Bits)];
        for (int i = 0; i < entries; i++)
        {
            ReadOnlySpan<byte> entry = bmp.Slice(tableStart + (i * entrySize), 3);
            palette[3 * i] = entry[2];
            palette[(3 * i) + 1] = entry[1];
            palette[(3 * i) + 2] = entry[0];
        }

        CheckOffset(bmp, pixelOffset);
        ReadOnlySpan<byte> pixels = bmp[(int)pixelOffset..];
        var image = new ImportedImage(layout.Width, layout.Height, compression == 0 ? layout.Bits : layout.Bits == 4 ? 4 : 8, ImportedColor.Palette)
        {
            Palette = palette,
        };
        try
        {
            Span<byte> plane = image.RentColor();
            int stride = image.ColorStride;
            if (compression == 0)
            {
                int fileStride = layout.FileStride;
                int rows = 0;
                for (int y = 0; y < layout.Height; y++)
                {
                    long start = (long)y * fileStride;
                    if (start + stride > pixels.Length)
                    {
                        break;
                    }

                    pixels.Slice((int)start, stride).CopyTo(plane.Slice(layout.Row(y) * stride, stride));
                    rows++;
                }

                if (rows < layout.Height)
                {
                    image.Repairs.Add(string.Create(CultureInfo.InvariantCulture, $"The pixel data ends after {rows} of {layout.Height} rows; the rest are index 0."));
                }
            }
            else
            {
                DecodeRle(pixels, plane, stride, layout, four: compression == 2, image.Repairs);
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Decodes BI_RLE8 or BI_RLE4 data ("Bitmap Compression" in the GDI reference) into rows of 8- or 4-bit indices.</summary>
    private static void DecodeRle(ReadOnlySpan<byte> data, Span<byte> plane, int stride, Layout layout, bool four, List<string> repairs)
    {
        int x = 0;
        int y = 0;
        int position = 0;
        bool ended = false;
        while (position + 1 < data.Length && y < layout.Height)
        {
            int count = data[position];
            int value = data[position + 1];
            position += 2;
            if (count > 0)
            {
                for (int i = 0; i < count && x < layout.Width; i++)
                {
                    int index = four ? (i % 2 == 0 ? value >> 4 : value & 0xF) : value;
                    Put(plane, stride, layout, x++, y, index, four);
                }

                continue;
            }

            switch (value)
            {
                case 0:
                    x = 0;
                    y++;
                    break;
                case 1:
                    ended = true;
                    break;
                case 2:
                    if (position + 1 >= data.Length)
                    {
                        ended = true;
                        break;
                    }

                    x += data[position];
                    y += data[position + 1];
                    position += 2;
                    break;
                default:
                    int bytes = four ? (value + 1) / 2 : value;
                    for (int i = 0; i < value && position + (four ? i / 2 : i) < data.Length; i++)
                    {
                        int index = four ? (i % 2 == 0 ? data[position + (i / 2)] >> 4 : data[position + (i / 2)] & 0xF) : data[position + i];
                        if (x < layout.Width)
                        {
                            Put(plane, stride, layout, x, y, index, four);
                        }

                        x++;
                    }

                    position += bytes + (bytes & 1);
                    break;
            }

            if (ended)
            {
                break;
            }
        }

        if (!ended && y < layout.Height)
        {
            repairs.Add("The RLE data ends without an end-of-bitmap code; the pixels not reached are index 0.");
        }
    }

    private static void Put(Span<byte> plane, int stride, Layout layout, int x, int y, int index, bool four)
    {
        if (x >= layout.Width || y >= layout.Height)
        {
            return;
        }

        int row = layout.Row(y) * stride;
        if (four)
        {
            int at = row + (x / 2);
            plane[at] = (byte)(x % 2 == 0 ? (plane[at] & 0x0F) | (index << 4) : (plane[at] & 0xF0) | index);
        }
        else
        {
            plane[row + x] = (byte)index;
        }
    }

    private static ImportedImage ReadTrueColor(ReadOnlySpan<byte> bmp, ReadOnlySpan<byte> header, int headerSize, Layout layout, int compression, long pixelOffset)
    {
        uint red;
        uint green;
        uint blue;
        uint alpha = 0;
        if (compression is 3 or 6)
        {
            ReadOnlySpan<byte> masks = headerSize >= 52 ? header[40..] : bmp.Length >= 14 + headerSize + 12 ? bmp[(14 + headerSize)..] : [];
            if (masks.Length < 12)
            {
                throw new InvalidDataException("The bit fields run past the end of the file.");
            }

            red = BinaryPrimitives.ReadUInt32LittleEndian(masks);
            green = BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]);
            blue = BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]);
            if ((headerSize >= 56 || compression == 6) && masks.Length >= 16)
            {
                alpha = BinaryPrimitives.ReadUInt32LittleEndian(masks[12..]);
            }
        }
        else if (layout.Bits == 16)
        {
            (red, green, blue) = (0x7C00, 0x03E0, 0x001F);
        }
        else
        {
            (red, green, blue) = (0x00FF0000, 0x0000FF00, 0x000000FF);
        }

        CheckOffset(bmp, pixelOffset);
        ReadOnlySpan<byte> pixels = bmp[(int)pixelOffset..];
        var image = new ImportedImage(layout.Width, layout.Height, 8, ImportedColor.Rgb);
        try
        {
            Span<byte> plane = image.RentColor();
            Span<byte> alphaPlane = alpha != 0 ? image.RentAlpha() : [];
            var r = new Field(red);
            var g = new Field(green);
            var b = new Field(blue);
            var a = new Field(alpha);
            int fileStride = layout.FileStride;
            int bytesPerPixel = layout.Bits / 8;
            int rows = 0;
            for (int y = 0; y < layout.Height; y++)
            {
                long start = (long)y * fileStride;
                if (start + ((long)layout.Width * bytesPerPixel) > pixels.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> source = pixels.Slice((int)start, layout.Width * bytesPerPixel);
                int row = layout.Row(y);
                Span<byte> target = plane.Slice(row * layout.Width * 3, layout.Width * 3);
                Span<byte> opacity = alpha != 0 ? alphaPlane.Slice(row * layout.Width, layout.Width) : [];
                for (int x = 0; x < layout.Width; x++)
                {
                    if (layout.Bits == 24)
                    {
                        target[3 * x] = source[(3 * x) + 2];
                        target[(3 * x) + 1] = source[(3 * x) + 1];
                        target[(3 * x) + 2] = source[3 * x];
                        continue;
                    }

                    uint pixel = layout.Bits == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(source[(2 * x)..]) : BinaryPrimitives.ReadUInt32LittleEndian(source[(4 * x)..]);
                    target[3 * x] = r.Read(pixel);
                    target[(3 * x) + 1] = g.Read(pixel);
                    target[(3 * x) + 2] = b.Read(pixel);
                    if (alpha != 0)
                    {
                        opacity[x] = a.Read(pixel);
                    }
                }

                rows++;
            }

            if (rows < layout.Height)
            {
                image.Repairs.Add(string.Create(CultureInfo.InvariantCulture, $"The pixel data ends after {rows} of {layout.Height} rows; the rest are black."));
            }

            image.DropOpaqueAlpha();
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void CheckOffset(ReadOnlySpan<byte> bmp, long pixelOffset)
    {
        if (pixelOffset < 14 || pixelOffset >= bmp.Length)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The pixel data offset {pixelOffset} is outside the file."));
        }
    }

    /// <summary>The size and row order of a bitmap.</summary>
    private readonly record struct Layout(int Width, int Height, int Bits, bool TopDown)
    {
        /// <summary>Gets the bytes of a row in the file: a multiple of 4.</summary>
        public int FileStride => (int)(((((long)Width * Bits) + 31) / 32) * 4);

        /// <summary>Returns the image row of the <paramref name="y"/>-th row in the file.</summary>
        public int Row(int y) => TopDown ? y : Height - 1 - y;
    }

    /// <summary>A bit field: a contiguous mask of a pixel, scaled to 8 bits.</summary>
    private readonly struct Field
    {
        private readonly uint _mask;
        private readonly int _shift;
        private readonly int _bits;

        public Field(uint mask)
        {
            _mask = mask;
            _shift = mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
            _bits = BitOperations.PopCount(mask);
        }

        public byte Read(uint pixel)
        {
            if (_bits == 0)
            {
                return 0;
            }

            uint value = (pixel & _mask) >> _shift;
            if (_bits >= 8)
            {
                return (byte)(value >> (_bits - 8));
            }

            // Replicate the high bits into the low ones, so that the largest value maps to 255.
            uint max = (1u << _bits) - 1;
            return (byte)((value * 255 + (max / 2)) / max);
        }
    }
}
