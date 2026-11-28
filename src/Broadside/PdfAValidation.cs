namespace Broadside;

/// <summary>
/// The outcome of <see cref="PdfDocument.ValidatePdfA"/>: the PDF/A level checked, whether the file claims it, the rules it breaks,
/// and the rules the check leaves to veraPDF.
/// </summary>
/// <remarks>
/// ISO 19005-2, -3 and -4, cited by veraPDF validation-profile rule identifiers (the standard is not in the repository).
/// <para>
/// ISO 19005-2 §6.1.2, §6.1.3, §6.1.7, §6.1.10, §6.2.2, §6.2.3, §6.2.4.3, §6.2.5, §6.2.8, §6.2.9, §6.2.10, §6.2.11, §6.3, §6.4, §6.5,
/// §6.6, §6.8, §6.9, §6.10 and §6.11.
/// </para>
/// <para>ISO 19005-3 §6.1.2, §6.2.11, §6.6 and §6.8.</para>
/// <para>
/// ISO 19005-4 §6.1.2, §6.1.3, §6.1.6, §6.1.9, §6.1.12, §6.2.3, §6.2.4.3, §6.2.5, §6.2.7, §6.2.8, §6.2.9, §6.2.10, §6.3, §6.4, §6.6,
/// §6.7, §6.9, §6.10, §6.11 and §6.12.
/// </para>
/// </remarks>
public sealed class PdfAValidation
{
    internal PdfAValidation(PdfAConformance conformance, bool isClaimed, IReadOnlyList<PdfARuleViolation> violations, IReadOnlyList<string> rulesNotChecked)
    {
        Conformance = conformance;
        IsClaimed = isClaimed;
        Violations = violations;
        RulesNotChecked = rulesNotChecked;
    }

    /// <summary>Gets the level checked: the one asked for, or the one the file claims.</summary>
    public PdfAConformance Conformance { get; }

    /// <summary>Gets whether the file's XMP identification (<c>pdfaid:part</c>, <c>pdfaid:conformance</c>) claims <see cref="Conformance"/>.</summary>
    public bool IsClaimed { get; }

    /// <summary>
    /// Gets whether the file breaks none of the rules checked. It may still break one of <see cref="RulesNotChecked"/>, which a full
    /// validator such as veraPDF checks.
    /// </summary>
    public bool IsConformant => Violations.Count == 0;

    /// <summary>Gets the rules the file breaks, one entry per finding, with the object it was found on when known.</summary>
    public IReadOnlyList<PdfARuleViolation> Violations { get; }

    /// <summary>
    /// Gets the veraPDF rules of the level's profile the check does not cover, or covers only in part: a rule identifier
    /// (<c>6.2.5-5</c>) or a clause, for every rule under it (<c>6.2.11.2</c>).
    /// </summary>
    public IReadOnlyList<string> RulesNotChecked { get; }
}
