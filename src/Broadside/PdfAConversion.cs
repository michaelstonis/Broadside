namespace Broadside;

/// <summary>A PDF/A rule a document does not meet, as veraPDF's validation profile identifies it.</summary>
/// <param name="Rule">The rule: the ISO 19005 clause and veraPDF test number, such as <c>6.2.11.4.1-1</c>.</param>
/// <param name="Message">What in the document breaks it.</param>
/// <remarks>ISO 19005-2, -3 and -4, cited by veraPDF validation-profile rule identifiers (the standard is not in the repository).</remarks>
public readonly record struct PdfARuleViolation(string Rule, string Message);

/// <summary>
/// The outcome of <see cref="PdfDocument.ConvertToPdfA"/>: what the conversion changed, what it could not fix, the rules the document
/// still does not meet, and the save options that write the conversion.
/// </summary>
/// <remarks>
/// ISO 19005-2, -3 and -4 (veraPDF PDF/A-2B, -2U, -3B, -3U, -4 and -4F validation profiles). The same lists are recorded on
/// <see cref="PdfDocument.Diagnostics"/>: <c>PdfAConverted</c> and <c>PdfANotConverted</c> (Information) and <c>PdfANonConformant</c>
/// (Warning).
/// </remarks>
public sealed class PdfAConversion
{
    private readonly bool _removeEncryption;

    internal PdfAConversion(PdfAConformance conformance, IReadOnlyList<string> changes, IReadOnlyList<string> notConverted, IReadOnlyList<PdfARuleViolation> violations, bool removeEncryption)
    {
        Conformance = conformance;
        Changes = changes;
        NotConverted = notConverted;
        Violations = violations;
        _removeEncryption = removeEncryption;
    }

    /// <summary>Gets the level the document was converted to.</summary>
    public PdfAConformance Conformance { get; }

    /// <summary>
    /// Gets whether the save's check step finds no rule of the level the document breaks: saving with <see cref="CreateSaveOptions"/>
    /// writes the PDF/A identification. The save checks again, including the version its features need (rule 6.1.2-1), and never
    /// identifies a file that does not conform.
    /// </summary>
    public bool IsConformant => Violations.Count == 0;

    /// <summary>Gets what the conversion changed or removed, one sentence each, citing the rule that required it.</summary>
    public IReadOnlyList<string> Changes { get; }

    /// <summary>Gets what the conversion could not fix and why (a font with no program, an embedded file it cannot verify), one sentence each.</summary>
    public IReadOnlyList<string> NotConverted { get; }

    /// <summary>Gets the rules the converted document still does not meet; empty when <see cref="IsConformant"/>.</summary>
    public IReadOnlyList<PdfARuleViolation> Violations { get; }

    /// <summary>
    /// Creates the options of the save that writes the conversion: a full save at <see cref="Conformance"/> that removes the
    /// encryption of an encrypted document (rule 6.1.3-2) and the objects the conversion left unreachable (the fonts it replaced, the
    /// annotations and actions it removed).
    /// </summary>
    /// <returns>New options, which the caller may change further.</returns>
    public PdfSaveOptions CreateSaveOptions() => new PdfSaveOptions()
        .WithPdfAConformance(Conformance)
        .WithRemoveEncryption(_removeEncryption)
        .WithRemoveUnreachableObjects(true);
}
