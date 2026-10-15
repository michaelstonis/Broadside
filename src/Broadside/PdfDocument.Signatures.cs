using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.Signatures;
using Broadside.Writing;

namespace Broadside;

/// <content>Byte range signing (issues #193, #199): ISO 32000-2 §12.8.1, §12.8.2 and §12.8.3.3.</content>
public sealed partial class PdfDocument
{
    /// <summary>The placeholder each of the three computed <c>ByteRange</c> numbers is written as, wide enough for any of them.</summary>
    private const long ByteRangePlaceholder = 9_999_999_999;

    /// <summary>
    /// The room reserved in <c>Contents</c> for a PDF MAC token attached to the signature (ISO/TS 32004 §6.5.2): the token with its
    /// signatureDigest (under 400 bytes) and the attribute around it.
    /// </summary>
    private const int IntegrityAttributeAllowance = 512;

    private static readonly CosName ByteRangeName = new("ByteRange");
    private static readonly CosName ContentsName = new("Contents");
    private static readonly CosName FilterName = new("Filter");
    private static readonly CosName SubFilterName = new("SubFilter");
    private static readonly CosName SigName = new("Sig");
    private static readonly CosName AdobePpkLite = new("Adobe.PPKLite");
    private static readonly CosName Pkcs7Detached = new("adbe.pkcs7.detached");
    private static readonly CosName CadesDetached = new("ETSI.CAdES.detached");
    private static readonly CosName MName = new("M");
    private static readonly CosName NameName = new("Name");
    private static readonly CosName ReasonName = new("Reason");
    private static readonly CosName LocationName = new("Location");
    private static readonly CosName ContactInfoName = new("ContactInfo");
    private static readonly CosName RectName = new("Rect");
    private static readonly CosName FName = new("F");
    private static readonly CosName WidgetName = new("Widget");
    private static readonly CosName SubtypeName = new("Subtype");

