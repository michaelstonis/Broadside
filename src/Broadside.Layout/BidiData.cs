namespace Broadside.Layout.Flow;

/// <summary>Lookups over the generated bidi tables (<c>BidiData.g.cs</c>).</summary>
internal static partial class BidiData
{
    /// <summary>Bidi_Paired_Bracket_Type Open.</summary>
    public const int OpenBracket = 1;

    /// <summary>Bidi_Paired_Bracket_Type Close.</summary>
    public const int CloseBracket = 2;

    /// <summary>
    /// The Bidi_Paired_Bracket_Type of <paramref name="character"/> (0 None, <see cref="OpenBracket"/>, <see cref="CloseBracket"/>)
    /// and its Bidi_Paired_Bracket.
    /// </summary>
    public static int Bracket(char character, out int paired)
    {
        int index = Find(BracketCodes, character);
        if (index < 0)
        {
            paired = 0;
            return 0;
        }

        paired = (BracketPairs[index * 2] << 8) | BracketPairs[(index * 2) + 1];
        return BracketTypes[index];
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
