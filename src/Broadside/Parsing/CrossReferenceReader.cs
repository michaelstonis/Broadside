using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// Reads a file's cross-reference information from its end: <c>startxref</c>, then the section it points to, then each older
/// section through the trailer's <c>Prev</c> entry.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4, §7.5.5, §7.5.6 and §7.5.8. Every offset the file states is relative to the <c>%PDF-</c> header (§7.5.2). A
/// section is a classic table or a cross-reference stream (<see cref="XrefStreamReader"/>). The <c>Prev</c> chain is guarded by a
/// set of visited offsets, so a loop ends the chain with a diagnostic instead of hanging.
/// </para>
/// <para>
/// Hybrid files (§7.5.8.4): a table whose trailer has <c>XRefStm</c> is followed, in <see cref="CrossReference.Sections"/>, by the
/// cross-reference stream it names, so lookup consults the table, then that stream, then the older sections; a free entry in the
/// table gives way to the stream's entry for the same number ("A PDF reader shall look in the cross-reference stream first"). The stream's own
/// <c>Prev</c> is not followed ("not meaningful in hybrid-reference files", Table 17) and its dictionary is not a trailer.
/// </para>
/// <para>
/// Extension: issue #41 locates <c>startxref</c> when it is wrong and reconstructs the table when nothing here can be read
/// (<see cref="Read"/> returns <see langword="null"/>).
/// </para>
/// </remarks>
internal static class CrossReferenceReader
{
    /// <summary>How many bytes the backward search for <c>startxref</c> reads at a time.</summary>
    private const int TailWindow = 1024;

    private static readonly CosName XRefStm = new("XRefStm");

    /// <summary>Reads the cross-reference sections of <paramref name="source"/>.</summary>
    /// <param name="source">The file.</param>
    /// <param name="header">The header, whose offset every stated offset is relative to.</param>
    /// <param name="streams">The filter pipeline cross-reference streams are decoded with.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>The cross-reference information, or <see langword="null"/> when no section could be read.</returns>
    public static CrossReference? Read(PdfSource source, FileHeader header, StreamDecoder streams, DiagnosticSink diagnostics)
    {
        if (!TryReadStartxref(source, header, diagnostics, out long first))
        {
            return null;
        }

        var sections = new List<XrefSection>();
        var visited = new HashSet<long>();
        long? next = first;
        while (next is { } offset)
        {
            if (!visited.Add(offset))
            {
                diagnostics.Report(
                    DiagnosticCodes.XrefPrevLoop,
                    DiagnosticSeverity.Error,
                    "The Prev chain of cross-reference sections loops back to a section already read; the chain ends here.",
                    offset);
                break;
            }

            XrefSection? section = ReadSection(source, offset, isFirst: sections.Count == 0, streams, diagnostics);
            if (section is null)
            {
                break;
            }

            XrefSection? hybridStream = section.Kind == XrefSectionKind.Table
                ? ReadHybridStream(source, header, section, visited, streams, diagnostics)
                : null;
            if (hybridStream is not null)
            {
                sections.Add(section with { XRefStreamOffset = hybridStream.Offset });
                sections.Add(hybridStream);
            }
            else
            {
                sections.Add(section);
            }

            next = PreviousSectionOffset(section, header, diagnostics);
        }

        if (sections.Count == 0)
        {
            return null;
        }

        var crossReference = new CrossReference(sections);
        ReportInheritedTrailerEntries(crossReference, diagnostics);
        return crossReference;
    }

    /// <summary>
    /// Reports the document-level trailer entries the newest revision's trailer lacks and an older one supplied: §7.5.6 says an
    /// update's trailer shall repeat every entry of the previous trailer except <c>Prev</c>. The older value is used.
    /// </summary>
    private static void ReportInheritedTrailerEntries(CrossReference crossReference, DiagnosticSink diagnostics)
    {
        foreach (CosName key in crossReference.InheritedTrailerKeys)
        {
            if (key.Equals(KnownNames.Root) || key.Equals(KnownNames.Info) || key.Equals(KnownNames.Encrypt) || key.Equals(KnownNames.ID))
            {
                diagnostics.Report(
                    DiagnosticCodes.TrailerEntryFromOlderRevision,
                    DiagnosticSeverity.Warning,
                    $"The newest trailer has no {key} entry, which every update's trailer shall repeat; the entry of an older revision's trailer is used.",
                    crossReference.Sections[0].Offset);
            }
        }
    }

