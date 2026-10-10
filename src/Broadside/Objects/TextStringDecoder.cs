using System.Text;

namespace Broadside.Objects;

/// <summary>Decodes the bytes of a text string: UTF-16BE or UTF-8 with a byte order marker, otherwise PDFDocEncoding.</summary>
/// <remarks>ISO 32000-2 §7.9.2.2 and Annex D, Table D.3.</remarks>
internal static class TextStringDecoder
{
    private const char Escape = '\u001B';

    /// <summary>
    /// PDFDocEncoding (Table D.3) for the codes that differ from their Unicode code point: 0x18–0x1F (spacing accents) and
    /// 0x80–0xA0. Codes the table leaves undefined (0x7F, 0x9F, 0xAD) and every other code map to the code point with the same number.
    /// </summary>
    private static readonly char[] PdfDocEncoding = BuildPdfDocEncoding();

    /// <summary>Gets the PDFDocEncoding table (Annex D, Table D.3): the character each byte stands for.</summary>
    public static ReadOnlySpan<char> PdfDocEncodingTable => PdfDocEncoding;

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            ReadOnlySpan<byte> body = bytes[2..];
            return RemoveLanguageEscapes(Encoding.BigEndianUnicode.GetString(body[..(body.Length & ~1)]));
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return RemoveLanguageEscapes(Encoding.UTF8.GetString(bytes[3..]));
        }

        return string.Create(bytes.Length, bytes, static (chars, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                chars[i] = PdfDocEncoding[source[i]];
            }
        });
    }

    /// <summary>
    /// Removes language escape sequences (§7.9.2.2.2): ESC, a two-character language code, an optional two-character country code,
    /// ESC. An ESC that does not open such a sequence is kept.
    /// </summary>
    private static string RemoveLanguageEscapes(string text)
    {
        int escape = text.IndexOf(Escape, StringComparison.Ordinal);
        if (escape < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int copied = 0;
        while (escape >= 0)
        {
            int close = escape + 3 < text.Length && text[escape + 3] == Escape ? escape + 3
                : escape + 5 < text.Length && text[escape + 5] == Escape && text[escape + 3] != Escape ? escape + 5
                : -1;
            if (close < 0)
            {
                escape = text.IndexOf(Escape, escape + 1);
                continue;
            }

            builder.Append(text, copied, escape - copied);
            copied = close + 1;
            escape = text.IndexOf(Escape, copied);
        }

        builder.Append(text, copied, text.Length - copied);
        return builder.ToString();
    }

    private static char[] BuildPdfDocEncoding()
    {
        char[] table = new char[256];
        for (int i = 0; i < table.Length; i++)
        {
            table[i] = (char)i;
        }

        ReadOnlySpan<char> accents = ['˘', 'ˇ', 'ˆ', '˙', '˝', '˛', '˚', '˜'];
        accents.CopyTo(table.AsSpan(0x18));

        ReadOnlySpan<char> high =
        [
            '•', '†', '‡', '…', '—', '–', 'ƒ', '⁄', // 0x80-0x87
            '‹', '›', '−', '‰', '„', '“', '”', '‘', // 0x88-0x8F
            '’', '‚', '™', 'ﬁ', 'ﬂ', 'Ł', 'Œ', 'Š', // 0x90-0x97
            'Ÿ', 'Ž', 'ı', 'ł', 'œ', 'š', 'ž', '\u009F', // 0x98-0x9F (0x9F undefined)
            '€', // 0xA0
        ];
        high.CopyTo(table.AsSpan(0x80));
        return table;
    }
}
