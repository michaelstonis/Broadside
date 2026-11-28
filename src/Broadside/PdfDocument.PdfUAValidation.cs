using Broadside.Writing;

namespace Broadside;

/// <content>
/// PDF/UA validation without a save (issue #652): the checks of a PDF/UA save, run on the document as read, with what the save would
/// write checked on the file's own values.
/// </content>
public sealed partial class PdfDocument
{
    /// <summary>The ISO 14289-2 clauses PDF/UA-2 validation does not, or not fully, check yet (issue #527).</summary>
    private static readonly IReadOnlyList<string> PdfUA2ClausesNotChecked = ["8.2", "8.4", "8.5", "8.6", "8.7", "8.8", "8.9", "8.10", "8.12", "8.13", "8.14"];

    /// <summary>
    /// Checks the document, as read, against PDF/UA-1 or PDF/UA-2 and lists the rules it does not meet, by veraPDF rule identifier and,
    /// for PDF/UA-1, Matterhorn Protocol 1.1 checkpoint. Nothing is changed and nothing is saved: the entries a PDF/UA save would write
    /// (the <c>pdfuaid</c> identification, the metadata stream, <c>MarkInfo</c> <c>Marked</c>, <c>DisplayDocTitle</c>, page
    /// <c>Tabs</c>) are checked as the file has them. Repairs made while reading the structure tree (lenient reading) are reported as
    /// violations of the rule each breaks. Well-Tagged PDF levels the file declares are checked with the PDF/UA-2 rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passing is necessary for PDF/UA, not sufficient: <see cref="PdfUAValidation.CheckpointsNotChecked"/> lists what a machine could
    /// check and this does not yet (issue #527), and <see cref="PdfUAValidation.MachineUncheckable"/> what only a person can judge.
    /// A read: safe alongside other reads and renders while nobody changes the document. In strict mode a structure repair throws, as
    /// any read does; a well-formed file that does not conform never throws.
    /// </para>
    /// <para>ISO 14289-1 clauses 5 to 7; ISO 14289-2 clauses 5 to 8; Well-Tagged PDF 1.0 clause 8; Matterhorn Protocol 1.1.</para>
    /// </remarks>
    /// <param name="conformance">
    /// The level to check; <see langword="null"/> for the one the file claims (<c>pdfuaid:part</c> in its XMP metadata).
    /// </param>
    /// <returns>The violations, the claim, and the coverage of the checks.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is <see cref="PdfUAConformance.None"/> or undefined.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="conformance"/> is <see langword="null"/> and the file claims no PDF/UA part 1 or 2.</exception>
    public PdfUAValidation ValidatePdfUA(PdfUAConformance? conformance = null)
    {
        int? claimedPart = Metadata?.Packet?.PdfUAPart;
        PdfUAConformance level = conformance ?? claimedPart switch
        {
            1 => PdfUAConformance.PdfUA1,
            2 => PdfUAConformance.PdfUA2,
            _ => throw new InvalidOperationException(claimedPart is { } part
                ? $"The file claims PDF/UA part {part}, which is not checked: pass the conformance to validate against."
                : "The file claims no PDF/UA conformance (no pdfuaid:part in its XMP metadata): pass the conformance to validate against."),
        };
        if (level is not (PdfUAConformance.PdfUA1 or PdfUAConformance.PdfUA2))
        {
            throw new ArgumentOutOfRangeException(nameof(conformance), conformance, "Validation checks PDF/UA-1 or PDF/UA-2.");
        }

        int partNumber = level == PdfUAConformance.PdfUA2 ? 2 : 1;
        IReadOnlyList<PdfUAFinding> findings = PdfUAChecker.Validate(this, partNumber, identification: true);

        PdfWellTaggedConformance declared = PdfWellTaggedConformance.None;
        foreach (PdfDeclaration declaration in Declarations)
        {
            declared |= declaration.ConformsTo switch
            {
                PdfDeclaration.WellTaggedReuse => PdfWellTaggedConformance.Reuse,
                PdfDeclaration.WellTaggedAccessibility => PdfWellTaggedConformance.Accessibility,
                _ => PdfWellTaggedConformance.None,
            };
        }

        PdfWellTaggedConformance conformant = PdfWellTaggedConformance.None;
        if (declared != PdfWellTaggedConformance.None)
        {
            // WTPDF is identified by its declaration, not pdfuaid: the PDF/UA-2 findings without identification (rules 5-n).
            IReadOnlyList<PdfUAFinding> part2 = partNumber == 2
                ? [.. findings.Where(finding => !finding.Rule.StartsWith("5-", StringComparison.Ordinal))]
                : PdfUAChecker.Validate(this, 2, identification: false);
            foreach (PdfWellTaggedConformance wellTagged in Levels(declared))
            {
                if (!part2.Any(finding => wellTagged != PdfWellTaggedConformance.Reuse || finding.Rule != "8.2.5.28.2-1"))
                {
                    conformant |= wellTagged;
                }
            }
        }

        return new PdfUAValidation(
            level,
            isClaimed: claimedPart == partNumber,
            [.. findings.Select(finding => new PdfUARuleViolation(finding.Rule, finding.Message) { Checkpoint = finding.Checkpoint, Reference = finding.Reference })],
            declared,
            conformant,
            partNumber == 1 ? MatterhornProtocol.NotChecked : PdfUA2ClausesNotChecked,
            MatterhornProtocol.Human);
    }
}
