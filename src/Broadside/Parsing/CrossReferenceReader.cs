using Broadside.Diagnostics;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// Reads a file's cross-reference information from its end: <c>startxref</c>, then the section it points to, then each older
/// section through the trailer's <c>Prev</c> entry.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4, §7.5.5 and §7.5.6. Every offset the file states is relative to the <c>%PDF-</c> header (§7.5.2). The
/// <c>Prev</c> chain is guarded by a set of visited offsets, so a loop ends the chain with a diagnostic instead of hanging.
/// </para>
/// <para>
/// Extension: issue #39 reads cross-reference streams in <see cref="ReadStreamSection"/> and follows a hybrid file's
/// <c>XRefStm</c> before <c>Prev</c>; issue #41 locates <c>startxref</c> when it is wrong and reconstructs the table when nothing
/// here can be read (<see cref="Read"/> returns <see langword="null"/>).
/// </para>
/// </remarks>
internal static class CrossReferenceReader
{
    /// <summary>How many bytes the backward search for <c>startxref</c> reads at a time.</summary>
    private const int TailWindow = 1024;

    /// <summary>Reads the cross-reference sections of <paramref name="source"/>.</summary>
    /// <param name="source">The file.</param>
    /// <param name="header">The header, whose offset every stated offset is relative to.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>The cross-reference information, or <see langword="null"/> when no section could be read.</returns>
    public static CrossReference? Read(PdfSource source, FileHeader header, DiagnosticSink diagnostics)
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

            XrefSection? section = ReadSection(source, offset, isFirst: sections.Count == 0, diagnostics);
            if (section is null)
            {
                break;
            }

            sections.Add(section);
            next = PreviousSectionOffset(section, header, diagnostics);
        }

        return sections.Count == 0 ? null : new CrossReference(sections);
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
    private static XrefSection? ReadSection(PdfSource source, long offset, bool isFirst, DiagnosticSink diagnostics)
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

            return isTable ? XrefTableReader.Read(window, offset, diagnostics) : ReadStreamSection(source, offset, diagnostics);
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

    /// <summary>Reads a cross-reference stream section (§7.5.8). Issue #39.</summary>
    private static XrefSection? ReadStreamSection(PdfSource source, long offset, DiagnosticSink diagnostics)
    {
        _ = source;
        diagnostics.Report(
            DiagnosticCodes.XrefStreamUnsupported,
            DiagnosticSeverity.Error,
            "The cross-reference section is a cross-reference stream, which this version cannot read yet.",
            offset);
        return null;
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
