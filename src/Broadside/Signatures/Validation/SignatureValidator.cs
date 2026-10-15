using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>What every signature of one validation shares: the options, the current time, extra certificates and revocation data.</summary>
/// <param name="Options">The caller's options.</param>
/// <param name="Now">The current time of the validation (the option's validation time, or when validation started).</param>
/// <param name="ExtraCertificates">The options' extra certificates and the document security store's.</param>
/// <param name="Revocation">The options' and the document security store's revocation data, and the fetcher if any.</param>
internal sealed record ValidationParameters(
    PdfSignatureValidationOptions Options,
    DateTimeOffset Now,
    IReadOnlyList<X509Certificate2> ExtraCertificates,
    RevocationSources Revocation);

/// <summary>
/// Validates one signature dictionary against the bytes of the file it is in: the byte range, the digest of the bytes it covers, the
/// CMS signature value, the signing-certificate attribute, the signature time-stamp token, and the signer's certificate path with the
/// revocation of each certificate.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 (byte range digest; verification), §12.8.3.1 (Table 260), §12.8.3.3.1 (CMS signatures: <c>adbe.pkcs7.detached</c>
/// and the deprecated <c>adbe.pkcs7.sha1</c>), §12.8.3.3.2 (revocation information in the signature), §12.8.3.4 (<c>ETSI.CAdES.detached</c>;
/// §12.8.3.4.5 to §12.8.3.4.8 for the signing-certificate check, the time in the past and revocation), §12.8.5 (<c>ETSI.RFC3161</c>
/// document timestamps, whose token's message imprint is the byte range digest); RFC 5652 §5.4 and §5.6; RFC 3161 §2.3, §2.4 and
/// Appendix A; RFC 5035 §3.
/// </remarks>
internal static class SignatureValidator
{
    private const string Pkcs7Detached = "adbe.pkcs7.detached";
    private const string Pkcs7Sha1 = "adbe.pkcs7.sha1";
    private const string CadesDetached = "ETSI.CAdES.detached";
    private const string Rfc3161 = "ETSI.RFC3161";
    private const string X509RsaSha1 = "adbe.x509.rsa_sha1";
    private const string TimeStampingUsage = "1.3.6.1.5.5.7.3.8";

    private static readonly CosName SubFilterName = new("SubFilter");
    private static readonly SearchValues<byte> WhiteSpace = SearchValues.Create("\0\t\n\f\r "u8);

