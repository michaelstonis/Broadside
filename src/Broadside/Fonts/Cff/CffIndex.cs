using System.Buffers.Binary;

namespace Broadside.Fonts.Cff;

/// <summary>
/// An INDEX of a CFF program: where its objects are. Offsets are read on demand from the program's bytes, so an INDEX costs no
/// allocation; damaged offsets are clamped into the data when read.
/// </summary>
/// <remarks>
/// Adobe Technical Note #5176 §5, Table 7 (p.12-13): <c>Card16 count</c>; when not 0, <c>OffSize offSize</c>, <c>count + 1</c>
/// offsets of <c>offSize</c> bytes relative to the byte before the object data (so the first is 1), then the data. An empty INDEX is
/// the two bytes of its count.
/// </remarks>
internal readonly struct CffIndex
{
    private readonly int _offsets;
    private readonly int _offSize;
    private readonly int _dataBase;
    private readonly int _maxOffset;

    private CffIndex(int count, int offsets, int offSize, int dataBase, int maxOffset, int end)
    {
        Count = count;
        _offsets = offsets;
        _offSize = offSize;
        _dataBase = dataBase;
        _maxOffset = maxOffset;
        End = end;
    }

    /// <summary>Gets the number of objects.</summary>
    public int Count { get; }

    /// <summary>Gets the position just past the INDEX.</summary>
    public int End { get; }

    /// <summary>Reads the INDEX at an offset; a damaged one is cut down to what is readable, and <paramref name="problem"/> says why.</summary>
    /// <param name="data">The CFF program.</param>
    /// <param name="offset">The INDEX's offset.</param>
    /// <param name="problem">A description of the damage, or <see langword="null"/>.</param>
    /// <returns>The INDEX; empty (at the end of the data) when not even its header is readable.</returns>
    public static CffIndex Read(ReadOnlySpan<byte> data, int offset, out string? problem)
    {
        problem = null;
        if (offset < 0 || offset > data.Length - 2)
        {
            problem = "lies outside the program";
            return new CffIndex(0, 0, 1, 0, 0, data.Length);
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        if (count == 0)
        {
            return new CffIndex(0, 0, 1, 0, 0, offset + 2);
        }

        if (offset + 3 > data.Length)
        {
            problem = "is cut short after its count";
            return new CffIndex(0, 0, 1, 0, 0, data.Length);
        }

        int offSize = data[offset + 2];
        if (offSize is < 1 or > 4)
        {
            problem = $"has offSize {offSize}, not 1 to 4";
            return new CffIndex(0, 0, 1, 0, 0, data.Length);
        }

        int offsets = offset + 3;
        long fitting = ((long)(data.Length - offsets) / offSize) - 1;
        if (count > fitting)
        {
            problem = "has more offsets than the program holds";
            count = (int)Math.Max(0, fitting);
            if (count == 0)
            {
                return new CffIndex(0, 0, 1, 0, 0, data.Length);
            }
        }

        int dataBase = offsets + ((count + 1) * offSize) - 1;
        int maxOffset = data.Length - dataBase;
        var index = new CffIndex(count, offsets, offSize, dataBase, maxOffset, 0);
        uint first = index.ReadOffset(data, 0);
        if (first != 1)
        {
            problem ??= "does not start its offsets at 1";
        }

        uint previous = first;
        for (int item = 1; item <= count; item++)
        {
            uint current = index.ReadOffset(data, item);
            if (current < previous || current > (uint)maxOffset)
            {
                problem ??= "has offsets that decrease or point past the program";
            }

            previous = current;
        }

        uint last = index.ReadOffset(data, count);
        int end = dataBase + (int)Math.Clamp(last, 1u, (uint)maxOffset);
        return new CffIndex(count, offsets, offSize, dataBase, maxOffset, end);
    }

    /// <summary>Gets an object's bytes, its offsets clamped into the data (empty when they cross).</summary>
    /// <param name="data">The CFF program the INDEX was read from.</param>
    /// <param name="item">The object's index, 0 to <see cref="Count"/> − 1.</param>
    /// <returns>The object.</returns>
    public ReadOnlySpan<byte> Get(ReadOnlySpan<byte> data, int item)
    {
        if ((uint)item >= (uint)Count)
        {
            return default;
        }

        uint start = Math.Clamp(ReadOffset(data, item), 1u, (uint)_maxOffset);
        uint end = Math.Clamp(ReadOffset(data, item + 1), 1u, (uint)_maxOffset);
        return end <= start ? default : data.Slice(_dataBase + (int)start, (int)(end - start));
    }

    private uint ReadOffset(ReadOnlySpan<byte> data, int item)
    {
        ReadOnlySpan<byte> bytes = data.Slice(_offsets + (item * _offSize), _offSize);
        uint value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        return value;
    }
}
