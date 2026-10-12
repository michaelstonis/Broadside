using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>The <c>RunLengthDecode</c> filter: byte-oriented run-length encoding. Decode only.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.5. A length byte of 0 to 127 copies the next length + 1 bytes; 129 to 255 repeats the next byte 257 - length
/// times; 128 ends the data. Takes no parameters.
/// </para>
/// <para>
/// Lenient repair: data that ends without the 128 marker, or in the middle of a run, decodes to its end, keeping the bytes of a
/// partial literal run (<c>FilterDataTruncated</c>).
/// </para>
/// </remarks>
public sealed class RunLengthDecodeFilter : IStreamFilter
{
    private const byte EndOfData = 128;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.5: <c>RunLengthDecode</c>; abbreviated <c>RL</c> in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.RunLengthDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.5.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> input = encoded.Span;
        var writer = new FilterOutput(output);
        int position = 0;
        bool ended = false;
        bool truncated = false;
        while (position < input.Length)
        {
            byte length = input[position++];
            if (length == EndOfData)
            {
                ended = true;
                break;
            }

            if (length < EndOfData)
            {
                int count = length + 1;
                int available = Math.Min(count, input.Length - position);
                writer.Write(input.Slice(position, available));
                position += available;
                truncated = available < count;
            }
            else if (position < input.Length)
            {
                writer.WriteRepeated(input[position++], 257 - length);
            }
            else
            {
                truncated = true;
            }
        }

        writer.Flush();
        if (!ended)
        {
            context.Report(
                DiagnosticCodes.FilterDataTruncated,
                DiagnosticSeverity.Warning,
                truncated
                    ? "RunLengthDecode data ends in the middle of a run; the bytes available are kept."
                    : "RunLengthDecode data ends without the end-of-data byte 128; it is decoded to its end.");
        }
    }
}
