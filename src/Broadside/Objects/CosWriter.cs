using System.Buffers;
using System.Globalization;

namespace Broadside.Objects;

/// <summary>Writes the PDF syntax of COS objects.</summary>
/// <remarks>
/// ISO 32000-2 §7.2 and §7.3. The output is canonical rather than a copy of any source bytes: single spaces between tokens, reals in
/// shortest round-trip form with a decimal point and no exponent, literal strings with every parenthesis and backslash escaped and
/// non-printable bytes as three-digit octal escapes, names with every byte outside <c>!</c>–<c>~</c> and every delimiter and
/// <c>#</c> as a <c>#xx</c> escape, and streams with a direct <c>Length</c>. Whatever it writes parses back to an equal object.
/// </remarks>
internal static class CosWriter
{
    private static readonly SearchValues<byte> NameNeedsEscape = SearchValues.Create("\0\t\n\f\r ()<>[]{}/%#"u8);

    public static void Write(CosObject value, IBufferWriter<byte> writer)
    {
        switch (value)
        {
            case CosBoolean boolean:
                writer.Write(boolean.Value ? "true"u8 : "false"u8);
                break;
            case CosInteger integer:
                WriteInteger(integer.Value, writer);
                break;
            case CosReal real:
                WriteReal(real.Value, writer);
                break;
            case CosString text:
                if (text.IsHexadecimal)
                {
                    WriteHexString(text.Bytes, writer);
                }
                else
                {
                    WriteLiteralString(text.Bytes, writer);
                }

                break;
            case CosName name:
                WriteName(name.Bytes, writer);
                break;
            case CosArray array:
                WriteArray(array, writer);
                break;
            case CosDictionary dictionary:
                WriteDictionary(dictionary, writer, streamLength: -1);
                break;
            case CosStream stream:
                WriteDictionary(stream.Dictionary, writer, stream.EncodedData.Length);
                writer.Write("\nstream\n"u8);
                writer.Write(stream.EncodedData.Span);
                writer.Write("\nendstream"u8);
                break;
            case CosReference reference:
                WriteInteger(reference.ObjectNumber, writer);
                writer.Write(" "u8);
                WriteInteger(reference.Generation, writer);
                writer.Write(" R"u8);
                break;
            default:
                writer.Write("null"u8);
                break;
        }
    }

    private static void WriteInteger(long value, IBufferWriter<byte> writer)
    {
        Span<byte> span = writer.GetSpan(20);
        value.TryFormat(span, out int written, default, CultureInfo.InvariantCulture);
        writer.Advance(written);
    }

    /// <summary>Writes a real with a decimal point and without an exponent, which PDF does not allow (§7.3.3).</summary>
    private static void WriteReal(double value, IBufferWriter<byte> writer)
    {
        string roundTrip = value.ToString("R", CultureInfo.InvariantCulture);
        string text = roundTrip.Contains('E', StringComparison.Ordinal) ? ExpandExponent(roundTrip) : roundTrip;
        if (!text.Contains('.', StringComparison.Ordinal))
        {
            text += ".0";
        }

        Span<byte> span = writer.GetSpan(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            span[i] = (byte)text[i];
        }

        writer.Advance(text.Length);
    }

    /// <summary>Rewrites <c>-1.25E-05</c> as <c>-0.0000125</c> and <c>1E+20</c> as <c>100000000000000000000</c>.</summary>
    private static string ExpandExponent(string text)
    {
        int exponentAt = text.IndexOf('E', StringComparison.Ordinal);
        int exponent = int.Parse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        ReadOnlySpan<char> mantissa = text.AsSpan(0, exponentAt);
        bool negative = mantissa[0] == '-';
        if (negative)
        {
            mantissa = mantissa[1..];
        }

        int point = mantissa.IndexOf('.');
        string digits = point < 0 ? mantissa.ToString() : string.Concat(mantissa[..point], mantissa[(point + 1)..]);
        int newPoint = (point < 0 ? mantissa.Length : point) + exponent;
        string body = newPoint <= 0
            ? string.Concat("0.", new string('0', -newPoint), digits)
            : newPoint >= digits.Length
                ? string.Concat(digits, new string('0', newPoint - digits.Length))
                : string.Concat(digits.AsSpan(0, newPoint), ".", digits.AsSpan(newPoint));
        return negative ? "-" + body : body;
    }

