using Broadside.Objects;

namespace Broadside;

/// <summary>A PDF/UA rule a document does not meet, as veraPDF's validation profile identifies it.</summary>
/// <param name="Rule">
/// The rule: the ISO 14289-1 or ISO 14289-2 clause and veraPDF test number, such as <c>7.3-1</c> or <c>8.2.5.28.2-1</c>, or an ISO/TS
/// 32005 containment rule such as <c>Table 5. L-P-1</c>.
/// </param>
/// <param name="Message">What in the document breaks it.</param>
/// <remarks>ISO 14289-1 clauses 5 to 7 and ISO 14289-2 clauses 5 to 8 (veraPDF PDF/UA-1 and PDF/UA-2 validation profiles); Matterhorn Protocol 1.1.</remarks>
public readonly record struct PdfUARuleViolation(string Rule, string Message)
{
    /// <summary>
    /// Gets the Matterhorn Protocol 1.1 failure condition the violation is, such as <c>13-004</c>, in a PDF/UA-1 validation;
    /// <see langword="null"/> for PDF/UA-2 (cited by <see cref="Rule"/> alone) or a rule with no Matterhorn condition (6.2-1).
    /// </summary>
    /// <remarks>Matterhorn Protocol 1.1, PDF/UA-1 failure conditions.</remarks>
    public string? Checkpoint { get; init; }

    /// <summary>Gets the object the rule fails on (a structure element, page, annotation or font), when one is known.</summary>
    public CosReference? Reference { get; init; }
}

/// <summary>
/// The outcome of <see cref="PdfDocument.ValidatePdfUA"/>: the PDF/UA rules a document as read does not meet, what it claims, and an
/// honest account of what was not checked. Passing the machine checks is necessary for PDF/UA, not sufficient: a person still has to
/// judge the conditions in <see cref="MachineUncheckable"/>, such as whether alternate text describes its figure.
/// </summary>
/// <remarks>
/// <para>
/// veraPDF PDF/UA-1 and PDF/UA-2 validation profiles; Well-Tagged PDF is checked as PDF/UA-2, whose rules it shares; Matterhorn
/// Protocol 1.1.
/// </para>
/// <para>ISO 14289-1 §5, §6.2, §7.1, §7.2, §7.3, §7.4, §7.5, §7.6, §7.8, §7.9, §7.15, §7.18 and §7.21.</para>
/// <para>ISO 14289-2 §5, §6.2, §8.1, §8.2, §8.3, §8.4, §8.9, §8.10 and §8.11.</para>
/// <para>Well-Tagged PDF 1.0 §6.1, §8.2, §8.3, §8.4, §8.9 and §8.11.</para>
/// </remarks>
public sealed class PdfUAValidation
{
    internal PdfUAValidation(
        PdfUAConformance conformance,
        bool isClaimed,
        IReadOnlyList<PdfUARuleViolation> violations,
        PdfWellTaggedConformance wellTagged,
        PdfWellTaggedConformance wellTaggedConformant,
        IReadOnlyList<string> checkpointsNotChecked,
        IReadOnlyList<string> machineUncheckable)
    {
        Conformance = conformance;
        IsClaimed = isClaimed;
        Violations = violations;
        WellTagged = wellTagged;
        WellTaggedConformant = wellTaggedConformant;
        CheckpointsNotChecked = checkpointsNotChecked;
        MachineUncheckable = machineUncheckable;
    }

    /// <summary>Gets the level checked: the one asked for, else the one the file claims.</summary>
    public PdfUAConformance Conformance { get; }

    /// <summary>
    /// Gets whether the file claims <see cref="Conformance"/>: its XMP <c>pdfuaid:part</c> is 1 or 2. A file claiming PDF/UA-1 checked
    /// as PDF/UA-2 is not claimed.
    /// </summary>
    /// <remarks>ISO 14289-1 clause 5; ISO 14289-2 clause 5.</remarks>
    public bool IsClaimed { get; }

    /// <summary>Gets whether no rule checked fails: <see cref="Violations"/> is empty. Not a verdict on what was not checked.</summary>
    public bool IsConformant => Violations.Count == 0;

    /// <summary>
    /// Gets the rules the document does not meet, in document order: catalog and identification first, then the structure tree, then
    /// each page; structure repairs made on reading last, under the rule each breaks.
    /// </summary>
    public IReadOnlyList<PdfUARuleViolation> Violations { get; }

    /// <summary>
    /// Gets the Well-Tagged PDF levels the file declares (PDF Declarations in its XMP); <see cref="PdfWellTaggedConformance.None"/>
    /// when none. They are checked against the PDF/UA-2 rules, which WTPDF 1.0 shares.
    /// </summary>
    /// <remarks>WTPDF 1.0 §6.1.2, §6.1.3 and clause 8.</remarks>
    public PdfWellTaggedConformance WellTagged { get; }

    /// <summary>
    /// Gets the declared Well-Tagged PDF levels whose machine checks pass: the PDF/UA-2 rules without the <c>pdfuaid</c>
    /// identification, and for the reuse level without figures' alternate text (8.2.5.28.2-1). Call
    /// <see cref="PdfDocument.ValidatePdfUA"/> with <see cref="PdfUAConformance.PdfUA2"/> for the rules a level fails.
    /// </summary>
    /// <remarks>WTPDF 1.0 §6.1.2, §6.1.3 and clause 8.</remarks>
    public PdfWellTaggedConformance WellTaggedConformant { get; }

    /// <summary>
    /// Gets what a machine could check and this validation does not yet (issue #527), so that the result is honest about its coverage.
    /// PDF/UA-1: the Matterhorn Protocol 1.1 machine failure conditions not checked, such as <c>20-001</c>. PDF/UA-2: the ISO 14289-2
    /// clauses not, or not fully, checked, such as <c>8.7</c>.
    /// </summary>
    /// <remarks>Matterhorn Protocol 1.1; ISO 14289-2 clause 8.</remarks>
    public IReadOnlyList<string> CheckpointsNotChecked { get; }

    /// <summary>
    /// Gets the Matterhorn Protocol 1.1 failure conditions that need human judgment (How: H), such as <c>01-001</c> (an artifact is
    /// tagged as real content) and <c>06-004</c> (the title does not identify the document), to review by hand. No machine check
    /// decides them; PDF/UA-2 has no Matterhorn edition of its own, so the PDF/UA-1 list is given for it too.
    /// </summary>
    /// <remarks>Matterhorn Protocol 1.1, PDF/UA-1 failure conditions; ISO 14289-2 §6.2 (cases PDF/UA does not cover).</remarks>
    public IReadOnlyList<string> MachineUncheckable { get; }
}
