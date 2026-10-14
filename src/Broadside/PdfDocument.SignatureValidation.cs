using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Signatures.Validation;

namespace Broadside;

/// <content>Signature validation (issue #196): ISO 32000-2 §12.8.1 and §12.8.3.</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Validates every signature of the document: for each signed signature field, the byte range, the digest of the bytes it covers,
    /// the CMS signature value, and the signer's certificate path to the trust anchors <paramref name="options"/> gives.
    /// </summary>
    /// <param name="options">The trust anchors, extra certificates and validation time; <see langword="null"/> for none and now.</param>
    /// <returns>One validation per signed signature field, in signing order (the order their byte ranges grow, §12.8.1, Table 255).</returns>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.8.1: the digest is recomputed over the <c>ByteRange</c> of the file the document was opened from (offsets from
    /// its first byte) and compared with the one the signature carries; a difference, or a signature value that does not verify with the
    /// signer's certificate, makes the signature <see cref="PdfSignatureStatus.Invalid"/>. The byte ranges must leave out exactly the
    /// <c>Contents</c> hexadecimal string (§12.8.3.3.1), so a range that leaves another gap is reported. SubFilters
    /// <c>adbe.pkcs7.detached</c>, <c>ETSI.CAdES.detached</c>, <c>adbe.pkcs7.sha1</c> and <c>ETSI.RFC3161</c> are validated (Table 260);
    /// <c>adbe.x509.rsa_sha1</c> is reported as unsupported.
    /// </para>
    /// <para>
    /// The signer's certificate path is built from the certificates the signature carries and
    /// <see cref="PdfSignatureValidationOptions.ExtraCertificates"/> to one of
    /// <see cref="PdfSignatureValidationOptions.TrustAnchors"/>, and checked as RFC 5280 §6.1 describes (signatures, validity at the
    /// validation time, basic constraints, key usage, path length). A path that reaches no anchor, or a certificate outside its
    /// validity period, makes the signature <see cref="PdfSignatureStatus.Indeterminate"/>. Nothing is fetched and revocation is not
    /// checked.
    /// </para>
    /// <para>
    /// Modification detection (§12.8.2.2.2, §12.8.2.4): the byte ranges shall end at the <c>%%EOF</c> that terminates a revision
    /// (§12.8.1), which <see cref="PdfSignatureValidation.SignedRevision"/> names, else <c>SignedRevisionUnknown</c>. Each later
    /// revision is compared object by object with the one before it, and every change is classified
    /// (<see cref="PdfModificationKind"/>) and checked against the permissions in effect: those of the document's certification
    /// signature (its DocMDP <c>P</c>, Table 257; 3 without one), narrowed by the <c>P</c> of every field lock signed before the change
    /// (Table 236). A change they do not permit makes every signature before it <see cref="PdfSignatureStatus.Invalid"/>
    /// (<c>ModificationNotPermitted</c>), and so does a change to a field the signature locks through its FieldMDP reference or its
    /// field's lock dictionary (<c>LockedFieldModified</c>). Updates that only add a document security store or a document timestamp
    /// are always permitted. See <see cref="PdfSignatureValidation.Modifications"/>.
    /// </para>
    /// <para>
    /// Edits made to the document in memory and not yet saved are not part of the file and play no part. A read: safe concurrently
    /// with other reads.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PdfSignatureValidation> ValidateSignatures(PdfSignatureValidationOptions? options = null)
    {
        options ??= new PdfSignatureValidationOptions();
        DateTimeOffset time = options.ValidationTime ?? DateTimeOffset.UtcNow;
        if (AcroForm is not { } form)
        {
            return [];
        }

        var validations = new List<(long End, int Order, PdfSignatureValidation Validation)>();
        foreach (PdfSignatureField field in form.TerminalFields.OfType<PdfSignatureField>())
        {
            if (field.SignatureDictionary is not CosDictionary dictionary)
            {
                continue;
            }

            PdfSignatureValidation validation = SignatureValidator.Validate(_source, field, dictionary, Resolve, options, time);
            IReadOnlyList<long> range = validation.ByteRange;
            long end = range.Count >= 2 && range[^2] >= 0 && range[^1] >= 0 && range[^2] <= long.MaxValue - range[^1] ? range[^2] + range[^1] : long.MaxValue;
            validations.Add((end, validations.Count, validation));
        }

        PdfSignatureValidation[] ordered = [.. validations.OrderBy(static item => item.End).ThenBy(static item => item.Order).Select(static item => item.Validation)];
        return ModificationDetector.Apply(_source, _loader, _streams, Revisions, ordered, Resolve);
    }
}
