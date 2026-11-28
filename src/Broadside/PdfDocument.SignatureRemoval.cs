using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;

namespace Broadside;

/// <content>Removing signatures (issue #660): ISO 32000-2 §12.7.5.5, §12.8.1, §12.8.2.2, §12.8.4.3 and §12.8.5.</content>
public sealed partial class PdfDocument
{
    /// <summary>Removes every signature and document timestamp, as <see cref="RemoveSignatures(PdfSignatureRemovalOptions)"/> does with default options.</summary>
    /// <returns>What was removed, and the save options that drop the removed bytes.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.5, §12.8.1, §12.8.2.2, §12.8.4.3 and §12.8.5.</remarks>
    public PdfSignatureRemoval RemoveSignatures() => RemoveSignatures(new PdfSignatureRemovalOptions());

    /// <summary>Removes the document's signatures: clears every signed signature field and optionally removes the fields.</summary>
    /// <param name="options">Whether the fields go too, whether document timestamps are included, and whether the security store goes.</param>
    /// <returns>What was removed, and the save options that drop the removed bytes.</returns>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.5.5, §12.8.1, §12.8.2.2, §12.8.4.3 and §12.8.5. Each signed signature field (and each document timestamp,
    /// unless <see cref="PdfSignatureRemovalOptions.IncludeDocumentTimestamps"/> is off) is cleared as
    /// <see cref="PdfSignatureField.ClearSignature"/> describes, which removes the certification from the catalog's <c>Perms</c>,
    /// and with <see cref="PdfSignatureRemovalOptions.RemoveFields"/> removed as <see cref="PdfAcroForm.RemoveField"/> describes.
    /// Once no signature or document timestamp is left, the catalog's document security store (<c>DSS</c>) is removed, unless
    /// <see cref="PdfSignatureRemovalOptions.RemoveSecurityStore"/> is off; while one is left the store is kept whole. The form's
    /// <c>SigFlags</c> is recomputed.
    /// </para>
    /// <para>
    /// The removed signatures' bytes are still in the file the document was opened from. Save with
    /// <see cref="PdfSignatureRemoval.CreateSaveOptions"/>, a full rewrite that leaves out unreachable objects: an incremental save
    /// keeps the earlier revision, signatures and all. A certification that permits no changes does not prevent removal. Removal
    /// never raises the document's version. A mutation: no read may run concurrently with it; on failure nothing is changed.
    /// </para>
    /// </remarks>
    public PdfSignatureRemoval RemoveSignatures(PdfSignatureRemovalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return AnnotationEditing.Atomically(() =>
        {
            int signatures = 0;
            int fields = 0;
            bool certification = false;
            PdfAcroForm? form = AcroForm;
            if (form is not null)
            {
                // The targets are chosen before anything changes; one that shares its signature dictionary with an earlier one was
                // cleared with it, and is still removed with the fields.
                PdfSignatureField[] targets =
                [
                    .. form.TerminalFields.OfType<PdfSignatureField>().Where(field => field.Holder(FormNames.V) is not null
                        && (options.IncludeDocumentTimestamps || field.ValueObject is not CosDictionary value || !TransformMethods.IsDocumentTimestamp(value, Resolve))),
                ];
                foreach (PdfSignatureField field in targets)
                {
                    SignatureRemoval.Cleared cleared = SignatureRemoval.ClearCore(field);
                    signatures += cleared.Fields;
                    certification |= cleared.Certification;
                    if (options.RemoveFields)
                    {
                        SignatureRemoval.RemoveCore(field);
                        fields++;
                    }
                }

                SignatureRemoval.UpdateFlags(form);
            }

            bool store = false;
            bool signed = form is not null && form.TerminalFields.OfType<PdfSignatureField>().Any(static field => field.Holder(FormNames.V) is not null);
            if (options.RemoveSecurityStore && !signed && Catalog.ContainsKey(DssName))
            {
                Catalog.Remove(DssName);
                store = true;
            }

            return new PdfSignatureRemoval(signatures, fields, certification, store);
        });
    }
}
