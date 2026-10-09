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

        return new SfntFile(data, version, [.. tables]);
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
