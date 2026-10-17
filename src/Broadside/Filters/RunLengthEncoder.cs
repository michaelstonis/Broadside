using System.Buffers;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>The <c>RunLengthDecode</c> encoder: runs of three or more equal bytes as repeats, everything else as literal runs, then the EOD byte 128.</summary>
/// <remarks>
/// ISO 32000-2 §7.4.5: a length byte of 0 to 127 is followed by length + 1 bytes copied as they are; a length byte of 129 to 255 is
/// followed by one byte repeated 257 - length times (2 to 128); 128 marks the end of the data. Runs longer than 128 bytes are split.
/// A repeat of two bytes costs what a literal does, so only runs of three or more become repeats. Allocates nothing.
/// </remarks>
public sealed class RunLengthEncoder : IStreamEncoder
{
    private const int MaxRun = 128;
    private const int MinRepeat = 3;
    private const byte EndOfData = 128;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.5: <c>RunLengthDecode</c>.</remarks>
    public CosName Name => FilterNames.RunLengthDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.5.</remarks>
    public void Encode(ReadOnlySpan<byte> data, IBufferWriter<byte> output, StreamEncoderContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        var writer = new FilterOutput(output);
        int index = 0;
        while (index < data.Length)
        {
            int run = RunAt(data, index);
            if (run >= MinRepeat)
            {
                writer.Write((byte)(257 - run));
                writer.Write(data[index]);
                index += run;
                continue;
            }

            int start = index;
            index += run;
            while (index < data.Length && index - start < MaxRun)
            {
                run = RunAt(data, index);
                if (run >= MinRepeat)
                {
                    break;
                }

                index = Math.Min(index + run, start + MaxRun);
            }

            writer.Write((byte)(index - start - 1));
            writer.Write(data[start..index]);
        }

        writer.Write(EndOfData);
        writer.Flush();
    }

    /// <summary>How many bytes from <paramref name="index"/> equal the one there, at most <see cref="MaxRun"/>.</summary>
    private static int RunAt(ReadOnlySpan<byte> data, int index)
    {
        ReadOnlySpan<byte> window = data.Slice(index, Math.Min(MaxRun, data.Length - index));
        int different = window.IndexOfAnyExcept(window[0]);
        return different < 0 ? window.Length : different;
    }
}
