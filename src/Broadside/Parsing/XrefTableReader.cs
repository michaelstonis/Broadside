using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Reads one classic cross-reference section: <c>xref</c>, its subsections, and the trailer dictionary after <c>trailer</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4 and §7.5.5. Entries are read as tokens, not as fixed 20-byte records, so a table with wrong line endings or
/// padding still reads (as pdf.js and PDFBox do), with one <see cref="DiagnosticCodes.XrefEntryFormatInvalid"/> per section. A
/// subsection numbered from 1 whose first entry is the head of the free list is renumbered from 0, as pdf.js does.
/// </para>
/// <para>
/// An in-use entry with offset 0 is read as free (as pdf.js does) with a diagnostic, and object number 0 is always free (§7.5.4)
/// and never appears in <see cref="XrefSection.Entries"/>.
/// </para>
/// </remarks>
internal static class XrefTableReader
{
    /// <summary>Reads the section that starts at the beginning of <paramref name="window"/>, whose first token is <c>xref</c>.</summary>
    /// <param name="window">The bytes from the section's <c>xref</c> keyword on.</param>
    /// <param name="sectionOffset">The absolute offset of <paramref name="window"/>[0].</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>The section, or <see langword="null"/> when it has no trailer dictionary.</returns>
    public static XrefSection? Read(ReadOnlySpan<byte> window, long sectionOffset, DiagnosticSink diagnostics)
    {
        var lexer = new CosLexer(window);
        _ = lexer.Next(); // xref
        var entries = new Dictionary<int, XrefEntry>();
        if (!ReadSubsections(ref lexer, entries, sectionOffset, diagnostics))
        {
            int trailerKeyword = window[lexer.Position..].IndexOf("trailer"u8);
            if (trailerKeyword < 0)
            {
                diagnostics.Report(
                    DiagnosticCodes.TrailerMissing,
                    DiagnosticSeverity.Error,
                    "The cross-reference section has no trailer keyword.",
                    sectionOffset);
                return null;
            }

            lexer.Position += trailerKeyword + "trailer"u8.Length;
        }

        var repairs = new CosRepairLog(keepAll: true);
        var parser = new CosParser(window, repairs, lexer.Position);
        CosObject trailer = parser.ParseObject();
        foreach (CosRepair repair in repairs.All)
        {
            diagnostics.Report(repair.Code, DiagnosticSeverity.Warning, repair.Message, sectionOffset + repair.Offset);
        }

        if (trailer is not CosDictionary dictionary)
        {
            diagnostics.Report(
                DiagnosticCodes.TrailerMissing,
                DiagnosticSeverity.Error,
                "The trailer keyword is not followed by a dictionary.",
                sectionOffset + lexer.Position);
            return null;
        }

        return new XrefSection(sectionOffset, sectionOffset + parser.Position, XrefSectionKind.Table, entries, dictionary);
    }

