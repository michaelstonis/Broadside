using System.Buffers;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>The <c>ASCIIHexDecode</c> encoder: two upper-case hexadecimal digits per byte, in lines of 64 digits, then <c>&gt;</c>.</summary>
/// <remarks>
/// ISO 32000-2 §7.4.2: each byte becomes two hexadecimal digits and the greater-than sign marks the end of the data; white space,
/// here a line feed after every 32 bytes, is ignored when decoding. The output is twice the input's length plus its line breaks and
/// the marker. Allocates nothing.
/// </remarks>
public sealed class AsciiHexEncoder : IStreamEncoder
{
    private const int BytesPerLine = 32;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.2: <c>ASCIIHexDecode</c>.</remarks>
    public CosName Name => FilterNames.AsciiHexDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.2.</remarks>
    public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> digits = "0123456789ABCDEF"u8;
        var writer = new FilterOutput(output);
        for (int index = 0; index < data.Length; index++)
        {
            if (index > 0 && index % BytesPerLine == 0)
            {
                writer.Write((byte)'\n');
            }

            Span<byte> pair = writer.Reserve(2);
            pair[0] = digits[data[index] >> 4];
            pair[1] = digits[data[index] & 0x0F];
        }

        writer.Write((byte)'>');
        writer.Flush();
    }
}
