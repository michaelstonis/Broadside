using System.Globalization;
using System.Text;

namespace Broadside.Signatures;

/// <summary>
/// Where the <c>ByteRange</c> array and the <c>Contents</c> string of a signature dictionary written with placeholders are in an
/// incremental update's bytes, and how they are filled in without moving a byte (ISO 32000-2 §12.8.1, §12.8.3.3.1).
/// </summary>
/// <param name="ByteRangeStart">The offset of the array's <c>[</c>.</param>
/// <param name="ByteRangeEnd">The offset after the array's <c>]</c>.</param>
/// <param name="ContentsStart">The offset of the string's <c>&lt;</c>.</param>
/// <param name="ContentsEnd">The offset after the string's <c>&gt;</c>.</param>
internal readonly record struct SignaturePlaceholders(int ByteRangeStart, int ByteRangeEnd, int ContentsStart, int ContentsEnd)
{
    /// <summary>
    /// Finds the placeholders in the serialized signature dictionary at <paramref name="start"/>: the writer writes a dictionary as
    /// <c>&lt;&lt; /Key value ...</c> in entry order, and the dictionary starts with <c>ByteRange</c> then <c>Contents</c>.
    /// </summary>
    /// <param name="bytes">The update's bytes.</param>
    /// <param name="start">The offset of the dictionary's first byte.</param>
    /// <param name="length">The length of the dictionary's bytes.</param>
    public static SignaturePlaceholders Find(ReadOnlySpan<byte> bytes, int start, int length)
    {
        ReadOnlySpan<byte> body = bytes.Slice(start, length);
        ReadOnlySpan<byte> rangePrefix = "<< /ByteRange "u8;
        ReadOnlySpan<byte> contentsPrefix = " /Contents "u8;
        if (!body.StartsWith(rangePrefix))
        {
            throw new InvalidOperationException("The signature dictionary was not written with its byte range first.");
        }

        int rangeStart = rangePrefix.Length;
        int rangeEnd = body.IndexOf((byte)']') + 1;
        if (body[rangeStart] != (byte)'[' || rangeEnd <= 0 || !body[rangeEnd..].StartsWith(contentsPrefix))
        {
            throw new InvalidOperationException("The signature dictionary was not written with its contents after its byte range.");
        }

        int contentsStart = rangeEnd + contentsPrefix.Length;
        int contentsEnd = body.IndexOf((byte)'>') + 1;
        if (body[contentsStart] != (byte)'<' || contentsEnd <= contentsStart)
        {
            throw new InvalidOperationException("The signature dictionary's contents were not written as a hexadecimal string.");
        }

        return new SignaturePlaceholders(start + rangeStart, start + rangeEnd, start + contentsStart, start + contentsEnd);
    }

    /// <summary>Writes the byte range over its placeholder, padded with spaces before the <c>]</c>.</summary>
    public void WriteByteRange(Span<byte> bytes, IReadOnlyList<long> byteRange)
    {
        string text = "[" + string.Join(' ', byteRange.Select(value => value.ToString(CultureInfo.InvariantCulture)));
        Span<byte> target = bytes[ByteRangeStart..ByteRangeEnd];
        if (text.Length + 1 > target.Length)
        {
            throw new InvalidOperationException("The file is too large for the byte range written for its signature.");
        }

        target.Fill((byte)' ');
        Encoding.ASCII.GetBytes(text, target);
        target[^1] = (byte)']';
    }

    /// <summary>Writes <paramref name="value"/> in hexadecimal into the reserved string, the rest of which stays zeros (§12.8.3.3.1).</summary>
    public void WriteContents(Span<byte> bytes, ReadOnlySpan<byte> value)
    {
        Span<byte> digits = bytes[(ContentsStart + 1)..(ContentsEnd - 1)];
        if (2 * value.Length > digits.Length)
        {
            throw new InvalidOperationException("The signature is larger than the space reserved for it.");
        }

        digits.Fill((byte)'0');
        Convert.TryToHexString(value, digits, out _);
    }
}
