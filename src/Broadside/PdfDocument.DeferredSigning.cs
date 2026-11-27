using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.Signatures;
using Broadside.Signatures.Validation;
using Broadside.Writing;

namespace Broadside;

/// <content>Deferred signing (issue #640): ISO 32000-2 §12.8.1 and §12.8.3.3, with the signature value supplied later.</content>
public sealed partial class PdfDocument
{
    private const string RsaPssKeyOid = "1.2.840.113549.1.1.10";

    /// <summary>
    /// Prepares a signature whose value is supplied later: writes the document's file followed by an incremental update holding
    /// the signature dictionary, with its <c>Contents</c> zero-filled, to <paramref name="stream"/>, and returns what the signer
    /// signs and the state that finishes it.
    /// </summary>
    /// <param name="stream">Where to write the prepared file, from its current position. It is not disposed.</param>
    /// <param name="certificate">The signer's certificate. Its private key is not needed.</param>
    /// <param name="chain">The other certificates of the signer's chain to embed; <see langword="null"/> to embed only <paramref name="certificate"/>.</param>
    /// <param name="options">
    /// The field, the signature properties and the digest algorithm, as for <see cref="Sign"/>; <see langword="null"/> for the
    /// defaults. Its <see cref="PdfSignatureOptions.TimestampClient"/> is not called: it reserves room for the token that
    /// <see cref="CompleteSignature"/> asks for.
    /// </param>
    /// <param name="rsaSignaturePadding">
    /// The signature scheme the signer uses with an RSA key; <see langword="null"/> for what <see cref="IPdfSigner.RsaSignaturePadding"/>
    /// defaults to (RSASSA-PSS for an <c>id-RSASSA-PSS</c> key, PKCS #1 v1.5 otherwise). Ignored for an ECDSA key.
    /// </param>
    /// <returns>The prepared signature; save its state with <see cref="PdfPreparedSignature.Save"/> to complete it in another process.</returns>
    /// <exception cref="InvalidOperationException">As for <see cref="Sign"/>: the field is signed or is not a signature field, or certification and field locks forbid the signature.</exception>
    /// <exception cref="ArgumentException"><see cref="PdfSignatureOptions.FieldName"/> is empty or, for a new field, holds a period.</exception>
    /// <exception cref="NotSupportedException">
    /// The key or the digest algorithm is not supported (see <see cref="Sign"/>), the document cannot be updated incrementally, or it
    /// carries ISO/TS 32004 integrity protection, which deferred signing does not support.
    /// </exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the signature needs a later PDF version than the document's.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.8.1, §12.8.3.3 and §12.8.3.4; ISO/TS 32002 §5.1.4. Everything <see cref="Sign"/> does before it asks the
    /// signer is done the same way: the field is placed, the appearance drawn, the update written with the reserved <c>Contents</c>,
    /// the <c>ByteRange</c> filled in and the byte ranges hashed into the CMS signed attributes. The signer later signs
    /// <see cref="PdfPreparedSignature.DataToSign"/> as <see cref="IPdfSigner.SignDigest"/> would: the same
    /// contract, split in time.
    /// </para>
    /// <para>
    /// Once written, the document reads from the prepared file, as after <see cref="Sign"/>; its signature field then has a
    /// signature dictionary whose <c>Contents</c> is still zeros, and the completion, which writes into the file, does not change
    /// the document. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public PdfPreparedSignature PrepareSignature(
        Stream stream,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2>? chain = null,
        PdfSignatureOptions? options = null,
        RSASignaturePadding? rsaSignaturePadding = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(certificate);
        if (Security is { } security && (security.Integrity != PdfIntegrityStatus.None || security.RequiresIntegrityCode))
        {
            throw new NotSupportedException(
                "The document carries an ISO/TS 32004 integrity MAC, which deferred signing does not support; sign it in one call with Sign or SignAsync.");
        }

