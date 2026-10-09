using System.Buffers;
using System.Globalization;
using System.Text;

namespace Broadside.Fonts;

/// <summary>Glyph name to Unicode, through the Adobe Glyph List, the ITC Zapf Dingbats Glyph List and the <c>uniXXXX</c>/<c>uXXXX</c> forms.</summary>
/// <remarks>
/// The data is in <c>AdobeGlyphList.g.cs</c>. The lookups allocate nothing. The full AGL specification algorithm (components joined by
/// underscores, suffixes after a period) is not applied here; a name either is in a list or has one of the two numeric forms.
/// </remarks>
internal static partial class AdobeGlyphList
{
    /// <summary>Looks up a glyph name's Unicode value.</summary>
    /// <param name="name">The glyph name.</param>
    /// <param name="zapfDingbats">Whether to look in the ITC Zapf Dingbats list (names <c>a1</c> to <c>a191</c>) first.</param>
    /// <param name="value">The Unicode value: one or more UTF-16 code units.</param>
    /// <returns><see langword="true"/> when the name has a value.</returns>
    public static bool TryGetUnicode(ReadOnlySpan<char> name, bool zapfDingbats, out ReadOnlySpan<char> value)
    {
        if (zapfDingbats && TryFind(name, ZapfNames, ZapfNameOffsets, ZapfValues, ZapfValueOffsets, ZapfCount, out value))
        {
            return true;
        }

        if (TryFind(name, AglNames, AglNameOffsets, AglValues, AglValueOffsets, AglCount, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Looks up a glyph name's single Unicode scalar value: from a list, or from a <c>uniXXXX</c> or <c>uXXXX</c>–<c>uXXXXXX</c> name.</summary>
    /// <param name="name">The glyph name.</param>
    /// <param name="zapfDingbats">Whether to look in the ITC Zapf Dingbats list first.</param>
    /// <param name="scalar">The scalar value.</param>
    /// <returns><see langword="true"/> when the name stands for exactly one scalar value.</returns>
    public static bool TryGetScalar(ReadOnlySpan<char> name, bool zapfDingbats, out int scalar)
    {
        scalar = 0;
        if (TryGetUnicode(name, zapfDingbats, out ReadOnlySpan<char> value))
        {
            if (Rune.DecodeFromUtf16(value, out Rune rune, out int consumed) != OperationStatus.Done || consumed != value.Length)
            {
                return false;
            }

            scalar = rune.Value;
            return true;
        }

        // "uni" followed by exactly four uppercase hexadecimal digits, not a surrogate; "u" followed by four to six.
        if (name.Length == 7 && name.StartsWith("uni", StringComparison.Ordinal))
        {
            return TryParseHex(name[3..], out scalar) && !IsSurrogate(scalar);
        }

        if (name.Length is >= 5 and <= 7 && name[0] == 'u')
        {
            return TryParseHex(name[1..], out scalar) && !IsSurrogate(scalar) && scalar <= 0x10FFFF;
        }

        return false;
    }

    private static bool IsSurrogate(int value) => value is >= 0xD800 and <= 0xDFFF;

    private static bool TryParseHex(ReadOnlySpan<char> digits, out int value)
    {
        foreach (char digit in digits)
        {
            if (!char.IsAsciiHexDigitUpper(digit) && !char.IsAsciiDigit(digit))
            {
                value = 0;
                return false;
            }
        }

        return int.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryFind(
        ReadOnlySpan<char> name,
        string names,
        ReadOnlySpan<ushort> nameOffsets,
        string values,
        ReadOnlySpan<ushort> valueOffsets,
        int count,
        out ReadOnlySpan<char> value)
    {
        int low = 0;
        int high = count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            ReadOnlySpan<char> candidate = names.AsSpan(nameOffsets[middle], nameOffsets[middle + 1] - nameOffsets[middle]);
            int order = candidate.SequenceCompareTo(name);
            if (order == 0)
            {
                value = values.AsSpan(valueOffsets[middle], valueOffsets[middle + 1] - valueOffsets[middle]);
                return true;
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        value = default;
        return false;
    }
}
