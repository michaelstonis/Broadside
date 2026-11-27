using System.Buffers.Binary;

namespace Broadside.Images.Import;

/// <summary>The TIFF tags the importer reads (TIFF 6.0 Appendix A, with the TIFF/EP, ICC and Adobe additions).</summary>
internal static class TiffTag
{
    public const int NewSubfileType = 254;
    public const int ImageWidth = 256;
    public const int ImageLength = 257;
    public const int BitsPerSample = 258;
    public const int Compression = 259;
    public const int PhotometricInterpretation = 262;
    public const int FillOrder = 266;
    public const int StripOffsets = 273;
    public const int Orientation = 274;
    public const int SamplesPerPixel = 277;
    public const int RowsPerStrip = 278;
    public const int StripByteCounts = 279;
    public const int XResolution = 282;
    public const int YResolution = 283;
    public const int PlanarConfiguration = 284;
    public const int T4Options = 292;
    public const int T6Options = 293;
    public const int ResolutionUnit = 296;
    public const int Predictor = 317;
    public const int ColorMap = 320;
    public const int TileWidth = 322;
    public const int TileLength = 323;
    public const int TileOffsets = 324;
    public const int TileByteCounts = 325;
    public const int InkSet = 332;
    public const int NumberOfInks = 334;
    public const int ExtraSamples = 338;
    public const int SampleFormat = 339;
    public const int JpegTables = 347;
    public const int IccProfile = 34675;
}

/// <summary>One entry of an image file directory: its type, count and where its value starts in the file.</summary>
internal readonly record struct TiffEntry(int Type, long Count, int Position);

/// <summary>
/// An image file directory (TIFF 6.0 §2, "Image File Directory"), the entries the importer reads, each pointing into the file.
/// </summary>
/// <remarks>
/// Entries whose values lie outside the file are dropped as if absent, and reported. Values are read in the file's byte order; a
/// SHORT, LONG or BYTE is accepted wherever an unsigned integer is expected (TIFF 6.0 §2 asks readers to accept either size).
/// </remarks>
internal sealed class TiffDirectory
{
    private readonly Dictionary<int, TiffEntry> _entries = [];
    private readonly bool _little;

    private TiffDirectory(bool little) => _little = little;

    /// <summary>Gets the offset of the next directory, or 0.</summary>
    public long Next { get; private set; }

    /// <summary>Gets the tags whose values were outside the file and were dropped.</summary>
    public List<int> Dropped { get; } = [];

    /// <summary>Returns the bytes of one value of a TIFF field type (TIFF 6.0 §2, "Types"), or 0 for an unknown type.</summary>
    public static int TypeSize(int type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12 => 8,
        _ => 0,
    };

    /// <summary>Reads the directory at <paramref name="offset"/>.</summary>
    /// <exception cref="InvalidDataException">The directory does not fit in the file.</exception>
    public static TiffDirectory Read(ReadOnlySpan<byte> tiff, long offset, bool little)
    {
        if (offset < 8 || offset + 2 > tiff.Length)
        {
            throw new InvalidDataException($"The image file directory at offset {offset} is outside the file (TIFF 6.0 §2).");
        }

        var directory = new TiffDirectory(little);
        int count = directory.U16(tiff, (int)offset);
        long end = offset + 2 + (12L * count);
        if (end > tiff.Length)
        {
            throw new InvalidDataException($"The image file directory at offset {offset} has {count} entries, which run past the end of the file (TIFF 6.0 §2).");
        }

        for (int i = 0; i < count; i++)
        {
            int at = (int)offset + 2 + (12 * i);
            int tag = directory.U16(tiff, at);
            int type = directory.U16(tiff, at + 2);
            long values = directory.U32(tiff, at + 4);
            int size = TypeSize(type);
            if (size == 0 || values == 0)
            {
                continue;
            }

            long bytes = size * values;
            long position = bytes <= 4 ? at + 8 : directory.U32(tiff, at + 8);
            if (position + bytes > tiff.Length)
            {
                directory.Dropped.Add(tag);
                continue;
            }

            directory._entries.TryAdd(tag, new TiffEntry(type, values, (int)position));
        }

        directory.Next = end + 4 <= tiff.Length ? directory.U32(tiff, (int)end) : 0;
        return directory;
    }

    /// <summary>Returns whether the directory has <paramref name="tag"/>.</summary>
    public bool Has(int tag) => _entries.ContainsKey(tag);

    /// <summary>Returns the number of values of <paramref name="tag"/>, or 0.</summary>
    public long Count(int tag) => _entries.TryGetValue(tag, out TiffEntry entry) ? entry.Count : 0;

    /// <summary>Returns the first value of <paramref name="tag"/> as an unsigned integer, or <paramref name="fallback"/>.</summary>
    public long Value(ReadOnlySpan<byte> tiff, int tag, long fallback) =>
        _entries.TryGetValue(tag, out TiffEntry entry) && Integer(tiff, entry, 0) is long value ? value : fallback;

    /// <summary>Returns every value of <paramref name="tag"/> as unsigned integers, or <see langword="null"/>.</summary>
    public long[]? Values(ReadOnlySpan<byte> tiff, int tag)
    {
        if (!_entries.TryGetValue(tag, out TiffEntry entry) || entry.Count > int.MaxValue / 8)
        {
            return null;
        }

        long[] values = new long[entry.Count];
        for (int i = 0; i < values.Length; i++)
        {
            if (Integer(tiff, entry, i) is not long value)
            {
                return null;
            }

            values[i] = value;
        }

        return values;
    }

    /// <summary>Returns the first RATIONAL of <paramref name="tag"/>, or 0.</summary>
    public double Rational(ReadOnlySpan<byte> tiff, int tag)
    {
        if (!_entries.TryGetValue(tag, out TiffEntry entry))
        {
            return 0;
        }

        if (entry.Type == 5)
        {
            long denominator = U32(tiff, entry.Position + 4);
            return denominator == 0 ? 0 : U32(tiff, entry.Position) / (double)denominator;
        }

        return Integer(tiff, entry, 0) ?? 0;
    }

    /// <summary>Returns the raw bytes of <paramref name="tag"/>'s values, or an empty span.</summary>
    public ReadOnlySpan<byte> Bytes(ReadOnlySpan<byte> tiff, int tag) =>
        _entries.TryGetValue(tag, out TiffEntry entry) ? tiff.Slice(entry.Position, (int)(entry.Count * TypeSize(entry.Type))) : [];

    /// <summary>Reads a 16-bit unsigned integer in the file's byte order.</summary>
    public int U16(ReadOnlySpan<byte> tiff, int at) =>
        _little ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[at..]);

    /// <summary>Reads a 32-bit unsigned integer in the file's byte order.</summary>
    public long U32(ReadOnlySpan<byte> tiff, int at) =>
        _little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[at..]);

    private long? Integer(ReadOnlySpan<byte> tiff, TiffEntry entry, int index)
    {
        if (index >= entry.Count)
        {
            return null;
        }

        int at = entry.Position + (index * TypeSize(entry.Type));
        return entry.Type switch
        {
            1 or 7 => tiff[at],
            3 => U16(tiff, at),
            4 or 13 => U32(tiff, at),
            _ => null,
        };
    }
}
