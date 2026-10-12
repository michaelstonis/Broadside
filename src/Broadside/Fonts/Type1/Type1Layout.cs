using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Fonts.Type1;

/// <summary>
/// Splits a Type 1 program into its clear-text portion and its decrypted eexec portion, repairing wrong lengths and the layouts
/// PDF files carry besides the one ISO 32000-2 describes (a whole PFB file, hexadecimal eexec text).
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.9, Table 125: <c>Length1</c> is the length of the clear text, <c>Length2</c> of the binary encrypted portion and
/// <c>Length3</c> of the fixed portion (512 zeros and <c>cleartomark</c>), which a reader does not need. Type 1 Font Format §7.2:
/// eexec decryption (R = 55665, four leading bytes) and the rule that tells binary from hexadecimal eexec text. TN 5040 §3.3: PFB
/// segments. Never throws; every repair is reported.
/// </remarks>
internal static class Type1Layout
{
    /// <summary>The initial key of eexec encryption (Type 1 Font Format §7.2).</summary>
    public const ushort EexecKey = 55665;

    /// <summary>The initial key of charstring encryption (Type 1 Font Format §7.3).</summary>
    public const ushort CharstringKey = 4330;

    private static readonly SearchValues<byte> HexDigits = SearchValues.Create("0123456789abcdefABCDEF"u8);

    /// <summary>Decrypts <paramref name="cipher"/> into <paramref name="plain"/> (Type 1 Font Format §7.1), dropping the first <paramref name="skip"/> bytes.</summary>
    /// <remarks><c>p = c ^ (r &gt;&gt; 8); r = ((c + r) × 52845 + 22719) mod 65536</c>.</remarks>
    public static void Decrypt(ReadOnlySpan<byte> cipher, ushort key, int skip, Span<byte> plain)
    {
        int r = key;
        for (int index = 0; index < cipher.Length; index++)
        {
            byte c = cipher[index];
            if (index >= skip)
            {
                plain[index - skip] = (byte)(c ^ (r >> 8));
            }

            r = (((c + r) * 52845) + 22719) & 0xFFFF;
        }
    }

    /// <summary>Splits and decrypts a program.</summary>
    /// <param name="data">The decoded <c>FontFile</c> stream.</param>
    /// <param name="context">The lengths and the diagnostics sink.</param>
    /// <param name="clear">The clear text.</param>
    /// <param name="plain">The decrypted eexec portion without its four leading bytes; <see langword="null"/> when the program has none.</param>
    public static void Split(ReadOnlyMemory<byte> data, FontProgramContext context, out ReadOnlyMemory<byte> clear, out byte[]? plain)
    {
        bool inDocument = context.Source != FontProgramSource.Unspecified;
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length >= 2 && span[0] == 0x80 && span[1] is 1 or 2)
        {
            SplitPfb(span, context, inDocument, out clear, out plain);
            return;
        }

