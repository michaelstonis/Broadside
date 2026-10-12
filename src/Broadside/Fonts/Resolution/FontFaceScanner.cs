using System.Buffers.Binary;
using System.Text;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// Reads what a font directory scan needs from an installed font file without loading it: the table directory of each face and
/// the "name", "OS/2", "head", "post" and "cmap" facts that identify it.
/// </summary>
/// <remarks>
/// <para>
/// OpenType specification: the table directory and the TrueType collection header ("ttcf": face offsets are from the start of the
/// file, and so are the table offsets inside each face), "name" (ids 1, 2, 4, 6, 16, 17; Windows Unicode records preferred, then
/// Unicode, then Macintosh Roman), "OS/2" (usWeightClass, usWidthClass, fsSelection), "head" (macStyle), "post" (isFixedPitch) and
/// "cmap" (whether it has a (3, 0) symbol subtable).
/// </para>
/// <para>
/// Installed fonts are untrusted data: every offset and length is checked against the file, tables are read only up to a small cap,
/// and a file or face that does not make sense is skipped. Nothing here throws for bad data. Faces without outlines (no "glyf",
/// "CFF " or "CFF2", such as bitmap-only fonts) and faces without a usable name are skipped.
/// </para>
/// </remarks>
internal static class FontFaceScanner
{
    /// <summary>The most faces read from one collection.</summary>
    private const int MaxFaces = 256;

    /// <summary>The most table records read from one face.</summary>
    private const int MaxTables = 512;

    /// <summary>The most bytes of a "name" table read.</summary>
    private const int MaxNameTable = 128 * 1024;

    private const uint TrueTypeVersion = 0x00010000;
    private const uint AppleTrueType = 0x74727565; // 'true'
    private const uint OpenTypeCff = 0x4F54544F; // 'OTTO'
    private const uint Collection = 0x74746366; // 'ttcf'

    /// <summary>Reads the faces of one font file.</summary>
    /// <param name="file">The file's bytes, read on demand.</param>
    /// <param name="path">The file's path, recorded in each face.</param>
    /// <returns>The faces found, in file order; empty when the file is not a font this scan reads.</returns>
    public static List<SystemFontFace> Scan(FontFileReader file, string path)
    {
        var faces = new List<SystemFontFace>();
        Span<byte> header = stackalloc byte[12];
        if (!file.TryRead(0, header))
        {
            return faces;
        }

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (tag == Collection)
        {
            uint count = Math.Min(BinaryPrimitives.ReadUInt32BigEndian(header[8..]), MaxFaces);
            Span<byte> offset = stackalloc byte[4];
            for (int index = 0; index < count; index++)
            {
                if (file.TryRead(12 + (4L * index), offset) && ScanFace(file, BinaryPrimitives.ReadUInt32BigEndian(offset), path, index) is { } face)
                {
                    faces.Add(face);
                }
            }
        }
        else if (tag is TrueTypeVersion or AppleTrueType or OpenTypeCff && ScanFace(file, 0, path, 0) is { } face)
        {
            faces.Add(face);
        }

        return faces;
    }

