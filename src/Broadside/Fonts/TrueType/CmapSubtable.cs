using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.TrueType;

/// <summary>One "cmap" subtable, looked up in place over the program's bytes: nothing is copied into a dictionary.</summary>
/// <remarks>
/// OpenType specification, "cmap — Character to glyph index mapping table": formats 0, 2, 4, 6, 12 and 13. Format 14 (variation
/// sequences) has no use in PDF text and is skipped; formats 8 and 10 are reported as unsupported. A glyph id at or past the
/// program's glyph count reads as 0 with a diagnostic. ISO 32000-2 §9.6.5.4 chooses among the subtables.
/// </remarks>
internal sealed class CmapSubtable : FontCharacterMap
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _glyphCount;
    private readonly FontProgramContext _context;
    private readonly bool _sorted;

    private CmapSubtable(int platformId, int encodingId, int format, ReadOnlyMemory<byte> data, int glyphCount, FontProgramContext context, bool sorted)
    {
        PlatformId = platformId;
        EncodingId = encodingId;
        Format = format;
        _data = data;
        _glyphCount = glyphCount;
        _context = context;
        _sorted = sorted;
    }

    /// <inheritdoc/>
    public override int PlatformId { get; }

    /// <inheritdoc/>
    public override int EncodingId { get; }

    /// <inheritdoc/>
    public override int Format { get; }

    /// <summary>Reads the subtable records of a "cmap" table; reports what it cannot use.</summary>
    /// <param name="cmap">The table.</param>
    /// <param name="glyphCount">The program's glyph count.</param>
    /// <param name="context">The diagnostics sink.</param>
    /// <returns>The usable subtables, in the order the table lists them.</returns>
    public static CmapSubtable[] ReadAll(ReadOnlyMemory<byte> cmap, int glyphCount, FontProgramContext context)
    {
        ReadOnlySpan<byte> span = cmap.Span;
        if (span.Length < 4)
        {
            Report(context, "the table is shorter than its header; it is ignored");
            return [];
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
        var subtables = new List<CmapSubtable>(count);
        for (int index = 0; index < count; index++)
        {
            int record = 4 + (8 * index);
            if (record + 8 > span.Length)
            {
                Report(context, "the table lists more subtables than it holds records for; the others are ignored");
                break;
            }

            int platform = BinaryPrimitives.ReadUInt16BigEndian(span[record..]);
            int encoding = BinaryPrimitives.ReadUInt16BigEndian(span[(record + 2)..]);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(span[(record + 4)..]);
            if (offset > (uint)(span.Length - 2))
            {
                Report(context, string.Create(CultureInfo.InvariantCulture, $"the ({platform}, {encoding}) subtable starts past the end of the table; it is ignored"));
                continue;
            }

            if (Read(cmap, (int)offset, platform, encoding, glyphCount, context) is { } subtable)
            {
                subtables.Add(subtable);
            }
        }

        return [.. subtables];
    }

    /// <inheritdoc/>
    public override int GetGlyphId(int code)
    {
        if (code < 0)
        {
            return 0;
        }

        ReadOnlySpan<byte> data = _data.Span;
        int glyph = Format switch
        {
            0 => code < 256 && 6 + code < data.Length ? data[6 + code] : 0,
            2 => LookUpFormat2(data, code),
            4 => LookUpFormat4(data, code),
            6 => LookUpFormat6(data, code),
            12 or 13 => LookUpGroups(data, (uint)code),
            _ => 0,
        };

        if (glyph >= _glyphCount)
        {
            _context.Report(
                DiagnosticCodes.FontCmapInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The ({PlatformId}, {EncodingId}) \"cmap\" subtable maps a code to glyph {glyph}, past the program's {_glyphCount} glyphs; the missing glyph is used."));
            return 0;
        }

        return glyph;
    }

    private static CmapSubtable? Read(ReadOnlyMemory<byte> cmap, int offset, int platform, int encoding, int glyphCount, FontProgramContext context)
    {
        ReadOnlySpan<byte> span = cmap.Span[offset..];
        int format = BinaryPrimitives.ReadUInt16BigEndian(span);
        long length;
        switch (format)
        {
            case 0 or 2 or 4 or 6:
                length = span.Length >= 4 ? BinaryPrimitives.ReadUInt16BigEndian(span[2..]) : 0;
                break;
            case 12 or 13:
                length = span.Length >= 8 ? BinaryPrimitives.ReadUInt32BigEndian(span[4..]) : 0;
                break;
            case 14:
                return null;
            case 8 or 10:
                Report(context, string.Create(CultureInfo.InvariantCulture, $"the ({platform}, {encoding}) subtable has format {format}, which is not supported; it is ignored"));
                return null;
            default:
                Report(context, string.Create(CultureInfo.InvariantCulture, $"the ({platform}, {encoding}) subtable has the unknown format {format}; it is ignored"));
                return null;
        }

        if (length > span.Length || length < MinimumLength(format))
        {
            Report(context, string.Create(CultureInfo.InvariantCulture, $"the length of the ({platform}, {encoding}) format {format} subtable does not fit the table; the bytes present are read"));
            length = span.Length;
        }

        ReadOnlyMemory<byte> data = cmap.Slice(offset, (int)length);
        if (data.Length < MinimumLength(format))
        {
            Report(context, string.Create(CultureInfo.InvariantCulture, $"the ({platform}, {encoding}) format {format} subtable is truncated; it is ignored"));
            return null;
        }

        bool sorted = format switch
        {
            4 => CheckFormat4(data.Span, platform, encoding, context),
            12 or 13 => CheckGroups(data.Span, platform, encoding, context),
            _ => true,
        };
        return new CmapSubtable(platform, encoding, format, data, glyphCount, context, sorted);
    }

    private static int MinimumLength(int format) => format switch
    {
        0 => 6,
        2 => 6 + 512,
        4 => 16,
        6 => 10,
        _ => 16,
    };

    /// <summary>Format 2 (high-byte mapping): a byte whose subheader key is 0 is a single-byte code; others start two-byte codes.</summary>
    private static int LookUpFormat2(ReadOnlySpan<byte> data, int code)
    {
        int high = code > 0xFF ? (code >> 8) & 0xFF : code;
        int low = code > 0xFF ? code & 0xFF : code;
        int key = BinaryPrimitives.ReadUInt16BigEndian(data[(6 + (2 * high))..]);
        if (code <= 0xFF && key != 0)
        {
            return 0;
        }

        if (code > 0xFFFF || (code > 0xFF && key == 0))
        {
            return 0;
        }

        int subHeader = 6 + 512 + key;
        if (subHeader + 8 > data.Length)
        {
            return 0;
        }

        int firstCode = BinaryPrimitives.ReadUInt16BigEndian(data[subHeader..]);
        int entryCount = BinaryPrimitives.ReadUInt16BigEndian(data[(subHeader + 2)..]);
        short delta = BinaryPrimitives.ReadInt16BigEndian(data[(subHeader + 4)..]);
        int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(data[(subHeader + 6)..]);
        if (low < firstCode || low >= firstCode + entryCount)
        {
            return 0;
        }

        int address = subHeader + 6 + rangeOffset + (2 * (low - firstCode));
        if (address + 2 > data.Length)
        {
            return 0;
        }

        int glyph = BinaryPrimitives.ReadUInt16BigEndian(data[address..]);
        return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
    }

    /// <summary>Format 4 (segment mapping to delta values): the Windows Unicode BMP and Symbol subtables.</summary>
    private int LookUpFormat4(ReadOnlySpan<byte> data, int code)
    {
        if (code > 0xFFFF)
        {
            return 0;
        }

        int segments = Format4Segments(data);
        int ends = 14;
        int starts = ends + (2 * segments) + 2;
        int deltas = starts + (2 * segments);
        int rangeOffsets = deltas + (2 * segments);
        int segment = -1;
        if (_sorted)
        {
            int low = 0;
            int high = segments - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                if (BinaryPrimitives.ReadUInt16BigEndian(data[(ends + (2 * middle))..]) < code)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            segment = low < segments && BinaryPrimitives.ReadUInt16BigEndian(data[(starts + (2 * low))..]) <= code ? low : -1;
        }
        else
        {
            for (int index = 0; index < segments && segment < 0; index++)
            {
                if (BinaryPrimitives.ReadUInt16BigEndian(data[(starts + (2 * index))..]) <= code
                    && BinaryPrimitives.ReadUInt16BigEndian(data[(ends + (2 * index))..]) >= code)
                {
                    segment = index;
                }
            }
        }

        if (segment < 0)
        {
            return 0;
        }

        int start = BinaryPrimitives.ReadUInt16BigEndian(data[(starts + (2 * segment))..]);
        int delta = BinaryPrimitives.ReadUInt16BigEndian(data[(deltas + (2 * segment))..]);
        int rangeOffsetPosition = rangeOffsets + (2 * segment);
        int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(data[rangeOffsetPosition..]);
        if (rangeOffset == 0)
        {
            return (code + delta) & 0xFFFF;
        }

        // idRangeOffset counts bytes from its own position; a glyphIdArray value of 0 stays 0 (the missing glyph).
        int address = rangeOffsetPosition + rangeOffset + (2 * (code - start));
        if (address + 2 > data.Length)
        {
            _context.Report(
                DiagnosticCodes.FontCmapInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"A segment of the ({PlatformId}, {EncodingId}) format 4 \"cmap\" subtable points past its glyph id array; its codes map to the missing glyph."));
            return 0;
        }

        int glyph = BinaryPrimitives.ReadUInt16BigEndian(data[address..]);
        return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
    }

    private static int Format4Segments(ReadOnlySpan<byte> data) =>
        Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data[6..]) / 2, Math.Max(0, (data.Length - 16) / 8));

    /// <summary>Format 6 (trimmed table): a dense array of glyph ids for a range of codes.</summary>
    private static int LookUpFormat6(ReadOnlySpan<byte> data, int code)
    {
        int first = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        int count = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        int index = code - first;
        int address = 10 + (2 * index);
        return index >= 0 && index < count && address + 2 <= data.Length ? BinaryPrimitives.ReadUInt16BigEndian(data[address..]) : 0;
    }

    /// <summary>Formats 12 (segmented coverage) and 13 (many-to-one range mappings): sequential groups of 32-bit codes.</summary>
    private int LookUpGroups(ReadOnlySpan<byte> data, uint code)
    {
        int groups = GroupCount(data);
        int found = -1;
        if (_sorted)
        {
            int low = 0;
            int high = groups - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                int group = 16 + (12 * middle);
                if (BinaryPrimitives.ReadUInt32BigEndian(data[(group + 4)..]) < code)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            found = low < groups && BinaryPrimitives.ReadUInt32BigEndian(data[(16 + (12 * low))..]) <= code ? low : -1;
        }
        else
        {
            for (int index = 0; index < groups && found < 0; index++)
            {
                int group = 16 + (12 * index);
                if (BinaryPrimitives.ReadUInt32BigEndian(data[group..]) <= code && BinaryPrimitives.ReadUInt32BigEndian(data[(group + 4)..]) >= code)
                {
                    found = index;
                }
            }
        }

        if (found < 0)
        {
            return 0;
        }

        int offset = 16 + (12 * found);
        uint start = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        uint glyph = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8)..]);
        ulong result = Format == 12 ? glyph + (ulong)(code - start) : glyph;
        return result > int.MaxValue ? int.MaxValue : (int)result;
    }

    private static int GroupCount(ReadOnlySpan<byte> data) =>
        (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data[12..]), (uint)((data.Length - 16) / 12));

    /// <summary>Format 4 needs ascending end codes for a binary search; when they are not, lookups scan and the first match wins.</summary>
    private static bool CheckFormat4(ReadOnlySpan<byte> data, int platform, int encoding, FontProgramContext context)
    {
        int declared = BinaryPrimitives.ReadUInt16BigEndian(data[6..]) / 2;
        int segments = Format4Segments(data);
        if (segments < declared)
        {
            Report(context, string.Create(CultureInfo.InvariantCulture, $"the ({platform}, {encoding}) format 4 subtable declares {declared} segments but holds {segments}; those are read"));
        }

        int previous = -1;
        for (int index = 0; index < segments; index++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(data[(14 + (2 * index))..]);
            int start = BinaryPrimitives.ReadUInt16BigEndian(data[(16 + (2 * segments) + (2 * index))..]);
            if (end <= previous || start > end)
            {
                Report(context, string.Create(CultureInfo.InvariantCulture, $"the segments of the ({platform}, {encoding}) format 4 subtable are not in ascending order; it is searched in order"));
                return false;
            }

            previous = end;
        }

        return true;
    }

    /// <summary>Formats 12 and 13 need ascending, non-overlapping groups for a binary search; otherwise lookups scan.</summary>
    private static bool CheckGroups(ReadOnlySpan<byte> data, int platform, int encoding, FontProgramContext context)
    {
        uint declared = BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        int groups = GroupCount(data);
        if (groups < declared)
        {
            Report(context, string.Create(CultureInfo.InvariantCulture, $"the ({platform}, {encoding}) subtable declares {declared} groups but holds {groups}; those are read"));
        }

        long previous = -1;
        for (int index = 0; index < groups; index++)
        {
            int group = 16 + (12 * index);
            uint start = BinaryPrimitives.ReadUInt32BigEndian(data[group..]);
            uint end = BinaryPrimitives.ReadUInt32BigEndian(data[(group + 4)..]);
            if (start <= previous || end < start)
            {
                Report(context, string.Create(CultureInfo.InvariantCulture, $"the groups of the ({platform}, {encoding}) subtable are not in ascending order; it is searched in order"));
                return false;
            }

            previous = end;
        }

        return true;
    }

    private static void Report(FontProgramContext context, string problem) =>
        context.Report(DiagnosticCodes.FontCmapInvalid, DiagnosticSeverity.Warning, $"The font program's \"cmap\" table deviates from the OpenType specification: {problem}.");
}
