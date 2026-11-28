using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>
/// Entity and numeric character references (CommonMark 0.31, "Entity and numeric character references"): the HTML named references
/// that end in a semicolon, and decimal and hexadecimal code points, with U+FFFD for zero and for code points that are not Unicode
/// scalar values.
/// </summary>
internal static partial class MarkdownEntities
{
    private static FrozenDictionary<string, string>? _named;

    private static FrozenDictionary<string, string> Named => _named ??= Parse();

    /// <summary>
    /// The length of the character reference at <paramref name="start"/> of <paramref name="text"/> (from its <c>&amp;</c> to its
    /// <c>;</c>), and its replacement text; 0 when no reference is there.
    /// </summary>
    public static int Match(ReadOnlySpan<char> text, int start, out string? replacement)
    {
        replacement = null;
        if (start >= text.Length || text[start] != '&')
        {
            return 0;
        }

        int index = start + 1;
        if (index < text.Length && text[index] == '#')
        {
            index++;
            bool hex = index < text.Length && (text[index] | 0x20) == 'x';
            if (hex)
            {
                index++;
            }

            int digitsStart = index;
            int limit = hex ? 6 : 7;
            int value = 0;
            while (index < text.Length && index - digitsStart < limit && (hex ? char.IsAsciiHexDigit(text[index]) : char.IsAsciiDigit(text[index])))
            {
                value = (value * (hex ? 16 : 10)) + (hex ? HexValue(text[index]) : text[index] - '0');
                index++;
            }

            if (index == digitsStart || index >= text.Length || text[index] != ';')
            {
                return 0;
            }

            replacement = value == 0 || value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF) ? "�" : char.ConvertFromUtf32(value);
            return index + 1 - start;
        }

        int nameStart = index;
        while (index < text.Length && index - nameStart < 32 && char.IsAsciiLetterOrDigit(text[index]))
        {
            index++;
        }

        if (index - nameStart < 2 || !char.IsAsciiLetter(text[nameStart]) || index >= text.Length || text[index] != ';')
        {
            return 0;
        }

        FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> lookup = Named.GetAlternateLookup<ReadOnlySpan<char>>();
        if (!lookup.TryGetValue(text[nameStart..index], out replacement))
        {
            return 0;
        }

        return index + 1 - start;
    }

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static FrozenDictionary<string, string> Parse()
    {
        var entries = new Dictionary<string, string>(2200, StringComparer.Ordinal);
        ReadOnlySpan<char> table = Table;
        var value = new StringBuilder(2);
        while (table.Length > 0)
        {
            int end = table.IndexOf(';');
            ReadOnlySpan<char> entry = table[..end];
            table = table[(end + 1)..];
            int equals = entry.IndexOf('=');
            value.Clear();
            foreach (Range range in entry[(equals + 1)..].Split(','))
            {
                value.Append(char.ConvertFromUtf32(int.Parse(entry[(equals + 1)..][range], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)));
            }

            entries.Add(entry[..equals].ToString(), value.ToString());
        }

        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
