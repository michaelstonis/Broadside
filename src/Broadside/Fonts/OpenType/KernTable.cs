using static Broadside.Fonts.OpenType.LayoutData;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// A font's legacy <c>kern</c> table: the horizontal format 0 subtables (sorted glyph pairs and their kerning values) of the
/// Microsoft (version 0) or Apple (version 1.0) layout. Pairs are found by binary search in place.
/// </summary>
/// <remarks>
/// OpenType specification, "kern — Kerning" (format 0; cross-stream, minimum-value and variation subtables and formats 1–3 are
/// skipped) and Apple TrueType Reference Manual, "kern". The table applies only when the font's <c>GPOS</c> table has no
/// <c>kern</c> feature. A subtable whose length runs past the table is cut to what the table holds.
/// </remarks>
internal sealed class KernTable
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly (int Pairs, int Count, bool Override)[] _subtables;

    private KernTable(ReadOnlyMemory<byte> data, (int Pairs, int Count, bool Override)[] subtables)
    {
        _data = data;
        _subtables = subtables;
    }

    /// <summary>Reads the table, or returns <see langword="null"/> when it has no horizontal format 0 subtable.</summary>
    public static KernTable? Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        bool apple = U32(span, 0) == 0x00010000;
        int count = apple ? (int)Math.Min(U32(span, 4), int.MaxValue) : U16(span, 2);
        int offset = apple ? 8 : 4;
        var subtables = new List<(int, int, bool)>();
        for (int index = 0; index < count && offset + (apple ? 8 : 6) <= span.Length; index++)
        {
            long length = apple ? U32(span, offset) : U16(span, offset + 2);
            int coverage = U16(span, offset + 4);
            int header = apple ? 8 : 6;
            bool horizontal;
            int format;
            bool replaces;
            if (apple)
            {
                // Apple: bit 15 vertical, 14 cross-stream, 13 variation; the low byte is the format.
                horizontal = (coverage & 0xE000) == 0;
                format = coverage & 0xFF;
                replaces = false;
            }
            else
            {
                // Microsoft: bit 0 horizontal, 1 minimum, 2 cross-stream, 3 override; the high byte is the format.
                horizontal = (coverage & 0x0007) == 0x0001 && U16(span, offset) == 0;
                format = coverage >> 8;
                replaces = (coverage & 0x0008) != 0;
            }

            int pairs = offset + header + 8;
            if (horizontal && format == 0 && pairs <= span.Length)
            {
                long end = index == count - 1 ? span.Length : Math.Min(offset + length, span.Length);
                int available = (int)((end - pairs) / 6);
                subtables.Add((pairs, Math.Clamp(U16(span, offset + header), 0, Math.Max(0, available)), replaces));
            }

            // A Microsoft subtable's 16-bit length overflows for large format 0 subtables: the last one runs to the end of the table.
            if (length < header)
            {
                break;
            }

            offset = (int)Math.Min(offset + length, span.Length);
        }

        return subtables.Count == 0 ? null : new KernTable(data, [.. subtables]);
    }

    /// <summary>The kerning between two glyphs, in font design units (negative brings them closer); 0 when no subtable lists the pair.</summary>
    public int Pair(int left, int right)
    {
        ReadOnlySpan<byte> span = _data.Span;
        uint key = ((uint)left << 16) | (uint)right;
        int value = 0;
        foreach ((int pairs, int count, bool replaces) in _subtables)
        {
            int low = 0;
            int high = count - 1;
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                int at = pairs + (middle * 6);
                uint candidate = U32(span, at);
                if (candidate == key)
                {
                    value = replaces ? I16(span, at + 4) : value + I16(span, at + 4);
                    break;
                }

                (low, high) = candidate < key ? (middle + 1, high) : (low, middle - 1);
            }
        }

        return value;
    }
}