    /// <summary>
    /// Signs the document and writes the signed file to <paramref name="stream"/>: the document's file followed by an incremental
    /// update holding the signature, whose byte ranges cover the whole file except the signature value.
    /// </summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="signer">Signs the digest; <see cref="PdfCertificateSigner"/> for a certificate with its private key.</param>
    /// <param name="options">The field, the signature properties and the digest algorithm; <see langword="null"/> for the defaults.</param>
    /// <exception cref="InvalidOperationException">
    /// The field <see cref="PdfSignatureOptions.FieldName"/> names is already signed or is not a signature field; a certification
    /// signature is asked for a signed document; a lock is given for a field that has one; or the document's certification or a signed
    /// field lock permits no changes.
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="PdfSignatureOptions.FieldName"/> is empty or, for a new field, holds a period.</exception>
    /// <exception cref="NotSupportedException">
    /// The signer's key is neither an RSA key nor an ECDSA key on P-256, P-384 or P-521, the digest algorithm is not one ISO/TS 32002
    /// Table 3 pairs with the key's curve, or the document cannot be updated incrementally (it is encrypted).
    /// </exception>
    /// <exception cref="Diagnostics.DiagnosticException">
    /// In strict mode, when the signature needs a later PDF version than the document's (ECDSA, SHA-3 or a PAdES signature need 2.0).
    /// </exception>
    /// <exception cref="PdfTimestampException">
    /// With <see cref="PdfSignatureOptions.TimestampClient"/>, the time-stamp authority refused, could not be reached, or returned a
    /// token that does not answer the request.
    /// </exception>
    /// <exception cref="PlatformNotSupportedException">
    /// With <see cref="PdfSignatureOptions.TimestampClient"/>, the platform cannot send HTTP requests synchronously (the browser): use
    /// <see cref="SignAsync"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.8.1, §12.8.3.3, §12.8.3.4 and §12.7.5.5; ISO/TS 32001; ISO/TS 32002; ETSI EN 319 142-1. The signature goes into the unsigned signature field the options name, or into a
    /// new invisible signature field (a widget with an empty rectangle on the first page, flags Print and Locked) added to the
    /// interactive form, which is created when the document has none; the form's <c>SigFlags</c> gets SignaturesExist and AppendOnly
    /// (Table 225). The field's value becomes a signature dictionary (Table 255, every value direct) with <c>Filter</c>
    /// <c>Adobe.PPKLite</c>, the options' <c>SubFilter</c> (<c>adbe.pkcs7.detached</c>, or <c>ETSI.CAdES.detached</c> for a PAdES
    /// signature, which has no <c>Cert</c> entry, §12.8.3.4.2), <c>M</c> and the properties the options give, a
    /// <c>Contents</c> hexadecimal string reserved before anything is hashed, and a <c>ByteRange</c> of two ranges: from the first byte
    /// of the file to the <c>&lt;</c> of <c>Contents</c>, and from after its <c>&gt;</c> to the end of the file, which is the end of
    /// the update's <c>%%EOF</c> line. The offsets count from the first byte of the file.
    /// </para>
    /// <para>
    /// The ranges are hashed with the options' digest algorithm (or the key's, <see cref="PdfSignatureOptions.DigestAlgorithm"/>), the
    /// signer signs the digest of the CMS signed attributes (content type, that message digest and the ESS signing-certificate-v2
    /// attribute; no signing time, §12.8.3.4.3), digested with the same algorithm (ISO/TS 32002 §5.1.4), and the DER-encoded detached
    /// CMS <c>SignedData</c> (RFC 5652, with the signer's certificate and chain, and the signature algorithm the key calls for: RSA
    /// PKCS #1 v1.5 or RSASSA-PSS as <see cref="IPdfSigner.RsaSignaturePadding"/> says, or ECDSA) is written into <c>Contents</c>,
    /// padded with zeros (§12.8.3.3.1). With <c>ETSI.CAdES.detached</c> that is a PAdES baseline B-B signature (ETSI EN 319 142-1).
    /// Earlier signatures stay valid: the update only appends (§7.5.6, NOTE 1 of §12.8.1).
    /// </para>
    /// <para>
    /// With <see cref="PdfSignatureOptions.Certification"/> the signature is a certification signature (§12.8.2.2): its dictionary gets
    /// a <c>Reference</c> with a DocMDP signature reference whose transform parameters hold the permissions (Table 257, <c>P</c>,
    /// <c>V</c> 1.2), and the catalog's permissions dictionary a <c>DocMDP</c> entry naming it (Table 263); it is refused when the
    /// document already has a signature (§12.8.1). When the field has a lock dictionary, or
    /// <see cref="PdfSignatureOptions.FieldLock"/> gives one (written to the field, indirect, Table 236), the lock's <c>Action</c> and
    /// <c>Fields</c> are copied into a FieldMDP signature reference whose <c>Data</c> is the catalog (§12.8.2.4, Table 256). A
    /// document whose certification signature, named by the catalog's <c>Perms</c>, or a signed field lock permits no changes (<c>P</c> 1) refuses another signature: the
    /// permissions dictionary asks processors to enforce them (Table 263).
    /// </para>
    /// <para>
    /// The save raises the version to what the signature needs (Table 260: PDF 1.6 for SHA-256, 1.7 for SHA-384 and SHA-512, 2.0 for
    /// ECDSA, SHA-3 and PAdES) and declares the ISO_ developer extensions of ISO/TS 32001 (SHA-3 digests) and ISO/TS 32002 (ECDSA), in
    /// the signed update; in strict mode a raise is refused before anything changes (issue #141).
    /// </para>
    /// <para>
    /// With <see cref="PdfSignatureOptions.TimestampClient"/>, once the signer returns the signature value the time-stamp authority is
    /// asked for a token over it, and the token is added as the signer's signature time-stamp unsigned attribute (§12.8.3.3.1,
    /// §12.8.3.4.3 b); RFC 3161 Appendix A): with <c>ETSI.CAdES.detached</c>, a PAdES B-T signature. Its room in <c>Contents</c> is
    /// reserved with <see cref="PdfTimestampClient.TokenSizeEstimate"/>. A failed request leaves the document as a failed signer does.
    /// </para>
    /// <para>
    /// An encrypted document is updated with its own key. When it has, or its permissions require, an integrity MAC (ISO/TS 32004),
    /// the new MAC is attached to this signature as its <c>pdfMacData</c> unsigned attribute, digesting the signature's byte ranges
    /// and its signature value, and the trailer's <c>AuthCode</c> points at the signature dictionary (§6.5.2, §6.6.3).
    /// </para>
    /// <para>
    /// Once the last byte is written the document reads from the signed file, as after an incremental save
    /// (<see cref="PdfSaveOptions.IncrementalUpdate"/>), so signing again adds another revision. If the signer throws, nothing is
    /// written and the document is left as it was: every object, including what the save inside signing changed before the signer was
    /// asked (the modification date, the metadata layout of a raised version, completed font subsets, the catalog's version and
    /// extensions), and a modification date set explicitly is still the one the next save writes. A mutation: no read may run
    /// concurrently with it.
    /// </para>
    /// </remarks>
    public void Sign(Stream stream, IPdfSigner signer, PdfSignatureOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(signer);
        PendingSignature pending = PrepareSignature(signer, options ?? new PdfSignatureOptions());
        byte[] signature;
        try
        {
            signature = signer.SignDigest(pending.ToSign, pending.DigestAlgorithm);
            if (pending.Timestamp is { } timestamp)
            {
                pending.TimestampToken = timestamp.RequestToken(timestamp.DigestAlgorithm, SignatureImprint(pending, signature));
            }
        }
        catch
        {
            pending.Undo();
            throw;
        }

        UpdateResult update = CompleteSignature(pending, signature);
        CopySource(stream);
        stream.Write(update.Bytes.Span);
        Rebase(update);
    }

