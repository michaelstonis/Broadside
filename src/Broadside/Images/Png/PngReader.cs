using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace Broadside.Images.Png;

/// <summary>
/// Reads a PNG file (ISO/IEC 15948, PNG Third Edition) into the parts a PDF image is made of (<see cref="PngImage"/>): every colour
/// type and bit depth, Adam7 interlacing, the five filter types, PLTE, tRNS and iCCP. Never throws for any input: an unreadable
/// file gives <see langword="null"/> and a message.
/// </summary>
/// <remarks>
/// <para>
/// ISO/IEC 15948 §5 (chunk layout, CRC), §11.2 (critical chunks), §11.3.2.1 (tRNS), §11.3.3.3 (iCCP), §9 (filtering) and §8.2
/// (Adam7). Rules a decoder must follow are enforced: the signature, IHDR first and valid, PLTE before IDAT and present for
/// indexed-colour, IDAT chunks consecutive, the CRC of every critical chunk, no unknown critical chunk, a valid filter type on every
/// row, and enough image data for every row. An ancillary chunk that is damaged (bad CRC, wrong length, out of place) is
/// ignored, as libpng does; so is an iCCP profile that does not inflate. A palette index past the end of the palette reads as
/// opaque black, as Go's and libpng's decoders read it. Image data after the last row is ignored; the image is then re-encoded
/// rather than passed through. A missing IEND is accepted once the image data is complete.
/// </para>
/// <para>
/// The image is checked against the pixel limit before anything the size of the image is allocated. Decoding inflates the
/// concatenated IDAT data row by row into two pooled rows (plus one pooled image for an interlaced file) and allocates nothing
/// per row: the BCL inflater is per file, and the IDAT copy is pooled unless it passes through.
/// </para>
/// </remarks>
internal static class PngReader
{
    /// <summary>The largest iCCP profile inflated; a larger one is ignored.</summary>
    public const int MaxProfileLength = 1 << 24;

    private static readonly uint[] CrcTable = CreateCrcTable();

    /// <summary>The Adam7 passes: first column, first row, column step, row step (ISO/IEC 15948 §8.2, Table 8.1).</summary>
    private static readonly (int X, int Y, int Dx, int Dy)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns whether <paramref name="data"/> starts with the PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

    /// <summary>Reads a PNG file.</summary>
    /// <param name="png">The file.</param>
    /// <param name="maxPixels">The largest width × height accepted.</param>
    /// <param name="keepSamples">Whether to keep the decoded colour samples even when the IDAT data can be passed through.</param>
    /// <param name="error">Why the file cannot be read, or <see langword="null"/>.</param>
    /// <returns>The image, or <see langword="null"/> when the file cannot be read.</returns>
    public static PngImage? Read(ReadOnlySpan<byte> png, long maxPixels, bool keepSamples, out string? error)
    {
        if (!IsPng(png))
        {
            error = "The data does not start with the PNG signature (ISO/IEC 15948 §5.2).";
            return null;
        }

        if (!ReadChunks(png, maxPixels, out Chunks chunks, out error))
        {
            return null;
        }

        PngImage? owned = new PngImage(chunks.Width, chunks.Height, chunks.BitDepth, chunks.ColorType, chunks.Interlaced);
        PngImage image = owned;
        bool passThrough = !chunks.Interlaced && chunks.ColorType is PngColorType.Gray or PngColorType.Rgb or PngColorType.Palette;

        // Data that may pass through becomes the image's stream data; any other is only inflated, so it is pooled.
        byte[] idat = passThrough ? new byte[chunks.IdatLength] : ArrayPool<byte>.Shared.Rent(chunks.IdatLength);
        try
        {
            int written = 0;
            int position = chunks.FirstIdat;
            while (written < chunks.IdatLength)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png[position..]);
                png.Slice(position + 8, length).CopyTo(idat.AsSpan(written));
                written += length;
                position += 12 + length;
            }

