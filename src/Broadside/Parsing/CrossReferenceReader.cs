using System.Globalization;
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
/// Repair (issue #41): a <c>startxref</c> or <c>Prev</c> offset that points at neither <c>xref</c> nor <c>N G obj</c> reads the
/// nearest section found by a <see cref="FileScan"/>; when nothing can be read, <see cref="Read"/> returns <see langword="null"/>
/// and the caller rebuilds the table with <see cref="CrossReferenceReconstructor"/>.
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
    /// <param name="scan">
    /// The scan of the file a wrong <c>startxref</c> or <c>Prev</c> offset is repaired from, run only when one is wrong; a new one
    /// when <see langword="null"/>.
    /// </param>
    /// <returns>The cross-reference information, or <see langword="null"/> when no section could be read.</returns>
    public static CrossReference? Read(PdfSource source, FileHeader header, StreamDecoder streams, DiagnosticSink diagnostics, Lazy<FileScan>? scan = null)
    {
        scan ??= new Lazy<FileScan>(() => FileScan.Run(source));
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

            XrefSection? section = ReadSection(source, offset, isFirst: sections.Count == 0, streams, diagnostics, scan, visited);
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
            diagnostics.Report(
                DiagnosticCodes.StartxrefMissing,
                DiagnosticSeverity.Warning,
                "The file has no startxref keyword; the cross-reference information is rebuilt by scanning the file.");
            return false;
        }

        Span<byte> tail = stackalloc byte[TailWindow];
        tail = tail[..source.Read(keyword, tail)];
        var lexer = new CosLexer(tail, "startxref"u8.Length);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long stated) || stated > long.MaxValue - header.Offset)
        {
            diagnostics.Report(
                DiagnosticCodes.StartxrefInvalid,
                DiagnosticSeverity.Warning,
                "The startxref keyword is not followed by a byte offset; the cross-reference information is rebuilt by scanning the file.",
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

    /// <summary>
    /// Reads the section at <paramref name="offset"/>: a table when it starts with <c>xref</c>, a stream when it starts with
    /// <c>N G obj</c>. When neither is there, reads the section whose start is nearest to <paramref name="offset"/> instead (issue
    /// #41, as PDFBox does): a table wins a tie, and a section already read is never chosen again.
    /// </summary>
    private static XrefSection? ReadSection(
        PdfSource source,
        long offset,
        bool isFirst,
        StreamDecoder streams,
        DiagnosticSink diagnostics,
        Lazy<FileScan>? scan,
        HashSet<long> visited)
    {
        ReadOnlySpan<byte> window = source.GetWindow(offset).Span;
        var lexer = new CosLexer(window);
        CosToken first = lexer.Next();
        bool isTable = StructureTokens.IsKeyword(window, first, "xref"u8);
        bool isStream = !isTable
            && first.Kind == CosTokenKind.Integer
            && StructureTokens.TryReadUnsigned(ref lexer, out _)
            && StructureTokens.TryReadKeyword(ref lexer, "obj"u8);
        if (isTable || isStream)
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

            return isTable ? ReadTable(source, offset, diagnostics) : XrefStreamReader.Read(source, offset, streams, diagnostics);
        }

        string stated = isFirst ? "startxref" : "Prev";
        if (scan is not null && FindNearestSection(scan.Value, offset, visited) is { } nearest)
        {
            diagnostics.Report(
                isFirst ? DiagnosticCodes.StartxrefInvalid : DiagnosticCodes.TrailerPrevInvalid,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The {stated} offset does not point at a cross-reference section; the section nearest to it, at offset {nearest}, is read instead."),
                offset);
            visited.Add(nearest);
            return ReadSection(source, nearest, isFirst, streams, diagnostics, scan: null, visited);
        }

        diagnostics.Report(
            isFirst ? DiagnosticCodes.StartxrefInvalid : DiagnosticCodes.TrailerPrevInvalid,
            isFirst ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
            isFirst
                ? "The startxref offset does not point at a cross-reference section, and the file has none; the cross-reference information is rebuilt by scanning the file."
                : "The Prev offset does not point at a cross-reference section; older sections are not read.",
            offset);
        return null;
    }

    /// <summary>
    /// Returns the start of the cross-reference section nearest to <paramref name="offset"/> that has not been read: an <c>xref</c>
    /// keyword, or the header of an object whose dictionary names the type <c>/XRef</c>.
    /// </summary>
    private static long? FindNearestSection(FileScan scan, long offset, HashSet<long> visited)
    {
        long? nearest = null;
        long distance = long.MaxValue;
        foreach (long table in scan.XrefKeywords)
        {
            Consider(table);
        }

        foreach (long name in scan.XrefStreamNames)
        {
            if (scan.TryFindObjectBefore(name, out ScannedObject header))
            {
                Consider(header.Offset);
            }
        }

        return nearest;

        // Tables are considered first, so on a tie (equal distance) the table wins.
        void Consider(long candidate)
        {
            long candidateDistance = Math.Abs(candidate - offset);
            if (candidateDistance < distance && !visited.Contains(candidate))
            {
                nearest = candidate;
                distance = candidateDistance;
            }
        }
    }

    /// <summary>
    /// Reads the table section at <paramref name="offset"/> in a window large enough for all of it, whatever its size: a windowed
    /// source is read again in a larger window while the section does not end inside the window (issue #45). Deviations are held
    /// back until the window is known to be large enough, so a window boundary is never reported as damage.
    /// </summary>
    private static XrefSection? ReadTable(PdfSource source, long offset, DiagnosticSink diagnostics) =>
        source.ReadGrowing(offset, (ReadOnlySpan<byte> window, bool final, out XrefSection? section) =>
        {
            var attempt = new DiagnosticSink(strict: false);
            DiagnosticException? failure = null;
            try
            {
                section = XrefTableReader.Read(window, offset, attempt);
            }
            catch (DiagnosticException exception)
            {
                failure = exception;
                section = null;
            }

            // The token after the section must be in the window too: a trailer dictionary followed by "stream" is not a trailer.
            Diagnostic[] found = attempt.Snapshot();
            if (!final && (found.Length > 0 || section is null || !StructureTokens.NextTokenEndsInside(window, (int)(section.End - offset))))
            {
                return false;
            }

            foreach (Diagnostic diagnostic in found)
            {
                diagnostics.Report(diagnostic.Code, diagnostic.Severity, diagnostic.Message, diagnostic.Offset, diagnostic.ObjectReference);
            }

            if (failure is not null)
            {
                throw failure;
            }

            return true;
        });

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