    /// <summary>Reads subsections up to and including the <c>trailer</c> keyword.</summary>
    /// <returns><see langword="false"/> when the table is malformed; the lexer is then somewhere inside it.</returns>
    private static bool ReadSubsections(ref CosLexer lexer, Dictionary<int, XrefEntry> entries, long sectionOffset, DiagnosticSink diagnostics)
    {
        bool formatReported = false;
        bool headerReported = false;
        while (true)
        {
            CosToken next = lexer.Peek();
            if (StructureTokens.IsKeyword(lexer.Source, next, "trailer"u8))
            {
                lexer.Position = next.End;
                return true;
            }

            long subsectionStart = sectionOffset + next.Start;
            bool readFirst = StructureTokens.TryReadUnsigned(ref lexer, out long first);
            int firstEnd = lexer.Position;
            int countStart = lexer.Peek().Start;
            if (!readFirst || !StructureTokens.TryReadUnsigned(ref lexer, out long count))
            {
                diagnostics.Report(
                    DiagnosticCodes.XrefSectionInvalid,
                    DiagnosticSeverity.Error,
                    "Expected a subsection header (first object number and entry count) or the trailer keyword.",
                    subsectionStart);
                return false;
            }

            if (!headerReported && !IsSubsectionHeaderLine(lexer.Source, next.Start, firstEnd, countStart, lexer.Position))
            {
                // §7.5.4: "The subsection shall begin with a line containing only two integers separated by a SPACE (20h) and
                // terminated by an end-of-line marker". Read as tokens anyway.
                headerReported = true;
                diagnostics.Report(
                    DiagnosticCodes.XrefSubsectionHeaderInvalid,
                    DiagnosticSeverity.Warning,
                    "A subsection header is not a line of exactly two integers separated by one space; it is read as tokens.",
                    subsectionStart);
            }

            for (long index = 0; index < count; index++)
            {
                int entryPosition = lexer.Peek().Start;
                long entryStart = sectionOffset + entryPosition;
                if (!TryReadEntry(ref lexer, out XrefEntry entry) || first + index > int.MaxValue)
                {
                    diagnostics.Report(
                        DiagnosticCodes.XrefEntryInvalid,
                        DiagnosticSeverity.Error,
                        "Expected a cross-reference entry: a 10-digit offset, a 5-digit generation and n or f.",
                        entryStart);
                    return false;
                }

                if (!formatReported && !IsTwentyByteEntry(lexer.Source[entryPosition..]))
                {
                    // §7.5.4: "each entry shall be exactly 20 bytes long, including the end-of-line marker". Read as tokens anyway.
                    formatReported = true;
                    diagnostics.Report(
                        DiagnosticCodes.XrefEntryFormatInvalid,
                        DiagnosticSeverity.Warning,
                        "A cross-reference entry is not 20 bytes long (10-digit offset, space, 5-digit generation, space, n or f, two-byte end of line); the section is read entry by entry.",
                        entryStart);
                }

                if (index == 0 && first == 1 && entry is { Kind: XrefEntryKind.Free, Offset: 0, Generation: CosReference.MaxGeneration })
                {
                    // The head of the free list is object 0 (§7.5.4); a writer that numbered the subsection from 1 shifted every
                    // entry by one. Renumber from 0, as pdf.js does.
                    first = 0;
                    diagnostics.Report(
                        DiagnosticCodes.XrefSubsectionNumberingInvalid,
                        DiagnosticSeverity.Warning,
                        "The subsection starts at object 1, but its first entry is the head of the free list, which is object 0; the subsection is read as starting at 0.",
                        subsectionStart);
                }

                long number = first + index;
                if (entry.Kind == XrefEntryKind.InUse && entry.Offset == 0)
                {
                    diagnostics.Report(
                        DiagnosticCodes.XrefEntryOffsetInvalid,
                        DiagnosticSeverity.Error,
                        "An in-use entry has offset 0, where the header is; the object is read as free.",
                        entryStart);
                    entry = XrefEntry.Free;
                }

                // §7.5.4: object number 0 is always free and is the head of the free list; it never names an object.
                if (number != 0)
                {
                    entries.TryAdd((int)number, entry);
                }
            }
        }
    }

    /// <summary>
    /// Whether the subsection header from <paramref name="start"/> to <paramref name="countEnd"/> is a line of its own holding two
    /// integers separated by exactly one space (§7.5.4). Spaces between the count and the end of the line are tolerated: many writers
    /// (Acrobat among them) end the line with <c>SP EOL</c>, as an entry may, and no reader misreads it.
    /// </summary>
    private static bool IsSubsectionHeaderLine(ReadOnlySpan<byte> source, int start, int firstEnd, int countStart, int countEnd)
    {
        if (start == 0 || source[start - 1] is not ((byte)'\r' or (byte)'\n') || countStart != firstEnd + 1 || source[firstEnd] != (byte)' ')
        {
            return false;
        }

        int end = countEnd;
        while (end < source.Length && source[end] == (byte)' ')
        {
            end++;
        }

        return end < source.Length && source[end] is (byte)'\r' or (byte)'\n';
    }

    /// <summary>Whether <paramref name="entry"/> starts with the exact 20-byte form of §7.5.4: <c>nnnnnnnnnn ggggg n</c> plus a two-byte end of line.</summary>
    private static bool IsTwentyByteEntry(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 20 || entry[10] != (byte)' ' || entry[16] != (byte)' ' || entry[17] is not ((byte)'n' or (byte)'f'))
        {
            return false;
        }

        for (int index = 0; index < 16; index++)
        {
            if (index != 10 && entry[index] is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return (entry[18], entry[19]) is ((byte)' ', (byte)'\r') or ((byte)' ', (byte)'\n') or ((byte)'\r', (byte)'\n');
    }

    private static bool TryReadEntry(ref CosLexer lexer, out XrefEntry entry)
    {
        entry = XrefEntry.Free;
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long offset)
            || !StructureTokens.TryReadUnsigned(ref lexer, out long generation)
            || generation > CosReference.MaxGeneration)
        {
            return false;
        }

        CosToken kind = lexer.Next();
        if (StructureTokens.IsKeyword(lexer.Source, kind, "n"u8))
        {
            entry = new XrefEntry(XrefEntryKind.InUse, offset, (int)generation);
            return true;
        }

        if (StructureTokens.IsKeyword(lexer.Source, kind, "f"u8))
        {
            entry = new XrefEntry(XrefEntryKind.Free, offset, (int)generation);
            return true;
        }

        return false;
    }
}
