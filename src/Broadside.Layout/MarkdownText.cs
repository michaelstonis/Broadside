using System.Globalization;
using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>Character classes and string transforms shared by the Markdown block and inline parsers (CommonMark 0.31, "Characters and lines").</summary>
internal static class MarkdownText
{
    private const string Hex = "0123456789ABCDEF";

    /// <summary>A space or a tab.</summary>
    public static bool IsSpaceOrTab(char c) => c is ' ' or '\t';

    /// <summary>A space, tab, line feed, line tabulation, form feed or carriage return.</summary>
    public static bool IsAsciiWhitespace(char c) => c is ' ' or '\t' or '\n' or '\u000B' or '\f' or '\r';

    /// <summary>The ASCII punctuation characters a backslash escapes.</summary>
    public static bool IsEscapable(char c) => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';

    /// <summary>Unicode whitespace: Zs, tab, line feed, form feed, carriage return.</summary>
    public static bool IsUnicodeWhitespace(Rune rune) =>
        rune.Value is '\t' or '\n' or '\f' or '\r' || Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;

    /// <summary>Unicode punctuation: general category P or S (CommonMark 0.31).</summary>
    public static bool IsUnicodePunctuation(Rune rune)
    {
        if (rune.IsAscii)
        {
            return IsEscapable((char)rune.Value);
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
            or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol;
    }

    /// <summary>Whether <paramref name="text"/> holds only spaces, tabs and line endings.</summary>
    public static bool IsBlank(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (!IsAsciiWhitespace(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Replaces backslash escapes and character references in <paramref name="text"/> by the characters they stand for.</summary>
    public static string Unescape(string text)
    {
        int first = text.AsSpan().IndexOfAny('\\', '&');
        if (first < 0)
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        result.Append(text, 0, first);
        for (int index = first; index < text.Length; index++)
        {
            char c = text[index];
            if (c == '\\' && index + 1 < text.Length && IsEscapable(text[index + 1]))
            {
                result.Append(text[index + 1]);
                index++;
            }
            else if (c == '&' && MarkdownEntities.Match(text, index, out string? replacement) is > 0 and int length)
            {
                result.Append(replacement);
                index += length - 1;
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Percent-encodes a link destination as the reference implementation does: characters outside the URI's unreserved and reserved
    /// sets as UTF-8, existing <c>%XX</c> escapes kept.
    /// </summary>
    public static string NormalizeUri(string uri)
    {
        StringBuilder? result = null;
        Span<byte> bytes = stackalloc byte[4];
        for (int index = 0; index < uri.Length; index++)
        {
            char c = uri[index];
            if (c == '%' && index + 2 < uri.Length && char.IsAsciiHexDigit(uri[index + 1]) && char.IsAsciiHexDigit(uri[index + 2]))
            {
                result?.Append(uri, index, 3);
                index += 2;
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c) || ";/?:@&=+$,-_.!~*'()#".Contains(c, StringComparison.Ordinal))
            {
                result?.Append(c);
                continue;
            }

            result ??= new StringBuilder(uri, 0, index, uri.Length + 16);
            int count;
            if (char.IsHighSurrogate(c) && index + 1 < uri.Length && char.IsLowSurrogate(uri[index + 1]))
            {
                count = new Rune(c, uri[index + 1]).EncodeToUtf8(bytes);
                index++;
            }
            else if (char.IsSurrogate(c))
            {
                count = Rune.ReplacementChar.EncodeToUtf8(bytes);
            }
            else
            {
                count = new Rune(c).EncodeToUtf8(bytes);
            }

            for (int b = 0; b < count; b++)
            {
                result.Append('%').Append(Hex[bytes[b] >> 4]).Append(Hex[bytes[b] & 0xF]);
            }
        }

        return result?.ToString() ?? uri;
    }

    /// <summary>
    /// A link label in the form references match on: trimmed, inner whitespace runs collapsed to one space, Unicode case folded (so
    /// <c>ẞ</c> matches <c>SS</c>).
    /// </summary>
    public static string NormalizeLabel(ReadOnlySpan<char> label)
    {
        var result = new StringBuilder(label.Length);
        bool space = false;
        foreach (char c in label.Trim(" \t\n\r\f\v"))
        {
            if (IsAsciiWhitespace(c))
            {
                space = true;
                continue;
            }

            if (space)
            {
                result.Append(' ');
                space = false;
            }

            result.Append(c);
        }

        string lower = result.ToString().ToLowerInvariant();
        return lower.Replace("ß", "ss", StringComparison.Ordinal).ToUpperInvariant();
    }

    /// <summary>Expands tabs to the next multiple of four columns, as a code block's text is drawn.</summary>
    public static string ExpandTabs(string text)
    {
        if (!text.Contains('\t', StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder(text.Length + 16);
        int column = 0;
        foreach (char c in text)
        {
            if (c == '\t')
            {
                int spaces = 4 - (column % 4);
                result.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                result.Append(c);
                column = c == '\n' ? 0 : column + 1;
            }
        }

        return result.ToString();
    }
}
