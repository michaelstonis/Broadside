using System.Buffers;
using Broadside.Diagnostics;
using Broadside.IO;

namespace Broadside.Parsing;

/// <summary>Where the <c>%PDF-</c> header is and the version it states.</summary>
/// <remarks>
/// ISO 32000-2 §7.5.2. Byte offsets in the file (<c>startxref</c>, cross-reference entries, <c>Prev</c>) are counted from the
/// percent sign of the header, and arbitrary bytes may precede it (NOTE 1), so every offset the file states is relative to
/// <see cref="Offset"/>. A file whose writer counted from byte 0 anyway is repaired by the loader's object search and the
/// nearest-section search for <c>startxref</c> (issue #41). A version followed by anything but an end-of-line marker is read with
/// <see cref="DiagnosticCodes.HeaderInvalid"/> (issue #47). The comment line of at least four bytes of 128 or more that shall follow
/// the header of a file with binary data is checked in strict mode only (<see cref="DiagnosticCodes.HeaderBinaryCommentMissing"/>):
/// when it is missing, deciding whether the file holds binary data can take a scan of the whole file, a cost lenient reading does
/// not pay for a deviation that needs no repair.
/// </remarks>
/// <param name="Offset">The absolute position of the <c>%</c> of <c>%PDF-</c>; 0 when no header was found.</param>
/// <param name="Version">The version the header states, or <see langword="null"/> when it is missing or malformed.</param>
internal readonly record struct FileHeader(long Offset, PdfVersion? Version)
{
    /// <summary>How far into the file the header is searched for, as pdf.js does.</summary>
    public const int SearchLength = 1024;

    /// <summary>The version assumed when neither the header nor the catalog states a valid one, as PDFBox does.</summary>
    public static readonly PdfVersion DefaultVersion = new(1, 4);

    private static readonly SearchValues<byte> VersionCharacters = SearchValues.Create("0123456789."u8);

    /// <summary>Finds the header in the first <see cref="SearchLength"/> bytes of <paramref name="source"/>.</summary>
    /// <param name="source">The file.</param>
    /// <param name="diagnostics">Where to report a missing or malformed header.</param>
    /// <returns>The header.</returns>
    public static FileHeader Locate(PdfSource source, DiagnosticSink diagnostics)
    {
        Span<byte> start = stackalloc byte[SearchLength];
        start = start[..source.Read(0, start)];
        int marker = start.IndexOf("%PDF-"u8);
        if (marker < 0)
        {
            diagnostics.Report(
                DiagnosticCodes.HeaderMissing,
                DiagnosticSeverity.Warning,
                "The file does not start with a %PDF- header; offsets are counted from byte 0 and the version is taken from the catalog.",
                offset: 0);
            return new FileHeader(0, null);
        }

        ReadOnlySpan<byte> afterMarker = start[(marker + "%PDF-"u8.Length)..];
        int end = afterMarker.IndexOfAnyExcept(VersionCharacters);
        ReadOnlySpan<byte> versionText = end < 0 ? afterMarker : afterMarker[..end];
        if (PdfVersion.TryParse(versionText, out PdfVersion version) && version.Major is 1 or 2)
        {
            // "The file header shall consist of %PDF-1.n or %PDF-2.n followed by a single EOL marker" (added in 2020).
            if (end >= 0 && afterMarker[end] is not ((byte)'\r' or (byte)'\n'))
            {
                diagnostics.Report(
                    DiagnosticCodes.HeaderInvalid,
                    DiagnosticSeverity.Warning,
                    "The header's version shall be followed by an end-of-line marker; the version is read and the rest of the line is ignored.",
                    offset: marker);
            }

            if (diagnostics.IsStrict)
            {
                CheckBinaryComment(source, start, marker, diagnostics);
            }

            return new FileHeader(marker, version);
        }

        diagnostics.Report(
            DiagnosticCodes.HeaderVersionInvalid,
            DiagnosticSeverity.Warning,
            "The header does not state a version of the form 1.n or 2.n; the version is taken from the catalog.",
            offset: marker);
        return new FileHeader(marker, null);
    }

    /// <summary>
    /// §7.5.2: "If a PDF file contains binary data ... the header line shall be immediately followed by a comment line containing at
    /// least four binary characters", bytes of 128 or more.
    /// </summary>
    private static void CheckBinaryComment(PdfSource source, ReadOnlySpan<byte> start, int marker, DiagnosticSink diagnostics)
    {
        ReadOnlySpan<byte> rest = start[marker..];
        int headerEnd = rest.IndexOfAny((byte)'\r', (byte)'\n');
        if (headerEnd >= 0)
        {
            int lineStart = headerEnd + (rest[headerEnd] == '\r' && headerEnd + 1 < rest.Length && rest[headerEnd + 1] == '\n' ? 2 : 1);
            ReadOnlySpan<byte> line = rest[lineStart..];
            int lineEnd = line.IndexOfAny((byte)'\r', (byte)'\n');
            line = lineEnd < 0 ? line : line[..lineEnd];
            if (!line.IsEmpty && line[0] == '%' && CountBinary(line) >= 4)
            {
                return;
            }
        }

        if (HoldsBinaryData(source, marker))
        {
            diagnostics.Report(
                DiagnosticCodes.HeaderBinaryCommentMissing,
                DiagnosticSeverity.Warning,
                "The file holds binary data, so the header line shall be immediately followed by a comment line with at least four bytes of 128 or more; it is not.",
                offset: marker);
        }
    }

    private static int CountBinary(ReadOnlySpan<byte> line)
    {
        int count = 0;
        foreach (byte b in line)
        {
            count += b >= 0x80 ? 1 : 0;
        }

        return count;
    }

    /// <summary>Whether any byte from the header on is 128 or more; reads the file in windows and stops at the first.</summary>
    private static bool HoldsBinaryData(PdfSource source, long from)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            for (long offset = from; offset < source.Length;)
            {
                int read = source.Read(offset, buffer);
                if (read <= 0)
                {
                    return false;
                }

                if (buffer.AsSpan(0, read).IndexOfAnyInRange((byte)0x80, (byte)0xFF) >= 0)
                {
                    return true;
                }

                offset += read;
            }

            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