    /// <summary>Validates the signature dictionary <paramref name="dictionary"/> of <paramref name="field"/>.</summary>
    /// <param name="source">The file, read from its first byte (the byte range counts from there).</param>
    /// <param name="field">The signature field.</param>
    /// <param name="dictionary">Its signature dictionary.</param>
    /// <param name="resolve">Resolves indirect references in the dictionary.</param>
    /// <param name="parameters">The options, the current time and the shared certificates and revocation data.</param>
    /// <param name="asynchronous">Whether an online revocation fetch may complete asynchronously.</param>
    /// <param name="cancellationToken">Cancels an online revocation fetch.</param>
    public static async ValueTask<PdfSignatureValidation> ValidateAsync(
        PdfSource source,
        PdfSignatureField field,
        CosDictionary dictionary,
        Func<CosObject?, CosObject> resolve,
        ValidationParameters parameters,
        bool asynchronous,
        CancellationToken cancellationToken)
    {
        var problems = new List<PdfSignatureProblem>();
        string? subFilter = resolve(dictionary.TryGetValue(SubFilterName, out CosObject? name) ? name : null) is CosName value ? value.Value : null;
        long[] range = ReadByteRange(resolve, dictionary);
        var context = new Context(source, field, subFilter, range, problems, parameters.Now);
        if (range.Length == 0)
        {
            problems.Add(new(PdfSignatureProblemKind.ByteRangeMissing, "The signature dictionary has no ByteRange array of integers."));
            return context.Result();
        }

        if (!CheckByteRange(source.Length, range, problems))
        {
            return context.Result();
        }

        context.CoversWholeFile = EndsTheFile(source, range);
        if (resolve(dictionary.TryGetValue(KnownNames.Contents, out CosObject? contentsValue) ? contentsValue : null) is not CosString contents)
        {
            problems.Add(new(PdfSignatureProblemKind.ContentsMissing, "The signature dictionary has no Contents string."));
            return context.Result();
        }

        CheckGaps(source, range, contents.Bytes, problems);
        if (subFilter == X509RsaSha1)
        {
            problems.Add(new(PdfSignatureProblemKind.SubFilterUnsupported, "PKCS #1 signatures (adbe.x509.rsa_sha1, deprecated in PDF 2.0) are not validated."));
            return context.Result();
        }

        if (!CmsSignature.TryDecode(contents.Bytes.ToArray(), out CmsSignature cms, out string error))
        {
            bool known = subFilter is null or Pkcs7Detached or Pkcs7Sha1 or CadesDetached or Rfc3161;
            problems.Add(known
                ? new(PdfSignatureProblemKind.SignatureEncodingInvalid, error)
                : new(PdfSignatureProblemKind.SubFilterUnsupported, $"The SubFilter {subFilter} is not one this validation implements, and Contents is not a CMS object ({error})"));
            return context.Result();
        }

        await ValidateCmsAsync(context, cms, parameters, asynchronous, cancellationToken).ConfigureAwait(false);
        return context.Result();
    }

