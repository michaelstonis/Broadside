using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.TrueType;

/// <summary>
/// The table directory of one font of an sfnt file (TrueType, OpenType, or one face of a TrueType collection): where each table is.
/// Shared by the TrueType outlines here and the CFF outlines of OpenType programs (#51).
/// </summary>
/// <remarks>
/// OpenType specification, "OpenType font file": table directory and TTC header. Table checksums are not verified (real files carry
/// wrong ones and no reader checks them). Offsets are from the start of the file, also inside a collection.
/// </remarks>
internal sealed class SfntFile
{
    /// <summary>The sfnt version of TrueType outlines (<c>00 01 00 00</c>).</summary>
    public const uint TrueTypeVersion = 0x00010000;

    /// <summary>Apple's sfnt version for TrueType outlines (<c>true</c>).</summary>
    public const uint AppleTrueTypeVersion = 0x74727565;

    /// <summary>The sfnt version of CFF outlines (<c>OTTO</c>).</summary>
    public const uint OpenTypeVersion = 0x4F54544F;

    /// <summary>The tag of a TrueType collection header (<c>ttcf</c>).</summary>
    public const uint CollectionTag = 0x74746366;

    private const uint HeadTag = 0x68656164;
    private const uint MaxpTag = 0x6D617870;
    private const uint HheaTag = 0x68686561;
    private const uint PostTag = 0x706F7374;
    private const uint CmapTag = 0x636D6170;
    private const uint Os2Tag = 0x4F532F32;
    private const uint NameTag = 0x6E616D65;
    private const uint LocaTag = 0x6C6F6361;
    private const uint GlyfTag = 0x676C7966;
    private const uint HmtxTag = 0x686D7478;

    /// <summary>The tables whose offsets are checked, those checked against others after them.</summary>
    private static readonly uint[] RecoveryOrder = [HeadTag, MaxpTag, HheaTag, PostTag, CmapTag, Os2Tag, NameTag, LocaTag, HmtxTag, GlyfTag];


    private readonly Table[] _tables;

    private SfntFile(ReadOnlyMemory<byte> data, uint version, Table[] tables)
    {
        Data = data;
        Version = version;
        _tables = tables;
    }

    /// <summary>Gets the whole file.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the font's sfnt version.</summary>
    public uint Version { get; }

