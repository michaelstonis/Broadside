using System.Text;
using Broadside.Objects;

namespace Broadside.Security;

/// <summary>Turns a <see cref="PdfPassword"/> into the byte strings the standard security handler tries, by revision.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.4.3.2 step a (revisions 2-4: PDFDocEncoding, at most 32 bytes) and §7.6.4.1 with §7.6.4.3.3 steps a-b (revision 6
/// and later: SASLprep of RFC 4013 with the Normalize option, UTF-8, at most 127 bytes). Readers disagree with the letter of both
/// rules in ways that matter for real files, and so does this one: for revisions 2-4 a password that PDFDocEncoding cannot represent,
/// or that fails, is retried as Latin-1 (what pdf.js and PDFBox send); for revision 6 a password SASLprep rejects or changes is also
/// tried as plain UTF-8 (pdf.js). The bidirectional-text check of RFC 3454 §6 is not applied (pdf.js does not either). The
/// deprecated revision 5 uses plain UTF-8.
/// <para>
/// SASLprep's NFKC step uses <see cref="string.Normalize(NormalizationForm)"/>, which needs the host's globalization data: in an
/// application built with <c>InvariantGlobalization</c> it leaves non-ASCII text unchanged, so a password typed decomposed (or with
/// compatibility characters) matches only when the document's password was set the same way. The mapping and prohibition steps
/// apply everywhere. <c>tests/Broadside.Globalization.Tests</c> covers NFKC with globalization on.
/// </para>
/// </remarks>
internal static class PasswordEncoding
{
    /// <summary>The longest legacy password (§7.6.4.3.2 step a).</summary>
    public const int MaxLegacyLength = 32;

    /// <summary>The longest revision 6 password (§7.6.4.3.3 step b).</summary>
    public const int MaxUnicodeLength = 127;

    private static readonly Dictionary<char, byte> PdfDocBytes = BuildPdfDocBytes();

    /// <summary>Returns the byte strings to try for revisions 2 to 4, most likely first, each at most 32 bytes.</summary>
    /// <param name="password">The password, or <see langword="null"/> for the empty one.</param>
    /// <returns>One or two candidates.</returns>
    public static List<byte[]> Legacy(PdfPassword? password)
    {
        if (password is null || password.IsEmpty)
        {
            return [[]];
        }

        if (password.IsRaw)
        {
            return [Truncate(password.RawBytes, MaxLegacyLength)];
        }

        string text = password.Text!;
        var candidates = new List<byte[]>(2);
        if (TryEncodePdfDoc(text, out byte[]? pdfDoc))
        {
            candidates.Add(Truncate(pdfDoc, MaxLegacyLength));
        }

        byte[] latin1 = Truncate(Encoding.Latin1.GetBytes(text), MaxLegacyLength);
        if (candidates.Count == 0 || !candidates[0].AsSpan().SequenceEqual(latin1))
        {
            candidates.Add(latin1);
        }

        return candidates;
    }

    /// <summary>Returns the byte strings to try for revision 6 and later, most likely first, each at most 127 bytes.</summary>
    /// <param name="password">The password, or <see langword="null"/> for the empty one.</param>
    /// <returns>One or two candidates.</returns>
    public static List<byte[]> Unicode(PdfPassword? password)
    {
        if (password is null || password.IsEmpty)
        {
            return [[]];
        }

        if (password.IsRaw)
        {
            return [Truncate(password.RawBytes, MaxUnicodeLength)];
        }

        string text = password.Text!;
        byte[] plain = Truncate(Encoding.UTF8.GetBytes(text), MaxUnicodeLength);
        string? prepared = SaslPrep(text);
        if (prepared is null)
        {
            return [plain];
        }

        byte[] preparedBytes = Truncate(Encoding.UTF8.GetBytes(prepared), MaxUnicodeLength);
        return preparedBytes.AsSpan().SequenceEqual(plain) ? [preparedBytes] : [preparedBytes, plain];
    }

    /// <summary>Returns the byte string to try for the deprecated revision 5: UTF-8, at most 127 bytes.</summary>
    /// <param name="password">The password, or <see langword="null"/> for the empty one.</param>
    /// <returns>One candidate.</returns>
    public static List<byte[]> Utf8(PdfPassword? password) =>
        password is null || password.IsEmpty ? [[]] : [Truncate(password.Utf8(), MaxUnicodeLength)];

