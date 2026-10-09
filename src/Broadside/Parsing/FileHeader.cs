using System.Buffers;
using Broadside.Diagnostics;
using Broadside.IO;

namespace Broadside.Parsing;

/// <summary>Where the <c>%PDF-</c> header is and the version it states.</summary>
/// <remarks>
/// ISO 32000-2 §7.5.2. Byte offsets in the file (<c>startxref</c>, cross-reference entries, <c>Prev</c>) are counted from the
/// percent sign of the header, and arbitrary bytes may precede it (NOTE 1), so every offset the file states is relative to
/// <see cref="Offset"/>. Issue #41 adds the retry with unshifted offsets for files whose writer counted from byte 0 anyway.
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
            return new FileHeader(marker, version);
        }

        diagnostics.Report(
            DiagnosticCodes.HeaderVersionInvalid,
            DiagnosticSeverity.Warning,
            "The header does not state a version of the form 1.n or 2.n; the version is taken from the catalog.",
            offset: marker);
        return new FileHeader(marker, null);
    }
}