    private static SystemFontFace? ScanFace(FontFileReader file, long start, string path, int faceIndex)
    {
        Span<byte> directory = stackalloc byte[12];
        if (!file.TryRead(start, directory))
        {
            return null;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(directory);
        if (version is not (TrueTypeVersion or AppleTrueType or OpenTypeCff))
        {
            return null;
        }

        int tableCount = Math.Min((int)BinaryPrimitives.ReadUInt16BigEndian(directory[4..]), MaxTables);
        TableRecord name = default, os2 = default, head = default, post = default, cmap = default;
        bool outlines = false;
        Span<byte> record = stackalloc byte[16];
        for (int index = 0; index < tableCount; index++)
        {
            if (!file.TryRead(start + 12 + (16L * index), record))
            {
                return null;
            }

            uint tableTag = BinaryPrimitives.ReadUInt32BigEndian(record);
            var table = new TableRecord(BinaryPrimitives.ReadUInt32BigEndian(record[8..]), BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
            switch (tableTag)
            {
                case 0x6E616D65: // 'name'
                    name = table;
                    break;
                case 0x4F532F32: // 'OS/2'
                    os2 = table;
                    break;
                case 0x68656164: // 'head'
                    head = table;
                    break;
                case 0x706F7374: // 'post'
                    post = table;
                    break;
                case 0x636D6170: // 'cmap'
                    cmap = table;
                    break;
                case 0x676C7966 or 0x43464620 or 0x43464632: // 'glyf', 'CFF ', 'CFF2'
                    outlines = true;
                    break;
            }
        }

        if (!outlines || head.Length < 46 || name.Length < 6)
        {
            return null;
        }

        string?[] names = ReadNames(file, name);
        string? postScriptName = names[6];
        string? family = names[16] ?? names[1];
        if (string.IsNullOrWhiteSpace(postScriptName) && string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        string? subfamily = names[17] ?? names[2];
        Span<byte> two = stackalloc byte[2];
        int macStyle = file.TryRead(head.Offset + 44, two) ? BinaryPrimitives.ReadUInt16BigEndian(two) : 0;
        int weight = 0, widthClass = 5, selection = 0;
        if (os2.Length >= 8 && file.TryRead(os2.Offset + 4, two))
        {
            weight = BinaryPrimitives.ReadUInt16BigEndian(two);
            widthClass = file.TryRead(os2.Offset + 6, two) ? BinaryPrimitives.ReadUInt16BigEndian(two) : 5;
            selection = os2.Length >= 64 && file.TryRead(os2.Offset + 62, two) ? BinaryPrimitives.ReadUInt16BigEndian(two) : 0;
        }

        bool bold = weight >= 600 || (selection & (1 << 5)) != 0 || (macStyle & 1) != 0;
        if (weight is < 1 or > 1000)
        {
            weight = bold ? 700 : 400;
        }

        bool italic = (selection & 1) != 0 || (selection & (1 << 9)) != 0 || (macStyle & 2) != 0
            || (subfamily is not null && (subfamily.Contains("Italic", StringComparison.OrdinalIgnoreCase) || subfamily.Contains("Oblique", StringComparison.OrdinalIgnoreCase)));
        Span<byte> four = stackalloc byte[4];
        bool fixedPitch = post.Length >= 16 && file.TryRead(post.Offset + 12, four) && BinaryPrimitives.ReadUInt32BigEndian(four) != 0;
        bool symbol = HasSymbolCmap(file, cmap);

        postScriptName = string.IsNullOrWhiteSpace(postScriptName) ? string.Concat(family!.Where(ch => !char.IsWhiteSpace(ch))) : postScriptName.Trim();
        family = string.IsNullOrWhiteSpace(family) ? FontNameParser.Parse(postScriptName).FamilyName : family.Trim();
        return new SystemFontFace(path, faceIndex, postScriptName, family, names[1]?.Trim(), subfamily?.Trim(), weight, widthClass, bold, italic, fixedPitch, symbol);
    }

    /// <summary>Reads name ids 0 to 17, each from the best record: Windows Unicode (English first), then Unicode, then Macintosh Roman.</summary>
    private static string?[] ReadNames(FontFileReader file, TableRecord table)
    {
        string?[] names = new string?[18];
        int[] ranks = new int[18];
        Array.Fill(ranks, int.MaxValue);
        int length = (int)Math.Min(table.Length, MaxNameTable);
        byte[] data = new byte[length];
        if (!file.TryRead(table.Offset, data))
        {
            return names;
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        int storage = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (int index = 0; index < count && 6 + (12 * (index + 1)) <= length; index++)
        {
            ReadOnlySpan<byte> record = data.AsSpan(6 + (12 * index), 12);
            int platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            int encoding = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            int language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            int id = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            int size = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            int offset = storage + BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            if (id >= names.Length || size == 0 || offset + size > length)
            {
                continue;
            }

            int rank = (platform, encoding) switch
            {
                (3, 1 or 10) => language == 0x409 ? 0 : 1,
                (3, 0) => 2,
                (0, _) => 3,
                (1, 0) => language == 0 ? 4 : 5,
                _ => int.MaxValue,
            };
            if (rank >= ranks[id])
            {
                continue;
            }

            ReadOnlySpan<byte> text = data.AsSpan(offset, size);
            string decoded = platform == 1 ? Latin1(text) : Encoding.BigEndianUnicode.GetString(text[..(size & ~1)]);
            if (decoded.Contains('\0', StringComparison.Ordinal) || decoded.Contains('|', StringComparison.Ordinal))
            {
                continue;
            }

            names[id] = decoded;
            ranks[id] = rank;
        }

        return names;
    }

    /// <summary>Macintosh Roman decoded as Latin-1: exact for the ASCII names fonts use.</summary>
    private static string Latin1(ReadOnlySpan<byte> text) => Encoding.Latin1.GetString(text);

    private static bool HasSymbolCmap(FontFileReader file, TableRecord table)
    {
        Span<byte> header = stackalloc byte[4];
        if (table.Length < 4 || !file.TryRead(table.Offset, header))
        {
            return false;
        }

        int count = Math.Min((int)BinaryPrimitives.ReadUInt16BigEndian(header[2..]), 64);
        Span<byte> record = stackalloc byte[8];
        for (int index = 0; index < count && 4 + (8 * (index + 1)) <= table.Length; index++)
        {
            if (file.TryRead(table.Offset + 4 + (8L * index), record)
                && BinaryPrimitives.ReadUInt16BigEndian(record) == 3 && BinaryPrimitives.ReadUInt16BigEndian(record[2..]) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct TableRecord(uint Offset, uint Length);
}

/// <summary>Reads byte ranges of a font file on demand.</summary>
internal abstract class FontFileReader
{
    /// <summary>Gets the length of the file.</summary>
    public abstract long Length { get; }

    /// <summary>Reads exactly <paramref name="destination"/>'s length of bytes at <paramref name="offset"/>.</summary>
    /// <returns><see langword="false"/> when the range is not wholly inside the file or cannot be read.</returns>
    public bool TryRead(long offset, Span<byte> destination) =>
        offset >= 0 && offset <= Length - destination.Length && ReadCore(offset, destination);

    /// <summary>Reads a range already known to be inside the file.</summary>
    protected abstract bool ReadCore(long offset, Span<byte> destination);
}

/// <summary>A font file in memory.</summary>
internal sealed class MemoryFontFileReader(ReadOnlyMemory<byte> data) : FontFileReader
{
    public override long Length => data.Length;

    protected override bool ReadCore(long offset, Span<byte> destination)
    {
        data.Span.Slice((int)offset, destination.Length).CopyTo(destination);
        return true;
    }
}

/// <summary>A font file on disk, read in place through its handle.</summary>
internal sealed class HandleFontFileReader(Microsoft.Win32.SafeHandles.SafeFileHandle handle, long length) : FontFileReader
{
    public override long Length => length;

    protected override bool ReadCore(long offset, Span<byte> destination)
    {
        try
        {
            while (!destination.IsEmpty)
            {
                int read = RandomAccess.Read(handle, destination, offset);
                if (read <= 0)
                {
                    return false;
                }

                destination = destination[read..];
                offset += read;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>One installed font face, as a font directory scan found it.</summary>
/// <param name="Path">The file.</param>
/// <param name="FaceIndex">The face's index in a collection; 0 otherwise.</param>
/// <param name="PostScriptName">Name id 6 (or the family without spaces).</param>
/// <param name="Family">The typographic family (name id 16), else the legacy family (id 1).</param>
/// <param name="LegacyFamily">Name id 1.</param>
/// <param name="Subfamily">The typographic subfamily (id 17), else the legacy subfamily (id 2).</param>
/// <param name="Weight">usWeightClass, or 400/700 from the style bits.</param>
/// <param name="WidthClass">usWidthClass (5 normal, below condensed).</param>
/// <param name="IsBold">Bold by weight or style bits.</param>
/// <param name="IsItalic">Italic or oblique by style bits or subfamily.</param>
/// <param name="IsFixedPitch">post isFixedPitch.</param>
/// <param name="HasSymbolCmap">Whether its "cmap" has a (3, 0) symbol subtable.</param>
internal sealed record SystemFontFace(
    string Path,
    int FaceIndex,
    string PostScriptName,
    string Family,
    string? LegacyFamily,
    string? Subfamily,
    int Weight,
    int WidthClass,
    bool IsBold,
    bool IsItalic,
    bool IsFixedPitch,
    bool HasSymbolCmap);
