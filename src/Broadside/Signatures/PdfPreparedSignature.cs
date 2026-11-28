using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures;

/// <summary>
/// A signature that <see cref="PdfDocument.PrepareSignature"/> reserved in a prepared file and that waits for its signature value:
/// what a remote signer signs, and the state <see cref="PdfDocument.CompleteSignature"/> needs to finish it later, in another
/// process, without the document.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.8.1 and §12.8.3.3; ISO/TS 32002 §5.1.4. Deferred signing is the <see cref="IPdfSigner"/> contract split in
/// time: the document builds every byte, the signer signs <see cref="DataToSign"/>, the digest of the DER-encoded
/// <see cref="SignedAttributes"/>, and the document builds the CMS object around the value it returns.
/// </para>
/// <para>
/// <see cref="Save"/> writes the state as a small blob: a format version, the byte range, the signed attributes, the digest
/// algorithm, the RSA signature scheme, the signer's certificate and chain (DER), the room reserved for the CMS object, and the
/// SHA-256 digest of the prepared file's byte ranges, which completion checks the file against. It holds no private key and no
/// document content. The blob is opaque: its layout is not part of the API, only that <see cref="Open"/> reads what
/// <see cref="Save"/> of the same or an earlier version wrote.
/// </para>
/// <para>An instance is immutable and safe to share across threads.</para>
/// </remarks>
public sealed class PdfPreparedSignature
{
    /// <summary>The version of the state blob <see cref="Save"/> writes.</summary>
    private const int FormatVersion = 1;

    /// <summary>The largest state blob <see cref="Open"/> reads; a certificate chain is a few kilobytes.</summary>
    private const int MaxStateLength = 4 << 20;

    /// <summary>The largest room for a CMS object a state may describe.</summary>
    private const int MaxReservedLength = 16 << 20;

    private const string MessageDigestOid = "1.2.840.113549.1.9.4";

    private static readonly HashAlgorithmName[] DigestAlgorithms =
    [
        HashAlgorithmName.SHA256, HashAlgorithmName.SHA384, HashAlgorithmName.SHA512,
        CmsSignedData.Sha3_256, CmsSignedData.Sha3_384, CmsSignedData.Sha3_512,
    ];

    private readonly byte[] _signedAttributes;
    private readonly byte[] _dataToSign;
    private readonly long[] _byteRange;
    private readonly byte[] _rangeFingerprint;
    private readonly byte[] _certificate;
    private readonly byte[][] _chain;

    private PdfPreparedSignature(
        string fieldName,
        HashAlgorithmName digestAlgorithm,
        RSASignaturePadding padding,
        long[] byteRange,
        int reservedLength,
        int timestampReserve,
        byte[] rangeFingerprint,
        byte[] signedAttributes,
        byte[] messageDigest,
        byte[] certificate,
        byte[][] chain,
        SigningKey key)
    {
        FieldName = fieldName;
        DigestAlgorithm = digestAlgorithm;
        Padding = padding;
        _byteRange = byteRange;
        ReservedLength = reservedLength;
        TimestampReserve = timestampReserve;
        _rangeFingerprint = rangeFingerprint;
        _signedAttributes = signedAttributes;
        MessageDigest = messageDigest;
        _certificate = certificate;
        _chain = chain;
        Key = key;
        _dataToSign = IncrementalDigest.HashData(digestAlgorithm, signedAttributes);
    }

    /// <summary>
    /// Gets what the signer signs: the digest of <see cref="SignedAttributes"/> with <see cref="DigestAlgorithm"/>, what
    /// <see cref="IPdfSigner.SignDigest"/> receives.
    /// </summary>
    /// <remarks>ISO/TS 32002 §5.1.4: the signed attributes are digested with the message digest's algorithm (RFC 5652 §5.4).</remarks>
    public ReadOnlyMemory<byte> DataToSign => _dataToSign;

