using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Reads one classic cross-reference section: <c>xref</c>, its subsections, and the trailer dictionary after <c>trailer</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4 and §7.5.5. Entries are read as tokens, not as fixed 20-byte records, so a table with wrong line endings or
/// padding still reads (as pdf.js and PDFBox do); whether such a table is reported is issue #41's decision.
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
        while (true)
        {
            CosToken next = lexer.Peek();
            if (StructureTokens.IsKeyword(lexer.Source, next, "trailer"u8))
            {
                lexer.Position = next.End;
                return true;
            }

            long subsectionStart = sectionOffset + next.Start;
            if (!StructureTokens.TryReadUnsigned(ref lexer, out long first) || !StructureTokens.TryReadUnsigned(ref lexer, out long count))
            {
                diagnostics.Report(
                    DiagnosticCodes.XrefSectionInvalid,
                    DiagnosticSeverity.Error,
                    "Expected a subsection header (first object number and entry count) or the trailer keyword.",
                    subsectionStart);
                return false;
            }

            for (long index = 0; index < count; index++)
            {
                long entryStart = sectionOffset + lexer.Peek().Start;
                long number = first + index;
                if (!TryReadEntry(ref lexer, out XrefEntry entry) || number > int.MaxValue)
                {
                    diagnostics.Report(
                        DiagnosticCodes.XrefEntryInvalid,
                        DiagnosticSeverity.Error,
                        "Expected a cross-reference entry: a 10-digit offset, a 5-digit generation and n or f.",
                        entryStart);
                    return false;
                }

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