        RSASignaturePadding padding = rsaSignaturePadding
            ?? (certificate.PublicKey.Oid.Value == RsaPssKeyOid ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
        PendingSignature pending = PreparePending(certificate, chain ?? [], padding, options ?? new PdfSignatureOptions(), fingerprint: true);
        PdfPreparedSignature prepared;
        try
        {
            prepared = PdfPreparedSignature.Create(
                pending.FieldName,
                pending.DigestAlgorithm,
                pending.Padding,
                pending.ByteRange,
                pending.Reserved,
                pending.Timestamp?.TokenSizeEstimate ?? 0,
                pending.RangeFingerprint!,
                pending.SignedAttributes,
                certificate,
                pending.Chain,
                pending.Key);
        }
        catch
        {
            pending.Undo();
            throw;
        }

        // The document's signature dictionary holds what the file holds: the final byte range and the zero-filled contents.
        pending.Signature[ByteRangeName] = new CosArray([.. pending.ByteRange.Select(value => (CosObject)new CosInteger(value))]);
        UpdateResult update = pending.Update;
        CopySource(stream);
        stream.Write(update.Bytes.Span);
        Rebase(update);
        return prepared;
    }

    /// <summary>
    /// Completes a prepared signature with the signature value the signer returned: builds the CMS object around it and writes it
    /// into the prepared file's reserved <c>Contents</c>, in place.
    /// </summary>
    /// <param name="preparedFile">The prepared file <see cref="PrepareSignature"/> wrote, readable, writable and seekable. It is not disposed.</param>
    /// <param name="prepared">The prepared signature, as <see cref="PrepareSignature"/> returned it or <see cref="PdfPreparedSignature.Open"/> read it.</param>
    /// <param name="signatureValue">
    /// The signature value over <see cref="PdfPreparedSignature.DataToSign"/>: for RSA, as <see cref="IPdfSigner.SignDigest"/>
    /// returns it; for ECDSA, DER or <c>r</c> and <c>s</c> concatenated.
    /// </param>
    /// <param name="timestampClient">
    /// The time-stamp authority to time-stamp the signature value with (PAdES B-T), or <see langword="null"/>. Room for its token
    /// must have been reserved with <see cref="PdfSignatureOptions.TimestampClient"/> when preparing.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="preparedFile"/> is not readable, writable and seekable, or the value does not verify with the certificate.</exception>
    /// <exception cref="InvalidOperationException">
    /// The file changed after preparation, the signature was already completed, no room was reserved for a time-stamp token, or the
    /// signature with its token does not fit the room reserved.
    /// </exception>
    /// <exception cref="PdfTimestampException">The time-stamp authority refused, could not be reached, or returned a token that does not answer the request.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.8.1, §12.8.3.3.1 and §12.8.3.4; ISO/TS 32002 §5.1.4; RFC 3161 Appendix A. The file's byte ranges are hashed
    /// again and compared with the digest the state holds, its <c>Contents</c> must still be zeros, and the signature value must
    /// verify with the certificate's public key over the signed attributes; only then is anything written, so a refused completion
    /// leaves the file as it was. Touches only <paramref name="preparedFile"/>, so it may run on any thread.
    /// </remarks>
    public static void CompleteSignature(Stream preparedFile, PdfPreparedSignature prepared, ReadOnlySpan<byte> signatureValue, PdfTimestampClient? timestampClient = null)
    {
        RequirePreparedFile(preparedFile);
        ArgumentNullException.ThrowIfNull(prepared);
        RequireTimestampRoom(prepared, timestampClient);
        VerifyPreparedFileAsync(preparedFile, prepared, asynchronous: false, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        byte[] value = CmsSignedData.NormalizeSignature(prepared.Key, signatureValue.ToArray());
        byte[]? token = timestampClient?.RequestToken(timestampClient.DigestAlgorithm, timestampClient.Hash(value));
        byte[] contents = DeferredContents(prepared, value, token);
        preparedFile.Position = prepared.ByteRange[1];
        preparedFile.Write(contents);
        preparedFile.Flush();
    }

    /// <summary>
    /// Completes a prepared signature with the signature value the signer returned, reading, time-stamping and writing
    /// asynchronously.
    /// </summary>
    /// <param name="preparedFile">The prepared file, readable, writable and seekable. It is not disposed.</param>
    /// <param name="prepared">The prepared signature.</param>
    /// <param name="signatureValue">The signature value over <see cref="PdfPreparedSignature.DataToSign"/>.</param>
    /// <param name="timestampClient">The time-stamp authority to time-stamp the signature value with, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the reads, the time-stamp request and the write.</param>
    /// <returns>A task that completes when the signature is written.</returns>
    /// <exception cref="ArgumentException"><paramref name="preparedFile"/> is not readable, writable and seekable, or the value does not verify with the certificate.</exception>
    /// <exception cref="InvalidOperationException">See <see cref="CompleteSignature"/>.</exception>
    /// <exception cref="PdfTimestampException">The time-stamp authority refused, could not be reached, or returned a token that does not answer the request.</exception>
    /// <remarks>ISO 32000-2 §12.8.1 and §12.8.3.3. Writes what <see cref="CompleteSignature"/> writes.</remarks>
    public static async Task CompleteSignatureAsync(
        Stream preparedFile,
        PdfPreparedSignature prepared,
        ReadOnlyMemory<byte> signatureValue,
        PdfTimestampClient? timestampClient = null,
        CancellationToken cancellationToken = default)
    {
        RequirePreparedFile(preparedFile);
        ArgumentNullException.ThrowIfNull(prepared);
        RequireTimestampRoom(prepared, timestampClient);
        await VerifyPreparedFileAsync(preparedFile, prepared, asynchronous: true, cancellationToken).ConfigureAwait(false);
        byte[] value = CmsSignedData.NormalizeSignature(prepared.Key, signatureValue.ToArray());
        byte[]? token = timestampClient is null
            ? null
            : await timestampClient.RequestTokenAsync(timestampClient.DigestAlgorithm, timestampClient.Hash(value), cancellationToken).ConfigureAwait(false);
        byte[] contents = DeferredContents(prepared, value, token);
        preparedFile.Position = prepared.ByteRange[1];
        await preparedFile.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
        await preparedFile.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes a prepared signature with a whole CMS <c>SignedData</c> a signing service built, after checking that it signs the
    /// prepared byte ranges with the prepared certificate.
    /// </summary>
    /// <param name="preparedFile">The prepared file, readable, writable and seekable. It is not disposed.</param>
    /// <param name="prepared">The prepared signature.</param>
    /// <param name="cms">The DER- or BER-encoded <c>ContentInfo</c> holding a detached <c>SignedData</c>; trailing zeros are ignored.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="preparedFile"/> is not readable, writable and seekable; or <paramref name="cms"/> cannot be read, is not
    /// detached, has more than one signer, does not sign the prepared message digest with the prepared digest algorithm, names
    /// another signer than the prepared certificate, or its signature value does not verify.
    /// </exception>
    /// <exception cref="InvalidOperationException">The file changed after preparation, the signature was already completed, or the object does not fit the room reserved.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.8.1, §12.8.3.3.1 and §12.8.3.4. A post-processing path for services that return a CMS object instead of a
    /// signature value (the signature value stays the primary contract): the object's signed attributes need not be the
    /// prepared ones, but their message digest must be the prepared byte ranges' digest. The object is written as it is.
    /// </remarks>
    public static void CompleteSignatureWithCms(Stream preparedFile, PdfPreparedSignature prepared, ReadOnlySpan<byte> cms)
    {
        RequirePreparedFile(preparedFile);
        ArgumentNullException.ThrowIfNull(prepared);
        VerifyPreparedFileAsync(preparedFile, prepared, asynchronous: false, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        int length;
        try
        {
            AsnDecoder.ReadEncodedValue(cms, AsnEncodingRules.BER, out _, out _, out length);
        }
        catch (AsnContentException exception)
        {
            throw new ArgumentException($"The CMS object cannot be read: {exception.Message}", nameof(cms), exception);
        }

        if (cms[length..].IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new ArgumentException("Bytes other than zero padding follow the CMS object.", nameof(cms));
        }

        byte[] encoded = cms[..length].ToArray();
        if (encoded.Length > prepared.ReservedLength)
        {
            throw new InvalidOperationException($"The CMS object takes {encoded.Length} bytes, more than the {prepared.ReservedLength} reserved for it.");
        }

        CheckDeferredCms(encoded, prepared, nameof(cms));
        byte[] contents = HexContents(prepared, encoded);
        preparedFile.Position = prepared.ByteRange[1];
        preparedFile.Write(contents);
        preparedFile.Flush();
    }

    private static void RequirePreparedFile(Stream preparedFile)
    {
        ArgumentNullException.ThrowIfNull(preparedFile);
        if (!preparedFile.CanRead || !preparedFile.CanWrite || !preparedFile.CanSeek)
        {
            throw new ArgumentException("The prepared file must be readable, writable and seekable: the signature is written into it in place.", nameof(preparedFile));
        }
    }

    private static void RequireTimestampRoom(PdfPreparedSignature prepared, PdfTimestampClient? timestampClient)
    {
        if (timestampClient is not null && prepared.TimestampReserve == 0)
        {
            throw new InvalidOperationException(
                $"The signature was prepared without room for a time-stamp token; set {nameof(PdfSignatureOptions)}.{nameof(PdfSignatureOptions.TimestampClient)} when preparing it.");
        }
    }

    /// <summary>
    /// Checks that <paramref name="preparedFile"/> is the prepared file, unchanged and not completed: its length, the SHA-256 digest of
    /// its byte ranges, and the zero-filled <c>Contents</c> between them.
    /// </summary>
    private static async ValueTask VerifyPreparedFileAsync(Stream preparedFile, PdfPreparedSignature prepared, bool asynchronous, CancellationToken cancellationToken)
    {
        if (preparedFile.Length != prepared.FileLength)
        {
            throw new InvalidOperationException(
                $"The prepared file is {preparedFile.Length} bytes long, not the {prepared.FileLength} it was prepared with: it changed after preparation.");
        }

        IReadOnlyList<long> byteRange = prepared.ByteRange;
        byte[] buffer = new byte[64 * 1024];
        using IncrementalDigest digest = IncrementalDigest.Create(HashAlgorithmName.SHA256);
        await HashAsync(0, byteRange[1]).ConfigureAwait(false);
        await HashAsync(byteRange[2], byteRange[3]).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(digest.GetHashAndReset(), prepared.RangeFingerprint))
        {
            throw new InvalidOperationException("The prepared file's signed bytes changed after preparation; prepare the signature again.");
        }

        // '<', 2n zero digits, '>' (§12.8.3.3.1); read in windows, so a large reservation is not one allocation.
        long start = byteRange[1];
        long end = byteRange[2];
        preparedFile.Position = start;
        for (long position = start; position < end;)
        {
            int count = (int)Math.Min(buffer.Length, end - position);
            await ReadExactlyAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
            for (int index = 0; index < count; index++)
            {
                long offset = position + index;
                byte expected = offset == start ? (byte)'<' : offset == end - 1 ? (byte)'>' : (byte)'0';
                if (buffer[index] != expected)
                {
                    throw new InvalidOperationException(expected == (byte)'0'
                        ? "The prepared signature was already completed: its Contents no longer holds zeros."
                        : "The prepared file's Contents is not where the prepared signature reserved it: the file changed after preparation.");
                }
            }

            position += count;
        }

        async ValueTask HashAsync(long offset, long length)
        {
            preparedFile.Position = offset;
            for (long remaining = length; remaining > 0;)
            {
                int count = (int)Math.Min(buffer.Length, remaining);
                await ReadExactlyAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
                digest.AppendData(buffer.AsSpan(0, count));
                remaining -= count;
            }
        }

        async ValueTask ReadExactlyAsync(Memory<byte> target)
        {
            if (asynchronous)
            {
                await preparedFile.ReadExactlyAsync(target, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                preparedFile.ReadExactly(target.Span);
            }
        }
    }

    /// <summary>
    /// The bytes that replace the reserved <c>Contents</c>: the CMS object built around <paramref name="value"/>, checked to fit and
    /// to verify, in hexadecimal and padded with zeros.
    /// </summary>
    private static byte[] DeferredContents(PdfPreparedSignature prepared, byte[] value, byte[]? timestampToken)
    {
        byte[] cms;
        using (X509Certificate2 certificate = prepared.LoadCertificate())
        {
            X509Certificate2[] chain = prepared.LoadChain();
            try
            {
                cms = CmsSignedData.Encode(
                    prepared.DigestAlgorithm,
                    prepared.Key,
                    prepared.Padding,
                    prepared.SignedAttributes.ToArray(),
                    value,
                    certificate,
                    chain,
                    macToken: null,
                    timestampToken);
            }
            finally
            {
                foreach (X509Certificate2 other in chain)
                {
                    other.Dispose();
                }
            }
        }

        RequireFits(cms, prepared.ReservedLength, timestampToken, prepared.TimestampReserve);
        CheckDeferredCms(cms, prepared, "signatureValue");
        return HexContents(prepared, cms);
    }

    /// <summary>
    /// Throws when <paramref name="cms"/> is not a detached, single-signer signature over the prepared message digest, by the prepared
    /// certificate, whose signature value verifies with its public key (RFC 5652 §5.4, §5.6).
    /// </summary>
    private static void CheckDeferredCms(byte[] cms, PdfPreparedSignature prepared, string parameter)
    {
        if (!CmsSignature.TryDecode(cms, out CmsSignature decoded, out string error))
        {
            throw new ArgumentException($"The CMS object cannot be read: {error}", parameter);
        }

        if (decoded.SignerCount != 1 || decoded.Content is not null || decoded.ContentType != CmsSignature.DataOid)
        {
            throw new ArgumentException("The CMS object must be a detached signature over id-data with exactly one signer (§12.8.3.3.1).", parameter);
        }

        if (!SignatureAlgorithms.TryGetDigest(decoded.DigestAlgorithm.Oid, out HashAlgorithmName digestAlgorithm) || digestAlgorithm != prepared.DigestAlgorithm)
        {
            throw new ArgumentException($"The CMS object's signer does not use the prepared digest algorithm, {prepared.DigestAlgorithm.Name}.", parameter);
        }

        if (decoded.SignedAttributes is null)
        {
            throw new ArgumentException("The CMS object has no signed attributes, so it cannot be checked against the prepared message digest (§12.8.3.3.1).", parameter);
        }

        if (decoded.MessageDigests.Count != 1 || !decoded.MessageDigests[0].AsSpan().SequenceEqual(prepared.MessageDigest))
        {
            throw new ArgumentException("The CMS object does not sign the prepared byte ranges: its message-digest attribute is not their digest.", parameter);
        }

        using X509Certificate2 certificate = prepared.LoadCertificate();
        bool sameSigner = decoded.SubjectKeyIdentifier.IsEmpty
            ? decoded.IssuerName.Span.SequenceEqual(certificate.IssuerName.RawData)
                && decoded.SerialNumber.Span.SequenceEqual(TrimLeadingZeros(certificate.SerialNumberBytes.Span))
            : certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault() is { } identifier
                && decoded.SubjectKeyIdentifier.Span.SequenceEqual(identifier.SubjectKeyIdentifierBytes.Span);
        if (!sameSigner)
        {
            throw new ArgumentException("The CMS object's signer is not the prepared certificate.", parameter);
        }

        if (SignatureAlgorithms.TryGetSignatureDigest(decoded.SignatureAlgorithm, digestAlgorithm, out HashAlgorithmName signatureDigest)
            && SignatureAlgorithms.Verify(
                decoded.SignatureAlgorithm,
                signatureDigest,
                certificate.PublicKey,
                IncrementalDigest.HashData(signatureDigest, decoded.SignedAttributes),
                decoded.Signature) == SignatureCheck.Invalid)
        {
            throw new ArgumentException(
                "The signature value does not verify with the prepared certificate's public key over the signed attributes; was it made with another key or over other data than DataToSign?",
                parameter);
        }

        static ReadOnlySpan<byte> TrimLeadingZeros(ReadOnlySpan<byte> value)
        {
            int start = value.IndexOfAnyExcept((byte)0);
            return start < 0 ? value[^1..] : value[start..];
        }
    }

    /// <summary>The reserved <c>Contents</c> string holding <paramref name="cms"/>: <c>&lt;</c>, the hexadecimal digits padded with zeros, <c>&gt;</c>.</summary>
    private static byte[] HexContents(PdfPreparedSignature prepared, byte[] cms)
    {
        byte[] contents = new byte[(2 * prepared.ReservedLength) + 2];
        contents[0] = (byte)'<';
        contents[^1] = (byte)'>';
        new SignaturePlaceholders(0, 0, 0, contents.Length).WriteContents(contents, cms);
        return contents;
    }
}