    /// <summary>
    /// Gets the DER encoding of the CMS signed attributes (with the universal <c>SET OF</c> tag), for a signer that hashes what it
    /// signs itself.
    /// </summary>
    /// <remarks>RFC 5652 §5.4; ISO 32000-2 §12.8.3.4.3: content type, message digest and the ESS signing-certificate-v2 attribute.</remarks>
    public ReadOnlyMemory<byte> SignedAttributes => _signedAttributes;

    /// <summary>Gets the digest algorithm of the byte ranges' message digest and of <see cref="DataToSign"/>.</summary>
    /// <remarks>ISO 32000-2 §12.8.3.1, Table 260; ISO/TS 32001 §5.1.4.</remarks>
    public HashAlgorithmName DigestAlgorithm { get; }

    /// <summary>
    /// Gets the signature scheme the signer must use with an RSA key (<see cref="RSASignaturePadding.Pkcs1"/> or
    /// <see cref="RSASignaturePadding.Pss"/>), or <see langword="null"/> for an ECDSA key.
    /// </summary>
    /// <remarks>RFC 4056 §2, as <see cref="IPdfSigner.RsaSignaturePadding"/>.</remarks>
    public RSASignaturePadding? RsaSignaturePadding => Key.Type == SigningKeyType.Ecdsa ? null : Padding;

    /// <summary>Gets the fully qualified name of the signature field the signature was reserved in.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.2, §12.7.5.5.</remarks>
    public string FieldName { get; }

    /// <summary>Gets how many bytes <c>Contents</c> holds for the CMS object.</summary>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255; §12.8.3.3.1: the rest is padded with zeros.</remarks>
    public int ReservedLength { get; }

    /// <summary>Gets the signer's key, as the CMS encoder describes it.</summary>
    internal SigningKey Key { get; }

    /// <summary>Gets the RSA signature scheme, <see cref="RSASignaturePadding.Pkcs1"/> for an ECDSA key.</summary>
    internal RSASignaturePadding Padding { get; }

    /// <summary>Gets the room reserved for a signature time-stamp token, 0 when none was reserved.</summary>
    internal int TimestampReserve { get; }

    /// <summary>Gets the message digest of the byte ranges, as the signed attributes hold it.</summary>
    internal byte[] MessageDigest { get; }

    /// <summary>Gets the signature's byte range: four numbers, the second and third bounding <c>Contents</c> with its delimiters.</summary>
    internal IReadOnlyList<long> ByteRange => _byteRange;

    /// <summary>Gets the SHA-256 digest of the prepared file's byte ranges.</summary>
    internal ReadOnlySpan<byte> RangeFingerprint => _rangeFingerprint;

    /// <summary>Gets the DER encoding of the signer's certificate.</summary>
    internal ReadOnlySpan<byte> CertificateData => _certificate;

    /// <summary>Gets the length the prepared file has: the end of the second byte range.</summary>
    internal long FileLength => _byteRange[2] + _byteRange[3];

    /// <summary>Loads the signer's certificate; the caller disposes it.</summary>
    internal X509Certificate2 LoadCertificate() => X509CertificateLoader.LoadCertificate(_certificate);

    /// <summary>Loads the chain's certificates; the caller disposes them.</summary>
    internal X509Certificate2[] LoadChain() => [.. _chain.Select(static der => X509CertificateLoader.LoadCertificate(der))];

    /// <summary>Writes the state to <paramref name="state"/>, from its current position. The stream is not disposed.</summary>
    /// <param name="state">Where to write.</param>
    /// <remarks>
    /// ISO 32000-2 §12.8.1. The blob holds no private key and no document content (see the type's remarks); it is a DER encoding,
    /// versioned, and opaque.
    /// </remarks>
    public void Save(Stream state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(FormatVersion);
            writer.WriteCharacterString(UniversalTagNumber.UTF8String, FieldName);
            writer.WriteObjectIdentifier(CmsSignedData.DigestOid(DigestAlgorithm));
            writer.WriteInteger(Padding == RSASignaturePadding.Pss ? 1 : 0);
            using (writer.PushSequence())
            {
                foreach (long value in _byteRange)
                {
                    writer.WriteInteger(value);
                }
            }

            writer.WriteInteger(ReservedLength);
            writer.WriteInteger(TimestampReserve);
            writer.WriteOctetString(_rangeFingerprint);
            writer.WriteOctetString(_signedAttributes);
            writer.WriteOctetString(_certificate);
            using (writer.PushSequence())
            {
                foreach (byte[] certificate in _chain)
                {
                    writer.WriteOctetString(certificate);
                }
            }
        }

