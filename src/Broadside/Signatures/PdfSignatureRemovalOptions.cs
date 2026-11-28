namespace Broadside.Signatures;

/// <summary>Options for <see cref="PdfDocument.RemoveSignatures(PdfSignatureRemovalOptions)"/>: what goes with the signatures.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.5, §12.8.1, §12.8.4.3 and §12.8.5. The defaults clear every signature and document timestamp, keep the
/// emptied fields for signing again, and remove the document security store once no signature is left.
/// </remarks>
public sealed class PdfSignatureRemovalOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the signature fields that held the removed signatures are removed with their widgets.
    /// The default is <see langword="false"/>: they are kept, empty, ready to be signed again. Unsigned signature fields are kept
    /// either way.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public bool RemoveFields { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether document timestamps (signature fields whose value is a <c>DocTimeStamp</c> dictionary)
    /// are removed too. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.5.</remarks>
    public bool IncludeDocumentTimestamps { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the catalog's document security store (<c>DSS</c>) is removed once no signature or
    /// document timestamp is left. The default is <see langword="true"/>. While one is left the store is kept whole.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.8.4.3.</remarks>
    public bool RemoveSecurityStore { get; set; } = true;

    /// <summary>Sets <see cref="RemoveFields"/>.</summary>
    /// <param name="remove">Whether the emptied signature fields are removed.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public PdfSignatureRemovalOptions WithRemoveFields(bool remove)
    {
        RemoveFields = remove;
        return this;
    }

    /// <summary>Sets <see cref="IncludeDocumentTimestamps"/>.</summary>
    /// <param name="include">Whether document timestamps are removed too.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.5.</remarks>
    public PdfSignatureRemovalOptions WithIncludeDocumentTimestamps(bool include)
    {
        IncludeDocumentTimestamps = include;
        return this;
    }

    /// <summary>Sets <see cref="RemoveSecurityStore"/>.</summary>
    /// <param name="remove">Whether the document security store is removed once no signature is left.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.8.4.3.</remarks>
    public PdfSignatureRemovalOptions WithRemoveSecurityStore(bool remove)
    {
        RemoveSecurityStore = remove;
        return this;
    }
}
