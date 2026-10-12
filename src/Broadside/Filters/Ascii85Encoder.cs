using System.Buffers;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>The <c>ASCII85Decode</c> encoder: five base-85 characters per four bytes, <c>z</c> for four zeros, then <c>~&gt;</c>.</summary>
/// <remarks>
/// ISO 32000-2 §7.4.3: each group of four bytes, read as a big-endian 32-bit number, becomes five digits in base 85, each written as
/// the character of its value plus 33 (<c>!</c> to <c>u</c>); a group of four zero bytes is written <c>z</c>. A final group of
/// n bytes (1 to 3) is padded with zeros and written as the first n + 1 characters of its five. The end-of-data marker <c>~&gt;</c>
/// follows. A line feed after every 15 groups keeps lines short; white space is ignored when decoding. Allocates nothing.
/// </remarks>
public sealed class Ascii85Encoder : IStreamEncoder
{
    private const int GroupsPerLine = 15;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.3: <c>ASCII85Decode</c>.</remarks>
    public CosName Name => FilterNames.Ascii85Decode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.3.</remarks>
    public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        var writer = new FilterOutput(output);
        Span<byte> characters = stackalloc byte[5];
        int groups = 0;
        while (!data.IsEmpty)
        {
            if (groups > 0 && groups % GroupsPerLine == 0)
            {
                writer.Write((byte)'\n');
            }

            groups++;
            int count = Math.Min(4, data.Length);
            uint value = 0;
            for (int index = 0; index < 4; index++)
            {
                value = (value << 8) | (index < count ? data[index] : 0u);
            }

            data = data[count..];
            if (value == 0 && count == 4)
            {
                writer.Write((byte)'z');
                continue;
            }

            for (int index = 4; index >= 0; index--)
            {
                characters[index] = (byte)((value % 85) + '!');
                value /= 85;
            }

            writer.Write(characters[..(count + 1)]);
        }

        writer.Write("~>"u8);
        writer.Flush();
    }
}
