using System.Buffers;
using System.Runtime.InteropServices;
using static Broadside.Layout.Flow.LineBreakClass;

namespace Broadside.Layout.Flow;

/// <summary>
/// Finds line break opportunities with the default Unicode Line Breaking Algorithm: UAX #14 revision 53, Unicode 16.0.0
/// (<see cref="LineBreakData.UnicodeVersion"/>, the version of the .NET 10 base class library), untailored. Breaks after hard
/// line breaks (BK, CR, LF, NL, with CR LF kept together) are mandatory, as is the end of the text.
/// </summary>
/// <remarks>
/// <para>
/// UAX #14 §6: rules LB1 to LB31 in order, the first that applies deciding each position. LB1 resolves classes with the table's
/// defaults (AI, SG and XX as AL; SA as CM when Mn or Mc, otherwise AL; CJ as NS), so complex-context scripts (Thai, Lao, Khmer,
/// Myanmar) break only where the rules allow, without dictionary breaking. LB9 folds a combining mark or ZWJ into the character
/// before it; LB10 treats one with nothing to attach to as AL. The text is decoded as UTF-16; a lone surrogate is AL.
/// </para>
/// <para>
/// One pass over the text: every code point is looked up in the generated table <see cref="LineBreakData"/>, the resolved
/// classes are held in pooled buffers, and each position is decided from bounded context (spaces, number runs and regional
/// indicator pairs are counted as they pass). Nothing is allocated per character.
/// </para>
/// </remarks>
internal sealed class UnicodeLineBreaker : ILineBreaker
{
    /// <summary>Set on an item whose base is U+2010 HYPHEN (rule LB20a).</summary>
    private const byte Hyphen = 0x01;

    /// <summary>Set on an item whose base is U+25CC DOTTED CIRCLE (rule LB28a).</summary>
    private const byte DottedCircle = 0x02;

    /// <summary>Set on an item whose last code point is U+200D ZERO WIDTH JOINER (rule LB8a).</summary>
    private const byte EndsWithJoiner = 0x04;

    private const int StackItems = 256;

    public static UnicodeLineBreaker Instance { get; } = new();

    private enum Decision
    {
        NoBreak,
        Break,
        Mandatory,
    }

    public void FindBreaks(ReadOnlySpan<char> text, List<BreakOpportunity> breaks)
    {
        if (text.Length > 1)
        {
            byte[]? rentedBytes = null;
            int[]? rentedStarts = null;
            Span<byte> bytes = text.Length <= StackItems
                ? stackalloc byte[StackItems * 2]
                : (rentedBytes = ArrayPool<byte>.Shared.Rent(text.Length * 2));
            Span<int> starts = text.Length <= StackItems
                ? stackalloc int[StackItems]
                : (rentedStarts = ArrayPool<int>.Shared.Rent(text.Length));
            try
            {
                Span<LineBreakClass> classes = MemoryMarshal.Cast<byte, LineBreakClass>(bytes[..text.Length]);
                Span<byte> flags = bytes.Slice(text.Length, text.Length);
                int count = Resolve(text, classes, flags, starts);
                Find(classes[..count], flags[..count], starts[..count], breaks);
            }
            finally
            {
                if (rentedBytes is not null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBytes);
                }

                if (rentedStarts is not null)
                {
                    ArrayPool<int>.Shared.Return(rentedStarts);
                }
            }
        }

