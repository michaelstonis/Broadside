using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;

namespace Broadside.Images.Import;

/// <summary>
/// Reads the first image of a GIF file (GIF87a and GIF89a, CompuServe 1990) into an <see cref="ImportedImage"/> of palette indices:
/// the global or local color table, the Graphic Control Extension's transparent index, LZW decoding (Appendix F) and interlaced
/// rows put back in order (Appendix E).
/// </summary>
/// <remarks>
/// The image is the first image descriptor's rectangle, at its own size. Malformed data throws <see cref="InvalidDataException"/>.
/// Image data that ends early, the usual damage in GIF files, keeps the rows decoded and is listed in
/// <see cref="ImportedImage.Repairs"/>; so is an index past the end of the color table, which reads as black. Indices are written
/// at the fewest bits the color table needs (1, 2, 4 or 8), and decoding allocates nothing per row.
/// <para>ISO 32000-2 §8.9.5 (the image XObject it becomes), §8.6.6.3 (Indexed) and §8.9.6.4 (the color key mask).</para>
/// </remarks>
internal static class GifReader
{
    /// <summary>Returns whether <paramref name="data"/> starts with a GIF signature and version.</summary>
    public static bool IsGif(ReadOnlySpan<byte> data) => data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8);

    /// <summary>Reads the first image.</summary>
    /// <exception cref="InvalidDataException">The file is malformed, has no image, or is larger than <paramref name="maxPixels"/>.</exception>
    public static ImportedImage Read(ReadOnlySpan<byte> gif, long maxPixels)
    {
        if (gif.Length < 13 || !IsGif(gif))
        {
            throw new InvalidDataException("The data does not start with a GIF87a or GIF89a header (GIF89a §17).");
        }

        int packed = gif[10];
        int position = 13;
        ReadOnlySpan<byte> global = [];
        if ((packed & 0x80) != 0)
        {
            int length = 3 << ((packed & 7) + 1);
            if (position + length > gif.Length)
            {
                throw new InvalidDataException("The global color table runs past the end of the file (GIF89a §19).");
            }

            global = gif.Slice(position, length);
            position += length;
        }

        int transparent = -1;
        while (position < gif.Length)
        {
            byte block = gif[position++];
            if (block == 0x3B)
            {
                break;
            }

            if (block == 0x21)
            {
                if (position >= gif.Length)
                {
                    break;
                }

                byte label = gif[position++];
                if (label == 0xF9 && position + 5 < gif.Length && gif[position] >= 4)
                {
                    transparent = (gif[position + 1] & 1) != 0 ? gif[position + 4] : -1;
                }

                position = SkipSubBlocks(gif, position);
                continue;
            }

            if (block == 0x2C)
            {
                return ReadImage(gif, position, global, transparent, maxPixels);
            }

            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"An unknown block 0x{block:X2} at offset {position - 1} (GIF89a §15)."));
        }

        throw new InvalidDataException("The GIF file has no image (GIF89a §20).");
    }

    private static int SkipSubBlocks(ReadOnlySpan<byte> gif, int position)
    {
        while (position < gif.Length)
        {
            int size = gif[position++];
            if (size == 0)
            {
                break;
            }

            position += size;
        }

        return Math.Min(position, gif.Length);
    }

    private static ImportedImage ReadImage(ReadOnlySpan<byte> gif, int position, ReadOnlySpan<byte> global, int transparent, long maxPixels)
    {
        if (position + 9 > gif.Length)
        {
            throw new InvalidDataException("The image descriptor runs past the end of the file (GIF89a §20).");
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(gif[(position + 4)..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(gif[(position + 6)..]);
        int packed = gif[position + 8];
        position += 9;
        if (width < 1 || height < 1)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The image is {width} x {height} pixels; both shall be at least 1."));
        }

        if ((long)width * height > Math.Min(maxPixels, Array.MaxLength))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The {width} x {height} image has more pixels than the {maxPixels} allowed (PdfOptions.MaxImagePixels)."));
        }

        ReadOnlySpan<byte> table = global;
        if ((packed & 0x80) != 0)
        {
            int length = 3 << ((packed & 7) + 1);
            if (position + length > gif.Length)
            {
                throw new InvalidDataException("The local color table runs past the end of the file (GIF89a §21).");
            }

            table = gif.Slice(position, length);
            position += length;
        }

        if (table.IsEmpty)
        {
            throw new InvalidDataException("The image has neither a local nor a global color table (GIF89a §19, §21).");
        }

        bool interlaced = (packed & 0x40) != 0;
        if (position >= gif.Length)
        {
            throw new InvalidDataException("The image data is missing (GIF89a §22).");
        }

        int codeSize = gif[position++];
        if (codeSize is < 2 or > 8)
        {
            if (codeSize != 1)
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The LZW minimum code size is {codeSize}; it shall be 2 to 8 (GIF89a §22, Appendix F)."));
            }

            codeSize = 2;
        }

        int dataLength = 0;
        for (int at = position; at < gif.Length;)
        {
            int size = gif[at++];
            if (size == 0)
            {
                break;
            }

            size = Math.Min(size, gif.Length - at);
            dataLength += size;
            at += size;
        }

        int entries = table.Length / 3;
        int bits = entries <= 2 ? 1 : entries <= 4 ? 2 : entries <= 16 ? 4 : 8;
        byte[] data = ArrayPool<byte>.Shared.Rent(Math.Max(dataLength, 1));
        byte[] indices = ArrayPool<byte>.Shared.Rent(width * height);
        ImportedImage? image = null;
        try
        {
            int written = 0;
            for (int at = position; at < gif.Length;)
            {
                int size = gif[at++];
                if (size == 0)
                {
                    break;
                }

                size = Math.Min(size, gif.Length - at);
                gif.Slice(at, size).CopyTo(data.AsSpan(written));
                written += size;
                at += size;
            }

            Span<byte> pixels = indices.AsSpan(0, width * height);
            pixels.Clear();
            using var lzw = new ImportLzwDecoder();
            int decoded = lzw.Decode(data.AsSpan(0, dataLength), pixels, codeSize, msbFirst: false, earlyChange: false, out bool invalid);
            image = new ImportedImage(width, height, bits, ImportedColor.Palette);
            if (decoded < pixels.Length)
            {
                image.Repairs.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The image data {(invalid ? "has an invalid LZW code" : "ends")} after {decoded} of {pixels.Length} pixels; the rest are index 0."));
            }

            int maxIndex = 0;
            foreach (byte index in pixels)
            {
                maxIndex = Math.Max(maxIndex, index);
            }

            byte[] palette = table.ToArray();
            if (maxIndex >= entries)
            {
                image.Repairs.Add(string.Create(CultureInfo.InvariantCulture, $"Index {maxIndex} is past the {entries}-entry color table; such indices read as black."));
                Array.Resize(ref palette, 3 * (maxIndex + 1));
                bits = 8;
                image.Dispose();
                image = new ImportedImage(width, height, 8, ImportedColor.Palette);
            }

            image.Palette = palette;
            image.TransparentIndex = transparent >= 0 && transparent < palette.Length / 3 ? transparent : -1;
            Span<byte> plane = image.RentColor();
            int stride = image.ColorStride;
            for (int y = 0; y < height; y++)
            {
                int row = interlaced ? InterlacedRow(y, height) : y;
                Pack(pixels.Slice(y * width, width), plane.Slice(row * stride, stride), bits);
            }

            ImportedImage result = image;
            image = null;
            return result;
        }
        finally
        {
            image?.Dispose();
            ArrayPool<byte>.Shared.Return(data);
            ArrayPool<byte>.Shared.Return(indices);
        }
    }

    /// <summary>Returns the image row that the <paramref name="index"/>-th stored row of an interlaced image is (GIF89a Appendix E).</summary>
    internal static int InterlacedRow(int index, int height)
    {
        int pass1 = (height + 7) / 8;
        if (index < pass1)
        {
            return index * 8;
        }

        index -= pass1;
        int pass2 = (height + 3) / 8;
        if (index < pass2)
        {
            return 4 + (index * 8);
        }

        index -= pass2;
        int pass3 = (height + 1) / 4;
        if (index < pass3)
        {
            return 2 + (index * 4);
        }

        index -= pass3;
        return 1 + (index * 2);
    }

    /// <summary>Packs 8-bit indices into a row of <paramref name="bits"/>-bit samples, MSB first.</summary>
    internal static void Pack(ReadOnlySpan<byte> indices, Span<byte> row, int bits)
    {
        if (bits == 8)
        {
            indices.CopyTo(row);
            return;
        }

        row.Clear();
        int perByte = 8 / bits;
        for (int x = 0; x < indices.Length; x++)
        {
            int shift = 8 - bits - ((x % perByte) * bits);
            row[x / perByte] |= (byte)(indices[x] << shift);
        }
    }
}