    /// <summary>Makes a tag from its four ASCII characters.</summary>
    public static uint Tag(string tag) =>
        ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];

    /// <summary>Whether a 32-bit value is one of the sfnt versions or the collection tag.</summary>
    public static bool IsKnownVersion(uint version) =>
        version is TrueTypeVersion or AppleTrueTypeVersion or OpenTypeVersion or CollectionTag;

    /// <summary>Whether the data starts with an sfnt or collection signature and its (first) font has a table of the given tag.</summary>
    public static bool HasTable(ReadOnlySpan<byte> data, uint tag)
    {
        if (data.Length < 12)
        {
            return false;
        }

        int start = 0;
        if (BinaryPrimitives.ReadUInt32BigEndian(data) == CollectionTag)
        {
            if (data.Length < 16 || BinaryPrimitives.ReadUInt32BigEndian(data[8..]) == 0)
            {
                return false;
            }

            uint first = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
            if (first > (uint)(data.Length - 12))
            {
                return false;
            }

            start = (int)first;
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(data[(start + 4)..]);
        for (int index = 0; index < count; index++)
        {
            int record = start + 12 + (16 * index);
            if (record + 16 > data.Length)
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(data[record..]) == tag)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the table directory of the font the context selects; reports and returns <see langword="null"/> when there is none.</summary>
    /// <param name="data">The program.</param>
    /// <param name="context">The context: face selection and diagnostics.</param>
    /// <returns>The directory.</returns>
    public static SfntFile? Open(ReadOnlyMemory<byte> data, FontProgramContext context)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < 12)
        {
            context.Report(DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The font program is shorter than an sfnt table directory (OpenType \"Organization of an OpenType font\"); it is not usable.");
            return null;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(span);
        if (version != CollectionTag)
        {
            if (!IsKnownVersion(version))
            {
                context.Report(
                    DiagnosticCodes.FontTableInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The font program's sfnt version 0x{version:X8} is not 0x00010000, 'true' or 'OTTO'; its table directory is read anyway."));
            }

            return ReadDirectory(data, 0, context);
        }

        // TTC header: tag, major, minor, numFonts, offsetTable[numFonts].
        uint fonts = BinaryPrimitives.ReadUInt32BigEndian(span[8..]);
        int available = (int)Math.Min(fonts, (uint)((span.Length - 12) / 4));
        if (available == 0)
        {
            context.Report(DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The font collection lists no fonts; the program is not usable.");
            return null;
        }

        int chosen = -1;
        if (context.FaceName is { } faceName)
        {
            for (int index = 0; index < available && chosen < 0; index++)
            {
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(span[(12 + (4 * index))..]);
                if (offset < (uint)span.Length && ReadDirectory(data, (int)offset, context: null) is { } face
                    && string.Equals(face.ReadPostScriptName(), faceName, StringComparison.Ordinal))
                {
                    chosen = index;
                }
            }
        }

        if (chosen < 0)
        {
            chosen = context.FaceIndex;
            if (chosen < 0 || chosen >= available)
            {
                context.Report(
                    DiagnosticCodes.FontTableInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The font collection has {available} fonts; font {context.FaceIndex} does not exist and font 0 is read."));
                chosen = 0;
            }
        }

        uint start = BinaryPrimitives.ReadUInt32BigEndian(span[(12 + (4 * chosen))..]);
        if (start > (uint)(span.Length - 12))
        {
            context.Report(DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The font collection's offset to the chosen font lies outside the program; it is not usable.");
            return null;
        }

        return ReadDirectory(data, (int)start, context);
    }

    /// <summary>Finds a table: its bytes, clamped to the file; <see langword="false"/> when absent or empty.</summary>
    public bool TryGetTable(uint tag, out ReadOnlyMemory<byte> table)
    {
        foreach (Table entry in _tables)
        {
            if (entry.Tag == tag)
            {
                table = Data.Slice(entry.Offset, entry.Length);
                return true;
            }
        }

        table = default;
        return false;
    }

    /// <summary>Whether the font has a table.</summary>
    public bool Has(uint tag) => TryGetTable(tag, out _);

    /// <summary>Reads the PostScript name (name id 6) from the "name" table: Windows Unicode, then Macintosh Roman, then Unicode platform.</summary>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public string? ReadPostScriptName()
    {
        if (!TryGetTable(Tag("name"), out ReadOnlyMemory<byte> memory))
        {
            return null;
        }

        ReadOnlySpan<byte> name = memory.Span;
        if (name.Length < 6)
        {
            return null;
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        int storage = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);
        string? best = null;
        int bestRank = int.MaxValue;
        for (int index = 0; index < count; index++)
        {
            int record = 6 + (12 * index);
            if (record + 12 > name.Length)
            {
                break;
            }

            int platform = BinaryPrimitives.ReadUInt16BigEndian(name[record..]);
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 6)..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 8)..]);
            int offset = storage + BinaryPrimitives.ReadUInt16BigEndian(name[(record + 10)..]);
            int rank = platform switch { 3 => 0, 1 => 1, 0 => 2, _ => int.MaxValue };
            if (nameId != 6 || rank >= bestRank || offset + length > name.Length)
            {
                continue;
            }

            ReadOnlySpan<byte> text = name.Slice(offset, length);
            best = platform == 1 ? Encoding.Latin1.GetString(text) : Encoding.BigEndianUnicode.GetString(text);
            bestRank = rank;
        }

        return best;
    }

    private static SfntFile? ReadDirectory(ReadOnlyMemory<byte> data, int start, FontProgramContext? context)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (start + 12 > span.Length)
        {
            context?.Report(DiagnosticCodes.FontProgramInvalid, DiagnosticSeverity.Error, "The font's table directory lies outside the program; it is not usable.");
            return null;
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(span[start..]);
        int declared = BinaryPrimitives.ReadUInt16BigEndian(span[(start + 4)..]);
        int count = Math.Min(declared, (span.Length - start - 12) / 16);
        if (count < declared)
        {
            context?.Report(
                DiagnosticCodes.FontTableInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The table directory lists {declared} tables but the program holds records for {count}; the others are ignored."));
        }

        var tables = new List<Table>(count);
        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> record = span.Slice(start + 12 + (16 * index), 16);
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(record);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            if (length == 0)
            {
                continue;
            }

            if (offset >= (uint)span.Length)
            {
                context?.Report(
                    DiagnosticCodes.FontTableInvalid,
                    DiagnosticSeverity.Warning,
                    $"The \"{TagText(tag)}\" table starts past the end of the font program; it is ignored.");
                continue;
            }

            if (length > (uint)span.Length - offset)
            {
                context?.Report(
                    DiagnosticCodes.FontTableInvalid,
                    DiagnosticSeverity.Warning,
                    $"The \"{TagText(tag)}\" table runs past the end of the font program; it is read up to the end.");
                length = (uint)span.Length - offset;
            }

            if (tables.Exists(table => table.Tag == tag))
            {
                context?.Report(
                    DiagnosticCodes.FontTableInvalid,
                    DiagnosticSeverity.Warning,
                    $"The table directory lists the \"{TagText(tag)}\" table twice; the first is used.");
                continue;
            }

            tables.Add(new Table(tag, (int)offset, (int)length));
        }

        RecoverShiftedOffsets(span, tables, context);
        return new SfntFile(data, version, [.. tables]);
    }

    /// <summary>
    /// Moves a table whose stated offset is off by one byte (pdfjs/bug1050040.pdf lost a byte inside "glyf", so every table after it
    /// starts one byte before its offset): a table whose start can be recognized, not found at its offset but one byte before or
    /// after it, is read there. Recognized: the "head" magic number; the versions of "maxp", "hhea", "post", "cmap" and "OS/2"
    /// (with plausible weight and width classes); the "name" format and record count; "loca" offsets that start at 0 and ascend
    /// within "glyf"; "hmtx" advances within the "hhea" maximum; a plausible header for the first "glyf" glyph.
    /// </summary>
    private static void RecoverShiftedOffsets(ReadOnlySpan<byte> span, List<Table> tables, FontProgramContext? context)
    {
        List<string>? moved = null;

        // One byte before, then one byte after; once "head" (whose magic number is unambiguous) has moved, its way first.
        int preferred = -1;
        foreach (uint tag in RecoveryOrder)
        {
            int index = tables.FindIndex(table => table.Tag == tag);
            if (index < 0 || StartsAt(span, tables, tag, tables[index].Offset) is not false)
            {
                continue;
            }

            foreach (int shift in (int[])[preferred, -preferred])
            {
                if (StartsAt(span, tables, tag, tables[index].Offset + shift) == true)
                {
                    if (tag == HeadTag)
                    {
                        preferred = shift;
                    }

                    Table table = tables[index];
                    int offset = table.Offset + shift;
                    tables[index] = table with { Offset = offset, Length = Math.Min(table.Length, span.Length - offset) };
                    (moved ??= []).Add(TagText(tag));
                    break;
                }
            }
        }

        if (moved is not null)
        {
            context?.Report(
                DiagnosticCodes.FontTableInvalid,
                DiagnosticSeverity.Warning,
                $"The table directory gives offsets one byte away from where these tables start: {string.Join(", ", moved.Select(tag => $"\"{tag}\""))}; they are read where they start.");
        }
    }

    /// <summary>
    /// Whether a table's recognizable start is at an offset; <see langword="null"/> when the table has no recognizable start or a
    /// table it is checked against is not where it should be.
    /// </summary>
    private static bool? StartsAt(ReadOnlySpan<byte> span, List<Table> tables, uint tag, int offset)
    {
        Table table = tables.Find(entry => entry.Tag == tag);
        int length = Math.Min(table.Length, span.Length - Math.Max(0, offset));
        if (offset < 0 || length < 4)
        {
            return false;
        }

        ReadOnlySpan<byte> data = span.Slice(offset, length);
        uint version = BinaryPrimitives.ReadUInt32BigEndian(data);
        ushort first = BinaryPrimitives.ReadUInt16BigEndian(data);
        switch (tag)
        {
            case HeadTag:
                return length >= 54 && BinaryPrimitives.ReadUInt32BigEndian(data[12..]) == 0x5F0F3CF5;
            case MaxpTag:
                return version is 0x00005000 or 0x00010000;
            case HheaTag:
                return length >= 36 && version == 0x00010000;
            case PostTag:
                return version is 0x00010000 or 0x00020000 or 0x00025000 or 0x00030000 or 0x00040000;
            case CmapTag:
                ushort subtables = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
                return first == 0 && subtables > 0 && 4 + (8 * subtables) <= length;
            case Os2Tag:
                return length >= 8 && first <= 5 && BinaryPrimitives.ReadUInt16BigEndian(data[4..]) is >= 1 and <= 1000
                    && BinaryPrimitives.ReadUInt16BigEndian(data[6..]) is >= 1 and <= 9;
            case NameTag:
                return length >= 6 && first <= 1 && 6 + (12 * BinaryPrimitives.ReadUInt16BigEndian(data[2..])) <= length;
            case LocaTag:
                return Located(span, tables, HeadTag, out ReadOnlySpan<byte> head) && Find(tables, GlyfTag) is { } glyfTable
                    ? LocaAscends(data, BinaryPrimitives.ReadInt16BigEndian(head[50..]) == 1, glyfTable.Length)
                    : null;
            case HmtxTag:
                if (!Located(span, tables, HheaTag, out ReadOnlySpan<byte> hhea))
                {
                    return null;
                }

                int maximum = BinaryPrimitives.ReadUInt16BigEndian(hhea[10..]);
                int minimumBearing = BinaryPrimitives.ReadInt16BigEndian(hhea[12..]);
                int metrics = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(hhea[34..]), length / 4);
                for (int index = 0; index < metrics; index++)
                {
                    if (BinaryPrimitives.ReadUInt16BigEndian(data[(4 * index)..]) > maximum
                        || BinaryPrimitives.ReadInt16BigEndian(data[((4 * index) + 2)..]) < minimumBearing)
                    {
                        return false;
                    }
                }

                return metrics > 0;
            case GlyfTag:
                return Located(span, tables, LocaTag, out ReadOnlySpan<byte> loca) && Located(span, tables, HeadTag, out ReadOnlySpan<byte> header)
                    ? FirstGlyphPlausible(data, loca, BinaryPrimitives.ReadInt16BigEndian(header[50..]) == 1)
                    : null;
            default:
                return null;
        }
    }

    /// <summary>Gets a table's bytes when its recognizable start is where the (possibly moved) directory entry says.</summary>
    private static bool Located(ReadOnlySpan<byte> span, List<Table> tables, uint tag, out ReadOnlySpan<byte> data)
    {
        data = default;
        if (Find(tables, tag) is not { } table || StartsAt(span, tables, tag, table.Offset) != true)
        {
            return false;
        }

        data = span.Slice(table.Offset, table.Length);
        return true;
    }

    private static Table? Find(List<Table> tables, uint tag)
    {
        int index = tables.FindIndex(table => table.Tag == tag);
        return index < 0 ? null : tables[index];
    }

    /// <summary>Whether "loca" starts at 0 and ascends to no more than the "glyf" length.</summary>
    private static bool LocaAscends(ReadOnlySpan<byte> loca, bool longOffsets, int glyfLength)
    {
        int size = longOffsets ? 4 : 2;
        int count = loca.Length / size;
        long previous = 0;
        for (int index = 0; index < count; index++)
        {
            long value = longOffsets ? BinaryPrimitives.ReadUInt32BigEndian(loca[(index * size)..]) : BinaryPrimitives.ReadUInt16BigEndian(loca[(index * size)..]) * 2L;
            if ((index == 0 && value != 0) || value < previous || value > glyfLength)
            {
                return false;
            }

            previous = value;
        }

        return count >= 2;
    }

    /// <summary>
    /// Whether the first glyph with data has a plausible header: numberOfContours of −1 or more, an ordered box, and for a simple glyph
    /// ascending contour end points and instructions that fit the glyph.
    /// </summary>
    private static bool FirstGlyphPlausible(ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, bool longOffsets)
    {
        int size = longOffsets ? 4 : 2;
        for (int index = 0; index + 1 < loca.Length / size; index++)
        {
            long start = longOffsets ? BinaryPrimitives.ReadUInt32BigEndian(loca[(index * size)..]) : BinaryPrimitives.ReadUInt16BigEndian(loca[(index * size)..]) * 2L;
            long end = longOffsets ? BinaryPrimitives.ReadUInt32BigEndian(loca[((index + 1) * size)..]) : BinaryPrimitives.ReadUInt16BigEndian(loca[((index + 1) * size)..]) * 2L;
            if (end <= start)
            {
                continue;
            }

            if (end > glyf.Length || end - start < 10)
            {
                return false;
            }

            ReadOnlySpan<byte> header = glyf[(int)start..(int)end];
            short contours = BinaryPrimitives.ReadInt16BigEndian(header);
            short xMin = BinaryPrimitives.ReadInt16BigEndian(header[2..]);
            short yMin = BinaryPrimitives.ReadInt16BigEndian(header[4..]);
            short xMax = BinaryPrimitives.ReadInt16BigEndian(header[6..]);
            short yMax = BinaryPrimitives.ReadInt16BigEndian(header[8..]);
            if (contours < -1 || xMin > xMax || yMin > yMax)
            {
                return false;
            }

            if (contours <= 0)
            {
                return true;
            }

            int instructions = 10 + (2 * contours);
            if (instructions + 2 > header.Length)
            {
                return false;
            }

            int previousEnd = -1;
            for (int contour = 0; contour < contours; contour++)
            {
                int endPoint = BinaryPrimitives.ReadUInt16BigEndian(header[(10 + (2 * contour))..]);
                if (endPoint <= previousEnd)
                {
                    return false;
                }

                previousEnd = endPoint;
            }

            return instructions + 2 + BinaryPrimitives.ReadUInt16BigEndian(header[instructions..]) <= header.Length;
        }

        return false;
    }

    /// <summary>A tag as text, for messages.</summary>
    public static string TagText(uint tag)
    {
        Span<char> text = stackalloc char[4];
        for (int index = 0; index < 4; index++)
        {
            byte value = (byte)(tag >> (24 - (8 * index)));
            text[index] = value is >= 0x20 and < 0x7F ? (char)value : '?';
        }

        return new string(text);
    }

    private readonly record struct Table(uint Tag, int Offset, int Length);
}
