using System.Buffers.Binary;
using Broadside.Content;
using Broadside.Objects;

namespace Broadside.Images;

/// <summary>The first filter of an inline image, as far as finding the end of its data cares.</summary>
internal enum InlineImageFilter : byte
{
    /// <summary>No filter: the data length is known from the dictionary.</summary>
    None,

    /// <summary><c>ASCIIHexDecode</c> (<c>AHx</c>): the data ends with <c>&gt;</c>.</summary>
    AsciiHex,

    /// <summary><c>ASCII85Decode</c> (<c>A85</c>): the data ends with <c>~&gt;</c>.</summary>
    Ascii85,

    /// <summary><c>DCTDecode</c> (<c>DCT</c>): the data ends with the JPEG EOI marker.</summary>
    Dct,

    /// <summary>Any other filter: no end marker this finder knows.</summary>
    Other,
}

/// <summary>What the dictionary of an inline image says about the length of its data.</summary>
/// <param name="Length">The <c>L</c> (or <c>Length</c>) entry, or -1.</param>
/// <param name="FirstFilter">The first filter.</param>
/// <param name="UnfilteredLength">For an unfiltered image whose size, depth and colour space are known, ⌈W × n × BPC / 8⌉ × H; else -1.</param>
internal readonly record struct InlineImageLayout(long Length, InlineImageFilter FirstFilter, long UnfilteredLength)
{
    /// <summary>Gets a layout that says nothing about the length.</summary>
    public static InlineImageLayout Unknown { get; } = new(-1, InlineImageFilter.Other, -1);
}

/// <summary>
/// Finds where the data of an inline image ends and its <c>EI</c> is, without allocating: binary image data routinely contains
/// <c>EI</c> between white-space, so the first <c>EI</c> is never trusted blindly.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.9.7. In order: the dictionary's <c>L</c> (PDF 2.0) when the data plus optional white-space is followed by
/// <c>EI</c>; for an unfiltered image, the length its dictionary implies (also when <c>ID</c> was followed by CR LF, two bytes, where
/// one white-space byte belongs to the syntax); the end-of-data marker of the first filter (a JPEG's EOI, <c>~&gt;</c>,
/// <c>&gt;</c>); else every <c>EI</c> followed by white-space or the end, in order, accepting the first one whose next 75 bytes are
/// text that lexes into a known operator with the operands it takes (pdf.js's <c>findDefaultInlineStreamEnd</c> heuristic,
/// strengthened with the operator table); failing that, the last candidate.
/// </para>
/// <para>Neither pdf.js nor PDFBox reads <c>L</c>; the first two steps make unfiltered and PDF 2.0 images exact.</para>
/// </remarks>
internal static class InlineImageEnd
{
    /// <summary>How many bytes after a candidate <c>EI</c> are checked to look like content.</summary>
    public const int Lookahead = 75;

    /// <summary>Finds the end of an inline image's data.</summary>
    /// <param name="source">The content stream.</param>
    /// <param name="dataStart">The offset of the data: after <c>ID</c> and its one white-space byte.</param>
    /// <param name="layout">What the dictionary says about the length.</param>
    /// <param name="start">The data's start, moved past a second byte of a CR LF after <c>ID</c> when the length shows it is not data.</param>
    /// <param name="dataEnd">The offset one past the data.</param>
    /// <param name="repaired">Whether the end was found despite a deviation: a wrong <c>L</c>, CR LF after <c>ID</c>, or no <c>EI</c> that looks right.</param>
    /// <returns>The offset of <c>EI</c>; -1 when there is none (the data then runs to the end).</returns>
    public static int Find(ReadOnlySpan<byte> source, int dataStart, InlineImageLayout layout, out int start, out int dataEnd, out bool repaired)
    {
        start = dataStart;
        repaired = false;
        if (layout.Length >= 0)
        {
            int position = layout.FirstFilter is InlineImageFilter.AsciiHex or InlineImageFilter.Ascii85 ? SkipWhitespace(source, dataStart) : dataStart;
            if (layout.Length <= source.Length - position && EndsAt(source, position + (int)layout.Length, out int ei))
            {
                dataEnd = position + (int)layout.Length;
                return ei;
            }

            repaired = true;
        }

        if (layout.FirstFilter == InlineImageFilter.None && layout.UnfilteredLength >= 0)
        {
            long end = dataStart + layout.UnfilteredLength;
            if (end <= source.Length && EndsAt(source, (int)end, out int ei))
            {
                dataEnd = (int)end;
                return ei;
            }

            if (end + 1 <= source.Length && dataStart > 0 && dataStart < source.Length && source[dataStart] == (byte)'\n'
                && source[dataStart - 1] == (byte)'\r' && EndsAt(source, (int)end + 1, out ei))
            {
                start = dataStart + 1;
                dataEnd = (int)end + 1;
                repaired = true;
                return ei;
            }
        }

        int from = dataStart;
        int marker = layout.FirstFilter switch
        {
            InlineImageFilter.Dct => JpegEnd(source, dataStart),
            InlineImageFilter.Ascii85 => Ascii85End(source, dataStart),
            InlineImageFilter.AsciiHex => source[dataStart..].IndexOf((byte)'>') is >= 0 and var found ? dataStart + found + 1 : -1,
            _ => -1,
        };
        if (marker >= 0)
        {
            if (EndsAt(source, marker, out int ei))
            {
                dataEnd = marker;
                return ei;
            }

            from = marker;
        }

        return Scan(source, start, from, out dataEnd, ref repaired);
    }

    /// <summary>Returns whether <paramref name="source"/> at <paramref name="position"/> holds optional white-space, then <c>EI</c> as a whole token.</summary>
    private static bool EndsAt(ReadOnlySpan<byte> source, int position, out int ei)
    {
        ei = SkipWhitespace(source, position);
        return source[ei..].StartsWith("EI"u8) && (ei + 2 == source.Length || !CosLexer.IsRegular(source[ei + 2]));
    }