    private static async ValueTask ValidateCmsAsync(Context context, CmsSignature cms, ValidationParameters parameters, bool asynchronous, CancellationToken cancellationToken)
    {
        List<PdfSignatureProblem> problems = context.Problems;
        SignerCheck check = CheckSigner(cms, algorithm => DigestRange(context.Source, context.ByteRange, algorithm), context.SubFilter, problems);
        context.DigestAlgorithm = check.DigestAlgorithm;
        context.Signer = check.Signer;
        if (check.Signer is not { } signer)
        {
            return;
        }

        RevocationSources sources = parameters.Revocation.Including(cms.OcspResponses, cms.Crls);
        X509Certificate2[] pool = [.. check.Certificates, .. parameters.ExtraCertificates];
        if (check.TstInfo is { } info)
        {
            // A document timestamp (§12.8.5): the signature is a time-stamp token, checked as one at the current time.
            CheckTimestampUnit(cms, signer, info, problems);
            context.Path = CertificatePath.Build(signer, pool, parameters.Options.TrustAnchors, parameters.Now, problems, out int unitAnchor);
            context.Revocations = await RevocationChecker.CheckAsync(context.Path, unitAnchor, parameters.Now, parameters.Now, info.Time, sources, problems, asynchronous, cancellationToken).ConfigureAwait(false);
            context.Timestamp = new PdfTimestampValidation(info.Time, info.Policy, check.ImprintAlgorithm, signer, context.Path, context.Revocations, problems);
            return;
        }

        if (cms.TimestampTokens.Count > 0)
        {
            PdfTimestampValidation timestamp = await ValidateTokenAsync(cms.TimestampTokens[0], cms.Signature, parameters, sources, asynchronous, cancellationToken).ConfigureAwait(false);
            context.Timestamp = timestamp;
            if (timestamp.Status == PdfSignatureStatus.Invalid)
            {
                problems.Add(new(PdfSignatureProblemKind.TimestampInvalid, $"The signature time-stamp token does not check out ({string.Join("; ", timestamp.Problems.Where(static problem => problem.Kind < PdfSignatureProblemKind.SubFilterUnsupported).Select(static problem => problem.Message))}); its time is not used."));
            }
            else if (timestamp.Status == PdfSignatureStatus.Valid && timestamp.Time < parameters.Now)
            {
                // §12.8.3.4.8: "the signer's signature shall be verified at the UTC time in the past indicated in that token".
                context.Time = timestamp.Time;
            }
        }

        context.Path = CertificatePath.Build(signer, pool, parameters.Options.TrustAnchors, context.Time, problems, out int anchor);
        context.Revocations = await RevocationChecker.CheckAsync(context.Path, anchor, context.Time, parameters.Now, null, sources, problems, asynchronous, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates a signature time-stamp token (RFC 3161 Appendix A) over <paramref name="signatureValue"/>: the token as a CMS signature
    /// whose TSTInfo message imprint is the hash of the signature value, the unit's signing-certificate attribute and extended key usage,
    /// and its path and revocation at the current time (ISO 32000-2 §12.8.3.4.8).
    /// </summary>
    private static async ValueTask<PdfTimestampValidation> ValidateTokenAsync(
        byte[] token,
        byte[] signatureValue,
        ValidationParameters parameters,
        RevocationSources sources,
        bool asynchronous,
        CancellationToken cancellationToken)
    {
        var problems = new List<PdfSignatureProblem>();
        if (!CmsSignature.TryDecode(token, out CmsSignature cms, out string error))
        {
            problems.Add(new(PdfSignatureProblemKind.SignatureEncodingInvalid, $"The time-stamp token is not a CMS object: {error}"));
            return new PdfTimestampValidation(DateTimeOffset.MinValue, null, null, null, [], [], problems);
        }

        SignerCheck check = CheckSigner(cms, algorithm => IncrementalDigest.HashData(algorithm, signatureValue), Rfc3161, problems);
        if (check.TstInfo is not { } info)
        {
            if (!problems.Exists(static problem => problem.Kind < PdfSignatureProblemKind.SubFilterUnsupported))
            {
                problems.Add(new(PdfSignatureProblemKind.SignatureEncodingInvalid, "The time-stamp token does not encapsulate a TSTInfo."));
            }

            return new PdfTimestampValidation(DateTimeOffset.MinValue, null, null, check.Signer, check.Signer is null ? [] : [check.Signer], [], problems);
        }

        if (check.Signer is not { } unit)
        {
            return new PdfTimestampValidation(info.Time, info.Policy, check.ImprintAlgorithm, null, [], [], problems);
        }

        CheckTimestampUnit(cms, unit, info, problems);
        X509Certificate2[] pool = [.. check.Certificates, .. parameters.ExtraCertificates];
        IReadOnlyList<X509Certificate2> path = CertificatePath.Build(unit, pool, parameters.Options.TrustAnchors, parameters.Now, problems, out int anchor);
        RevocationSources tokenSources = sources.Including(cms.OcspResponses, cms.Crls);
        IReadOnlyList<PdfCertificateRevocation> revocations = await RevocationChecker.CheckAsync(path, anchor, parameters.Now, parameters.Now, info.Time, tokenSources, problems, asynchronous, cancellationToken).ConfigureAwait(false);
        return new PdfTimestampValidation(info.Time, info.Policy, check.ImprintAlgorithm, unit, path, revocations, problems);
    }

    /// <summary>
    /// What RFC 3161 asks of a token's signer beyond the CMS signature: the ESS signing-certificate attribute naming it (§2.4.1, RFC 5816),
    /// the time-stamping extended key usage, critical (§2.3), and a time within its validity period.
    /// </summary>
    private static void CheckTimestampUnit(CmsSignature cms, X509Certificate2 unit, TstInfo info, List<PdfSignatureProblem> problems)
    {
        if (cms.SigningCertificate is null)
        {
            problems.Add(new(PdfSignatureProblemKind.SigningCertificateMismatch, "The time-stamp token has no ESS signing-certificate attribute naming the unit's certificate (RFC 3161 §2.4.1)."));
        }

        X509EnhancedKeyUsageExtension? usage = unit.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (usage is null || !usage.Critical || !usage.EnhancedKeyUsages.Cast<Oid>().Any(static oid => oid.Value == TimeStampingUsage))
        {
            problems.Add(new(PdfSignatureProblemKind.KeyUsageNotPermitted, $"The time-stamping unit's certificate '{unit.Subject}' does not have the critical time-stamping extended key usage (RFC 3161 §2.3)."));
        }

        DateTime time = info.Time.UtcDateTime;
        if (time > unit.NotAfter.ToUniversalTime() || time < unit.NotBefore.ToUniversalTime())
        {
            problems.Add(new(
                time > unit.NotAfter.ToUniversalTime() ? PdfSignatureProblemKind.CertificateExpired : PdfSignatureProblemKind.CertificateNotYetValid,
                string.Create(CultureInfo.InvariantCulture, $"The token's time {info.Time:u} is outside the validity period of the unit's certificate '{unit.Subject}'.")));
        }
    }

    /// <summary>
    /// Checks a CMS object's first signer: the digest the content or the detached data must have, the signed attributes, the signer
    /// certificate, the signature value and the signing-certificate attribute.
    /// </summary>
    /// <param name="cms">The CMS object.</param>
    /// <param name="covered">The digest, with a given algorithm, of what the signature (or a token's message imprint) covers.</param>
    /// <param name="subFilter">The SubFilter, which decides what encapsulated content may be.</param>
    /// <param name="problems">Where problems go.</param>
    private static SignerCheck CheckSigner(CmsSignature cms, Func<HashAlgorithmName, byte[]> covered, string? subFilter, List<PdfSignatureProblem> problems)
    {
        if (!SignatureAlgorithms.TryGetDigest(cms.DigestAlgorithm.Oid, out HashAlgorithmName digestAlgorithm) || !IncrementalDigest.IsSupported(digestAlgorithm))
        {
            problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The digest algorithm {cms.DigestAlgorithm.Oid} is not supported."));
            return new SignerCheck(null, null, [], null, null);
        }

        X509Certificate2[] certificates = LoadCertificates(cms.Certificates);
        X509Certificate2? signer = FindSigner(cms, certificates);

        // What the signer info's message digest (or, with no signed attributes, the signature itself) covers (RFC 5652 §5.4).
        byte[] contentDigest = CheckContent(cms, digestAlgorithm, covered, subFilter, problems, out TstInfo? info, out HashAlgorithmName? imprintAlgorithm);
        bool supported = SignatureAlgorithms.TryGetSignatureDigest(cms.SignatureAlgorithm, digestAlgorithm, out HashAlgorithmName signatureDigest);
        if (!supported)
        {
            problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The signature algorithm {cms.SignatureAlgorithm.Oid} is not supported."));
        }

        if (cms.SignedAttributes is not null)
        {
            if (cms.MessageDigests.Count != 1)
            {
                problems.Add(new(PdfSignatureProblemKind.SignedAttributesInvalid, "The signed attributes do not hold exactly one message digest."));
            }
            else if (!cms.MessageDigests[0].AsSpan().SequenceEqual(contentDigest))
            {
                problems.Add(new(PdfSignatureProblemKind.DigestMismatch, "The message digest in the signature is not the digest of the signed bytes: they changed after signing."));
            }

            if (cms.ContentTypes.Count != 1 || cms.ContentTypes[0] != cms.ContentType)
            {
                problems.Add(new(PdfSignatureProblemKind.SignedAttributesInvalid, $"The signed attributes do not name the content type {cms.ContentType} exactly once."));
            }
        }

        if (signer is null)
        {
            problems.Add(new(PdfSignatureProblemKind.SignerCertificateMissing, "The CMS object does not carry the certificate its signer information names."));
            return new SignerCheck(digestAlgorithm, null, certificates, info, imprintAlgorithm);
        }

        if (supported)
        {
            // ISO/TS 32002 §5.1.4 and RFC 5652 §5.4: the signed attributes (or, without them, the content) are hashed with the
            // signer's digest algorithm; a signature algorithm that names its own digest (sha256WithRSAEncryption) is tried first,
            // since signers that write a different one sign with it.
            SignatureCheck verified = Verify(signatureDigest, signer.PublicKey);
            if (verified == SignatureCheck.Invalid && signatureDigest != digestAlgorithm)
            {
                verified = Verify(digestAlgorithm, signer.PublicKey) == SignatureCheck.Valid ? SignatureCheck.Valid : verified;
            }

            switch (verified)
            {
                case SignatureCheck.Invalid:
                    problems.Add(new(PdfSignatureProblemKind.SignatureValueInvalid, $"The signature value does not verify with the key of '{signer.Subject}'."));
                    break;
                case SignatureCheck.Unsupported:
                    problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The signature algorithm {cms.SignatureAlgorithm.Oid} cannot be verified here."));
                    break;
            }
        }

        CheckSigningCertificate(cms, signer, problems);
        return new SignerCheck(digestAlgorithm, signer, certificates, info, imprintAlgorithm);

        SignatureCheck Verify(HashAlgorithmName digest, PublicKey key)
        {
            byte[] signed = cms.SignedAttributes is { } attributes ? IncrementalDigest.HashData(digest, attributes)
                : digest == digestAlgorithm ? contentDigest
                : cms.Content is { } content ? IncrementalDigest.HashData(digest, content)
                : covered(digest);
            return SignatureAlgorithms.Verify(cms.SignatureAlgorithm, digest, key, signed, cms.Signature);
        }
    }

    /// <summary>
    /// ISO 32000-2 §12.8.3.4.5 a); RFC 5035 §3 and RFC 2634 §5.4: the first certificate identifier of an ESS signing-certificate(-v2)
    /// attribute must be the hash (and, when given, the serial number) of the signer's certificate.
    /// </summary>
    private static void CheckSigningCertificate(CmsSignature cms, X509Certificate2 signer, List<PdfSignatureProblem> problems)
    {
        if (cms.SigningCertificate is not { } identifier)
        {
            return;
        }

        if (!SignatureAlgorithms.TryGetDigest(identifier.HashAlgorithm, out HashAlgorithmName algorithm) || !IncrementalDigest.IsSupported(algorithm))
        {
            problems.Add(new(PdfSignatureProblemKind.SigningCertificateMismatch, $"The signing-certificate attribute cannot be read, or hashes with the unsupported algorithm {identifier.HashAlgorithm}."));
            return;
        }

        bool hashMatches = IncrementalDigest.HashData(algorithm, signer.RawDataMemory.Span).AsSpan().SequenceEqual(identifier.Hash);
        bool serialMatches = identifier.SerialNumber.IsEmpty || identifier.SerialNumber.Span.SequenceEqual(OcspResponse.TrimLeadingZeros(signer.SerialNumberBytes).Span);
        if (!hashMatches || !serialMatches)
        {
            problems.Add(new(PdfSignatureProblemKind.SigningCertificateMismatch, $"The signing-certificate attribute does not identify the signer's certificate '{signer.Subject}'."));
        }
    }

    /// <summary>
    /// Checks the digest the CMS object carries itself (the SHA-1 digest of <c>adbe.pkcs7.sha1</c>, the message imprint of a time-stamp
    /// token) and returns the digest the signer info signs: of the encapsulated content, or of what the signature covers.
    /// </summary>
    private static byte[] CheckContent(
        CmsSignature cms,
        HashAlgorithmName digestAlgorithm,
        Func<HashAlgorithmName, byte[]> covered,
        string? subFilter,
        List<PdfSignatureProblem> problems,
        out TstInfo? info,
        out HashAlgorithmName? imprintAlgorithm)
    {
        info = null;
        imprintAlgorithm = null;
        if (cms.Content is not { } content)
        {
            return covered(digestAlgorithm);
        }

        if (cms.ContentType == CmsSignature.TstInfoOid)
        {
            if (!CmsSignature.TryReadTstInfo(content, out TstInfo read))
            {
                problems.Add(new(PdfSignatureProblemKind.SignatureEncodingInvalid, "The timestamp token's TSTInfo cannot be decoded."));
            }
            else if (!SignatureAlgorithms.TryGetDigest(read.ImprintAlgorithm.Oid, out HashAlgorithmName imprintDigest) || !IncrementalDigest.IsSupported(imprintDigest))
            {
                info = read;
                problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The message imprint's digest algorithm {read.ImprintAlgorithm.Oid} is not supported."));
            }
            else
            {
                info = read;
                imprintAlgorithm = imprintDigest;
                if (!read.Imprint.AsSpan().SequenceEqual(covered(imprintDigest)))
                {
                    problems.Add(new(PdfSignatureProblemKind.DigestMismatch, "The timestamp token's message imprint is not the digest of what it time-stamps: the data changed after time-stamping."));
                }
            }
        }
        else if (subFilter is Pkcs7Sha1 or null)
        {
            // §12.8.3.3.1: the SHA-1 digest of the byte range is encapsulated as id-data.
            if (!content.AsSpan().SequenceEqual(covered(HashAlgorithmName.SHA1)))
            {
                problems.Add(new(PdfSignatureProblemKind.DigestMismatch, "The encapsulated SHA-1 digest is not the digest of the signed bytes: they changed after signing."));
            }
        }
        else
        {
            problems.Add(new(PdfSignatureProblemKind.SignatureEncodingInvalid, $"A {subFilter} signature encapsulates content; it must be detached."));
        }

        return IncrementalDigest.HashData(digestAlgorithm, content);
    }

    private static long[] ReadByteRange(Func<CosObject?, CosObject> resolve, CosDictionary dictionary)
    {
        if (resolve(dictionary.TryGetValue(KnownNames.ByteRange, out CosObject? value) ? value : null) is not CosArray array || array.Count == 0)
        {
            return [];
        }

        long[] range = new long[array.Count];
        for (int index = 0; index < range.Length; index++)
        {
            if (resolve(array[index]) is not CosInteger integer)
            {
                return [];
            }

            range[index] = integer.Value;
        }

        return range;
    }

    /// <summary>Pairs of non-negative offset and length, ascending and apart, within the file (§12.8.1, Table 255).</summary>
    private static bool CheckByteRange(long fileLength, long[] range, List<PdfSignatureProblem> problems)
    {
        if (range.Length % 2 != 0)
        {
            problems.Add(new(PdfSignatureProblemKind.ByteRangeInvalid, $"The ByteRange holds {range.Length} numbers, not pairs."));
            return false;
        }

        long end = 0;
        for (int index = 0; index < range.Length; index += 2)
        {
            long offset = range[index];
            long length = range[index + 1];
            if (offset < 0 || length < 0 || offset > fileLength || length > fileLength - offset)
            {
                problems.Add(new(PdfSignatureProblemKind.ByteRangeInvalid, $"The range {offset} {length} is not within the file's {fileLength} bytes."));
                return false;
            }

            if (offset < end)
            {
                problems.Add(new(PdfSignatureProblemKind.ByteRangeInvalid, $"The range {offset} {length} starts before the previous one ends."));
                return false;
            }

            end = offset + length;
        }

        return true;
    }

    /// <summary>
    /// The bytes outside the ranges up to the last one's end must be exactly one gap, holding <paramref name="contents"/> as one
    /// hexadecimal string (§12.8.1; §12.8.3.3.1: Contents "shall fit precisely in the space between the ranges").
    /// </summary>
    private static void CheckGaps(PdfSource source, long[] range, ReadOnlySpan<byte> contents, List<PdfSignatureProblem> problems)
    {
        if (range[0] > 0)
        {
            problems.Add(new(PdfSignatureProblemKind.ByteRangeGap, $"The byte range starts at {range[0]}, leaving the first bytes of the file unsigned."));
        }

        int holding = 0;
        var others = new List<(long Start, long End)>();
        for (int index = 2; index < range.Length; index += 2)
        {
            long start = range[index - 2] + range[index - 1];
            long end = range[index];
            if (end == start)
            {
                continue;
            }

            if (holding == 0 && GapHolds(source, start, end, contents))
            {
                holding++;
            }
            else
            {
                others.Add((start, end));
            }
        }

        if (holding == 0)
        {
            problems.Add(new(PdfSignatureProblemKind.ContentsOutsideGap, "No bytes the byte range leaves out are this signature's Contents as one hexadecimal string."));
            if (others.Count > 0)
            {
                others.RemoveAt(0);
            }
        }

        foreach ((long start, long end) in others)
        {
            problems.Add(new(PdfSignatureProblemKind.ByteRangeGap, $"The byte range leaves bytes {start} to {end} unsigned besides the signature value."));
        }
    }

    /// <summary>
    /// Whether the file's bytes from <paramref name="start"/> to <paramref name="end"/> are <c>&lt;</c>, the hexadecimal digits of
    /// <paramref name="contents"/> and <c>&gt;</c>; or only those digits, between a signed <c>&lt;</c> and <c>&gt;</c>, as some signers
    /// write the range (the delimiters are then signed, which leaves nothing else out).
    /// </summary>
    private static bool GapHolds(PdfSource source, long start, long end, ReadOnlySpan<byte> contents)
    {
        long length = end - start;
        if (start < 1 || length < 0 || length > (2L * contents.Length) + 2 + 4096)
        {
            return false;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)length + 2);
        try
        {
            Span<byte> around = buffer.AsSpan(0, (int)length + 2);
            int read = source.Read(start - 1, around);
            Span<byte> digits;
            if (read >= length + 1 && length >= 2 && around[1] == (byte)'<' && around[(int)length] == (byte)'>')
            {
                digits = around[2..(int)length];
            }
            else if (read == around.Length && around[0] == (byte)'<' && around[^1] == (byte)'>')
            {
                digits = around[1..^1];
            }
            else
            {
                return false;
            }

            int index = 0;
            int high = -1;
            foreach (byte character in digits)
            {
                if (WhiteSpace.Contains(character))
                {
                    continue;
                }

                int digit = HexValue(character);
                if (digit < 0)
                {
                    return false;
                }

                if (high < 0)
                {
                    high = digit;
                    continue;
                }

                if (index >= contents.Length || contents[index] != (byte)((high << 4) | digit))
                {
                    return false;
                }

                index++;
                high = -1;
            }

            // §7.3.4.3: a final odd digit is followed by an assumed zero.
            if (high >= 0)
            {
                if (index >= contents.Length || contents[index] != (byte)(high << 4))
                {
                    return false;
                }

                index++;
            }

            return index == contents.Length;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int HexValue(byte character) => character switch
    {
        >= (byte)'0' and <= (byte)'9' => character - '0',
        >= (byte)'A' and <= (byte)'F' => character - 'A' + 10,
        >= (byte)'a' and <= (byte)'f' => character - 'a' + 10,
        _ => -1,
    };

    /// <summary>Whether only white space follows the last range (§7.5.5: an end-of-line marker may follow <c>%%EOF</c>).</summary>
    private static bool EndsTheFile(PdfSource source, long[] range)
    {
        long end = range[^2] + range[^1];
        long rest = source.Length - end;
        if (rest > 64)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[(int)rest];
        return source.Read(end, tail) == tail.Length && tail.IndexOfAnyExcept(WhiteSpace) < 0;
    }

    private static byte[] DigestRange(PdfSource source, long[] range, HashAlgorithmName algorithm)
    {
        using IncrementalDigest digest = IncrementalDigest.Create(algorithm);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            for (int index = 0; index < range.Length; index += 2)
            {
                long position = range[index];
                long remaining = range[index + 1];
                while (remaining > 0)
                {
                    int read = source.Read(position, buffer.AsSpan(0, (int)Math.Min(buffer.Length, remaining)));
                    if (read <= 0)
                    {
                        throw new InvalidOperationException("The file ended inside a signed byte range.");
                    }

                    digest.AppendData(buffer.AsSpan(0, read));
                    position += read;
                    remaining -= read;
                }
            }

            return digest.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static X509Certificate2[] LoadCertificates(IReadOnlyList<byte[]> encoded)
    {
        var certificates = new List<X509Certificate2>(encoded.Count);
        foreach (byte[] certificate in encoded)
        {
            try
            {
                certificates.Add(X509CertificateLoader.LoadCertificate(certificate));
            }
            catch (CryptographicException)
            {
                // A certificate the platform cannot parse cannot be the signer's or an issuer; it is left out.
            }
        }

        return [.. certificates];
    }

    /// <summary>The certificate the signer identifier names: by issuer and serial number, or by subject key identifier (RFC 5652 §5.3).</summary>
    private static X509Certificate2? FindSigner(CmsSignature cms, X509Certificate2[] certificates)
    {
        string? issuerName = null;
        if (cms.SubjectKeyIdentifier.IsEmpty)
        {
            try
            {
                issuerName = new X500DistinguishedName(cms.IssuerName.Span).Name;
            }
            catch (CryptographicException)
            {
                // Only the encodings can be compared.
            }
        }

        foreach (X509Certificate2 certificate in certificates)
        {
            if (!cms.SubjectKeyIdentifier.IsEmpty)
            {
                if (certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault()?.SubjectKeyIdentifierBytes is { } key
                    && key.Span.SequenceEqual(cms.SubjectKeyIdentifier.Span))
                {
                    return certificate;
                }

                continue;
            }

            ReadOnlySpan<byte> serial = certificate.SerialNumberBytes.Span;
            int start = serial.IndexOfAnyExcept((byte)0);
            serial = start < 0 ? [] : serial[start..];
            if (serial.SequenceEqual(cms.SerialNumber.Span)
                && (certificate.IssuerName.RawData.AsSpan().SequenceEqual(cms.IssuerName.Span)
                    || (issuerName is { Length: > 0 } && string.Equals(certificate.IssuerName.Name, issuerName, StringComparison.OrdinalIgnoreCase))))
            {
                return certificate;
            }
        }

        return null;
    }

    /// <summary>What checking a CMS object's signer found.</summary>
    private sealed record SignerCheck(
        HashAlgorithmName? DigestAlgorithm,
        X509Certificate2? Signer,
        X509Certificate2[] Certificates,
        TstInfo? TstInfo,
        HashAlgorithmName? ImprintAlgorithm);

    /// <summary>What validation has found so far for one signature.</summary>
    private sealed class Context(PdfSource source, PdfSignatureField field, string? subFilter, long[] byteRange, List<PdfSignatureProblem> problems, DateTimeOffset now)
    {
        public PdfSource Source => source;

        public string? SubFilter => subFilter;

        public long[] ByteRange => byteRange;

        public List<PdfSignatureProblem> Problems => problems;

        public bool CoversWholeFile { get; set; }

        public HashAlgorithmName? DigestAlgorithm { get; set; }

        public X509Certificate2? Signer { get; set; }

        public IReadOnlyList<X509Certificate2> Path { get; set; } = [];

        public DateTimeOffset Time { get; set; } = now;

        public PdfTimestampValidation? Timestamp { get; set; }

        public IReadOnlyList<PdfCertificateRevocation> Revocations { get; set; } = [];

        public PdfSignatureValidation Result() =>
            new(field, subFilter, byteRange, CoversWholeFile, DigestAlgorithm, Signer, Signer is null ? [] : Path.Count == 0 ? [Signer] : Path, problems, Time, Timestamp, Revocations);
    }
}
