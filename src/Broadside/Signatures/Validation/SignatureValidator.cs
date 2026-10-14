using System.Buffers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>
/// Validates one signature dictionary against the bytes of the file it is in: the byte range, the digest of the bytes it covers, the
/// CMS signature value and the signer's certificate path.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.1 (byte range digest; verification), §12.8.3.1 (Table 260), §12.8.3.3.1 (CMS signatures: <c>adbe.pkcs7.detached</c>
/// and the deprecated <c>adbe.pkcs7.sha1</c>), §12.8.3.4 (<c>ETSI.CAdES.detached</c>), §12.8.5 (<c>ETSI.RFC3161</c> document
/// timestamps, whose token's message imprint is the byte range digest); RFC 5652 §5.4 and §5.6.
/// </remarks>
internal static class SignatureValidator
{
    private const string Pkcs7Detached = "adbe.pkcs7.detached";
    private const string Pkcs7Sha1 = "adbe.pkcs7.sha1";
    private const string CadesDetached = "ETSI.CAdES.detached";
    private const string Rfc3161 = "ETSI.RFC3161";
    private const string X509RsaSha1 = "adbe.x509.rsa_sha1";

    private static readonly CosName SubFilterName = new("SubFilter");
    private static readonly SearchValues<byte> WhiteSpace = SearchValues.Create("\0\t\n\f\r "u8);

    /// <summary>Validates the signature dictionary <paramref name="dictionary"/> of <paramref name="field"/>.</summary>
    /// <param name="source">The file, read from its first byte (the byte range counts from there).</param>
    /// <param name="field">The signature field.</param>
    /// <param name="dictionary">Its signature dictionary.</param>
    /// <param name="resolve">Resolves indirect references in the dictionary.</param>
    /// <param name="options">The trust anchors, extra certificates and validation time.</param>
    /// <param name="time">The validation time.</param>
    public static PdfSignatureValidation Validate(
        PdfSource source,
        PdfSignatureField field,
        CosDictionary dictionary,
        Func<CosObject?, CosObject> resolve,
        PdfSignatureValidationOptions options,
        DateTimeOffset time)
    {
        var problems = new List<PdfSignatureProblem>();
        string? subFilter = resolve(dictionary.TryGetValue(SubFilterName, out CosObject? name) ? name : null) is CosName value ? value.Value : null;
        long[] range = ReadByteRange(resolve, dictionary);
        var context = new Context(source, field, subFilter, range, problems);
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

        ValidateCms(context, cms, options, time);
        return context.Result();
    }

    private static void ValidateCms(Context context, CmsSignature cms, PdfSignatureValidationOptions options, DateTimeOffset time)
    {
        List<PdfSignatureProblem> problems = context.Problems;
        if (!SignatureAlgorithms.TryGetDigest(cms.DigestAlgorithm.Oid, out HashAlgorithmName digestAlgorithm) || !IncrementalDigest.IsSupported(digestAlgorithm))
        {
            problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The digest algorithm {cms.DigestAlgorithm.Oid} is not supported."));
            return;
        }

        context.DigestAlgorithm = digestAlgorithm;
        X509Certificate2[] certificates = LoadCertificates(cms.Certificates);
        X509Certificate2? signer = FindSigner(cms, certificates);
        context.Signer = signer;

        // What the signer info's message digest (or, with no signed attributes, the signature itself) covers (RFC 5652 §5.4).
        byte[] contentDigest = CheckDocumentDigest(context, cms, digestAlgorithm);
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
            return;
        }

        if (supported)
        {
            // ISO/TS 32002 §5.1.4 and RFC 5652 §5.4: the signed attributes (or, without them, the content) are hashed with the
            // signer's digest algorithm; a signature algorithm that names its own digest (sha256WithRSAEncryption) is tried first,
            // since signers that write a different one sign with it.
            SignatureCheck check = Verify(signatureDigest, signer.PublicKey);
            if (check == SignatureCheck.Invalid && signatureDigest != digestAlgorithm)
            {
                check = Verify(digestAlgorithm, signer.PublicKey) == SignatureCheck.Valid ? SignatureCheck.Valid : check;
            }

            switch (check)
            {
                case SignatureCheck.Invalid:
                    problems.Add(new(PdfSignatureProblemKind.SignatureValueInvalid, $"The signature value does not verify with the key of '{signer.Subject}'."));
                    break;
                case SignatureCheck.Unsupported:
                    problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The signature algorithm {cms.SignatureAlgorithm.Oid} cannot be verified here."));
                    break;
            }
        }