    /// <summary>Finds the last <c>startxref</c> in the file and reads the offset after it (§7.5.5).</summary>
    private static bool TryReadStartxref(PdfSource source, FileHeader header, DiagnosticSink diagnostics, out long offset)
    {
        offset = 0;
        long keyword = FindLast(source, "startxref"u8);
        if (keyword < 0)
        {
            diagnostics.Report(DiagnosticCodes.StartxrefMissing, DiagnosticSeverity.Error, "The file has no startxref keyword.");
            return false;
        }

        Span<byte> tail = stackalloc byte[TailWindow];
        tail = tail[..source.Read(keyword, tail)];
        var lexer = new CosLexer(tail, "startxref"u8.Length);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long stated) || stated > long.MaxValue - header.Offset)
        {
            diagnostics.Report(
                DiagnosticCodes.StartxrefInvalid,
                DiagnosticSeverity.Error,
                "The startxref keyword is not followed by a byte offset.",
                keyword);
            return false;
        }

        if (tail[lexer.Position..].IndexOf("%%EOF"u8) < 0)
        {
            diagnostics.Report(
                DiagnosticCodes.EndOfFileMarkerMissing,
                DiagnosticSeverity.Warning,
                "The startxref offset is not followed by the %%EOF marker.",
                keyword + lexer.Position);
        }

        offset = header.Offset + stated;
        return true;
    }

    /// <summary>Reads the section at <paramref name="offset"/>: a table when it starts with <c>xref</c>, else a stream.</summary>
    private static XrefSection? ReadSection(PdfSource source, long offset, bool isFirst, StreamDecoder streams, DiagnosticSink diagnostics)
    {
        ReadOnlySpan<byte> window = source.GetWindow(offset).Span;
        var lexer = new CosLexer(window);
        CosToken first = lexer.Next();
        bool isTable = StructureTokens.IsKeyword(window, first, "xref"u8);
        if (isTable || first.Kind == CosTokenKind.Integer)
        {
            if (first.Start != 0)
            {
                // The offset points at white-space or a comment before the section: read from the section itself.
                diagnostics.Report(
                    isFirst ? DiagnosticCodes.StartxrefInvalid : DiagnosticCodes.TrailerPrevInvalid,
                    DiagnosticSeverity.Warning,
                    "The offset points before the cross-reference section rather than at it.",
                    offset);
                offset += first.Start;
                window = window[first.Start..];
            }

            return isTable ? XrefTableReader.Read(window, offset, diagnostics) : XrefStreamReader.Read(source, offset, streams, diagnostics);
        }

        diagnostics.Report(
            isFirst ? DiagnosticCodes.StartxrefInvalid : DiagnosticCodes.TrailerPrevInvalid,
            DiagnosticSeverity.Error,
            isFirst
                ? "The startxref offset does not point at a cross-reference section."
                : "The Prev offset does not point at a cross-reference section.",
            offset);
        return null;
    }

    /// <summary>
    /// Reads the cross-reference stream a hybrid file's table names through its trailer's <c>XRefStm</c> entry (§7.5.8.4, Table 19).
    /// </summary>
    /// <returns>The stream section, whose trailer is empty, or <see langword="null"/> when there is none or it cannot be read.</returns>
    private static XrefSection? ReadHybridStream(
        PdfSource source,
        FileHeader header,
        XrefSection table,
        HashSet<long> visited,
        StreamDecoder streams,
        DiagnosticSink diagnostics)
    {
        if (!table.Trailer.TryGetValue(XRefStm, out CosObject? entry))
        {
            return null;
        }

        if (entry is not CosInteger { Value: >= 0 } stated || stated.Value > long.MaxValue - header.Offset)
        {
            diagnostics.Report(
                DiagnosticCodes.TrailerXRefStmInvalid,
                DiagnosticSeverity.Error,
                "The trailer's XRefStm entry is not a direct, non-negative integer; the cross-reference stream it names is not read.",
                table.Offset);
            return null;
        }

        long offset = header.Offset + stated.Value;
        if (!visited.Add(offset))
        {
            diagnostics.Report(
                DiagnosticCodes.XrefPrevLoop,
                DiagnosticSeverity.Error,
                "The trailer's XRefStm entry names a cross-reference section already read; it is not read again.",
                offset);
            return null;
        }

        ReadOnlySpan<byte> window = source.GetWindow(offset).Span;
        CosToken first = new CosLexer(window).Next();
        if (first.Kind != CosTokenKind.Integer)
        {
            diagnostics.Report(
                DiagnosticCodes.TrailerXRefStmInvalid,
                DiagnosticSeverity.Error,
                "The trailer's XRefStm offset does not point at a cross-reference stream; it is not read.",
                offset);
            return null;
        }

        if (first.Start != 0)
        {
            diagnostics.Report(
                DiagnosticCodes.TrailerXRefStmInvalid,
                DiagnosticSeverity.Warning,
                "The XRefStm offset points before the cross-reference stream rather than at it.",
                offset);
            offset += first.Start;
        }

        return XrefStreamReader.Read(source, offset, streams, diagnostics) is { } section
            ? section with { Trailer = new CosDictionary() }
            : null;
    }

    /// <summary>Returns the absolute offset of the section before <paramref name="section"/>, from its trailer's <c>Prev</c> entry.</summary>
    private static long? PreviousSectionOffset(XrefSection section, FileHeader header, DiagnosticSink diagnostics)
    {
        if (!section.Trailer.TryGetValue(KnownNames.Prev, out CosObject? prev))
        {
            return null;
        }

        if (prev is CosInteger { Value: >= 0 } stated && stated.Value <= long.MaxValue - header.Offset)
        {
            return header.Offset + stated.Value;
        }

        diagnostics.Report(
            DiagnosticCodes.TrailerPrevInvalid,
            DiagnosticSeverity.Error,
            "The trailer's Prev entry is not a direct, non-negative integer; older sections are not read.",
            section.Offset);
        return null;
    }

    /// <summary>Finds the last occurrence of <paramref name="value"/> in the file, searching backwards one window at a time.</summary>
    private static long FindLast(PdfSource source, ReadOnlySpan<byte> value)
    {
        Span<byte> buffer = stackalloc byte[TailWindow];
        long end = source.Length;
        while (end > 0)
        {
            long start = Math.Max(0, end - TailWindow);
            Span<byte> window = buffer[..source.Read(start, buffer[..(int)(end - start)])];
            int found = window.LastIndexOf(value);
            if (found >= 0)
            {
                return start + found;
            }

            if (start == 0)
            {
                break;
            }

            // Overlap the windows so a keyword split across a boundary is still found.
            end = start + value.Length - 1;
        }

        return -1;
    }
}