        // LB3: always break at the end of text.
        breaks.Add(new BreakOpportunity(text.Length, Mandatory: true));
    }

    /// <summary>
    /// LB1, LB9 and LB10: one item per code point that is not folded into the one before it, with its resolved class, flags and
    /// UTF-16 start. Positions inside an item never break (LB9 applies after LB4 to LB8, none of which breaks before a mark that
    /// LB9 folds).
    /// </summary>
    private static int Resolve(ReadOnlySpan<char> text, Span<LineBreakClass> classes, Span<byte> flags, Span<int> starts)
    {
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            int codePoint = text[index];
            int length = 1;
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                codePoint = char.ConvertToUtf32(text[index], text[index + 1]);
                length = 2;
            }

            byte value = LineBreakData.Lookup(codePoint);
            var lineBreakClass = (LineBreakClass)(value & LineBreakData.ClassMask);
            if (lineBreakClass is CM or ZWJ)
            {
                byte joiner = lineBreakClass == ZWJ ? EndsWithJoiner : (byte)0;
                if (count > 0 && classes[count - 1] is not (BK or CR or LF or NL or SP or ZW))
                {
                    // LB9: X (CM | ZWJ)* is X.
                    flags[count - 1] = (byte)((flags[count - 1] & ~EndsWithJoiner) | joiner);
                    index += length;
                    continue;
                }

                // LB10: a remaining CM or ZWJ is U+0041: AL, East_Asian_Width Na, not Extended_Pictographic.
                classes[count] = AL;
                flags[count] = joiner;
            }
            else
            {
                classes[count] = lineBreakClass;
                flags[count] = (byte)((value & ~LineBreakData.ClassMask) | codePoint switch
                {
                    0x2010 => Hyphen,
                    0x25CC => DottedCircle,
                    _ => 0,
                });
            }

            starts[count++] = index;
            index += length;
        }

        return count;
    }

    private static void Find(ReadOnlySpan<LineBreakClass> classes, ReadOnlySpan<byte> flags, ReadOnlySpan<int> starts, List<BreakOpportunity> breaks)
    {
        // LB2: never break at the start of text. Regional indicators ending before the position are counted for LB30a.
        int indicators = classes[0] == RI ? 1 : 0;
        for (int index = 1; index < classes.Length; index++)
        {
            Decision decision = Decide(classes, flags, index, indicators);
            if (decision != Decision.NoBreak)
            {
                breaks.Add(new BreakOpportunity(starts[index], decision == Decision.Mandatory));
            }

            indicators = classes[index] == RI ? indicators + 1 : 0;
        }
    }

    /// <summary>Whether the position before item <paramref name="index"/> breaks: rules LB4 to LB31 of UAX #14 §6.1 and §6.2.</summary>
    private static Decision Decide(ReadOnlySpan<LineBreakClass> classes, ReadOnlySpan<byte> flags, int index, int indicators)
    {
        LineBreakClass before = classes[index - 1];
        LineBreakClass after = classes[index];
        LineBreakClass next = index + 1 < classes.Length ? classes[index + 1] : (LineBreakClass)0xFF;

        // LB4, LB5: always break after hard line breaks; CR LF is one.
        if (before == BK || (before == CR && after != LF) || before is LF or NL)
        {
            return Decision.Mandatory;
        }

        // LB5 (CR × LF), LB6: no break before hard line breaks. LB7: no break before spaces or zero width space.
        if (after is BK or CR or LF or NL or SP or ZW)
        {
            return Decision.NoBreak;
        }

        // The last item before the position that is not a space, for the rules with SP*.
        int last = index - 1;
        while (last >= 0 && classes[last] == SP)
        {
            last--;
        }

        LineBreakClass beforeSpaces = last >= 0 ? classes[last] : (LineBreakClass)0xFF;

        // LB8: ZW SP* ÷. LB8a: ZWJ ×.
        if (beforeSpaces == ZW)
        {
            return Decision.Break;
        }

        if ((flags[index - 1] & EndsWithJoiner) != 0)
        {
            return Decision.NoBreak;
        }

        // LB11: × WJ, WJ ×. LB12: GL ×. LB12a: [^SP BA HY] × GL.
        if (after == WJ || before is WJ or GL || (after == GL && before is not (SP or BA or HY)))
        {
            return Decision.NoBreak;
        }

        // LB13: × CL, × CP, × EX, × SY. LB14: OP SP* ×.
        if (after is CL or CP or EX or SY || beforeSpaces == OP)
        {
            return Decision.NoBreak;
        }

        // LB15a: (sot | BK | CR | LF | NL | OP | QU | GL | SP | ZW) [\p{Pi}&QU] SP* ×.
        if (beforeSpaces == QUPi && (last == 0 || classes[last - 1] is BK or CR or LF or NL or OP or QU or QUPi or QUPf or GL or SP or ZW))
        {
            return Decision.NoBreak;
        }

        // LB15b: × [\p{Pf}&QU] ( SP | GL | WJ | CL | QU | CP | EX | IS | SY | BK | CR | LF | NL | ZW | eot ).
        if (after == QUPf && (index + 1 == classes.Length
            || next is SP or GL or WJ or CL or QU or QUPi or QUPf or CP or EX or IS or SY or BK or CR or LF or NL or ZW))
        {
            return Decision.NoBreak;
        }

        // LB15c: SP ÷ IS NU. LB15d: × IS.
        if (after == IS)
        {
            return before == SP && next == NU ? Decision.Break : Decision.NoBreak;
        }

        // LB16: (CL | CP) SP* × NS. LB17: B2 SP* × B2.
        if ((after == NS && beforeSpaces is CL or CP) || (after == B2 && beforeSpaces == B2))
        {
            return Decision.NoBreak;
        }

        // LB18: SP ÷.
        if (before == SP)
        {
            return Decision.Break;
        }

        // LB19: × [QU - \p{Pi}], [QU - \p{Pf}] ×.
        if (after is QU or QUPf || before is QU or QUPi)
        {
            return Decision.NoBreak;
        }

        // LB19a: [^$EastAsian] × QU, × QU ( [^$EastAsian] | eot ), QU × [^$EastAsian], ( sot | [^$EastAsian] ) QU ×.
        if (after == QUPi && (!IsEastAsian(flags, index - 1) || index + 1 == classes.Length || !IsEastAsian(flags, index + 1)))
        {
            return Decision.NoBreak;
        }

        if (before == QUPf && (!IsEastAsian(flags, index) || index == 1 || !IsEastAsian(flags, index - 2)))
        {
            return Decision.NoBreak;
        }

        // LB20: ÷ CB, CB ÷.
        if (after == CB || before == CB)
        {
            return Decision.Break;
        }

        // LB20a: ( sot | BK | CR | LF | NL | SP | ZW | CB | GL ) ( HY | [‐] ) × AL.
        if (after == AL && (before == HY || (flags[index - 1] & Hyphen) != 0)
            && (index == 1 || classes[index - 2] is BK or CR or LF or NL or SP or ZW or CB or GL))
        {
            return Decision.NoBreak;
        }

        // LB21: × BA, × HY, × NS, BB ×.
        if (after is BA or HY or NS || before == BB)
        {
            return Decision.NoBreak;
        }

        // LB21a: HL ( HY | [ BA - $EastAsian ] ) × [^HL]. LB21b: SY × HL. LB22: × IN.
        if ((after != HL && index >= 2 && classes[index - 2] == HL && (before == HY || (before == BA && !IsEastAsian(flags, index - 1))))
            || (before == SY && after == HL)
            || after == IN)
        {
            return Decision.NoBreak;
        }

        // LB23: (AL | HL) × NU, NU × (AL | HL). LB23a: PR × (ID | EB | EM), (ID | EB | EM) × PO.
        // LB24: (PR | PO) × (AL | HL), (AL | HL) × (PR | PO).
        if ((before is AL or HL && after is NU or PR or PO)
            || (before == NU && after is AL or HL)
            || (before == PR && after is ID or EB or EM)
            || (before is ID or EB or EM && after == PO)
            || (before is PR or PO && after is AL or HL))
        {
            return Decision.NoBreak;
        }

        // LB25: NU ( SY | IS )* ( CL | CP )? × ( PO | PR ), NU ( SY | IS )* × NU, ( PO | PR ) × OP IS? NU, ( PO | PR | HY | IS ) × NU.
        if (after is PO or PR or NU)
        {
            int number = index - 1;
            if (after != NU && classes[number] is CL or CP)
            {
                number--;
            }

            while (number >= 0 && classes[number] is SY or IS)
            {
                number--;
            }

            if (number >= 0 && classes[number] == NU)
            {
                return Decision.NoBreak;
            }
        }

        if ((before is PO or PR && (after == NU || (after == OP && (next == NU || (next == IS && index + 2 < classes.Length && classes[index + 2] == NU)))))
            || (before is HY or IS && after == NU))
        {
            return Decision.NoBreak;
        }

        // LB26: JL × (JL | JV | H2 | H3), (JV | H2) × (JV | JT), (JT | H3) × JT.
        // LB27: (JL | JV | JT | H2 | H3) × PO, PR × (JL | JV | JT | H2 | H3).
        if ((before == JL && after is JL or JV or H2 or H3)
            || (before is JV or H2 && after is JV or JT)
            || (before is JT or H3 && after == JT)
            || (before is JL or JV or JT or H2 or H3 && after == PO)
            || (before == PR && after is JL or JV or JT or H2 or H3))
        {
            return Decision.NoBreak;
        }

        // LB28: (AL | HL) × (AL | HL).
        if (before is AL or HL && after is AL or HL)
        {
            return Decision.NoBreak;
        }

        // LB28a: AP × (AK | ◌ | AS), (AK | ◌ | AS) × (VF | VI), (AK | ◌ | AS) VI × (AK | ◌), (AK | ◌ | AS) × (AK | ◌ | AS) VF.
        if ((before == AP && IsAksara(classes, flags, index))
            || (IsAksara(classes, flags, index - 1) && after is VF or VI)
            || (before == VI && index >= 2 && IsAksara(classes, flags, index - 2) && (after == AK || (flags[index] & DottedCircle) != 0))
            || (IsAksara(classes, flags, index - 1) && IsAksara(classes, flags, index) && next == VF))
        {
            return Decision.NoBreak;
        }

        // LB29: IS × (AL | HL). LB30: (AL | HL | NU) × [OP - $EastAsian], [CP - $EastAsian] × (AL | HL | NU).
        if ((before == IS && after is AL or HL)
            || (before is AL or HL or NU && after == OP && !IsEastAsian(flags, index))
            || (before == CP && !IsEastAsian(flags, index - 1) && after is AL or HL or NU))
        {
            return Decision.NoBreak;
        }

        // LB30a: break between regional indicators only after an even number of them.
        if (before == RI && after == RI && indicators % 2 == 1)
        {
            return Decision.NoBreak;
        }

        // LB30b: EB × EM, [\p{Extended_Pictographic}&\p{Cn}] × EM. LB31: break everywhere else.
        return after == EM && (before == EB || (flags[index - 1] & LineBreakData.PictographicUnassigned) != 0)
            ? Decision.NoBreak
            : Decision.Break;
    }

    private static bool IsEastAsian(ReadOnlySpan<byte> flags, int index) => (flags[index] & LineBreakData.EastAsian) != 0;

    /// <summary>AK, AS or U+25CC DOTTED CIRCLE: the bases of rule LB28a.</summary>
    private static bool IsAksara(ReadOnlySpan<LineBreakClass> classes, ReadOnlySpan<byte> flags, int index) =>
        classes[index] is AK or AS || (flags[index] & DottedCircle) != 0;
}
