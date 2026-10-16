namespace Broadside.Fonts.OpenType;

/// <summary>Lookups over the generated mirroring table (<c>BidiMirroringData.g.cs</c>).</summary>
internal static partial class BidiMirroringData
{
    /// <summary>The Bidi_Mirroring_Glyph of <paramref name="codePoint"/>, or -1 when it has none (UAX #9 rule L4).</summary>
    public static int Mirror(int codePoint)
    {
        if (codePoint > ushort.MaxValue)
        {
            return -1;
        }

        int index = Find(Codes, codePoint);
        return index < 0 ? -1 : (Mirrors[index * 2] << 8) | Mirrors[(index * 2) + 1];
    }

    /// <summary>The index of <paramref name="value"/> in the ascending <paramref name="codes"/>, or -1.</summary>
    private static int Find(ReadOnlySpan<byte> codes, int value)
    {
        int low = 0;
        int high = (codes.Length / 2) - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            int code = (codes[middle * 2] << 8) | codes[(middle * 2) + 1];
            if (code == value)
            {
                return middle;
            }

            if (code < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }
}
