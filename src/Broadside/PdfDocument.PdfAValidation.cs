using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <content>
/// PDF/A validation of any document (issue #651): the checks of a PDF/A save, run on the file as it is, without converting or
/// changing it. ISO 19005-2, ISO 19005-3 and ISO 19005-4, cited by veraPDF validation-profile rule identifiers.
/// </content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Validates the document against a PDF/A level without converting it: the level the file claims in its XMP metadata
    /// (<c>pdfaid:part</c> and <c>pdfaid:conformance</c>), or <paramref name="conformance"/> when given. Nothing is changed.
    /// </summary>
    /// <param name="conformance">The level to check against; <see langword="null"/> (the default) for the level the file claims.</param>
    /// <returns>The level checked, whether the file claims it, and the rules it breaks among those checked.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is <see cref="PdfAConformance.None"/> or not a level.</exception>
    /// <exception cref="InvalidOperationException">No level is given and the file does not claim PDF/A.</exception>
    /// <exception cref="NotSupportedException">No level is given and the file claims one Broadside cannot check (PDF/A-1, for example).</exception>
    /// <remarks>
    /// <para>
    /// ISO 19005-2, -3 and -4 (veraPDF PDF/A-2B, -2U, -3B, -3U, -4 and -4F validation profiles). The rules are those a PDF/A save
    /// checks (<see cref="PdfSaveOptions.PdfAConformance"/>), and where a save writes something conformant by itself, the file's own is
    /// checked: its header and binary comment (6.1.2-1, 6.1.2-2), its last trailer's <c>ID</c> (6.1.3-1), its encryption (6.1.3-2,
    /// reported while what decrypts is still checked), the bytes after its last <c>%%EOF</c> (6.1.3-3), its output intents (6.2.3-1,
    /// and device colour without a PDF/A output intent, 6.2.4.3-2 to -4), its XMP packet and identification (6.6.2.1, 6.6.4; ISO
    /// 19005-4 6.7.2, 6.7.3), and for PDF/A-4 its document information dictionary and catalog version (6.1.3-4, 6.1.3-5, 6.1.12-1).
    /// An incremental update is checked as its last revision, as veraPDF does.
    /// </para>
    /// <para>
    /// Leniency never hides non-conformance (ADR 0005): every repair the parser made of the file structure or the object syntax is a
    /// violation, a wrong stream <c>Length</c> under 6.1.7.1-1 and the others (a cross-reference table rebuilt by scanning, a wrong
    /// <c>startxref</c>) under the ISO 32000-2 clause their diagnostic cites, as <c>ISO 32000-2 7.5.5-1</c>. A repaired file is never
    /// conformant. The byte-level rules read the bytes the document was read from: changes not yet saved are checked as objects only.
    /// </para>
    /// <para>
    /// A read: validation never throws for a non-conforming file and may run while other threads render the document (it uses the
    /// same loaders). <see cref="PdfAValidation.RulesNotChecked"/> lists the veraPDF rules left to veraPDF (issue #533).
    /// </para>
    /// </remarks>
    public PdfAValidation ValidatePdfA(PdfAConformance? conformance = null)
    {
        if (conformance is { } given && (given == PdfAConformance.None || !Enum.IsDefined(given)))
        {
            throw new ArgumentOutOfRangeException(nameof(conformance), given, "Choose the PDF/A level to validate against.");
        }

        bool known = PdfARules.TryGetClaimed(Metadata?.Packet, out PdfAConformance? claimed, out string? claim);
        if (conformance is null && !known)
        {
            throw claim is null
                ? new InvalidOperationException("The file does not claim PDF/A in its XMP metadata (pdfaid:part); pass a level.")
                : new NotSupportedException($"The file claims {claim}, which Broadside cannot validate; pass a level it can check.");
        }

        PdfAConformance level = conformance ?? claimed!.Value;
        IReadOnlyList<PdfAViolation> violations = PdfAChecker.Validate(this, level);
        return new PdfAValidation(
            level,
            claimed == level,
            [.. violations.Select(violation => new PdfARuleViolation(violation.Rule, violation.Message) { Reference = violation.Reference })],
            PdfARules.NotChecked(level));
    }

    /// <summary>Gets the length of the bytes the document was read from (issue #651: the byte-level PDF/A rules).</summary>
    internal long SourceLength => _source.Length;

    /// <summary>Gets the newest trailer as the file has it, without the entries only older trailers hold (ISO 32000-2 §7.5.6).</summary>
    internal CosDictionary NewestTrailer => _loader.CrossReference.Revisions is [.., var newest] ? newest.Newest.Trailer : Trailer;

    /// <summary>Reads the bytes the document was read from at <paramref name="offset"/> into <paramref name="destination"/>.</summary>
    /// <returns>How many bytes were read.</returns>
    internal int ReadSource(long offset, Span<byte> destination) => _source.Read(offset, destination);
}