    /// <summary>Writes a literal string (§7.3.4.2). Parentheses are always escaped, so balance never matters.</summary>
    private static void WriteLiteralString(ReadOnlySpan<byte> bytes, IBufferWriter<byte> writer)
    {
        writer.Write("("u8);
        Span<byte> escape = stackalloc byte[4];
        foreach (byte value in bytes)
        {
            ReadOnlySpan<byte> output = value switch
            {
                (byte)'(' => "\\("u8,
                (byte)')' => "\\)"u8,
                (byte)'\\' => "\\\\"u8,
                (byte)'\n' => "\\n"u8,
                (byte)'\r' => "\\r"u8,
                (byte)'\t' => "\\t"u8,
                0x08 => "\\b"u8,
                0x0C => "\\f"u8,
                >= 0x20 and <= 0x7E => Single(value, escape),
                _ => Octal(value, escape),
            };
            writer.Write(output);
        }

        writer.Write(")"u8);
    }

    private static Span<byte> Single(byte value, Span<byte> escape)
    {
        escape[0] = value;
        return escape[..1];
    }

    private static Span<byte> Octal(byte value, Span<byte> escape)
    {
        escape[0] = (byte)'\\';
        escape[1] = (byte)('0' + (value >> 6));
        escape[2] = (byte)('0' + ((value >> 3) & 7));
        escape[3] = (byte)('0' + (value & 7));
        return escape;
    }

    /// <summary>Writes a hexadecimal string (§7.3.4.3) in upper case.</summary>
    private static void WriteHexString(ReadOnlySpan<byte> bytes, IBufferWriter<byte> writer)
    {
        Span<byte> span = writer.GetSpan((bytes.Length * 2) + 2);
        span[0] = (byte)'<';
        int written = 1;
        foreach (byte value in bytes)
        {
            span[written++] = HexDigit(value >> 4);
            span[written++] = HexDigit(value & 0xF);
        }

        span[written++] = (byte)'>';
        writer.Advance(written);
    }

    /// <summary>Writes a name (§7.3.5): <c>#</c>, delimiters, white-space and bytes outside <c>!</c>–<c>~</c> as <c>#xx</c>.</summary>
    private static void WriteName(ReadOnlySpan<byte> bytes, IBufferWriter<byte> writer)
    {
        Span<byte> span = writer.GetSpan((bytes.Length * 3) + 1);
        span[0] = (byte)'/';
        int written = 1;
        foreach (byte value in bytes)
        {
            if (value is < 0x21 or > 0x7E || NameNeedsEscape.Contains(value))
            {
                span[written++] = (byte)'#';
                span[written++] = HexDigit(value >> 4);
                span[written++] = HexDigit(value & 0xF);
            }
            else
            {
                span[written++] = value;
            }
        }

        writer.Advance(written);
    }

    private static void WriteArray(CosArray array, IBufferWriter<byte> writer)
    {
        writer.Write("["u8);
        for (int i = 0; i < array.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(" "u8);
            }

            Write(array[i], writer);
        }

        writer.Write("]"u8);
    }

    /// <summary>Writes a dictionary; for a stream dictionary, <paramref name="streamLength"/> replaces or supplies <c>Length</c>.</summary>
    private static void WriteDictionary(CosDictionary dictionary, IBufferWriter<byte> writer, long streamLength)
    {
        writer.Write("<<"u8);
        bool wroteLength = false;
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            writer.Write(" "u8);
            WriteName(entry.Key.Bytes, writer);
            writer.Write(" "u8);
            if (streamLength >= 0 && entry.Key.Equals(KnownNames.Length))
            {
                WriteInteger(streamLength, writer);
                wroteLength = true;
            }
            else
            {
                Write(entry.Value, writer);
            }
        }

        if (streamLength >= 0 && !wroteLength)
        {
            writer.Write(" /Length "u8);
            WriteInteger(streamLength, writer);
        }

        writer.Write(" >>"u8);
    }

    private static byte HexDigit(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
}
