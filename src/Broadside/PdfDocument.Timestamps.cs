using System.Formats.Asn1;
using System.Security.Cryptography;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.Signatures;
using Broadside.Writing;

namespace Broadside;

/// <content>Document timestamps (issue #195): ISO 32000-2 §12.8.5; RFC 3161.</content>
public sealed partial class PdfDocument
{
    private static readonly CosName DocTimeStampName = new("DocTimeStamp");
    private static readonly CosName Rfc3161SubFilter = new("ETSI.RFC3161");

    /// <summary>
    /// Time-stamps the whole document and writes the time-stamped file to <paramref name="stream"/>: the document's file followed by
    /// an incremental update holding a document timestamp, whose byte ranges cover the whole file except the token.
    /// </summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="timestampClient">The client of the time-stamp authority; the request is sent synchronously.</param>
    /// <param name="fieldName">
    /// The fully qualified name of an unsigned signature field to hold the timestamp, or of a new invisible one;
    /// <see langword="null"/> (the default) creates a new field named <c>SignatureN</c>.
    /// </param>
    /// <exception cref="PdfTimestampException">The authority refused, could not be reached, or returned a token that does not answer the request.</exception>
    /// <exception cref="InvalidOperationException">
    /// The field <paramref name="fieldName"/> names is already signed or is not a signature field, or the token is longer than
    /// <see cref="PdfTimestampClient.TokenSizeEstimate"/> reserved.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="fieldName"/> is empty or, for a new field, holds a period.</exception>
    /// <exception cref="NotSupportedException">The document cannot be updated incrementally.</exception>
    /// <exception cref="PlatformNotSupportedException">The platform cannot send HTTP requests synchronously (the browser): use <see cref="TimestampAsync"/>.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the document is earlier than PDF 2.0.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.8.5.2 and §12.8.1, Table 255; RFC 3161 as updated by RFC 5816; ETSI EN 319 142-1 (the document timestamp of
    /// PAdES B-LTA). The timestamp goes into a signature field as for <see cref="Sign"/> (§12.8.5.2: "The existence of one or more
    /// document timestamps shall be determined by examining signature fields"); its value is a document timestamp dictionary with
    /// <c>Type</c> <c>DocTimeStamp</c>, <c>Filter</c> <c>Adobe.PPKLite</c>, <c>SubFilter</c> <c>ETSI.RFC3161</c>, a <c>ByteRange</c>
    /// over the whole file except <c>Contents</c>, and none of the entries Table 255 says not to use with <c>ETSI.RFC3161</c>
    /// (<c>M</c>, <c>Name</c>, <c>Reason</c>, <c>Location</c>, <c>ContactInfo</c>, <c>Cert</c>, <c>Reference</c>, <c>Changes</c>).
    /// The byte ranges are hashed with <see cref="PdfTimestampClient.DigestAlgorithm"/>, the hash is sent to the authority, and the
    /// <c>TimeStampToken</c> it returns, once checked against the request, is written into <c>Contents</c>, padded with zeros.
    /// Document timestamps are PDF 2.0: the update raises the version (in the catalog), or strict mode refuses before anything changes.
    /// </para>
    /// <para>
    /// An encrypted document is updated with its own key. When it has, or its permissions require, an integrity MAC, the new MAC is
    /// attached to the token's signer as its <c>pdfMacData</c> unsigned attribute, digesting the byte ranges and the token's signature
    /// value, and the trailer's <c>AuthCode</c> points at the document timestamp dictionary (ISO/TS 32004 §6.5.2, §6.6.3).
    /// </para>
    /// <para>
    /// Once the last byte is written the document reads from the time-stamped file. If the authority fails, nothing is written and the
    /// document is left as it was, as a failed signer leaves it (<see cref="Sign"/>): every object, including what the save inside
    /// time-stamping changed before the authority was asked. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public void Timestamp(Stream stream, PdfTimestampClient timestampClient, string? fieldName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(timestampClient);
        PendingTimestamp pending = PrepareTimestamp(timestampClient, fieldName);
        byte[] token;
        try
        {
            token = timestampClient.RequestToken(pending.DigestAlgorithm, pending.Digest);
        }
        catch
        {
            pending.Undo();
            throw;
        }