        state.Write(writer.Encode());
    }

    /// <summary>Reads a state <see cref="Save"/> wrote, from the current position of <paramref name="state"/> to its end.</summary>
    /// <param name="state">The state blob. It is not disposed.</param>
    /// <returns>The prepared signature.</returns>
    /// <exception cref="InvalidDataException">The blob is damaged, truncated, or describes an impossible signature.</exception>
    /// <exception cref="NotSupportedException">The blob was written by a later format version than this library reads.</exception>
    /// <remarks>ISO 32000-2 §12.8.1. Every value is checked; a blob that loads describes a signature completion can finish.</remarks>
    public static PdfPreparedSignature Open(Stream state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = state.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxStateLength)
            {
                throw Damaged($"it is longer than {MaxStateLength} bytes");
            }

            buffer.Write(chunk, 0, read);
        }

        return Decode(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)));
    }

    /// <summary>A new prepared signature from what preparing produced.</summary>
    internal static PdfPreparedSignature Create(
        string fieldName,
        HashAlgorithmName digestAlgorithm,
        RSASignaturePadding padding,
        long[] byteRange,
        int reservedLength,
        int timestampReserve,
        byte[] rangeFingerprint,
        byte[] signedAttributes,
        X509Certificate2 certificate,
        IReadOnlyList<X509Certificate2> chain,
        SigningKey key) =>
        new(
            fieldName,
            digestAlgorithm,
            padding,
            [.. byteRange],
            reservedLength,
            timestampReserve,
            rangeFingerprint,
            signedAttributes,
            ReadMessageDigest(signedAttributes),
            certificate.RawData,
            [.. chain.Select(static other => other.RawData)],
            key);

    /// <summary>Decodes and checks a state blob.</summary>
    internal static PdfPreparedSignature Decode(ReadOnlyMemory<byte> encoded)
    {
        try
        {
            var outer = new AsnReader(encoded, AsnEncodingRules.DER);
            AsnReader sequence = outer.ReadSequence();
            if (outer.HasData)
            {
                throw Damaged("bytes follow its end");
            }

            if (!sequence.TryReadInt32(out int version) || version < 1)
            {
                throw Damaged("its version is not a positive number");
            }

            if (version > FormatVersion)
            {
                throw new NotSupportedException(
                    $"The prepared signature state has format version {version}; this version of Broadside reads version {FormatVersion} and earlier.");
            }

            string fieldName = sequence.ReadCharacterString(UniversalTagNumber.UTF8String);
            if (fieldName.Length == 0)
            {
                throw Damaged("its field name is empty");
            }

            string digestOid = sequence.ReadObjectIdentifier();
            HashAlgorithmName digestAlgorithm = Array.Find(DigestAlgorithms, algorithm => CmsSignedData.DigestOid(algorithm) == digestOid);
            if (digestAlgorithm.Name is null)
            {
                throw Damaged($"its digest algorithm {digestOid} is not one signing writes");
            }

            RSASignaturePadding padding = ReadSmall(sequence, "RSA signature scheme") switch
            {
                0 => RSASignaturePadding.Pkcs1,
                1 => RSASignaturePadding.Pss,
                _ => throw Damaged("its RSA signature scheme is unknown"),
            };

            AsnReader rangeReader = sequence.ReadSequence();
            var byteRange = new long[4];
            for (int index = 0; index < byteRange.Length; index++)
            {
                if (!rangeReader.TryReadInt64(out byteRange[index]))
                {
                    throw Damaged("its byte range is not four numbers");
                }
            }

            if (rangeReader.HasData)
            {
                throw Damaged("its byte range is not four numbers");
            }

            int reserved = ReadSmall(sequence, "reserved length");
            int timestampReserve = ReadSmall(sequence, "time-stamp reserve");
            if (reserved <= 0 || reserved > MaxReservedLength || timestampReserve > reserved)
            {
                throw Damaged("its reserved length is out of range");
            }

            // [0, a] covers the file up to the '<' of Contents, [b, c] from after its '>' to the end; between them are the 2n hex
            // digits of the n reserved bytes and the two delimiters (§12.8.1, §12.8.3.3.1).
            if (byteRange[0] != 0 || byteRange[1] <= 0 || byteRange[3] < 0 || byteRange[2] - byteRange[1] != (2L * reserved) + 2
                || byteRange[2] > long.MaxValue - byteRange[3])
            {
                throw Damaged("its byte range does not frame the reserved contents");
            }

            byte[] fingerprint = sequence.ReadOctetString();
            if (fingerprint.Length != SHA256.HashSizeInBytes)
            {
                throw Damaged("its file digest is not a SHA-256 digest");
            }

            byte[] signedAttributes = sequence.ReadOctetString();
            byte[] messageDigest = ReadMessageDigest(signedAttributes);
            if (messageDigest.Length != IncrementalDigest.HashData(digestAlgorithm, []).Length)
            {
                throw Damaged($"its message digest is not a {digestAlgorithm.Name} digest");
            }

            byte[] certificateData = sequence.ReadOctetString();
            SigningKey key;
            try
            {
                using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateData);
                key = CmsSignedData.ReadKey(certificate);
                CmsSignedData.RequireCompatible(key, digestAlgorithm, padding);
            }
            catch (NotSupportedException exception)
            {
                throw new InvalidDataException($"The prepared signature state is damaged: {exception.Message}", exception);
            }

            AsnReader chainReader = sequence.ReadSequence();
            var chain = new List<byte[]>();
            while (chainReader.HasData)
            {
                byte[] der = chainReader.ReadOctetString();
                using X509Certificate2 _ = X509CertificateLoader.LoadCertificate(der);
                chain.Add(der);
            }

            if (sequence.HasData)
            {
                throw Damaged("it holds more values than its version has");
            }

            return new PdfPreparedSignature(
                fieldName, digestAlgorithm, padding, byteRange, reserved, timestampReserve, fingerprint, signedAttributes, messageDigest, certificateData, [.. chain], key);
        }
        catch (Exception exception) when (exception is AsnContentException or CryptographicException or OverflowException)
        {
            throw new InvalidDataException($"The prepared signature state is damaged: {exception.Message}", exception);
        }

        static int ReadSmall(AsnReader reader, string what) =>
            reader.TryReadInt32(out int value) && value >= 0 ? value : throw Damaged($"its {what} is not a small non-negative number");
    }

    /// <summary>The value of the message-digest attribute (RFC 5652 §11.2) of DER-encoded signed attributes.</summary>
    private static byte[] ReadMessageDigest(byte[] signedAttributes)
    {
        var reader = new AsnReader(signedAttributes, AsnEncodingRules.DER);
        AsnReader attributes = reader.ReadSetOf();
        if (reader.HasData)
        {
            throw Damaged("bytes follow its signed attributes");
        }

        byte[]? digest = null;
        while (attributes.HasData)
        {
            AsnReader attribute = attributes.ReadSequence();
            string type = attribute.ReadObjectIdentifier();
            AsnReader values = attribute.ReadSetOf();
            if (type == MessageDigestOid)
            {
                if (digest is not null)
                {
                    throw Damaged("its signed attributes hold two message digests");
                }

                digest = values.ReadOctetString();
                if (values.HasData)
                {
                    throw Damaged("its message-digest attribute has more than one value");
                }
            }
        }

        return digest ?? throw Damaged("its signed attributes hold no message digest");
    }

    private static InvalidDataException Damaged(string reason) => new($"The prepared signature state is damaged: {reason}.");
}
