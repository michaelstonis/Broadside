namespace Broadside.Signatures;

/// <summary>What <see cref="PdfDocument.RemoveSignatures(PdfSignatureRemovalOptions)"/> removed, and how to save the result.</summary>
/// <remarks>ISO 32000-2 §12.7.5.5, §12.8.1, §12.8.2.2 and §7.5.6.</remarks>
public sealed class PdfSignatureRemoval
{
    internal PdfSignatureRemoval(int signaturesRemoved, int fieldsRemoved, bool certificationRemoved, bool securityStoreRemoved)
    {
        SignaturesRemoved = signaturesRemoved;
        FieldsRemoved = fieldsRemoved;
        CertificationRemoved = certificationRemoved;
        SecurityStoreRemoved = securityStoreRemoved;
    }

    /// <summary>Gets how many signature fields lost their signature (or document timestamp) value.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public int SignaturesRemoved { get; }

    /// <summary>Gets how many signature fields were removed with their widgets (<see cref="PdfSignatureRemovalOptions.RemoveFields"/>).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public int FieldsRemoved { get; }

    /// <summary>Gets a value indicating whether a certification signature was removed with the catalog's <c>Perms</c> <c>DocMDP</c> entry.</summary>
    /// <remarks>ISO 32000-2 §12.8.2.2, Table 263.</remarks>
    public bool CertificationRemoved { get; }

    /// <summary>Gets a value indicating whether the catalog's document security store (<c>DSS</c>) was removed.</summary>
    /// <remarks>ISO 32000-2 §12.8.4.3.</remarks>
    public bool SecurityStoreRemoved { get; }

    /// <summary>Returns save options that write the document without the removed signatures' bytes: a full rewrite that leaves out unreachable objects.</summary>
    /// <returns>New save options with <see cref="PdfSaveOptions.IncrementalUpdate"/> off and <see cref="PdfSaveOptions.RemoveUnreachableObjects"/> on.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.5.6 and §7.3.10. An incremental update appends to the file the document was opened from, so the earlier
    /// revision, which still holds the signatures, stays in the saved file and a validator can still find them there. Only a full
    /// rewrite that drops the objects nothing reaches any more removes their bytes.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The save options belong to the result, as PdfRedactionResult.CreateSaveOptions does.")]
    public PdfSaveOptions CreateSaveOptions() => new PdfSaveOptions().WithIncrementalUpdate(false).WithRemoveUnreachableObjects(true);
}