        UpdateResult update = CompleteTimestamp(pending, token);
        CopySource(stream);
        stream.Write(update.Bytes.Span);
        Rebase(update);
    }

    /// <summary>Time-stamps the whole document and writes the time-stamped file to <paramref name="stream"/>, asynchronously.</summary>
    /// <param name="stream">Where to write, from its current position. It is not disposed.</param>
    /// <param name="timestampClient">The client of the time-stamp authority.</param>
    /// <param name="fieldName">The signature field to hold the timestamp, or <see langword="null"/> for a new one.</param>
    /// <param name="cancellationToken">Cancels the request and the writes.</param>
    /// <returns>A task that completes when the time-stamped file is written.</returns>
    /// <exception cref="PdfTimestampException">The authority refused, could not be reached, or returned a token that does not answer the request.</exception>
    /// <exception cref="InvalidOperationException">
    /// The field <paramref name="fieldName"/> names is already signed or is not a signature field, or the token is longer than
    /// <see cref="PdfTimestampClient.TokenSizeEstimate"/> reserved.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="fieldName"/> is empty or, for a new field, holds a period.</exception>
    /// <exception cref="NotSupportedException">The document cannot be updated incrementally.</exception>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when the document is earlier than PDF 2.0.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.8.5; RFC 3161. Writes what <see cref="Timestamp"/> writes; the request and the writes to
    /// <paramref name="stream"/> are asynchronous. A cancelled or failed request leaves the document as it was.
    /// </remarks>
    public async Task TimestampAsync(Stream stream, PdfTimestampClient timestampClient, string? fieldName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(timestampClient);
        PendingTimestamp pending = PrepareTimestamp(timestampClient, fieldName);
        byte[] token;
        try
        {
            token = await timestampClient.RequestTokenAsync(pending.DigestAlgorithm, pending.Digest, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            pending.Undo();
            throw;
        }

        UpdateResult update = CompleteTimestamp(pending, token);
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
    /// The features a timestamp adds (Table 255; §12.8.3.3.1): <paramref name="feature"/>, and the SHA-3 digest row when the client
    /// hashes with SHA-3 (ISO/TS 32001).
    /// </summary>
    private static VersionedFeature[] TimestampFeatures(VersionedFeature feature, PdfTimestampClient client) =>
        CmsSignedData.IsSha3(client.DigestAlgorithm) ? [feature, VersionedFeatures.Sha3SignatureDigest] : [feature];

    /// <summary>
    /// Places the document timestamp dictionary in its field, writes the incremental update with placeholders, fills in the byte
    /// ranges and hashes them; the document is changed in memory only, and <see cref="PendingTimestamp.Undo"/> reverts it.
    /// </summary>
    private PendingTimestamp PrepareTimestamp(PdfTimestampClient client, string? fieldName)
    {
        HashAlgorithmName digestAlgorithm = client.DigestAlgorithm;
        int reserved = client.TokenSizeEstimate;
        if (Security is { } security && (security.Integrity != PdfIntegrityStatus.None || security.RequiresIntegrityCode))
        {
            // ISO/TS 32004 §6.5.2: the MAC goes on the document time stamp signature as a pdfMacData unsigned attribute.
            reserved += IntegrityAttributeAllowance;
        }

        var undo = new List<Action>();
        try
        {
            foreach (VersionedFeature feature in TimestampFeatures(VersionedFeatures.DocumentTimestamp, client))
            {
                RequireSignatureFeature(feature, undo);
            }

            var dictionary = new CosDictionary
            {
                [ByteRangeName] = new CosArray([new CosInteger(0), new CosInteger(ByteRangePlaceholder), new CosInteger(ByteRangePlaceholder), new CosInteger(ByteRangePlaceholder)]),
                [ContentsName] = new CosString(new byte[reserved], hexadecimal: true),
                [KnownNames.Type] = DocTimeStampName,
                [FilterName] = AdobePpkLite,
                [SubFilterName] = Rfc3161SubFilter,
            };
            CosReference reference = AddObject(dictionary);
            undo.Add(() => RemoveObject(reference));
            PlaceSignature(fieldName, reference, undo);

            // Issue #306: what the save changes (the dating, the metadata layout, completed font subsets, the catalog's version and
            // extensions) is reverted with the rest when the signer or the authority fails.
            var saveChanges = new CosChangeLog();
            undo.Add(saveChanges.Revert);
            FileWriter writer = PlanSave(new PdfSaveOptions { IncrementalUpdate = true }, saveChanges);
            IntegrityCodeWriter? integrity = writer.Integrity;
            if (integrity is not null)
            {
                writer.IntegritySignature = reference;
            }

            foreach (ReadOnlyMemory<byte> _ in writer.Write())
            {
                // The source's bytes are copied again from the source once time-stamped; the update is kept by the writer.
            }

            UpdateResult update = writer.Update!;
            WrittenObject written = update.Objects.Single(item => ReferenceEquals(item.Value, dictionary));
            byte[] bytes = update.Bytes.ToArray();
            long sourceLength = _source.Length;
            var placeholders = SignaturePlaceholders.Find(bytes, checked((int)(written.Offset - sourceLength)), written.Length);
            long contentsEnd = sourceLength + placeholders.ContentsEnd;
            long[] byteRange = [0, sourceLength + placeholders.ContentsStart, contentsEnd, sourceLength + bytes.Length - contentsEnd];
            placeholders.WriteByteRange(bytes, byteRange);

            // §12.8.5.2: the hash of the byte range is what is sent to the authority.
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
            return new PendingTimestamp(dictionary, update with { Bytes = bytes }, placeholders, byteRange, digestAlgorithm, digest.GetHashAndReset(), reserved, client, undo)
            {
                Integrity = integrity,
                IntegrityDataDigest = macDigest?.GetHashAndReset(),
            };
        }
        catch
        {
            Revert(undo);
            throw;
        }
    }

    /// <summary>Writes the token into the reserved <c>Contents</c> and records the final values in the dictionary.</summary>
    private static UpdateResult CompleteTimestamp(PendingTimestamp pending, byte[] token)
    {
        try
        {
            if (pending.Integrity is { } integrity)
            {
                // ISO/TS 32004 §6.6.3: the MAC digests the byte range and the signature value of the SignerInfo it is attached to.
                byte[] signatureValue;
                try
                {
                    signatureValue = CmsUnsignedAttributes.SignatureValue(token);
                }
                catch (AsnContentException exception)
                {
                    throw new PdfTimestampException($"The time-stamp token's signer has no readable signature value to attach the integrity MAC to: {exception.Message}", exception);
                }

                byte[] macToken = integrity.CreateToken(pending.IntegrityDataDigest, IncrementalDigest.HashData(HashAlgorithmName.SHA256, signatureValue));
                token = CmsUnsignedAttributes.Add(token, CmsUnsignedAttributes.PdfMacDataOid, macToken);
            }

            if (token.Length > pending.Reserved)
            {
                throw new InvalidOperationException(
                    $"The time-stamp token takes {token.Length} bytes, more than the {pending.Reserved} reserved for it; raise {nameof(PdfTimestampClient)}.{nameof(PdfTimestampClient.TokenSizeEstimate)} ({pending.Client.TokenSizeEstimate}).");
            }
        }
        catch
        {
            pending.Undo();
            throw;
        }

        byte[] bytes = pending.Update.Bytes.ToArray();
        pending.Placeholders.WriteContents(bytes, token);
        pending.Dictionary[ByteRangeName] = new CosArray([.. pending.ByteRange.Select(value => (CosObject)new CosInteger(value))]);
        byte[] contents = new byte[pending.Reserved];
        token.CopyTo(contents, 0);
        pending.Dictionary[ContentsName] = new CosString(contents, hexadecimal: true);
        return pending.Update with { Bytes = bytes };
    }

    /// <summary>A document timestamp written with placeholders and hashed, waiting for its token.</summary>
    private sealed record PendingTimestamp(
        CosDictionary Dictionary,
        UpdateResult Update,
        SignaturePlaceholders Placeholders,
        long[] ByteRange,
        HashAlgorithmName DigestAlgorithm,
        byte[] Digest,
        int Reserved,
        PdfTimestampClient Client,
        List<Action> UndoActions)
    {
        /// <summary>Gets the composer of the PDF MAC token attached to the timestamp, or <see langword="null"/> when the document has no MAC.</summary>
        public IntegrityCodeWriter? Integrity { get; init; }

        /// <summary>Gets the SHA-256 digest of the byte range, the MAC token's dataDigest (ISO/TS 32004 §6.6.3).</summary>
        public byte[]? IntegrityDataDigest { get; init; }

        public void Undo() => Revert(UndoActions);
    }
}