    /// <summary>Signs the document and writes the signed file to <paramref name="stream"/>, signing and writing asynchronously.</summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="signer">Signs the digest, through <see cref="IPdfSigner.SignDigestAsync"/>.</param>
    /// <param name="options">The field, the signature properties and the digest algorithm; <see langword="null"/> for the defaults.</param>
    /// <param name="cancellationToken">Cancels the signing and the writes.</param>
    /// <returns>A task that completes when the signed file is written.</returns>
    /// <exception cref="InvalidOperationException">
    /// The field <see cref="PdfSignatureOptions.FieldName"/> names is already signed or is not a signature field, or what
    /// certification and field locks do not allow is asked for (see <see cref="Sign"/>).
    /// </exception>
    /// <exception cref="ArgumentException"><see cref="PdfSignatureOptions.FieldName"/> is empty or, for a new field, holds a period.</exception>
    /// <exception cref="NotSupportedException">
    /// The signer's key or the digest algorithm is not supported (see <see cref="Sign"/>), or the document cannot be updated
    /// incrementally (it is encrypted).
    /// </exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the signature needs a later PDF version than the document's.</exception>
    /// <exception cref="PdfTimestampException">
    /// With <see cref="PdfSignatureOptions.TimestampClient"/>, the time-stamp authority refused, could not be reached, or returned a
    /// token that does not answer the request.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §12.8.1, §12.8.3.3 and §12.8.3.4. Writes what <see cref="Sign"/> writes; only the signer call and the writes to
    /// <paramref name="stream"/> are asynchronous. A cancelled or failed signer leaves the document as it was.
    /// </remarks>
    public async Task SignAsync(Stream stream, IPdfSigner signer, PdfSignatureOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(signer);
        PendingSignature pending = PrepareSignature(signer, options ?? new PdfSignatureOptions());
        byte[] signature;
        try
        {
            signature = await signer.SignDigestAsync(pending.ToSign, pending.DigestAlgorithm, cancellationToken).ConfigureAwait(false);
            if (pending.Timestamp is { } timestamp)
            {
                pending.TimestampToken = await timestamp.RequestTokenAsync(timestamp.DigestAlgorithm, SignatureImprint(pending, signature), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            pending.Undo();
            throw;
        }

        UpdateResult update = CompleteSignature(pending, signature);
        long length = _source.Length;
        for (long position = 0; position < length;)
        {
            ReadOnlyMemory<byte> window = SourceWindow(position, length);
            await stream.WriteAsync(window, cancellationToken).ConfigureAwait(false);
            position += window.Length;
        }

        await stream.WriteAsync(update.Bytes, cancellationToken).ConfigureAwait(false);
        Rebase(update);
    }

    /// <summary>
    /// Places the signature dictionary in its field, writes the incremental update with placeholders, fills in the byte ranges and
    /// hashes them; the document is changed in memory only, and <see cref="PendingSignature.Undo"/> reverts it.
    /// </summary>
    private PendingSignature PrepareSignature(IPdfSigner signer, PdfSignatureOptions options)
    {
        ArgumentNullException.ThrowIfNull(signer.Certificate);
        SigningKey key = CmsSignedData.ReadKey(signer.Certificate);
        RSASignaturePadding padding = signer.RsaSignaturePadding ?? RSASignaturePadding.Pkcs1;
        HashAlgorithmName digestAlgorithm = options.DigestAlgorithm ?? CmsSignedData.DefaultDigest(key);
        CmsSignedData.RequireCompatible(key, digestAlgorithm, padding);
        IReadOnlyList<X509Certificate2> chain = signer.CertificateChain ?? [];
        PdfTimestampClient? timestamp = options.TimestampClient;
        int reserved = CmsSignedData.EstimateLength(key, signer.Certificate, chain, timestamp?.TokenSizeEstimate ?? 0);
        if (Security is { } security && (security.Integrity != PdfIntegrityStatus.None || security.RequiresIntegrityCode))
        {
            // ISO/TS 32004 §6.5.2: the save keeps the document's MAC, as a pdfMacData unsigned attribute of this signature.
            reserved += IntegrityAttributeAllowance;
        }

        ModificationDetectionPlan modificationDetection = PlanModificationDetection(options);
        var undo = new List<Action>();
        try
        {
            VersionedFeature[] features = [.. SignatureFeatures(key, digestAlgorithm, options.SubFilter), .. modificationDetection.Features];
            if (timestamp is not null)
            {
                features = [.. features, .. TimestampFeatures(VersionedFeatures.SignatureTimestamp, timestamp)];
            }
            foreach (VersionedFeature feature in features)
            {
                RequireSignatureFeature(feature, undo);
            }

            bool pdf20 = Version >= Pdf20 || features.Any(feature => feature.MinimumVersion >= Pdf20);
            CosDictionary signature = SignatureDictionary(options, reserved, pdf20);
            CosReference signatureReference = AddObject(signature);
            undo.Add(() => RemoveObject(signatureReference));
            CosDictionary field = PlaceSignature(options.FieldName, signatureReference, undo);
            ApplyModificationDetection(modificationDetection, signature, signatureReference, field, digestAlgorithm, undo);

            // Issue #306: what the save changes (the dating, the metadata layout, completed font subsets, the catalog's version and
            // extensions) is reverted with the rest when the signer or the authority fails.
            var saveChanges = new CosChangeLog();
            undo.Add(saveChanges.Revert);
            FileWriter writer = PlanSave(new PdfSaveOptions { IncrementalUpdate = true }, saveChanges);
            IntegrityCodeWriter? integrity = writer.Integrity;
            if (integrity is not null)
            {
                writer.IntegritySignature = signatureReference;
            }

            foreach (ReadOnlyMemory<byte> _ in writer.Write())
            {
                // The source's bytes are copied again from the source once signed; the update is kept by the writer.
            }

            UpdateResult update = writer.Update!;
            WrittenObject written = update.Objects.Single(item => ReferenceEquals(item.Value, signature));
            byte[] bytes = update.Bytes.ToArray();
            long sourceLength = _source.Length;
            var placeholders = SignaturePlaceholders.Find(bytes, checked((int)(written.Offset - sourceLength)), written.Length);

            long contentsStart = sourceLength + placeholders.ContentsStart;
            long contentsEnd = sourceLength + placeholders.ContentsEnd;
            long total = sourceLength + bytes.Length;
            long[] byteRange = [0, contentsStart, contentsEnd, total - contentsEnd];
            placeholders.WriteByteRange(bytes, byteRange);

            using IncrementalDigest digest = IncrementalDigest.Create(digestAlgorithm);
            using IncrementalDigest? macDigest = integrity is null ? null : IntegrityCodeWriter.CreateDigest();
            for (long position = 0; position < sourceLength;)
            {
                ReadOnlyMemory<byte> window = SourceWindow(position, sourceLength);
                digest.AppendData(window.Span);
                macDigest?.AppendData(window.Span);
                position += window.Length;
            }

            digest.AppendData(bytes.AsSpan(0, placeholders.ContentsStart));
            digest.AppendData(bytes.AsSpan(placeholders.ContentsEnd));
            macDigest?.AppendData(bytes.AsSpan(0, placeholders.ContentsStart));
            macDigest?.AppendData(bytes.AsSpan(placeholders.ContentsEnd));
            byte[] signedAttributes = CmsSignedData.SignedAttributes(digest.GetHashAndReset(), digestAlgorithm, signer.Certificate);

            // ISO/TS 32002 §5.1.4: the signed attributes are digested with the message digest's algorithm.
            byte[] toSign = IncrementalDigest.HashData(digestAlgorithm, signedAttributes);

            return new PendingSignature(
                signer, chain, key, padding, digestAlgorithm, signature, update with { Bytes = bytes }, placeholders, byteRange, signedAttributes, toSign, reserved, undo)
            {
                Integrity = integrity,
                IntegrityDataDigest = macDigest?.GetHashAndReset(),
                Timestamp = timestamp,
            };
        }
        catch
        {
            Revert(undo);
            throw;
        }
    }

    /// <summary>Writes the CMS object into the reserved <c>Contents</c> and records the final values in the signature dictionary.</summary>
    private static UpdateResult CompleteSignature(PendingSignature pending, byte[] signature)
    {
        byte[] cms;
        try
        {
            ArgumentNullException.ThrowIfNull(signature);
            byte[] value = CmsSignedData.NormalizeSignature(pending.Key, signature);

            // ISO/TS 32004 §6.6.3: the token digests the signature's byte range and its signature value, as the CMS object holds it.
            byte[]? macToken = pending.Integrity?.CreateToken(
                pending.IntegrityDataDigest,
                IncrementalDigest.HashData(HashAlgorithmName.SHA256, value));
            cms = CmsSignedData.Encode(
                pending.DigestAlgorithm,
                pending.Key,
                pending.Padding,
                pending.SignedAttributes,
                value,
                pending.Signer.Certificate,
                pending.Chain,
                macToken,
                pending.TimestampToken);
            if (cms.Length > pending.Reserved)
            {
                throw new InvalidOperationException(pending.TimestampToken is null
                    ? $"The signature takes {cms.Length} bytes, more than the {pending.Reserved} reserved for it; the signer returned an unexpectedly long signature value."
                    : $"The signature with its {pending.TimestampToken.Length}-byte time-stamp token takes {cms.Length} bytes, more than the {pending.Reserved} reserved for it; raise {nameof(PdfTimestampClient)}.{nameof(PdfTimestampClient.TokenSizeEstimate)} ({pending.Timestamp!.TokenSizeEstimate}).");
            }
        }
        catch
        {
            pending.Undo();
            throw;
        }

        byte[] bytes = pending.Update.Bytes.ToArray();
        pending.Placeholders.WriteContents(bytes, cms);

        // The document's own objects now hold what the file holds, so the rebase marks them clean with the right values.
        pending.Signature[ByteRangeName] = new CosArray([.. pending.ByteRange.Select(value => (CosObject)new CosInteger(value))]);
        byte[] contents = new byte[pending.Reserved];
        cms.CopyTo(contents, 0);
        pending.Signature[ContentsName] = new CosString(contents, hexadecimal: true);
        return pending.Update with { Bytes = bytes };
    }

    /// <summary>
    /// The message imprint of a signature time-stamp: the hash of the signature value as the <c>SignerInfo</c> holds it (RFC 3161
    /// Appendix A; ETSI EN 319 122-1 §5.3), with the time-stamp client's algorithm.
    /// </summary>
    private static byte[] SignatureImprint(PendingSignature pending, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return pending.Timestamp!.Hash(CmsSignedData.NormalizeSignature(pending.Key, signature));
    }

    /// <summary>
    /// The features a signature writes (Table 260; ISO/TS 32001 and 32002): the sub-filter, the key and the digest, each with the
    /// version it needs and, for ECDSA and SHA-3, the developer extension it declares.
    /// </summary>
    private static VersionedFeature[] SignatureFeatures(SigningKey key, HashAlgorithmName digestAlgorithm, PdfSignatureSubFilter subFilter)
    {
        var features = new List<VersionedFeature>
        {
            subFilter == PdfSignatureSubFilter.EtsiCadesDetached ? VersionedFeatures.CadesSignature : VersionedFeatures.ByteRangeSignature,
        };
        if (key.Type == SigningKeyType.Ecdsa)
        {
            features.Add(VersionedFeatures.EcdsaSignature);
        }
        else if (key.Size > 1024)
        {
            features.Add(VersionedFeatures.LongRsaKeySignature);
        }

        features.Add(
            CmsSignedData.IsSha3(digestAlgorithm) ? VersionedFeatures.Sha3SignatureDigest
            : digestAlgorithm == HashAlgorithmName.SHA256 ? VersionedFeatures.Sha256SignatureDigest
            : VersionedFeatures.Sha2SignatureDigest);
        return [.. features];
    }

    /// <summary>Records <paramref name="feature"/> (<see cref="RequireFeature"/>), forgetting it again on undo when it is new.</summary>
    private void RequireSignatureFeature(VersionedFeature feature, List<Action> undo)
    {
        bool added;
        lock (_featureGate)
        {
            added = _features.Add(feature);
        }

        if (added)
        {
            undo.Add(() =>
            {
                lock (_featureGate)
                {
                    _features.Remove(feature);
                }
            });
        }
    }

    /// <summary>A new signature dictionary (Table 255): the two placeholders first, so the writer puts them where they are found.</summary>
    /// <remarks>
    /// §12.8.3.4.2: a PAdES signature has no <c>Cert</c> entry and gives the time of signing in <c>M</c>, its signed attributes
    /// holding no signing time.
    /// </remarks>
    private static CosDictionary SignatureDictionary(PdfSignatureOptions options, int reserved, bool pdf20)
    {
        var dictionary = new CosDictionary
        {
            [ByteRangeName] = new CosArray([new CosInteger(0), new CosInteger(ByteRangePlaceholder), new CosInteger(ByteRangePlaceholder), new CosInteger(ByteRangePlaceholder)]),
            [ContentsName] = new CosString(new byte[reserved], hexadecimal: true),
            [KnownNames.Type] = SigName,
            [FilterName] = AdobePpkLite,
            [SubFilterName] = options.SubFilter == PdfSignatureSubFilter.EtsiCadesDetached ? CadesDetached : Pkcs7Detached,
            [MName] = new CosString(Encoding.ASCII.GetBytes(FormatDate(options.SigningTime ?? DateTimeOffset.Now, pdf20))),
        };
        AddText(dictionary, NameName, options.SignerName);
        AddText(dictionary, ReasonName, options.Reason);
        AddText(dictionary, LocationName, options.Location);
        AddText(dictionary, ContactInfoName, options.ContactInfo);
        return dictionary;

        static void AddText(CosDictionary dictionary, CosName key, string? text)
        {
            if (text is not null)
            {
                dictionary[key] = new CosString(TextStringEncoder.Encode(text));
            }
        }
    }

    /// <summary>A date string (§7.9.4): <c>D:YYYYMMDDHHmmSS</c> and the offset, with the apostrophe after the minutes before PDF 2.0.</summary>
    private static string FormatDate(DateTimeOffset time, bool pdf20)
    {
        var text = new StringBuilder("D:");
        text.Append(time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        if (time.Offset == TimeSpan.Zero)
        {
            return text.Append('Z').ToString();
        }

        TimeSpan offset = time.Offset.Duration();
        text.Append(time.Offset < TimeSpan.Zero ? '-' : '+')
            .Append(offset.Hours.ToString("00", CultureInfo.InvariantCulture))
            .Append('\'')
            .Append(offset.Minutes.ToString("00", CultureInfo.InvariantCulture));
        return (pdf20 ? text : text.Append('\'')).ToString();
    }

    /// <summary>
    /// Makes <paramref name="signature"/> the value of the signature field: the unsigned one <paramref name="fieldName"/> names, or a
    /// new invisible one; sets SignaturesExist and AppendOnly (§12.7.3, Table 225).
    /// </summary>
    private CosDictionary PlaceSignature(string? fieldName, CosReference signature, List<Action> undo)
    {
        if (fieldName is not null && fieldName.Length == 0)
        {
            throw new ArgumentException("The signature field name is empty.", nameof(fieldName));
        }

        CosDictionary form = InteractiveForm(undo);
        PdfAcroForm view = AcroForm!;
        CosDictionary field;
        if (fieldName is not null && view.FindField(fieldName) is { } existing)
        {
            if (existing is not PdfSignatureField signatureField)
            {
                throw new InvalidOperationException($"The field '{fieldName}' is not a signature field.");
            }

            if (signatureField.IsSigned)
            {
                throw new InvalidOperationException($"The signature field '{fieldName}' is already signed.");
            }

            field = signatureField.Dictionary;
        }
        else
        {
            field = NewSignatureField(form, fieldName ?? UnusedFieldName(view), undo);
        }

        Set(field, FormNames.V, signature, undo);
        long flags = Resolve(form.TryGetValue(FormNames.SigFlags, out CosObject? sigFlags) ? sigFlags : null) is CosInteger integer ? integer.Value : 0;
        Set(form, FormNames.SigFlags, new CosInteger(flags | 3), undo);
        return field;
    }

    /// <summary>The interactive form dictionary, created (indirect, with an empty <c>Fields</c>) when the catalog has none.</summary>
    private CosDictionary InteractiveForm(List<Action> undo)
    {
        if (AcroForm is { } existing)
        {
            if (Resolve(existing.Dictionary.TryGetValue(FormNames.Fields, out CosObject? fields) ? fields : null) is not CosArray)
            {
                Set(existing.Dictionary, FormNames.Fields, new CosArray(), undo);
            }

            return existing.Dictionary;
        }

        var form = new CosDictionary { [FormNames.Fields] = new CosArray() };
        CosReference reference = AddObject(form);
        undo.Add(() => RemoveObject(reference));
        Set(Catalog, FormNames.AcroForm, reference, undo);
        return form;
    }

    /// <summary>
    /// A new signature field merged with its widget (§12.7.5.5, §12.5.6.19): an empty rectangle makes it invisible, on the first page
    /// when there is one, flags Print and Locked (Table 167).
    /// </summary>
    private CosDictionary NewSignatureField(CosDictionary form, string name, List<Action> undo)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException($"A new field's name cannot hold a period (§12.7.4.2): '{name}'.", nameof(name));
        }

        var field = new CosDictionary
        {
            [FormNames.FT] = FormNames.Sig,
            [FormNames.T] = new CosString(TextStringEncoder.Encode(name)),
            [KnownNames.Type] = AnnotationNames.Annot,
            [SubtypeName] = WidgetName,
            [RectName] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(0), new CosInteger(0)]),
            [FName] = new CosInteger(132),
        };
        PdfPage? page = Pages.Count > 0 ? Pages[0] : null;
        if (page?.Reference is { } pageReference)
        {
            field[FormNames.P] = pageReference;
        }

