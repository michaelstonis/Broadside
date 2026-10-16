using System.Buffers.Binary;

namespace Broadside.Fonts.OpenType;

/// <summary>
/// Bounds-checked big-endian reads and the common table formats of the OpenType layout tables (OpenType specification, "OpenType
/// Layout Common Table Formats": Coverage and Class Definition tables). A read past the end gives 0, so a damaged offset finds no
/// coverage and no class instead of failing; nothing allocates.
/// </summary>
internal static class LayoutData
{
    /// <summary>The unsigned 16-bit value at <paramref name="offset"/>, or 0 past the end.</summary>
    public static int U16(ReadOnlySpan<byte> data, int offset) =>
        offset >= 0 && offset <= data.Length - 2 ? BinaryPrimitives.ReadUInt16BigEndian(data[offset..]) : 0;

    /// <summary>The signed 16-bit value at <paramref name="offset"/>, or 0 past the end.</summary>
    public static int I16(ReadOnlySpan<byte> data, int offset) =>
        offset >= 0 && offset <= data.Length - 2 ? BinaryPrimitives.ReadInt16BigEndian(data[offset..]) : 0;

    /// <summary>The unsigned 32-bit value at <paramref name="offset"/>, or 0 past the end.</summary>
    public static uint U32(ReadOnlySpan<byte> data, int offset) =>
        offset >= 0 && offset <= data.Length - 4 ? BinaryPrimitives.ReadUInt32BigEndian(data[offset..]) : 0;

    /// <summary>
    /// The absolute position of a 16-bit offset stored at <paramref name="at"/> relative to <paramref name="baseOffset"/>, or -1
    /// when it is null (0) or points past the end.
    /// </summary>
    public static int Offset16(ReadOnlySpan<byte> data, int baseOffset, int at)
    {
        int relative = U16(data, at);
        int absolute = baseOffset + relative;
        return relative == 0 || absolute >= data.Length ? -1 : absolute;
    }

    /// <summary>The coverage index of <paramref name="glyph"/> in the Coverage table at <paramref name="offset"/>, or -1 when it is not covered.</summary>
    public static int CoverageIndex(ReadOnlySpan<byte> data, int offset, int glyph)
    {
        if (offset < 0)
        {
            return -1;
        }

        int format = U16(data, offset);
        int count = Math.Min(U16(data, offset + 2), Math.Max(0, (data.Length - offset - 4) / (format == 1 ? 2 : 6)));
        int low = 0;
        int high = count - 1;
        if (format == 1)
        {
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                int value = U16(data, offset + 4 + (middle * 2));
                if (value == glyph)
                {
                    return middle;
                }

                (low, high) = value < glyph ? (middle + 1, high) : (low, middle - 1);
            }
        }
        else if (format == 2)
        {
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                int record = offset + 4 + (middle * 6);
                int start = U16(data, record);
                int end = U16(data, record + 2);
                if (glyph < start)
                {
                    high = middle - 1;
                }
                else if (glyph > end)
                {
                    low = middle + 1;
                }
                else
                {
                    return U16(data, record + 4) + glyph - start;
                }
            }
        }

        return -1;
    }

    /// <summary>The class of <paramref name="glyph"/> in the Class Definition table at <paramref name="offset"/>; 0 when it is not listed or the table is absent.</summary>
    public static int ClassOf(ReadOnlySpan<byte> data, int offset, int glyph)
    {
        if (offset < 0)
        {
            return 0;
        }

        int format = U16(data, offset);
        if (format == 1)
        {
            int start = U16(data, offset + 2);
            int count = U16(data, offset + 4);
            int index = glyph - start;
            return index >= 0 && index < count ? U16(data, offset + 6 + (index * 2)) : 0;
        }

        if (format == 2)
        {
            int low = 0;
            int high = Math.Min(U16(data, offset + 2), Math.Max(0, (data.Length - offset - 4) / 6)) - 1;
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                int record = offset + 4 + (middle * 6);
                if (glyph < U16(data, record))
                {
                    high = middle - 1;
                }
                else if (glyph > U16(data, record + 2))
                {
                    low = middle + 1;
                }
                else
                {
                    return U16(data, record + 4);
                }
            }
        }

        return 0;
    }
}
