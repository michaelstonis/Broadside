using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>The <c>ASCIIHexDecode</c> filter: pairs of hexadecimal digits to bytes. Decode only.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.2. White-space (§7.2.3: NUL, TAB, LF, FF, CR, SP) is ignored, <c>&gt;</c> ends the data, and an odd final
/// digit decodes as if a 0 followed it. Takes no parameters.
/// </para>
/// <para>
/// Lenient repairs: any other character is skipped (<c>FilterDataInvalid</c>); data without the <c>&gt;</c> marker decodes to
/// its end (<c>FilterDataTruncated</c>).
/// </para>
/// </remarks>
public sealed class AsciiHexDecodeFilter : IStreamFilter
{
    private const sbyte Invalid = -2;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.2: <c>ASCIIHexDecode</c>; abbreviated <c>AHx</c> in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.AsciiHexDecode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.2.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> input = encoded.Span;
        ReadOnlySpan<sbyte> values = DigitValues;
        var writer = new FilterOutput(output);
        int high = -1;
        bool invalidReported = false;
        bool ended = false;
        foreach (byte character in input)
        {
            if (character == (byte)'>')
            {
                ended = true;
                break;
            }

            int value = values[character];
            if (value >= 0)
            {
                if (high < 0)
                {
                    high = value;
                }
                else
                {
                    writer.Write((byte)((high << 4) | value));
                    high = -1;
                }
            }
            else if (value == Invalid && !invalidReported)
            {
                invalidReported = true;
                writer.Flush();
                context.Report(DiagnosticCodes.FilterDataInvalid, DiagnosticSeverity.Warning, "ASCIIHexDecode data holds a character that is neither a hexadecimal digit nor white-space; such characters are skipped.");
            }
        }

        if (high >= 0)
        {
            writer.Write((byte)(high << 4));
        }

        writer.Flush();
        if (!ended)
        {
            context.Report(DiagnosticCodes.FilterDataTruncated, DiagnosticSeverity.Warning, "ASCIIHexDecode data ends without the > end-of-data marker; it is decoded to its end.");
        }
    }

    /// <summary>The value of each byte as a hexadecimal digit; -1 for white-space (§7.2.3), <see cref="Invalid"/> otherwise.</summary>
    private static ReadOnlySpan<sbyte> DigitValues =>
    [
        -1, -2, -2, -2, -2, -2, -2, -2, -2, -1, -1, -2, -1, -1, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -1, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, -2, -2, -2, -2, -2, -2,
        -2, 10, 11, 12, 13, 14, 15, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, 10, 11, 12, 13, 14, 15, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
        -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
    ];
}
