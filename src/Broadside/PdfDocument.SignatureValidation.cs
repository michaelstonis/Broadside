using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Signatures.Validation;

namespace Broadside;

/// <content>Signature validation (issues #196 and #197): ISO 32000-2 §12.8.1, §12.8.3 and §12.8.4.3.</content>
public sealed partial class PdfDocument
{
    private static readonly CosName DssName = new("DSS");
    private static readonly CosName CertsName = new("Certs");
    private static readonly CosName OcspsName = new("OCSPs");
    private static readonly CosName CrlsName = new("CRLs");

    /// <summary>
    /// Validates every signature of the document: for each signed signature field, the byte range, the digest of the bytes it covers,
    /// the CMS signature value, its time-stamp token, and the signer's certificate path to the trust anchors <paramref name="options"/>
    /// gives, with the revocation of each certificate.
    /// </summary>
    /// <param name="options">The trust anchors, extra certificates, validation time and revocation settings; <see langword="null"/> for none and now.</param>
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
    /// The signer's certificate path is built from the certificates the signature carries, the document security store's
    /// (§12.8.4.3) and <see cref="PdfSignatureValidationOptions.ExtraCertificates"/> to one of
    /// <see cref="PdfSignatureValidationOptions.TrustAnchors"/>, and checked as RFC 5280 §6.1 describes (signatures, validity at the
    /// validation time, basic constraints, key usage, path length). A path that reaches no anchor, or a certificate outside its
    /// validity period, makes the signature <see cref="PdfSignatureStatus.Indeterminate"/>. A signature time-stamp token that verifies,
    /// with its unit's chain trusted, moves the validation time back to its time (§12.8.3.4.8).
    /// </para>
    /// <para>
    /// Each certificate's revocation is checked with the CRLs and OCSP responses the signature (§12.8.3.3.2), the document security
    /// store and the options hold; they are fetched only through <see cref="PdfSignatureValidationOptions.OnlineRevocationClient"/>
    /// when the caller sets it. This method sends synchronously; use <see cref="ValidateSignaturesAsync"/> to fetch asynchronously.
    /// </para>
    /// <para>
    /// Long-term validation (§12.8.5.3; ETSI EN 319 142-1 B-LTA): a document timestamp that validates proves that everything its byte
    /// range covers (earlier signatures, their time-stamp tokens, the security store written before it) existed at its time, so each
    /// earlier signature is validated at the time of the nearest later valid document timestamp instead of now: its unit's and
    /// signer's certificates need only have been valid, and the store's revocation data current, then. The latest timestamp is
    /// validated now. See <see cref="AddValidationData"/>.
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
        ValueTask<IReadOnlyList<PdfSignatureValidation>> validation = ValidateSignaturesCoreAsync(options, asynchronous: false, CancellationToken.None);
        return validation.IsCompletedSuccessfully ? validation.Result : validation.AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Validates every signature of the document as <see cref="ValidateSignatures"/> does, fetching revocation data (when
    /// <see cref="PdfSignatureValidationOptions.OnlineRevocationClient"/> is set) asynchronously.
    /// </summary>
    /// <param name="options">The trust anchors, extra certificates, validation time and revocation settings; <see langword="null"/> for none and now.</param>
    /// <param name="cancellationToken">Cancels the online fetches.</param>
    /// <returns>One validation per signed signature field, in signing order.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, §12.8.3.4.6; RFC 6960 Appendix A.1.</remarks>
    public async Task<IReadOnlyList<PdfSignatureValidation>> ValidateSignaturesAsync(PdfSignatureValidationOptions? options = null, CancellationToken cancellationToken = default) =>
        await ValidateSignaturesCoreAsync(options, asynchronous: true, cancellationToken).ConfigureAwait(false);

    private async ValueTask<IReadOnlyList<PdfSignatureValidation>> ValidateSignaturesCoreAsync(PdfSignatureValidationOptions? options, bool asynchronous, CancellationToken cancellationToken)
    {
        options ??= new PdfSignatureValidationOptions();
        if (AcroForm is not { } form)
        {
            return [];
        }

        DateTimeOffset now = options.ValidationTime ?? DateTimeOffset.UtcNow;
        (List<X509Certificate2> certificates, List<byte[]> ocsps, List<byte[]> crls) = ReadSecurityStore();
        RevocationFetcher? fetcher = options.OnlineRevocationClient is { } client ? new RevocationFetcher(client) : null;
        var parameters = new ValidationParameters(
            options,
            now,
            [.. options.ExtraCertificates, .. certificates],
            RevocationSources.Create([.. options.OcspResponses, .. ocsps], [.. options.CertificateRevocationLists, .. crls], fetcher, options.RequireRevocationStatus));

        var signed = new List<(long End, int Order, PdfSignatureField Field, CosDictionary Dictionary)>();
        foreach (PdfSignatureField field in form.TerminalFields.OfType<PdfSignatureField>())
        {
            if (field.SignatureDictionary is CosDictionary dictionary)
            {
                signed.Add((RangeEnd(dictionary), signed.Count, field, dictionary));
            }
        }

        // §12.8.5.3 (PAdES-LTA): a valid document timestamp proves that everything it covers (earlier signatures, their tokens, the
        // security store before it) existed at its time, so those are validated at that time; the latest first, at the current time.
        signed.Sort(static (left, right) => left.End != right.End ? left.End.CompareTo(right.End) : left.Order.CompareTo(right.Order));
        var ordered = new PdfSignatureValidation[signed.Count];
        long proofEnd = long.MinValue;
        DateTimeOffset? proof = null;
        for (int index = signed.Count - 1; index >= 0; index--)
        {
            (long end, _, PdfSignatureField field, CosDictionary dictionary) = signed[index];
            ValidationParameters current = proof is { } existed && existed < now && end < proofEnd ? parameters with { Now = existed } : parameters;
            PdfSignatureValidation validation = await SignatureValidator.ValidateAsync(_source, field, dictionary, Resolve, current, asynchronous, cancellationToken).ConfigureAwait(false);
            ordered[index] = validation;
            if (validation.Status == PdfSignatureStatus.Valid && validation.Timestamp is { Status: PdfSignatureStatus.Valid } token
                && TransformMethods.IsDocumentTimestamp(dictionary, Resolve) && end != long.MaxValue)
            {
                proof = token.Time;
                proofEnd = end;
            }
        }

        return ModificationDetector.Apply(_source, _loader, _streams, Revisions, ordered, Resolve);
    }

    /// <summary>Where a signature's byte range ends (its offset plus length), or <see cref="long.MaxValue"/> when it cannot tell.</summary>
    private long RangeEnd(CosDictionary dictionary)
    {
        if (Resolve(dictionary.TryGetValue(ByteRangeName, out CosObject? value) ? value : null) is CosArray { Count: >= 2 } array
            && Resolve(array[^2]) is CosInteger { Value: >= 0 and var offset }
            && Resolve(array[^1]) is CosInteger { Value: >= 0 and var length }
            && offset <= long.MaxValue - length)
        {
            return offset + length;
        }

        return long.MaxValue;
    }

    /// <summary>
    /// The certificates, OCSP responses and CRLs of the document security store (§12.8.4.3, Table 261): streams in the catalog's
    /// <c>DSS</c> dictionary's <c>Certs</c>, <c>OCSPs</c> and <c>CRLs</c> arrays. Entries that are not streams, or certificates that do
    /// not parse, are left out; the <c>VRI</c> dictionaries are not needed, since every entry of theirs is in these arrays too.
    /// </summary>
    private (List<X509Certificate2> Certificates, List<byte[]> Ocsps, List<byte[]> Crls) ReadSecurityStore()
    {
        var certificates = new List<X509Certificate2>();
        var ocsps = new List<byte[]>();
        var crls = new List<byte[]>();
        if (Resolve(Catalog.TryGetValue(DssName, out CosObject? store) ? store : null) is not CosDictionary dss)
        {
            return (certificates, ocsps, crls);
        }

        foreach (byte[] encoded in Streams(CertsName))
        {
            try
            {
                certificates.Add(X509CertificateLoader.LoadCertificate(encoded));
            }
            catch (CryptographicException)
            {
                // A certificate that does not parse cannot complete a chain.
            }
        }

        ocsps.AddRange(Streams(OcspsName));
        crls.AddRange(Streams(CrlsName));
        return (certificates, ocsps, crls);

        IEnumerable<byte[]> Streams(CosName key)
        {
            if (Resolve(dss.TryGetValue(key, out CosObject? value) ? value : null) is not CosArray array)
            {
                yield break;
            }

            foreach (CosObject item in array)
            {
                if (Resolve(item) is CosStream stream)
                {
                    byte[]? data = null;
                    try
                    {
                        data = DecodeStream(stream).ToArray();
                    }
                    catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or global::Broadside.Diagnostics.DiagnosticException)
                    {
                        // A stream that does not decode holds nothing usable.
                    }

                    if (data is not null)
                    {
                        yield return data;
                    }
                }
            }
        }
    }
}
