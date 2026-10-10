using System.Buffers;
using System.Buffers.Binary;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Filters;

/// <summary>The <c>ASCII85Decode</c> filter: groups of five base-85 digits to four bytes. Decode only.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.3. Digits are <c>!</c> to <c>u</c>; <c>z</c> stands for a whole group of four zero bytes; <c>~&gt;</c> ends the
/// data; white-space (§7.2.3) is ignored; a final partial group of n digits (2 to 4) decodes to n - 1 bytes. Takes no parameters.
/// </para>
/// <para>
/// Lenient repairs, one diagnostic per kind: other characters and a <c>z</c> inside a group are skipped, a group above
/// 2<sup>32</sup> - 1 keeps its low 32 bits, a final group of one digit is dropped (<c>FilterDataInvalid</c>); data without
/// <c>~&gt;</c> decodes to its end (<c>FilterDataTruncated</c>). A <c>~</c> not followed by <c>&gt;</c> also ends the data, and a
/// leading PostScript-style <c>&lt;~</c> is skipped, as other readers do.
/// </para>
/// </remarks>
public sealed class Ascii85DecodeFilter : IStreamFilter
{
    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.3: <c>ASCII85Decode</c>; abbreviated <c>A85</c> in inline images (§8.9.7).</remarks>
    public CosName Name => FilterNames.Ascii85Decode;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §7.4.3.</remarks>
    public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> input = SkipPostScriptPrefix(encoded.Span);
        var writer = new FilterOutput(output);
        ulong value = 0;
        int digits = 0;
        bool ended = false;
        bool invalidCharacter = false;
        bool misplacedZ = false;
        bool overflow = false;
        foreach (byte character in input)
        {
            if (character is >= (byte)'!' and <= (byte)'u')
            {
                value = (value * 85) + (ulong)(character - '!');
                if (++digits == 5)
                {
                    overflow |= value > uint.MaxValue;
                    BinaryPrimitives.WriteUInt32BigEndian(writer.Reserve(4), (uint)value);
                    value = 0;
                    digits = 0;
                }
            }
            else if (character == (byte)'z' && digits == 0)
            {
                writer.Reserve(4).Clear();
            }
            else if (character == (byte)'~')
            {
                ended = true;
                break;
            }
            else if (character == (byte)'z')
            {
                misplacedZ = true;
            }
            else if (!CosLexer.IsWhitespace(character))
            {
                invalidCharacter = true;
            }
        }

        bool loneDigit = digits == 1;
        if (digits > 1)
        {
            for (int padding = digits; padding < 5; padding++)
            {
                value = (value * 85) + 84;
            }

            Span<byte> group = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(group, (uint)value);
            writer.Write(group[..(digits - 1)]);
        }

        writer.Flush();
        ReportIf(context, invalidCharacter, "ASCII85Decode data holds a character outside ! to u, z, ~ and white-space; such characters are skipped.");
        ReportIf(context, misplacedZ, "ASCII85Decode data has a z inside a group; it is skipped.");
        ReportIf(context, overflow, "ASCII85Decode data has a group greater than 2^32 - 1; its low 32 bits are kept.");
        ReportIf(context, loneDigit, "ASCII85Decode data ends with a partial group of one character, which cannot occur; it is dropped.");
        if (!ended)
        {
            context.Report(DiagnosticCodes.FilterDataTruncated, DiagnosticSeverity.Warning, "ASCII85Decode data ends without the ~> end-of-data marker; it is decoded to its end.");
        }
    }

    private static void ReportIf(FilterContext context, bool condition, string message)
    {
        if (condition)
        {
            context.Report(DiagnosticCodes.FilterDataInvalid, DiagnosticSeverity.Warning, message);
        }
    }

    private static ReadOnlySpan<byte> SkipPostScriptPrefix(ReadOnlySpan<byte> input)
    {
        int start = 0;
        while (start < input.Length && CosLexer.IsWhitespace(input[start]))
        {
            start++;
        }

        return input[start..].StartsWith("<~"u8) ? input[(start + 2)..] : input;
    }
}