        SplitPdf(data, context.Length1, context.Length2, context, inDocument, out clear, out plain);
    }

    private static void SplitPdf(
        ReadOnlyMemory<byte> data,
        long? length1,
        long? length2,
        FontProgramContext context,
        bool inDocument,
        out ReadOnlyMemory<byte> clear,
        out byte[]? plain)
    {
        ReadOnlySpan<byte> span = data.Span;
        int clearLength;
        if (length1 is { } stated && stated > 0 && stated <= span.Length && EndsWithEexec(span, (int)stated))
        {
            clearLength = (int)stated;
        }
        else
        {
            int found = FindEexecEnd(span);
            if (found < 0)
            {
                context.Report(
                    DiagnosticCodes.FontType1NoEexec,
                    DiagnosticSeverity.Warning,
                    "The Type 1 program has no \"currentfile eexec\" (Type 1 Font Format §2.3, ISO 32000-2 §9.9 Table 125); the whole program is read as clear text.");
                clear = data;
                plain = null;
                return;
            }

            if (length1 is not null || inDocument)
            {
                context.Report(
                    DiagnosticCodes.FontType1Length1Repaired,
                    DiagnosticSeverity.Warning,
                    length1 is { } wrong
                        ? string.Create(CultureInfo.InvariantCulture, $"The font file stream's Length1 is {wrong}, but its clear text ends after \"eexec\" at byte {found} (ISO 32000-2 §9.9, Table 125); the clear text is taken up to there.")
                        : string.Create(CultureInfo.InvariantCulture, $"The font file stream has no Length1 (ISO 32000-2 §9.9, Table 125); the clear text ends after \"eexec\" at byte {found}."));
            }

            clearLength = found;
        }

        clear = data[..clearLength];
        ReadOnlySpan<byte> rest = span[clearLength..];
        ReadOnlySpan<byte> cipher = rest;
        if (length2 is { } encrypted && encrypted > 0 && encrypted <= rest.Length)
        {
            cipher = rest[..(int)encrypted];
        }
        else if (length2 is not null || inDocument)
        {
            context.Report(
                DiagnosticCodes.FontType1Length2Repaired,
                DiagnosticSeverity.Warning,
                length2 is { } wrong
                    ? string.Create(CultureInfo.InvariantCulture, $"The font file stream's Length2 is {wrong}, but {rest.Length} bytes follow the clear text (ISO 32000-2 §9.9, Table 125); all of them are decrypted.")
                    : "The font file stream has no Length2 (ISO 32000-2 §9.9, Table 125); every byte after the clear text is decrypted.");
        }

        plain = DecryptEexec(cipher, context, inDocument);
    }

    /// <summary>TN 5040 §3.3, Table 1: <c>0x80</c>, the type (1 ASCII, 2 binary, 3 end of file), a 4-byte little-endian length, the data.</summary>
    private static void SplitPfb(ReadOnlySpan<byte> span, FontProgramContext context, bool inDocument, out ReadOnlyMemory<byte> clear, out byte[]? plain)
    {
        var ascii = new ArrayBufferWriter<byte>();
        var binary = new ArrayBufferWriter<byte>();
        string? problem = null;
        int position = 0;
        while (position < span.Length)
        {
            if (span[position] != 0x80 || position + 1 >= span.Length)
            {
                problem = string.Create(CultureInfo.InvariantCulture, $"byte {position} does not start a segment; the rest is ignored");
                break;
            }

            byte type = span[position + 1];
            if (type == 3)
            {
                break;
            }

            if (type is not (1 or 2) || position + 6 > span.Length)
            {
                problem = string.Create(CultureInfo.InvariantCulture, $"the segment at byte {position} has type {type} or a truncated header; the rest is ignored");
                break;
            }

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(span[(position + 2)..]);
            position += 6;
            int available = span.Length - position;
            if (length > available)
            {
                problem = string.Create(CultureInfo.InvariantCulture, $"a segment declares {length} bytes but {available} remain; the bytes present are read");
                length = (uint)available;
            }

            ReadOnlySpan<byte> segment = span.Slice(position, (int)length);
            position += (int)length;
            if (type == 2)
            {
                binary.Write(segment);
            }
            else if (binary.WrittenCount == 0)
            {
                ascii.Write(segment);
            }

            // ASCII segments after the binary ones hold the fixed portion (zeros and cleartomark), which is not needed.
        }

        if (inDocument || problem is not null)
        {
            context.Report(
                DiagnosticCodes.FontType1PfbWrapped,
                DiagnosticSeverity.Warning,
                problem is null
                    ? "The font file stream holds a PFB file (TN 5040 §3.3), not the program itself (ISO 32000-2 §9.9, Table 125); its segment headers are removed."
                    : $"The font file stream holds a PFB file (TN 5040 §3.3), not the program itself (ISO 32000-2 §9.9, Table 125); its segment headers are removed, and {problem}.");
        }

        if (binary.WrittenCount == 0)
        {
            // A PFB whose eexec portion is in an ASCII segment (hexadecimal): split its text as an unwrapped program.
            SplitPdf(ascii.WrittenMemory, null, null, context, inDocument: false, out clear, out plain);
            return;
        }

        clear = ascii.WrittenMemory;
        plain = DecryptEexec(binary.WrittenSpan, context, inDocument);
    }

    /// <summary>Type 1 Font Format §7.2: hexadecimal eexec text has four hexadecimal digits first; binary text never does.</summary>
    private static byte[] DecryptEexec(ReadOnlySpan<byte> cipher, FontProgramContext context, bool inDocument)
    {
        byte[]? decoded = null;
        if (cipher.Length >= 4 && !cipher[..4].ContainsAnyExcept(HexDigits))
        {
            if (inDocument)
            {
                context.Report(
                    DiagnosticCodes.FontType1HexEexec,
                    DiagnosticSeverity.Warning,
                    "The Type 1 program's encrypted portion is hexadecimal text; ISO 32000-2 §9.9 allows only the binary form (Type 1 Font Format §7.2). It is decoded.");
            }

            decoded = DecodeHex(cipher);
            cipher = decoded;
        }

        int length = Math.Max(cipher.Length - 4, 0);
        byte[] plain = new byte[length];
        Decrypt(cipher, EexecKey, 4, plain);
        return plain;
    }

    /// <summary>Hexadecimal digit pairs, white space ignored; stops at the first other byte; an odd trailing digit is dropped.</summary>
    private static byte[] DecodeHex(ReadOnlySpan<byte> text)
    {
        byte[] bytes = new byte[text.Length / 2];
        int count = 0;
        int high = -1;
        foreach (byte character in text)
        {
            int digit = character switch
            {
                >= (byte)'0' and <= (byte)'9' => character - '0',
                >= (byte)'a' and <= (byte)'f' => character - 'a' + 10,
                >= (byte)'A' and <= (byte)'F' => character - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
            {
                if (PostScriptTokenizer.IsWhiteSpace(character))
                {
                    continue;
                }

                break;
            }

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                bytes[count++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }

        return bytes.AsSpan(0, count).ToArray();
    }

    /// <summary>Whether the clear text of the stated length ends with <c>eexec</c> and white space, and the encrypted text starts right after.</summary>
    private static bool EndsWithEexec(ReadOnlySpan<byte> data, int length)
    {
        ReadOnlySpan<byte> clear = data[..length];
        int end = clear.Length;
        while (end > 0 && PostScriptTokenizer.IsWhiteSpace(clear[end - 1]))
        {
            end--;
        }

        return end < clear.Length
            && clear[..end].EndsWith("eexec"u8)
            && (length == data.Length || data[length] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'));
    }

    /// <summary>The offset just past the first <c>eexec</c> token and the white space after it, or −1.</summary>
    private static int FindEexecEnd(ReadOnlySpan<byte> data)
    {
        var tokenizer = new PostScriptTokenizer(data);
        while (true)
        {
            PostScriptToken token = tokenizer.Next();
            if (token.Kind == PostScriptTokenKind.EndOfInput)
            {
                return -1;
            }

            if (tokenizer.IsName(token, "eexec"u8))
            {
                int end = token.Start + token.Length;
                while (end < data.Length && data[end] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
                {
                    end++;
                }

                return end;
            }
        }
    }
}