        X509Certificate2[] pool = [.. certificates, .. options.ExtraCertificates];
        context.Path = CertificatePath.Build(signer, pool, options.TrustAnchors, time, problems);

        SignatureCheck Verify(HashAlgorithmName digest, PublicKey key)
        {
            byte[] signed = cms.SignedAttributes is { } attributes ? IncrementalDigest.HashData(digest, attributes)
                : digest == digestAlgorithm ? contentDigest
                : cms.Content is { } content ? IncrementalDigest.HashData(digest, content)
                : DigestRange(context.Source, context.ByteRange, digest);
            return SignatureAlgorithms.Verify(cms.SignatureAlgorithm, digest, key, signed, cms.Signature);
        }
    }

    /// <summary>
    /// Checks the byte range digest where the CMS object carries it itself (the SHA-1 digest of <c>adbe.pkcs7.sha1</c>, the message
    /// imprint of a timestamp token) and returns the digest the signer info signs: of the encapsulated content, or of the byte range.
    /// </summary>
    private static byte[] CheckDocumentDigest(Context context, CmsSignature cms, HashAlgorithmName digestAlgorithm)
    {
        if (cms.Content is not { } content)
        {
            return DigestRange(context.Source, context.ByteRange, digestAlgorithm);
        }

        if (cms.ContentType == CmsSignature.TstInfoOid)
        {
            if (!CmsSignature.TryReadMessageImprint(content, out AlgorithmIdentifier imprintAlgorithm, out byte[] imprint))
            {
                context.Problems.Add(new(PdfSignatureProblemKind.SignatureEncodingInvalid, "The timestamp token's TSTInfo cannot be decoded."));
            }
            else if (!SignatureAlgorithms.TryGetDigest(imprintAlgorithm.Oid, out HashAlgorithmName imprintDigest) || !IncrementalDigest.IsSupported(imprintDigest))
            {
                context.Problems.Add(new(PdfSignatureProblemKind.AlgorithmUnsupported, $"The message imprint's digest algorithm {imprintAlgorithm.Oid} is not supported."));
            }
            else if (!imprint.AsSpan().SequenceEqual(DigestRange(context.Source, context.ByteRange, imprintDigest)))
            {
                context.Problems.Add(new(PdfSignatureProblemKind.DigestMismatch, "The timestamp token's message imprint is not the digest of the signed bytes: they changed after signing."));
            }
        }
        else if (context.SubFilter is Pkcs7Sha1 or null)
        {
            // §12.8.3.3.1: the SHA-1 digest of the byte range is encapsulated as id-data.
            if (!content.AsSpan().SequenceEqual(DigestRange(context.Source, context.ByteRange, HashAlgorithmName.SHA1)))
            {
                context.Problems.Add(new(PdfSignatureProblemKind.DigestMismatch, "The encapsulated SHA-1 digest is not the digest of the signed bytes: they changed after signing."));
            }
        }
        else
        {
            context.Problems.Add(new(PdfSignatureProblemKind.SignatureEncodingInvalid, $"A {context.SubFilter} signature encapsulates content; it must be detached."));
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

    /// <summary>What validation has found so far for one signature.</summary>
    private sealed class Context(PdfSource source, PdfSignatureField field, string? subFilter, long[] byteRange, List<PdfSignatureProblem> problems)
    {
        public PdfSource Source => source;

        public string? SubFilter => subFilter;

        public long[] ByteRange => byteRange;

        public List<PdfSignatureProblem> Problems => problems;

        public bool CoversWholeFile { get; set; }

        public HashAlgorithmName? DigestAlgorithm { get; set; }

        public X509Certificate2? Signer { get; set; }

        public IReadOnlyList<X509Certificate2> Path { get; set; } = [];

        public PdfSignatureValidation Result() =>
            new(field, subFilter, byteRange, CoversWholeFile, DigestAlgorithm, Signer, Signer is null ? [] : Path.Count == 0 ? [Signer] : Path, problems);
    }
}