    /// <summary>
    /// The SASLprep profile (RFC 4013) of stringprep (RFC 3454): map non-ASCII spaces to SPACE and the characters of table B.1 to
    /// nothing, normalize with NFKC, reject the prohibited characters. Returns <see langword="null"/> for a prohibited character.
    /// </summary>
    /// <param name="text">The password.</param>
    /// <returns>The prepared password, or <see langword="null"/>.</returns>
    public static string? SaslPrep(string text)
    {
        var mapped = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int codePoint = ReadCodePoint(text, i, out int width);
            i += width - 1;
            if (IsNonAsciiSpace(codePoint))
            {
                mapped.Append(' ');
            }
            else if (!IsMappedToNothing(codePoint))
            {
                mapped.Append(text, i - width + 1, width);
            }
        }

        string normalized;
        try
        {
            normalized = mapped.ToString().Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // An unpaired surrogate: prohibited (RFC 3454 table C.5).
            return null;
        }

        for (int i = 0; i < normalized.Length; i++)
        {
            if (char.IsSurrogate(normalized[i]) && !char.IsSurrogatePair(normalized, i))
            {
                return null;
            }

            int codePoint = ReadCodePoint(normalized, i, out int width);
            i += width - 1;
            if (IsProhibited(codePoint))
            {
                return null;
            }
        }

        return normalized;
    }

    private static int ReadCodePoint(string text, int index, out int width)
    {
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(text[index], text[index + 1]);
        }

        width = 1;
        return text[index];
    }

    /// <summary>RFC 3454 table C.1.2.</summary>
    private static bool IsNonAsciiSpace(int c) =>
        c is 0x00A0 or 0x1680 or (>= 0x2000 and <= 0x200B) or 0x202F or 0x205F or 0x3000;

    /// <summary>RFC 3454 table B.1.</summary>
    private static bool IsMappedToNothing(int c) =>
        c is 0x00AD or 0x034F or 0x1806 or (>= 0x180B and <= 0x180D) or (>= 0x200B and <= 0x200D) or 0x2060
            or (>= 0xFE00 and <= 0xFE0F) or 0xFEFF;

    /// <summary>RFC 4013 §2.3: RFC 3454 tables C.1.2, C.2.1, C.2.2, C.3, C.4, C.5, C.6, C.7, C.8 and C.9.</summary>
    private static bool IsProhibited(int c) =>
        IsNonAsciiSpace(c)
        || c is <= 0x1F or 0x7F
        || c is (>= 0x80 and <= 0x9F) or 0x06DD or 0x070F or 0x180E or 0x200C or 0x200D or 0x2028 or 0x2029
            or (>= 0x2060 and <= 0x2063) or (>= 0x206A and <= 0x206F) or 0xFEFF or (>= 0xFFF9 and <= 0xFFFC)
            or (>= 0x1D173 and <= 0x1D17A)
        || c is (>= 0xE000 and <= 0xF8FF) or (>= 0xF0000 and <= 0xFFFFD) or (>= 0x100000 and <= 0x10FFFD)
        || c is (>= 0xFDD0 and <= 0xFDEF) || (c & 0xFFFE) == 0xFFFE
        || c is (>= 0xD800 and <= 0xDFFF)
        || c is (>= 0xFFF9 and <= 0xFFFD)
        || c is (>= 0x2FF0 and <= 0x2FFB)
        || c is 0x0340 or 0x0341 or 0x200E or 0x200F or (>= 0x202A and <= 0x202E)
        || c is 0xE0001 or (>= 0xE0020 and <= 0xE007F);

    private static bool TryEncodePdfDoc(string text, out byte[] bytes)
    {
        bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (!PdfDocBytes.TryGetValue(text[i], out byte value))
            {
                return false;
            }

            bytes[i] = value;
        }

        return true;
    }

    private static byte[] Truncate(ReadOnlySpan<byte> bytes, int length) => bytes[..Math.Min(bytes.Length, length)].ToArray();

    private static Dictionary<char, byte> BuildPdfDocBytes()
    {
        ReadOnlySpan<char> table = TextStringDecoder.PdfDocEncodingTable;
        var bytes = new Dictionary<char, byte>(table.Length);
        for (int i = 0; i < table.Length; i++)
        {
            bytes.TryAdd(table[i], (byte)i);
        }

        return bytes;
    }
}
