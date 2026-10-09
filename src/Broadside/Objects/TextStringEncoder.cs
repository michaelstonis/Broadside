using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Broadside.Objects;

/// <summary>Encodes text as the bytes of a text string: PDFDocEncoding when every character has a code, otherwise UTF-16BE with a byte order marker.</summary>
/// <remarks>ISO 32000-2 §7.9.2.2 and Annex D, Table D.3. The inverse of <see cref="TextStringDecoder"/> for text it can decode.</remarks>
internal static class TextStringEncoder
{
    private static readonly Dictionary<char, byte> PdfDocCodes = BuildPdfDocCodes();

    /// <summary>Encodes <paramref name="text"/> in PDFDocEncoding.</summary>
    /// <param name="text">The text.</param>
    /// <param name="bytes">The PDFDocEncoding bytes, when every character has a code.</param>
    /// <returns><see langword="false"/> when a character has no PDFDocEncoding code, or the bytes would start with a byte order marker.</returns>
    public static bool TryEncodePdfDoc(string text, [NotNullWhen(true)] out byte[]? bytes)
    {
        var buffer = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (!PdfDocCodes.TryGetValue(text[i], out buffer[i]))
            {
                bytes = null;
                return false;
            }
        }

        // A PDFDocEncoded string cannot start with a marker: it would decode as UTF-16BE or UTF-8.
        if (buffer.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || buffer.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            bytes = null;
            return false;
        }

        bytes = buffer;
        return true;
    }

    /// <summary>Encodes <paramref name="text"/> as a text string: PDFDocEncoding when possible, otherwise UTF-16BE with the marker.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Encode(string text) =>
        TryEncodePdfDoc(text, out byte[]? bytes) ? bytes : [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)];

    private static Dictionary<char, byte> BuildPdfDocCodes()
    {
        ReadOnlySpan<char> table = TextStringDecoder.PdfDocEncodingTable;
        var codes = new Dictionary<char, byte>(table.Length);
        for (int code = 0; code < table.Length; code++)
        {
            codes.TryAdd(table[code], (byte)code);
        }

        return codes;
    }
}
