namespace Broadside.Fonts;

/// <summary>
/// One codespace range of a CMap: codes of <see cref="Length"/> bytes whose every byte lies between the corresponding bytes of
/// <see cref="Low"/> and <see cref="High"/> (big-endian packed).
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.7.6.2; Adobe TN 5014 §5.2 and Figure 6: a range is multi-dimensional, so <c>&lt;8210&gt;</c> is not in
/// <c>&lt;8140&gt; &lt;9FFC&gt;</c> although it lies between them as a number.
/// </remarks>
internal readonly record struct CodespaceRange(int Length, uint Low, uint High)
{
    /// <summary>Returns whether a code of this range's length lies in the range, byte by byte.</summary>
    public bool Contains(uint code)
    {
        for (int index = 0; index < Length; index++)
        {
            int shift = 8 * index;
            uint value = (code >> shift) & 0xFF;
            if (value < ((Low >> shift) & 0xFF) || value > ((High >> shift) & 0xFF))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns whether the byte at position <paramref name="index"/> (0 = first) of a code may be <paramref name="value"/> in this range.</summary>
    public bool Admits(int index, byte value)
    {
        int shift = 8 * (Length - 1 - index);
        return value >= ((Low >> shift) & 0xFF) && value <= ((High >> shift) & 0xFF);
    }
}
