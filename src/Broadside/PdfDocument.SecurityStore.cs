using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Security.Cryptography;
using Broadside.Signatures;
using Broadside.Writing;

namespace Broadside;

/// <content>Long-term validation (issue #198): ISO 32000-2 §12.8.4.3, §12.8.4.4 and §12.8.5.3; ETSI EN 319 142-1 (B-LT, B-LTA).</content>
public sealed partial class PdfDocument
{
    private static readonly CosName VriName = new("VRI");
    private static readonly CosName VriCertName = new("Cert");
    private static readonly CosName VriOcspName = new("OCSP");
    private static readonly CosName VriCrlName = new("CRL");

    /// <summary>
    /// Adds the validation data of every signature and document timestamp (their certificate chains, OCSP responses and CRLs) to the
    /// document security store and writes the document, with an incremental update holding it, to <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="options">
    /// The trust anchors, extra certificates, OCSP responses and CRLs to validate with, and the client to fetch missing revocation data
    /// through (<see cref="PdfSignatureValidationOptions.UseOnlineRevocation"/>); <see langword="null"/> for none: nothing is fetched
    /// unless the options give a client. The request is sent synchronously.
    /// </param>
    /// <exception cref="InvalidOperationException">The document has no signed signature field.</exception>
    /// <exception cref="NotSupportedException">The document cannot be updated incrementally.</exception>
    /// <exception cref="PlatformNotSupportedException">A fetch is needed and the platform cannot send synchronously (the browser): use <see cref="AddValidationDataAsync"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the document is earlier than PDF 2.0 (nothing is fetched then).</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.8.4.3, Table 261: the signatures are validated as <see cref="ValidateSignatures"/> does with
    /// <paramref name="options"/>, and every certificate of each signer's and time-stamping unit's path, and every OCSP response and
    /// CRL that established a certificate's status (from the signature, the store, the options or fetched), goes into the store's
    /// <c>Certs</c>, <c>OCSPs</c> and <c>CRLs</c> arrays, each a stream, once: what the store already holds is not added again. A
    /// signature that validates gets a VRI dictionary (§12.8.4.4, Table 262) under the uppercase hexadecimal SHA-1 of its whole
    /// <c>Contents</c> string, padding included, naming the entries used for it; one that does not gets none ("shall not be used to
    /// record the information used in an unsuccessful validation attempt"). The store is a PDF 2.0 feature: the update raises the version
    /// (in the catalog), or strict mode refuses before anything is fetched or changed.
    /// </para>
    /// <para>
    /// ETSI EN 319 142-1: a B-T signature (signed with <see cref="PdfSignatureOptions.TimestampClient"/>) with its validation data
    /// stored is B-LT; a document timestamp added after it (<see cref="Timestamp"/>) makes it B-LTA, and validation then checks
    /// everything that timestamp covers at its time (§12.8.5.3), so the document validates offline after the certificates expire. To
    /// renew, add the validation data of the last document timestamp (this method, which covers it like any signature) and time-stamp
    /// again before its unit's certificate expires. Adding a store or a document timestamp is a change every certification permits
    /// (§12.8.2.2.2, Table 257), so earlier signatures stay valid.
    /// </para>
    /// <para>
    /// Edits made in memory and not yet saved are written with the update but play no part in the validation. Once the last byte is
    /// written the document reads from the new file; if validation or the save fails, nothing is written and the document is left as it
    /// was. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public void AddValidationData(Stream stream, PdfSignatureValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ValueTask<FileWriter> planned = PlanValidationDataAsync(options, asynchronous: false, CancellationToken.None);
        FileWriter writer = planned.IsCompletedSuccessfully ? planned.Result : planned.AsTask().GetAwaiter().GetResult();
        WriteTo(writer, stream);
        Rebase(writer);
    }

    /// <summary>
    /// Adds the validation data of every signature to the document security store and writes the document to
    /// <paramref name="stream"/>, as <see cref="AddValidationData"/> does, fetching and writing asynchronously.
    /// </summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="options">The validation options; <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels the fetches and the writes.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">The document has no signed signature field.</exception>
    /// <exception cref="NotSupportedException">The document cannot be updated incrementally.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the document is earlier than PDF 2.0.</exception>
    /// <remarks>ISO 32000-2 §12.8.4.3, §12.8.4.4; ETSI EN 319 142-1 (B-LT). A cancelled or failed validation leaves the document as it was.</remarks>
    public async Task AddValidationDataAsync(Stream stream, PdfSignatureValidationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        FileWriter writer = await PlanValidationDataAsync(options, asynchronous: true, cancellationToken).ConfigureAwait(false);
        foreach (ReadOnlyMemory<byte> chunk in writer.Write())
        {
            await stream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
        }

        Rebase(writer);
    }

    /// <summary>Validates, places the store's new entries and plans the incremental save; any failure reverts every change.</summary>
    private async ValueTask<FileWriter> PlanValidationDataAsync(PdfSignatureValidationOptions? options, bool asynchronous, CancellationToken cancellationToken)
    {
        if (AcroForm is not { } form || !form.TerminalFields.OfType<PdfSignatureField>().Any(static field => field.SignatureDictionary is not null))
        {
            throw new InvalidOperationException("The document has no signed signature field to add validation data for.");
        }

        var undo = new List<Action>();
        try
        {
            if (_diagnostics.IsStrict)
            {
                // Strict mode refuses the version raise before anything is fetched (the save would refuse it too, later).
                VersionPlan.Check(Version, [VersionedFeatures.DocumentSecurityStore], _diagnostics);
            }

            RequireSignatureFeature(VersionedFeatures.DocumentSecurityStore, undo);
            IReadOnlyList<PdfSignatureValidation> validations = await ValidateSignaturesCoreAsync(options, asynchronous, cancellationToken).ConfigureAwait(false);

            // Issue #306: the store, and what the save changes, are reverted together when anything fails.
            var changes = new CosChangeLog();
            undo.Add(changes.Revert);
            using (changes.Record())
            {
                WriteSecurityStore(validations);
            }

            return PlanSave(new PdfSaveOptions { IncrementalUpdate = true }, changes);
        }
        catch
        {
            Revert(undo);
            throw;
        }
    }

    /// <summary>Adds what <paramref name="validations"/> used to the catalog's <c>DSS</c> (created when absent) and its VRI entries.</summary>
    private void WriteSecurityStore(IReadOnlyList<PdfSignatureValidation> validations)
    {
        if (Resolve(Catalog.TryGetValue(DssName, out CosObject? existing) ? existing : null) is not CosDictionary store)
        {
            store = new CosDictionary { [KnownNames.Type] = DssName };
            Catalog[DssName] = AddObject(store);
        }

        var certificates = new StoreArray(this, store, CertsName);
        var ocsps = new StoreArray(this, store, OcspsName);
        var crls = new StoreArray(this, store, CrlsName);
        CosDictionary? vri = null;
        foreach (PdfSignatureValidation validation in validations)
        {
            var used = new List<CosObject>[] { [], [], [] };
            foreach (X509Certificate2 certificate in Certificates(validation))
            {
                Use(used[0], certificates.Add(certificate.RawDataMemory));
            }

            foreach (PdfCertificateRevocation revocation in Revocations(validation))
            {
                if (revocation.Evidence.IsEmpty)
                {
                    continue;
                }

                Use(revocation.Source == PdfRevocationSource.Ocsp ? used[1] : used[2], (revocation.Source == PdfRevocationSource.Ocsp ? ocsps : crls).Add(revocation.Evidence));
            }

            if (validation.Status != PdfSignatureStatus.Valid
                || Resolve(validation.Field.SignatureDictionary is CosDictionary signature && signature.TryGetValue(ContentsName, out CosObject? value) ? value : null) is not CosString contents)
            {
                continue;
            }

            if (vri is null)
            {
                if (Resolve(store.TryGetValue(VriName, out CosObject? found) ? found : null) is not CosDictionary present)
                {
                    present = new CosDictionary();
                    store[VriName] = present;
                }

                vri = present;
            }

            // Table 261, note a: the key is the SHA-1 of the whole Contents string, zero padding included.
            var entry = new CosDictionary { [KnownNames.Type] = VriName };
            Entry(entry, VriCertName, used[0]);
            Entry(entry, VriOcspName, used[1]);
            Entry(entry, VriCrlName, used[2]);
            vri[new CosName(Convert.ToHexString(IncrementalDigest.HashData(HashAlgorithmName.SHA1, contents.Bytes)))] = entry;
        }

        certificates.Finish();
        ocsps.Finish();
        crls.Finish();

        static void Use(List<CosObject> list, CosReference reference)
        {
            if (!list.Contains(reference))
            {
                list.Add(reference);
            }
        }

        static void Entry(CosDictionary entry, CosName key, List<CosObject> references)
        {
            // §12.8.4.4: an empty array is omitted.
            if (references.Count > 0)
            {
                entry[key] = new CosArray(references);
            }
        }
    }

    /// <summary>Every certificate of the signer's and the token's paths (anchors included), and the signer's when no path was built.</summary>
    private static IEnumerable<X509Certificate2> Certificates(PdfSignatureValidation validation)
    {
        if (validation.CertificatePath.Count == 0 && validation.SignerCertificate is { } signer)
        {
            yield return signer;
        }

        foreach (X509Certificate2 certificate in validation.CertificatePath)
        {
            yield return certificate;
        }

        foreach (X509Certificate2 certificate in validation.Timestamp?.CertificatePath ?? [])
        {
            yield return certificate;
        }
    }

    private static IEnumerable<PdfCertificateRevocation> Revocations(PdfSignatureValidation validation) =>
        validation.Timestamp is { } timestamp ? validation.Revocations.Concat(timestamp.Revocations) : validation.Revocations;

    /// <summary>
    /// One of the store's arrays of streams (Table 261), holding each encoding once: entries already there are matched by their decoded
    /// bytes, new ones become new streams appended at <see cref="Finish"/>.
    /// </summary>
    private sealed class StoreArray
    {
        private readonly PdfDocument _document;
        private readonly CosDictionary _store;
        private readonly CosName _key;
        private readonly Dictionary<string, CosReference> _known = new(StringComparer.Ordinal);
        private readonly List<CosObject> _added = [];

        public StoreArray(PdfDocument document, CosDictionary store, CosName key)
        {
            _document = document;
            _store = store;
            _key = key;
            if (document.Resolve(store.TryGetValue(key, out CosObject? value) ? value : null) is not CosArray array)
            {
                return;
            }

            foreach (CosObject item in array)
            {
                if (item is CosReference reference && document.Resolve(reference) is CosStream stream)
                {
                    try
                    {
                        _known.TryAdd(Key(document.DecodeStream(stream).Span), reference);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or global::Broadside.Diagnostics.DiagnosticException)
                    {
                        // A stream that does not decode matches nothing; it stays where it is.
                    }
                }
            }
        }

        public CosReference Add(ReadOnlyMemory<byte> encoded)
        {
            string key = Key(encoded.Span);
            if (!_known.TryGetValue(key, out CosReference? reference))
            {
                reference = _document.AddObject(new CosStream(new CosDictionary(), encoded.ToArray()));
                _known.Add(key, reference);
                _added.Add(reference);
            }

            return reference;
        }

        public void Finish()
        {
            if (_added.Count == 0)
            {
                return;
            }

            if (_document.Resolve(_store.TryGetValue(_key, out CosObject? value) ? value : null) is CosArray array)
            {
                foreach (CosObject reference in _added)
                {
                    array.Add(reference);
                }
            }
            else
            {
                _store[_key] = new CosArray(_added);
            }
        }

        private static string Key(ReadOnlySpan<byte> data) => Convert.ToBase64String(IncrementalDigest.HashData(HashAlgorithmName.SHA256, data));
    }
}
