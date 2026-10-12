using Broadside.Diagnostics;
using Broadside.IO;

namespace Broadside.Parsing;

/// <summary>Finds where each revision of a file ends: just past the <c>%%EOF</c> marker that terminates its trailer.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.5 and §7.5.6: "Each trailer shall be terminated by its own end-of-file (%%EOF) marker." Revision k is the bytes
/// from the start of the file to the end of that marker, including the end-of-line marker after it when there is one, which is
/// what a signature's byte range covers (§12.8.1).
/// </para>
/// <para>
/// The marker is looked for right after the trailer of the revision's last section in the file: <c>startxref</c>, its offset, then
/// <c>%%EOF</c>. A revision whose marker is missing ends after the <c>startxref</c> offset (or after the trailer when that is
/// missing too) with a diagnostic; for the newest revision the reader of <c>startxref</c> has already reported it.
/// </para>
/// </remarks>
internal static class RevisionReader
{
    /// <summary>Returns the absolute end offset of each revision of <paramref name="crossReference"/>, oldest first.</summary>
    /// <param name="source">The file.</param>
    /// <param name="crossReference">The cross-reference information.</param>
    /// <param name="diagnostics">Where to report a missing marker.</param>
    /// <returns>One exclusive end offset per revision.</returns>
    public static long[] ReadEnds(PdfSource source, CrossReference crossReference, DiagnosticSink diagnostics)
    {
        IReadOnlyList<XrefRevision> revisions = crossReference.Revisions;
        long[] ends = new long[revisions.Count];
        for (int index = 0; index < revisions.Count; index++)
        {
            bool newest = index == revisions.Count - 1;
            if (revisions[index].LastInFile.IsReconstructed)
            {
                // A rebuilt cross-reference has no trailer of its own to end at: the one revision is the whole file.
                ends[index] = source.Length;
                continue;
            }

            ends[index] = FindEnd(source, revisions[index].LastInFile.End, reportMissing: !newest, diagnostics);
        }

        return ends;
    }

    private static long FindEnd(PdfSource source, long trailerEnd, bool reportMissing, DiagnosticSink diagnostics)
    {
        ReadOnlySpan<byte> window = source.GetWindow(trailerEnd).Span;
        int position = SkipWhiteSpace(window, 0);
        if (window[position..].StartsWith("startxref"u8))
        {
            position = SkipWhiteSpace(window, position + "startxref"u8.Length);
            int digits = position;
            while (position < window.Length && char.IsAsciiDigit((char)window[position]))
            {
                position++;
            }

            int afterOffset = position;
            position = SkipWhiteSpace(window, position);
            if (position > digits && window[position..].StartsWith("%%EOF"u8))
            {
                position += "%%EOF"u8.Length;
                if (window[position..].StartsWith("\r\n"u8))
                {
                    position += 2;
                }
                else if (position < window.Length && window[position] is (byte)'\r' or (byte)'\n')
                {
                    position++;
                }

                return trailerEnd + position;
            }

            position = afterOffset;
        }
        else
        {
            position = 0;
        }

        if (reportMissing)
        {
            diagnostics.Report(
                DiagnosticCodes.EndOfFileMarkerMissing,
                DiagnosticSeverity.Warning,
                "The trailer of an earlier revision is not followed by startxref, its offset and the %%EOF marker; the revision ends here.",
                trailerEnd + position);
        }

        return trailerEnd + position;
    }

    private static int SkipWhiteSpace(ReadOnlySpan<byte> window, int position)
    {
        while (position < window.Length && window[position] is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ')
        {
            position++;
        }

        return position;
    }
}
