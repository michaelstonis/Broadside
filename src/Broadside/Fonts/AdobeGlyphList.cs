using System.Buffers;
using System.Globalization;
using System.Text;

namespace Broadside.Fonts;

/// <summary>Glyph name to Unicode, through the Adobe Glyph List, the ITC Zapf Dingbats Glyph List and the <c>uniXXXX</c>/<c>uXXXX</c> forms.</summary>
/// <remarks>
/// The data is in <c>AdobeGlyphList.g.cs</c>. The lookups allocate nothing. <see cref="TryGetUnicode"/> and <see cref="TryGetScalar"/>
/// look a whole name up; <see cref="MapName"/> applies the AGL specification's algorithm (ISO 32000-2 §9.10.2 refers to it): the
/// suffix after the first period is dropped, components joined by underscores map one by one, and <c>uniXXXX…</c>/<c>uXXXX</c>
/// components stand for their values.
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

    /// <summary>Maps a glyph name to its Unicode text by the Adobe Glyph List specification's algorithm.</summary>
    /// <param name="name">The glyph name.</param>
    /// <param name="zapfDingbats">Whether the font is ZapfDingbats, whose list is consulted first for each component.</param>
    /// <param name="destination">Where the UTF-16 text goes; text that does not fit is cut at a component boundary.</param>
    /// <param name="nonStandard">
    /// Set when the name maps only by accepting lowercase hexadecimal digits in a <c>uni</c> or <c>u</c> component, as PDFBox does
    /// (the specification wants uppercase).
    /// </param>
    /// <returns>The number of UTF-16 units written; 0 when the name maps to nothing (<c>.notdef</c>, <c>foo</c>).</returns>
    /// <remarks>
    /// AGL specification §2: (1) drop everything from the first period; (2) split the rest at underscores; (3) map each component,
    /// in order: the ITC Zapf Dingbats list (ZapfDingbats only), the Adobe Glyph List, <c>uni</c> followed by groups of four
    /// uppercase hexadecimal digits each outside D800-DFFF, <c>u</c> followed by four to six uppercase hexadecimal digits outside
    /// D800-DFFF and at most 10FFFF; any other component maps to nothing. So <c>f_i</c> is f and i (not U+FB01) and <c>a.sc</c>
    /// is a. Allocates nothing.
    /// </remarks>
    public static int MapName(ReadOnlySpan<char> name, bool zapfDingbats, Span<char> destination, out bool nonStandard)
    {
        nonStandard = false;
        int dot = name.IndexOf('.');
        ReadOnlySpan<char> stem = dot >= 0 ? name[..dot] : name;
        if (stem.IsEmpty)
        {
            return 0;
        }

        int written = MapComponents(stem, zapfDingbats, destination, lenient: false);
        if (written == 0)
        {
            written = MapComponents(stem, zapfDingbats, destination, lenient: true);
            nonStandard = written > 0;
        }

        return written;
    }

    private static int MapComponents(ReadOnlySpan<char> stem, bool zapfDingbats, Span<char> destination, bool lenient)
    {
        int written = 0;
        while (true)
        {
            int separator = stem.IndexOf('_');
            ReadOnlySpan<char> component = separator >= 0 ? stem[..separator] : stem;
            written += MapComponent(component, zapfDingbats, destination[written..], lenient);
            if (separator < 0)
            {
                return written;
            }

            stem = stem[(separator + 1)..];
        }
    }

    private static int MapComponent(ReadOnlySpan<char> component, bool zapfDingbats, Span<char> destination, bool lenient)
    {
        if (component.IsEmpty)
        {
            return 0;
        }

        if ((zapfDingbats && TryFind(component, ZapfNames, ZapfNameOffsets, ZapfValues, ZapfValueOffsets, ZapfCount, out ReadOnlySpan<char> value))
            || TryFind(component, AglNames, AglNameOffsets, AglValues, AglValueOffsets, AglCount, out value))
        {
            return value.TryCopyTo(destination) ? value.Length : 0;
        }

        if (component.Length > 3 && (component.Length - 3) % 4 == 0 && component.StartsWith("uni", StringComparison.Ordinal))
        {
            ReadOnlySpan<char> digits = component[3..];
            int groups = digits.Length / 4;
            for (int group = 0; group < groups; group++)
            {
                if (!TryParseHex(digits.Slice(group * 4, 4), lenient, out int unit) || IsSurrogate(unit))
                {
                    return 0;
                }
            }

            if (groups > destination.Length)
            {
                return 0;
            }

            for (int group = 0; group < groups; group++)
            {
                TryParseHex(digits.Slice(group * 4, 4), lenient, out int unit);
                destination[group] = (char)unit;
            }

            return groups;
        }

        if (component.Length is >= 5 and <= 7 && component[0] == 'u'
            && TryParseHex(component[1..], lenient, out int scalar) && !IsSurrogate(scalar) && scalar <= 0x10FFFF)
        {
            return new Rune(scalar).TryEncodeToUtf16(destination, out int units) ? units : 0;
        }

        return 0;
    }

    private static bool TryParseHex(ReadOnlySpan<char> digits, bool lenient, out int value)
    {
        value = 0;
        foreach (char digit in digits)
        {
            int nibble = digit switch
            {
                >= '0' and <= '9' => digit - '0',
                >= 'A' and <= 'F' => digit - 'A' + 10,
                >= 'a' and <= 'f' when lenient => digit - 'a' + 10,
                _ => -1,
            };
            if (nibble < 0)
            {
                return false;
            }

            value = (value << 4) | nibble;
        }

        return true;
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