        CosReference reference = AddObject(field);
        undo.Add(() => RemoveObject(reference));
        Append((CosArray)Resolve(form[FormNames.Fields]), reference, undo);
        if (page is not null)
        {
            if (Resolve(page.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? annotations) ? annotations : null) is CosArray array)
            {
                Append(array, reference, undo);
            }
            else
            {
                Set(page.Dictionary, AnnotationNames.Annots, new CosArray([reference]), undo);
            }
        }

        return field;
    }

    /// <summary>The first name <c>SignatureN</c>, N from 1, that no field of the form has.</summary>
    private static string UnusedFieldName(PdfAcroForm form)
    {
        for (int number = 1; ; number++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"Signature{number}");
            if (form.FindFields(name).Count == 0)
            {
                return name;
            }
        }
    }

    private static void Set(CosDictionary dictionary, CosName key, CosObject value, List<Action> undo)
    {
        bool had = dictionary.TryGetValue(key, out CosObject? previous);
        dictionary[key] = value;
        undo.Add(() =>
        {
            if (had)
            {
                dictionary[key] = previous!;
            }
            else
            {
                dictionary.Remove(key);
            }
        });
    }

    private static void Append(CosArray array, CosObject value, List<Action> undo)
    {
        array.Add(value);
        undo.Add(() => array.RemoveAt(array.Count - 1));
    }

    private static void Revert(List<Action> undo)
    {
        for (int index = undo.Count - 1; index >= 0; index--)
        {
            undo[index]();
        }

        undo.Clear();
    }

    /// <summary>Writes every byte of the document's file to <paramref name="stream"/>.</summary>
    private void CopySource(Stream stream)
    {
        long length = _source.Length;
        for (long position = 0; position < length;)
        {
            ReadOnlyMemory<byte> window = SourceWindow(position, length);
            stream.Write(window.Span);
            position += window.Length;
        }
    }

    /// <summary>The source's bytes from <paramref name="position"/>, at most 1 MiB and no further than <paramref name="length"/>.</summary>
    private ReadOnlyMemory<byte> SourceWindow(long position, long length)
    {
        ReadOnlyMemory<byte> window = _source.GetWindow(position, 64 * 1024);
        if (window.IsEmpty)
        {
            throw new InvalidOperationException("The source file ended before its length.");
        }

        return window[..(int)Math.Min(window.Length, Math.Min(length - position, 1 << 20))];
    }

    /// <summary>A signature written with placeholders and hashed, waiting for its signature value.</summary>
    private sealed record PendingSignature(
        IPdfSigner Signer,
        IReadOnlyList<X509Certificate2> Chain,
        SigningKey Key,
        RSASignaturePadding Padding,
        HashAlgorithmName DigestAlgorithm,
        CosDictionary Signature,
        UpdateResult Update,
        SignaturePlaceholders Placeholders,
        long[] ByteRange,
        byte[] SignedAttributes,
        byte[] ToSign,
        int Reserved,
        List<Action> UndoActions)
    {
        /// <summary>Gets the composer of the PDF MAC token attached to the signature, or <see langword="null"/> when the document has no MAC.</summary>
        public IntegrityCodeWriter? Integrity { get; init; }

        /// <summary>Gets the SHA-256 digest of the signature's byte range, the token's dataDigest (ISO/TS 32004 §6.6.3).</summary>
        public byte[]? IntegrityDataDigest { get; init; }

        /// <summary>Gets the client of the authority that time-stamps the signature value, or <see langword="null"/> for no timestamp.</summary>
        public PdfTimestampClient? Timestamp { get; init; }

        /// <summary>Gets or sets the signature time-stamp token, once the authority issued it.</summary>
        public byte[]? TimestampToken { get; set; }

        public void Undo() => Revert(UndoActions);
    }
}