            ReadAncillary(png, chunks, image);
            ReadOnlySpan<byte> paletteAlpha = chunks.ColorType == PngColorType.Palette && chunks.Transparency >= 0
                ? png.Slice(chunks.Transparency + 8, BinaryPrimitives.ReadInt32BigEndian(png[chunks.Transparency..]))
                : [];
            bool keep = keepSamples || !passThrough;
            if (!Decode(image, idat, chunks.IdatLength, paletteAlpha, keep, out bool extraData, out int maxIndex, out error))
            {
                return null;
            }

            if (passThrough && extraData && !keep && !Decode(image, idat, chunks.IdatLength, paletteAlpha, keepColor: true, out _, out _, out error))
            {
                return null;
            }

            image.PassThrough = passThrough && !extraData ? idat : null;
            if (chunks.ColorType == PngColorType.Palette && maxIndex >= image.Palette.Length / 3)
            {
                // ISO/IEC 15948 §11.2.3: an index past the palette is an error that decoders read as black; the table grows to cover it.
                byte[] padded = new byte[(maxIndex + 1) * 3];
                image.Palette.CopyTo(padded, 0);
                image.Palette = padded;
            }

            owned = null;
            return image;
        }
        finally
        {
            owned?.Dispose();
            if (!passThrough)
            {
                ArrayPool<byte>.Shared.Return(idat);
            }
        }
    }

    /// <summary>Computes the CRC-32 of ISO/IEC 15948 Annex D (the one of ISO 3309 and zlib).</summary>
    internal static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] CreateCrcTable()
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

    /// <summary>Walks the chunks once: validates the critical ones and records where the others are.</summary>
    private static bool ReadChunks(ReadOnlySpan<byte> png, long maxPixels, out Chunks chunks, out string? error)
    {
        chunks = new Chunks { Transparency = -1, Profile = -1, Palette = -1, FirstIdat = -1 };
        bool header = false;
        bool idatEnded = false;
        int position = Signature.Length;
        while (position < png.Length)
        {
            if (png.Length - position < 12)
            {
                if (chunks.FirstIdat >= 0)
                {
                    break;
                }

                return Fail("The file ends inside a chunk (ISO/IEC 15948 §5.3).", out error);
            }

            uint length = BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
            if (length > int.MaxValue || length > png.Length - position - 12)
            {
                return Fail("A chunk is longer than the rest of the file (ISO/IEC 15948 §5.3).", out error);
            }

            ReadOnlySpan<byte> type = png.Slice(position + 4, 4);
            if (!IsLetters(type))
            {
                return Fail("A chunk type is not four ASCII letters (ISO/IEC 15948 §5.3).", out error);
            }

            int dataStart = position + 8;
            ReadOnlySpan<byte> data = png.Slice(dataStart, (int)length);
            bool critical = (type[0] & 0x20) == 0;
            bool crcValid = Crc(png.Slice(position + 4, 4 + (int)length)) == BinaryPrimitives.ReadUInt32BigEndian(png[(dataStart + (int)length)..]);
            int chunkStart = position;
            position = dataStart + (int)length + 4;
            if (!crcValid)
            {
                if (critical)
                {
                    return Fail(string.Create(CultureInfo.InvariantCulture, $"The CRC of the {Name(type)} chunk does not match its data (ISO/IEC 15948 §5.3)."), out error);
                }

                continue;
            }

            if (!header && !type.SequenceEqual("IHDR"u8))
            {
                return Fail("The first chunk is not IHDR (ISO/IEC 15948 §5.6).", out error);
            }

            if (chunks.FirstIdat >= 0 && !type.SequenceEqual("IDAT"u8))
            {
                idatEnded = true;
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (header)
                {
                    return Fail("The file has a second IHDR chunk (ISO/IEC 15948 §5.6).", out error);
                }

                if (!ReadHeader(data, maxPixels, ref chunks, out error))
                {
                    return false;
                }

                header = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (chunks.FirstIdat >= 0 || chunks.Palette >= 0 || length == 0 || length % 3 != 0 || length > 768)
                {
                    return Fail("The PLTE chunk is repeated, after the image data, or not 1 to 256 three-byte entries (ISO/IEC 15948 §11.2.3).", out error);
                }

                chunks.Palette = chunkStart;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (idatEnded)
                {
                    return Fail("The IDAT chunks are not consecutive (ISO/IEC 15948 §11.2.4).", out error);
                }

                if (chunks.FirstIdat < 0)
                {
                    chunks.FirstIdat = chunkStart;
                }

                if (chunks.IdatLength + length > Array.MaxLength)
                {
                    return Fail("The image data is too long.", out error);
                }

                chunks.IdatLength += (int)length;
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (chunks.FirstIdat < 0 && chunks.Transparency < 0)
                {
                    chunks.Transparency = chunkStart;
                }
            }
            else if (type.SequenceEqual("iCCP"u8))
            {
                if (chunks.FirstIdat < 0 && chunks.Palette < 0 && chunks.Profile < 0)
                {
                    chunks.Profile = chunkStart;
                }
            }
            else if (critical)
            {
                return Fail(string.Create(CultureInfo.InvariantCulture, $"The file has a critical chunk this reader does not know, {Name(type)} (ISO/IEC 15948 §5.4)."), out error);
            }
        }

        return Complete(chunks, out error);
    }

    /// <summary>Checks that what the image needs was found.</summary>
    private static bool Complete(in Chunks chunks, out string? error)
    {
        if (chunks.Width == 0)
        {
            return Fail("The file has no IHDR chunk (ISO/IEC 15948 §11.2.2).", out error);
        }

        if (chunks.ColorType == PngColorType.Palette && chunks.Palette < 0)
        {
            return Fail("An indexed-colour image has no PLTE chunk (ISO/IEC 15948 §11.2.3).", out error);
        }

        if (chunks.FirstIdat < 0)
        {
            return Fail("The file has no IDAT chunk (ISO/IEC 15948 §11.2.4).", out error);
        }

        error = null;
        return true;
    }

    private static bool ReadHeader(ReadOnlySpan<byte> data, long maxPixels, ref Chunks chunks, out string? error)
    {
        if (data.Length != 13)
        {
            return Fail("The IHDR chunk is not 13 bytes long (ISO/IEC 15948 §11.2.2).", out error);
        }

        uint width = BinaryPrimitives.ReadUInt32BigEndian(data);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        int depth = data[8];
        int colorType = data[9];
        bool validDepth = colorType switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => depth is 8 or 16,
            _ => false,
        };
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue || !validDepth || data[10] != 0 || data[11] != 0 || data[12] > 1)
        {
            return Fail("The IHDR chunk is invalid: a zero or too large size, a colour type and bit depth Table 11.1 does not allow, or an unknown compression, filter or interlace method (ISO/IEC 15948 §11.2.2).", out error);
        }

        if ((long)width * height > maxPixels)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"The image is {width} x {height} pixels, more than the {maxPixels} allowed (PdfOptions.MaxImagePixels)."), out error);
        }

        int channels = Channels((PngColorType)colorType);
        long stride = (((long)width * channels * depth) + 7) / 8;
        if ((stride + 1) * height > Array.MaxLength)
        {
            return Fail("The image is too large to decode.", out error);
        }

        chunks.Width = (int)width;
        chunks.Height = (int)height;
        chunks.BitDepth = depth;
        chunks.ColorType = (PngColorType)colorType;
        chunks.Interlaced = data[12] == 1;
        error = null;
        return true;
    }

    /// <summary>Reads PLTE, tRNS (gray and truecolour) and iCCP into the image.</summary>
    private static void ReadAncillary(ReadOnlySpan<byte> png, in Chunks chunks, PngImage image)
    {
        if (chunks.ColorType == PngColorType.Palette)
        {
            image.Palette = ChunkData(png, chunks.Palette).ToArray();
        }

        if (chunks.Transparency >= 0 && chunks.ColorType is PngColorType.Gray or PngColorType.Rgb)
        {
            ReadOnlySpan<byte> data = ChunkData(png, chunks.Transparency);
            int components = chunks.ColorType == PngColorType.Gray ? 1 : 3;
            int max = (1 << chunks.BitDepth) - 1;
            if (data.Length == 2 * components)
            {
                ushort[] key = new ushort[components];
                bool inRange = true;
                for (int c = 0; c < components; c++)
                {
                    key[c] = BinaryPrimitives.ReadUInt16BigEndian(data[(2 * c)..]);
                    inRange &= key[c] <= max;
                }

                // §11.3.2.1: a value outside the bit depth's range matches no pixel, so it masks nothing.
                image.ColorKey = inRange ? key : null;
            }
        }

        if (chunks.Profile >= 0)
        {
            image.IccProfile = InflateProfile(ChunkData(png, chunks.Profile));
        }
    }

    /// <summary>Inflates an iCCP chunk's profile: name, NUL, compression method 0, zlib data (§11.3.3.3); <see langword="null"/> when it cannot.</summary>
    private static byte[]? InflateProfile(ReadOnlySpan<byte> data)
    {
        int nul = data[..Math.Min(data.Length, 80)].IndexOf((byte)0);
        if (nul < 1 || nul + 2 > data.Length || data[nul + 1] != 0)
        {
            return null;
        }

        byte[] compressed = data[(nul + 2)..].ToArray();
        try
        {
            using var source = new MemoryStream(compressed, writable: false);
            using var inflater = new ZLibStream(source, CompressionMode.Decompress);
            using var profile = new MemoryStream();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(16384);
            try
            {
                int count;
                while ((count = inflater.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (profile.Length + count > MaxProfileLength)
                    {
                        return null;
                    }

                    profile.Write(buffer, 0, count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return profile.Length == 0 ? null : profile.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Inflates and unfilters every row, de-interlacing when needed, and separates colour, alpha and palette transparency.</summary>
    private static bool Decode(PngImage image, byte[] idat, int idatLength, ReadOnlySpan<byte> paletteAlpha, bool keepColor, out bool extraData, out int maxIndex, out string? error)
    {
        int channels = Channels(image.ColorType);
        int bitsPerPixel = channels * image.BitDepth;
        int stride = (int)((((long)image.Width * bitsPerPixel) + 7) / 8);
        int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        var target = new RowTarget(image, paletteAlpha, keepColor);
        byte[] current = ArrayPool<byte>.Shared.Rent(stride + 1);
        byte[] previous = ArrayPool<byte>.Shared.Rent(stride);
        byte[]? whole = null;
        extraData = false;
        maxIndex = 0;
        try
        {
            using var source = new MemoryStream(idat, 0, idatLength, writable: false);
            using var inflater = new ZLibStream(source, CompressionMode.Decompress);
            if (!image.Interlaced)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    if (!ReadRow(inflater, current.AsSpan(0, stride + 1), previous.AsSpan(0, stride), y == 0, bytesPerPixel, out error))
                    {
                        return false;
                    }

                    target.Row(y, current.AsSpan(1, stride));
                    current.AsSpan(1, stride).CopyTo(previous);
                }
            }
            else
            {
                whole = ArrayPool<byte>.Shared.Rent(stride * image.Height);
                Span<byte> pixels = whole.AsSpan(0, stride * image.Height);
                pixels.Clear();
                foreach ((int x0, int y0, int dx, int dy) in Adam7)
                {
                    int width = (image.Width - x0 + dx - 1) / dx;
                    int height = (image.Height - y0 + dy - 1) / dy;
                    if (width <= 0 || height <= 0)
                    {
                        continue;
                    }

                    int passStride = (int)((((long)width * bitsPerPixel) + 7) / 8);
                    for (int row = 0; row < height; row++)
                    {
                        if (!ReadRow(inflater, current.AsSpan(0, passStride + 1), previous.AsSpan(0, passStride), row == 0, bytesPerPixel, out error))
                        {
                            return false;
                        }

                        Scatter(current.AsSpan(1, passStride), pixels.Slice((y0 + (row * dy)) * stride, stride), width, x0, dx, bitsPerPixel);
                        current.AsSpan(1, passStride).CopyTo(previous);
                    }
                }

                for (int y = 0; y < image.Height; y++)
                {
                    target.Row(y, pixels.Slice(y * stride, stride));
                }
            }

            Span<byte> probe = stackalloc byte[1];
            extraData = inflater.Read(probe) > 0;
        }
        catch (InvalidDataException)
        {
            error = "The image data is not valid zlib data (ISO/IEC 15948 §10).";
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(current);
            ArrayPool<byte>.Shared.Return(previous);
            if (whole is not null)
            {
                ArrayPool<byte>.Shared.Return(whole);
            }
        }

        target.Finish();
        maxIndex = target.MaxIndex;
        error = null;
        return true;
    }

    /// <summary>Reads one filtered row (type byte and data) and unfilters it in place.</summary>
    private static bool ReadRow(Stream inflater, Span<byte> row, Span<byte> previous, bool first, int bytesPerPixel, out string? error)
    {
        int read = 0;
        while (read < row.Length)
        {
            int count = inflater.Read(row[read..]);
            if (count == 0)
            {
                return Fail("The image data ends before the last row (ISO/IEC 15948 §11.2.4).", out error);
            }

            read += count;
        }

        if (first)
        {
            previous.Clear();
        }

        if (!Unfilter(row[0], row[1..], previous, bytesPerPixel))
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"A row has filter type {row[0]}; only 0 to 4 exist (ISO/IEC 15948 §9.2)."), out error);
        }

        error = null;
        return true;
    }

    /// <summary>Reverses a row's filter (ISO/IEC 15948 §9.2 and §9.4) against the unfiltered row above it.</summary>
    private static bool Unfilter(byte type, Span<byte> row, ReadOnlySpan<byte> above, int bytesPerPixel)
    {
        switch (type)
        {
            case 0:
                return true;
            case 1:
                for (int i = bytesPerPixel; i < row.Length; i++)
                {
                    row[i] += row[i - bytesPerPixel];
                }

                return true;
            case 2:
                for (int i = 0; i < row.Length; i++)
                {
                    row[i] += above[i];
                }

                return true;
            case 3:
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    row[i] += (byte)((left + above[i]) >> 1);
                }

                return true;
            case 4:
                for (int i = 0; i < row.Length; i++)
                {
                    int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    int upperLeft = i >= bytesPerPixel ? above[i - bytesPerPixel] : 0;
                    row[i] += Paeth(left, above[i], upperLeft);
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>The Paeth predictor (ISO/IEC 15948 §9.4).</summary>
    private static byte Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);
        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return (byte)left;
        }

        return toAbove <= toUpperLeft ? (byte)above : (byte)upperLeft;
    }

    /// <summary>Copies the pixels of a pass row to their places in a full row (ISO/IEC 15948 §8.2).</summary>
    private static void Scatter(ReadOnlySpan<byte> passRow, Span<byte> row, int count, int x0, int dx, int bitsPerPixel)
    {
        if (bitsPerPixel >= 8)
        {
            int bytes = bitsPerPixel / 8;
            for (int i = 0; i < count; i++)
            {
                passRow.Slice(i * bytes, bytes).CopyTo(row[((x0 + (i * dx)) * bytes)..]);
            }

            return;
        }

        int mask = (1 << bitsPerPixel) - 1;
        for (int i = 0; i < count; i++)
        {
            int source = i * bitsPerPixel;
            int value = (passRow[source >> 3] >> (8 - bitsPerPixel - (source & 7))) & mask;
            int destination = (x0 + (i * dx)) * bitsPerPixel;
            int shift = 8 - bitsPerPixel - (destination & 7);
            row[destination >> 3] = (byte)((row[destination >> 3] & ~(mask << shift)) | (value << shift));
        }
    }

    private static int Channels(PngColorType type) => type switch
    {
        PngColorType.Rgb => 3,
        PngColorType.GrayAlpha => 2,
        PngColorType.Rgba => 4,
        _ => 1,
    };

    private static ReadOnlySpan<byte> ChunkData(ReadOnlySpan<byte> png, int chunk) =>
        png.Slice(chunk + 8, BinaryPrimitives.ReadInt32BigEndian(png[chunk..]));

    private static bool IsLetters(ReadOnlySpan<byte> type)
    {
        foreach (byte value in type)
        {
            if (value is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z')))
            {
                return false;
            }
        }

        return true;
    }

    private static string Name(ReadOnlySpan<byte> type) => System.Text.Encoding.ASCII.GetString(type);

    private static bool Fail(string? message, out string? error)
    {
        error = message;
        return false;
    }

    /// <summary>Where the chunks the image needs are, from the first walk.</summary>
    private struct Chunks
    {
        public int Width;
        public int Height;
        public int BitDepth;
        public PngColorType ColorType;
        public bool Interlaced;
        public int Palette;
        public int Transparency;
        public int Profile;
        public int FirstIdat;
        public int IdatLength;
    }

    /// <summary>Receives unfiltered full rows in order and fills the image's planes.</summary>
    private ref struct RowTarget
    {
        private readonly PngImage _image;
        private readonly ReadOnlySpan<byte> _paletteAlpha;
        private readonly Span<byte> _color;
        private readonly Span<byte> _alpha;
        private readonly bool _keepColor;
        private bool _translucent;

        public RowTarget(PngImage image, ReadOnlySpan<byte> paletteAlpha, bool keepColor)
        {
            _image = image;
            _paletteAlpha = paletteAlpha;
            _keepColor = keepColor;
            _color = keepColor ? image.RentColor() : [];
            bool alpha = image.ColorType is PngColorType.GrayAlpha or PngColorType.Rgba
                || (image.ColorType == PngColorType.Palette && paletteAlpha.ContainsAnyExcept((byte)255));
            _alpha = alpha ? image.RentAlpha() : [];
        }

        public int MaxIndex { get; private set; }

        public void Row(int y, ReadOnlySpan<byte> row)
        {
            switch (_image.ColorType)
            {
                case PngColorType.GrayAlpha or PngColorType.Rgba:
                    Split(y, row);
                    break;
                case PngColorType.Palette:
                    if (_keepColor)
                    {
                        row.CopyTo(_color.Slice(y * _image.ColorStride, _image.ColorStride));
                    }

                    Indices(y, row);
                    break;
                default:
                    if (_keepColor)
                    {
                        row.CopyTo(_color.Slice(y * _image.ColorStride, _image.ColorStride));
                    }

                    break;
            }
        }

        public readonly void Finish()
        {
            if (!_translucent)
            {
                _image.DropAlpha();
            }
        }

        /// <summary>Separates the colour samples from the alpha sample of each pixel.</summary>
        private void Split(int y, ReadOnlySpan<byte> row)
        {
            int bytes = _image.BitDepth / 8;
            int colors = _image.ColorComponents * bytes;
            Span<byte> color = _color.Slice(y * _image.ColorStride, _image.ColorStride);
            Span<byte> alpha = _alpha.Slice(y * _image.AlphaStride, _image.AlphaStride);
            int pixel = colors + bytes;
            bool translucent = false;
            for (int x = 0; x < _image.Width; x++)
            {
                ReadOnlySpan<byte> source = row.Slice(x * pixel, pixel);
                source[..colors].CopyTo(color[(x * colors)..]);
                ReadOnlySpan<byte> value = source[colors..];
                value.CopyTo(alpha[(x * bytes)..]);
                translucent |= value[0] != 255 || value[^1] != 255;
            }

            _translucent |= translucent;
        }

        /// <summary>Finds the largest palette index and maps each index to its tRNS alpha.</summary>
        private void Indices(int y, ReadOnlySpan<byte> row)
        {
            int depth = _image.BitDepth;
            int mask = (1 << depth) - 1;
            int max = MaxIndex;
            Span<byte> alpha = _alpha.IsEmpty ? [] : _alpha.Slice(y * _image.AlphaStride, _image.AlphaStride);
            bool translucent = false;
            for (int x = 0; x < _image.Width; x++)
            {
                int bit = x * depth;
                int index = depth == 8 ? row[x] : (row[bit >> 3] >> (8 - depth - (bit & 7))) & mask;
                max = Math.Max(max, index);
                if (!alpha.IsEmpty)
                {
                    byte value = index < _paletteAlpha.Length ? _paletteAlpha[index] : (byte)255;
                    alpha[x] = value;
                    translucent |= value != 255;
                }
            }

            MaxIndex = max;
            _translucent |= translucent;
        }
    }
}