    private static int SkipWhitespace(ReadOnlySpan<byte> source, int position)
    {
        while (position < source.Length && CosLexer.IsWhitespace(source[position]))
        {
            position++;
        }

        return position;
    }

    private static int Scan(ReadOnlySpan<byte> source, int start, int from, out int dataEnd, ref bool repaired)
    {
        int last = -1;
        int position = from;
        while (position < source.Length)
        {
            int found = source[position..].IndexOf("EI"u8);
            if (found < 0)
            {
                break;
            }

            int candidate = position + found;
            position = candidate + 1;
            if (candidate + 2 != source.Length && !CosLexer.IsWhitespace(source[candidate + 2]))
            {
                continue;
            }

            last = candidate;
            if (LooksLikeContent(source, candidate + 2))
            {
                dataEnd = candidate > start && CosLexer.IsWhitespace(source[candidate - 1]) ? candidate - 1 : candidate;
                return candidate;
            }
        }

        repaired = true;
        if (last >= 0)
        {
            dataEnd = last > start && CosLexer.IsWhitespace(source[last - 1]) ? last - 1 : last;
            return last;
        }

        dataEnd = source.Length;
        return -1;
    }

    /// <summary>
    /// Returns whether the bytes after a candidate <c>EI</c> look like content: printable text that lexes into a known operator with
    /// as many operands as it takes, or only white-space to the end of the stream.
    /// </summary>
    private static bool LooksLikeContent(ReadOnlySpan<byte> source, int from)
    {
        bool truncated = source.Length - from > Lookahead;
        ReadOnlySpan<byte> window = source.Slice(from, truncated ? Lookahead : source.Length - from);
        bool nul = false;
        foreach (byte value in window)
        {
            if (value is >= 0x20 and <= 0x7E or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r')
            {
                continue;
            }

            if (value != 0 || nul)
            {
                return false;
            }

            nul = true;
        }

        var lexer = new CosLexer(window);
        int operands = 0;
        int depth = 0;
        while (true)
        {
            CosToken token = lexer.Next();
            switch (token.Kind)
            {
                case CosTokenKind.EndOfInput:
                    return truncated || (operands == 0 && depth == 0);
                case CosTokenKind.Invalid:
                    return false;
                case CosTokenKind.ArrayStart or CosTokenKind.DictionaryStart:
                    depth++;
                    continue;
                case CosTokenKind.ArrayEnd or CosTokenKind.DictionaryEnd:
                    if (--depth < 0)
                    {
                        return false;
                    }

                    operands += depth == 0 ? 1 : 0;
                    continue;
                case CosTokenKind.Keyword:
                    break;
                default:
                    operands += depth == 0 ? 1 : 0;
                    continue;
            }

            ReadOnlySpan<byte> keyword = window.Slice(token.Start, token.Length);
            if (keyword.SequenceEqual("true"u8) || keyword.SequenceEqual("false"u8) || keyword.SequenceEqual("null"u8))
            {
                operands += depth == 0 ? 1 : 0;
                continue;
            }

            if (depth > 0)
            {
                return false;
            }

            ContentOperatorCode code = OperatorTable.Lookup(keyword);
            if (code == ContentOperatorCode.Unknown)
            {
                // A keyword cut by the end of the window may be an operator.
                return truncated && token.End == window.Length;
            }

            if (code == ContentOperatorCode.BeginInlineImage)
            {
                // Another inline image: BI is written without operands.
                return operands == 0;
            }

            OperandSignature signature = OperatorTable.Signature(code);
            return signature.IsVariable ? operands > 0 : operands == signature.Kinds.Length;
        }
    }

    /// <summary>Returns the offset after a JPEG's EOI marker, walking the marker segments from SOI; -1 when it cannot be found.</summary>
    private static int JpegEnd(ReadOnlySpan<byte> source, int position)
    {
        if (source.Length - position < 2 || source[position] != 0xFF || source[position + 1] != 0xD8)
        {
            return -1;
        }

        position += 2;
        while (position < source.Length)
        {
            if (source[position] != 0xFF)
            {
                // Entropy-coded data after SOS: skip to the next marker that is not stuffing (FF 00) or a restart (FF D0-D7).
                int next = source[position..].IndexOf((byte)0xFF);
                if (next < 0)
                {
                    return -1;
                }

                position += next;
                continue;
            }

            while (position + 1 < source.Length && source[position + 1] == 0xFF)
            {
                position++;
            }

            if (position + 1 >= source.Length)
            {
                return -1;
            }

            byte marker = source[position + 1];
            position += 2;
            if (marker == 0xD9)
            {
                return position;
            }

            if (marker is 0x00 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            if (source.Length - position < 2)
            {
                return -1;
            }

            position += BinaryPrimitives.ReadUInt16BigEndian(source.Slice(position, 2));
        }

        return -1;
    }

    /// <summary>Returns the offset after the ASCII85 end-of-data marker <c>~&gt;</c> (white-space allowed between); after a lone <c>~</c> followed by <c>EI</c>; else -1.</summary>
    private static int Ascii85End(ReadOnlySpan<byte> source, int position)
    {
        while (position < source.Length)
        {
            int tilde = source[position..].IndexOf((byte)'~');
            if (tilde < 0)
            {
                return -1;
            }

            position += tilde + 1;
            int after = SkipWhitespace(source, position);
            if (after < source.Length && source[after] == (byte)'>')
            {
                return after + 1;
            }

            if (source[after..].StartsWith("EI"u8))
            {
                return position;
            }
        }

        return -1;
    }
}
