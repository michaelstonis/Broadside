using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Security;

/// <summary>A new public-key security handler encryption for a save: its encryption dictionary and crypt filter.</summary>
/// <param name="Dictionary">The encryption dictionary (Tables 20 and 27), written unencrypted.</param>
/// <param name="Filter">The crypt filter strings, streams and embedded files are encrypted with.</param>
/// <param name="EncryptMetadata">Whether metadata streams are encrypted.</param>
/// <remarks>
/// The inverse of <see cref="PublicKeySecurityHandler"/>: <c>adbe.pkcs7.s5</c>, <c>V</c> 5, one crypt filter <c>DefaultCryptFilter</c>
/// (<c>AESV3</c>, 256 bits) holding the recipient lists, which <c>StmF</c> and <c>StrF</c> name.
/// </remarks>
internal sealed record PublicKeyEncryption(CosDictionary Dictionary, CryptFilter Filter, bool EncryptMetadata)
{
    private static readonly CosName PubSec = new("Adobe.PubSec");
    private static readonly CosName S5 = new("adbe.pkcs7.s5");
    private static readonly CosName DefaultCryptFilter = new("DefaultCryptFilter");
    private static readonly CosName Recipients = new("Recipients");
    private static readonly CosName AuthEvent = new("AuthEvent");
    private static readonly CosName DocOpen = new("DocOpen");

    /// <summary>AES-256 in CBC mode (NIST, 2.16.840.1.101.3.4.1.42): the one content-encryption algorithm §7.6.5.3 does not deprecate.</summary>
    private const string Aes256CbcOid = "2.16.840.1.101.3.4.1.42";

    /// <summary>
    /// The bits of the permissions word a writer sets whatever the permissions: those Table 24 does not use (7, 8, 13 to 32), and 10,
    /// which PDF 2.0 writers shall set. Bit 13 set also means no integrity MAC is required (ISO/TS 32004 §5.1.2).
    /// </summary>
    private const uint ReservedPermissionBits = 0xFFFF_F2C0;

    /// <summary>Table 24, bit 2: change of encryption, and every other permission.</summary>
    private const uint ChangeEncryption = 1 << 1;

    /// <summary>Creates the encryption <paramref name="options"/> describe, with a new seed and file encryption key.</summary>
    /// <param name="options">The recipients and the metadata choice.</param>
    /// <returns>The encryption.</returns>
    /// <exception cref="ArgumentException">The options name no recipient.</exception>
    /// <exception cref="PdfEncryptionNotSupportedException">In a browser, where certificates and CMS are not available.</exception>
    /// <remarks>ISO 32000-2 §7.6.5.3, Table 27.</remarks>
    public static PublicKeyEncryption Create(PdfPublicKeyEncryptionOptions options)
    {
        if (options.Recipients.Count == 0)
        {
            throw new ArgumentException("Public-key encryption needs at least one recipient certificate (ISO 32000-2 §7.6.5.1).", nameof(options));
        }

        if (OperatingSystem.IsBrowser())
        {
            throw new PdfEncryptionNotSupportedException(
                PdfEncryptionNotSupportedReason.Platform,
                "Public-key encryption encrypts for certificate recipients with CMS, which is not available in a browser.");
        }

        // One seed for every list: the key digests the seed and every list, so each recipient must reach the same seed.
        byte[] seed = RandomNumberGenerator.GetBytes(20);
        List<byte[]> envelopes = [];
        foreach (IGrouping<uint, PdfEncryptionRecipient> list in options.Recipients.GroupBy(PermissionWord))
        {
            envelopes.Add(Envelope(seed, list.Key, list));
        }

        // §7.6.5.3 steps a-d: SHA-256 of the seed, every CMS object in order, and four 0xFF bytes for plaintext metadata.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(seed);
        foreach (byte[] envelope in envelopes)
        {
            hash.AppendData(envelope);
        }

        if (!options.EncryptMetadata)
        {
            hash.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        byte[] fileKey = hash.GetHashAndReset();
        var recipients = new CosArray();
        foreach (byte[] envelope in envelopes)
        {
            recipients.Add(CosString.FromOwnedBytes(envelope, hexadecimal: true));
        }

        var filter = new CosDictionary
        {
            [KnownNames.Type] = new CosName("CryptFilter"),
            [KnownNames.CFM] = KnownNames.AESV3,
            [AuthEvent] = DocOpen,
            [KnownNames.Length] = new CosInteger(256),
            [Recipients] = recipients,
        };
        if (!options.EncryptMetadata)
        {
            filter[KnownNames.EncryptMetadata] = CosBoolean.False;
        }

        var dictionary = new CosDictionary
        {
            [FilterNames.Filter] = PubSec,
            [KnownNames.SubFilter] = S5,
            [KnownNames.V] = new CosInteger(5),
            [KnownNames.Length] = new CosInteger(256),
            [KnownNames.CF] = new CosDictionary { [DefaultCryptFilter] = filter },
            [KnownNames.StmF] = DefaultCryptFilter,
            [KnownNames.StrF] = DefaultCryptFilter,
        };
        return new PublicKeyEncryption(dictionary, new CryptFilter(DefaultCryptFilter, CryptMethod.AesV3, fileKey), options.EncryptMetadata);
    }

    /// <summary>The 4-byte permissions word of Table 24 a recipient's list carries.</summary>
    private static uint PermissionWord(PdfEncryptionRecipient recipient) =>
        ReservedPermissionBits | (uint)(recipient.Permissions & PdfPermissions.All) | (recipient.Access == PdfAccessLevel.Owner ? ChangeEncryption : 0);

    /// <summary>
    /// One recipient list (§7.6.5.3): CMS enveloped data whose content is the seed and the permissions, most significant byte first,
    /// encrypted with AES-256-CBC under a key encrypted for each recipient with RSA (PKCS #1 v1.5), named by issuer and serial number.
    /// </summary>
    private static byte[] Envelope(byte[] seed, uint permissions, IEnumerable<PdfEncryptionRecipient> recipients)
    {
        byte[] content = new byte[24];
        seed.CopyTo(content, 0);
        BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(20), permissions);
        var envelope = new EnvelopedCms(new ContentInfo(content), new AlgorithmIdentifier(new Oid(Aes256CbcOid)));
        var collection = new CmsRecipientCollection();
        foreach (PdfEncryptionRecipient recipient in recipients)
        {
            collection.Add(new CmsRecipient(SubjectIdentifierType.IssuerAndSerialNumber, recipient.Certificate, RSAEncryptionPadding.Pkcs1));
        }

        envelope.Encrypt(collection);
        return envelope.Encode();
    }
}
