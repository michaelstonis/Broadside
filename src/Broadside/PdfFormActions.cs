using Broadside.Objects;

namespace Broadside;

/// <summary>A submit-form action: sends the values of interactive form fields to a URL. The library never submits anything.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.6.2, Tables 239 and 240 (PDF 1.2). Table 239 marks <c>Flags</c> and <c>CharSet</c> "inheritable" without saying
/// from where; they are read from the action dictionary only.
/// </remarks>
public sealed class PdfSubmitFormAction : PdfAction
{
    internal PdfSubmitFormAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.SubmitForm;

    /// <summary>Gets the URL to submit to (<c>F</c>, required; a URL file specification, §7.11.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.2, Table 239. The URL is untrusted data from the document.</remarks>
    public PdfFileSpecification? File => ReadFileSpecification(NavigationNames.F);

    /// <summary>
    /// Gets the fields to include or, with <see cref="PdfSubmitFormFlags.Exclude"/>, exclude (<c>Fields</c>): field dictionaries and
    /// (PDF 1.3) fully qualified field names; <see langword="null"/> when absent: every field.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.6.2, Table 239.</remarks>
    public IReadOnlyList<PdfActionTarget>? Fields => ReadTargets(ActionNames.Fields, "an array of field dictionaries and text strings");

    /// <summary>Gets the flags (<c>Flags</c>); <see cref="PdfSubmitFormFlags.None"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.2, Tables 239 and 240. Reserved bits are kept as they are.</remarks>
    public PdfSubmitFormFlags Flags => (PdfSubmitFormFlags)(ReadInteger(ActionNames.Flags) ?? 0);

    /// <summary>Gets the character set of the submitted data (<c>CharSet</c>, PDF 2.0), such as <c>utf-8</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.2, Table 239: utf-8, utf-16, Shift-JIS, BigFive, GBK or UHC are supported values.</remarks>
    public string? CharacterSet => ReadString(ActionNames.CharSet) is { } charSet ? System.Text.Encoding.Latin1.GetString(charSet.Bytes) : null;

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(NavigationNames.F))
        {
            Report("A submit-form action shall have an F entry giving the URL; it has none, so it submits nowhere.");
        }
    }
}

/// <summary>A reset-form action: resets interactive form fields to their default values.</summary>
/// <remarks>ISO 32000-2 §12.7.6.3, Tables 241 and 242 (PDF 1.2).</remarks>
public sealed class PdfResetFormAction : PdfAction
{
    internal PdfResetFormAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.ResetForm;

    /// <summary>
    /// Gets the fields to reset or, with <see cref="PdfResetFormFlags.Exclude"/>, not to reset (<c>Fields</c>): field dictionaries and
    /// fully qualified field names; <see langword="null"/> when absent: every field.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.6.3, Table 241.</remarks>
    public IReadOnlyList<PdfActionTarget>? Fields => ReadTargets(ActionNames.Fields, "an array of field dictionaries and text strings");

    /// <summary>Gets the flags (<c>Flags</c>); <see cref="PdfResetFormFlags.None"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.3, Tables 241 and 242.</remarks>
    public PdfResetFormFlags Flags => (PdfResetFormFlags)(ReadInteger(ActionNames.Flags) ?? 0);
}

/// <summary>An import-data action: imports field values from a file (FDF, XFDF or another format). The library never imports anything.</summary>
/// <remarks>ISO 32000-2 §12.7.6.4, Table 243 (PDF 1.2).</remarks>
public sealed class PdfImportDataAction : PdfAction
{
    internal PdfImportDataAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.ImportData;

    /// <summary>Gets the file to import from (<c>F</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.6.4, Table 243, and §7.11. The file name is untrusted data from the document.</remarks>
    public PdfFileSpecification? File => ReadFileSpecification(NavigationNames.F);

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(NavigationNames.F))
        {
            Report("An import-data action shall have an F entry naming the file; it has none.");
        }
    }
}
