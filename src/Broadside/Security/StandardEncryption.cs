using System.Buffers.Binary;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Security.Cryptography;

namespace Broadside.Security;

/// <summary>A new standard security handler encryption for a save: its encryption dictionary and crypt filter.</summary>
/// <param name="Dictionary">The encryption dictionary (Tables 20 and 21), written unencrypted.</param>
/// <param name="Filter">The crypt filter strings, streams and embedded files are encrypted with.</param>
/// <param name="EncryptMetadata">Whether metadata streams are encrypted.</param>
internal sealed record StandardEncryption(CosDictionary Dictionary, CryptFilter Filter, bool EncryptMetadata)
{
    private static readonly CosName StandardFilterName = new("StdCF");
    private static readonly CosName O = new("O");
    private static readonly CosName U = new("U");
    private static readonly CosName OE = new("OE");
    private static readonly CosName UE = new("UE");
    private static readonly CosName Perms = new("Perms");
    private static readonly CosName AuthEvent = new("AuthEvent");
    private static readonly CosName DocOpen = new("DocOpen");

    /// <summary>
    /// The bits of <c>P</c> a writer sets whatever the permissions: 7 and 8 and 13 to 32 (reserved, 1; bit 13 set means no integrity
    /// MAC is required, ISO/TS 32004 §5.1.2) and 10 (PDF 2.0 writers shall set it). ISO 32000-2 §7.6.4.2, Table 22.
    /// </summary>
    private const uint ReservedPermissionBits = 0xFFFF_F2C0;

    /// <summary>Creates the encryption <paramref name="options"/> describe, with a new file encryption key.</summary>
    /// <param name="options">The passwords, permissions, algorithm and metadata choice.</param>
    /// <param name="documentId">The first file identifier the file is written with (Algorithms 2 and 5 hash it for revisions 3 and 4).</param>
    /// <returns>The encryption.</returns>
    /// <exception cref="ArgumentException">A password the algorithm cannot encode, or RC4 without metadata encryption.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.6.4.4.7-§7.6.4.4.9 (Algorithms 8, 9 and 10, revision 6) and §7.6.4.3.2, §7.6.4.4.2 and §7.6.4.4.4 (Algorithms
    /// 2, 3 and 5, revisions 3 and 4).
    /// </remarks>
    public static StandardEncryption Create(PdfEncryptionOptions options, ReadOnlySpan<byte> documentId)
    {
        int permissions = unchecked((int)(ReservedPermissionBits | (uint)(options.Permissions & PdfPermissions.All)));
        return options.Algorithm switch
        {
            PdfEncryptionAlgorithm.Aes256 => CreateRevision6(options, permissions),
            PdfEncryptionAlgorithm.Aes128 => CreateLegacy(options, permissions, documentId, revision: 4),
            _ when !options.EncryptMetadata => throw new ArgumentException(
                "RC4 encryption (V 2) always encrypts metadata streams: EncryptMetadata is meaningful from V 4 (ISO 32000-2 §7.6.4.2, Table 21).",
                nameof(options)),
            _ => CreateLegacy(options, permissions, documentId, revision: 3),
        };
    }

    /// <summary>Revision 6: a random 256-bit key, <c>U</c>/<c>UE</c> (Algorithm 8), <c>O</c>/<c>OE</c> (Algorithm 9), <c>Perms</c> (Algorithm 10).</summary>
    private static StandardEncryption CreateRevision6(PdfEncryptionOptions options, int permissions)
    {
        byte[] user = PasswordEncoding.EncodeUnicode(options.UserPassword, nameof(options));
        byte[] owner = PasswordEncoding.EncodeUnicode(options.OwnerPassword, nameof(options));
        byte[] fileKey = RandomNumberGenerator.GetBytes(32);
        Span<byte> salts = stackalloc byte[32];
        RandomNumberGenerator.Fill(salts);
        Span<byte> hash = stackalloc byte[32];

        // Algorithm 8: U = hash(password ‖ validation salt) ‖ validation salt ‖ key salt; UE = AES-256(hash(password ‖ key salt), key).
        byte[] u = new byte[48];
        StandardSecurityHandler.HashRevision6([.. user, .. salts[..8]], user, [], hash);
        hash.CopyTo(u);
        salts[..16].CopyTo(u.AsSpan(32));
        StandardSecurityHandler.HashRevision6([.. user, .. salts[8..16]], user, [], hash);
        byte[] ue = EncryptKey(hash, fileKey);

        // Algorithm 9: the same for the owner password, with the 48-byte U appended to every hash input.
        byte[] o = new byte[48];
        StandardSecurityHandler.HashRevision6([.. owner, .. salts[16..24], .. u], owner, u, hash);
        hash.CopyTo(o);
        salts[16..].CopyTo(o.AsSpan(32));
        StandardSecurityHandler.HashRevision6([.. owner, .. salts[24..32], .. u], owner, u, hash);
        byte[] oe = EncryptKey(hash, fileKey);

        // Algorithm 10: P (low-order byte first), four 0xFF bytes, T or F, "adb", four random bytes; AES-256 ECB with the file key.
        Span<byte> block = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(block, permissions);
        block[4..8].Fill(0xFF);
        block[8] = options.EncryptMetadata ? (byte)'T' : (byte)'F';
        "adb"u8.CopyTo(block[9..]);
        RandomNumberGenerator.Fill(block[12..]);
        byte[] perms = new byte[16];
        using (AesCipher aes = AesCipher.Create(fileKey))
        {
            aes.EncryptEcb(block, perms);
        }

        CosDictionary dictionary = BuildDictionary(version: 5, revision: 6, keyLength: 256, KnownNames.AESV3, 32, permissions, o, u, options.EncryptMetadata);
        dictionary[OE] = Binary(oe);
        dictionary[UE] = Binary(ue);
        dictionary[Perms] = Binary(perms);
        return new StandardEncryption(dictionary, new CryptFilter(StandardFilterName, CryptMethod.AesV3, fileKey), options.EncryptMetadata);
    }

    /// <summary>Revisions 3 (RC4, <c>V</c> 2) and 4 (AES-128, <c>V</c> 4), 128-bit keys: <c>O</c> (Algorithm 3), the key (Algorithm 2), <c>U</c> (Algorithm 5).</summary>
    private static StandardEncryption CreateLegacy(PdfEncryptionOptions options, int permissions, ReadOnlySpan<byte> documentId, int revision)
    {
        const int keyLength = 16;
        byte[] user = Pad(PasswordEncoding.EncodeLegacy(options.UserPassword, nameof(options)));
        byte[] owner = Pad(PasswordEncoding.EncodeLegacy(options.OwnerPassword, nameof(options)));
        bool encryptMetadata = revision < 4 || options.EncryptMetadata;

        // Algorithm 3: MD5 of the padded owner password, rehashed 50 times; RC4 the padded user password with it, then 19 times
        // with the key XORed with the round number.
        Span<byte> digest = stackalloc byte[Md5.HashSize];
        Md5.HashData(owner, digest);
        for (int i = 0; i < 50; i++)
        {
            Md5.HashData(digest, digest);
        }

        byte[] o = new byte[32];
        Rc4.Transform(digest[..keyLength], user, o);
        XorRounds(digest[..keyLength], o);

        // Algorithm 2: MD5 of the padded user password, O, P (low-order byte first), the first file identifier and, for revision 4
        // without metadata encryption, four 0xFF bytes; the first n bytes rehashed 50 times.
        byte[] input = [.. user, .. o, 0, 0, 0, 0, .. documentId, .. revision >= 4 && !encryptMetadata ? [0xFF, 0xFF, 0xFF, 0xFF] : Array.Empty<byte>()];
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(64), permissions);
        Md5.HashData(input, digest);
        for (int i = 0; i < 50; i++)
        {
            Md5.HashData(digest[..keyLength], digest);
        }

        byte[] fileKey = digest[..keyLength].ToArray();

        // Algorithm 5: MD5 of the padding string and the first file identifier, RC4 with the key, 19 more rounds, then 16 bytes of
        // padding (zeros here).
        byte[] u = new byte[32];
        Md5.HashData([.. StandardSecurityHandler.PasswordPadding, .. documentId], u.AsSpan(0, 16));
        Rc4.Transform(fileKey, u.AsSpan(0, 16), u.AsSpan(0, 16));
        XorRounds(fileKey, u.AsSpan(0, 16));

        CosDictionary dictionary = revision == 4
            ? BuildDictionary(version: 4, revision: 4, keyLength: 128, KnownNames.AESV2, keyLength, permissions, o, u, encryptMetadata)
            : BuildDictionary(version: 2, revision: 3, keyLength: 128, method: null, keyLength, permissions, o, u, encryptMetadata: true);
        var filter = new CryptFilter(StandardFilterName, revision == 4 ? CryptMethod.AesV2 : CryptMethod.Rc4, fileKey);
        return new StandardEncryption(dictionary, filter, encryptMetadata);
    }

    /// <summary>Algorithms 3 step g and 5 step e: 19 more RC4 passes, each with the key's bytes XORed with the pass number 1 to 19.</summary>
    private static void XorRounds(ReadOnlySpan<byte> key, Span<byte> data)
    {
        Span<byte> roundKey = stackalloc byte[key.Length];
        for (int round = 1; round <= 19; round++)
        {
            for (int b = 0; b < key.Length; b++)
            {
                roundKey[b] = (byte)(key[b] ^ round);
            }

            Rc4.Transform(roundKey, data, data);
        }
    }

    /// <summary>Algorithm 2 step a: the password, at most 32 bytes, completed with the padding string.</summary>
    private static byte[] Pad(byte[] password)
    {
        byte[] padded = new byte[32];
        password.CopyTo(padded, 0);
        StandardSecurityHandler.PasswordPadding[..(32 - password.Length)].CopyTo(padded.AsSpan(password.Length));
        return padded;
    }

    /// <summary>Algorithms 8 step c and 9 step c: AES-256 in CBC mode, zero IV, no padding, of the 32-byte file key.</summary>
    private static byte[] EncryptKey(ReadOnlySpan<byte> intermediateKey, ReadOnlySpan<byte> fileKey)
    {
        byte[] encrypted = new byte[32];
        using AesCipher aes = AesCipher.Create(intermediateKey);
        aes.EncryptCbc(fileKey, stackalloc byte[16], encrypted);
        return encrypted;
    }

    private static CosString Binary(byte[] bytes) => CosString.FromOwnedBytes(bytes, hexadecimal: true);

    /// <summary>The entries every revision writes (Tables 20 and 21), with a <c>StdCF</c> crypt filter (Table 25) for <c>V</c> 4 and 5.</summary>
    private static CosDictionary BuildDictionary(int version, int revision, int keyLength, CosName? method, int filterLength, int permissions, byte[] o, byte[] u, bool encryptMetadata)
    {
        var dictionary = new CosDictionary
        {
            [FilterNames.Filter] = KnownNames.Standard,
            [KnownNames.V] = new CosInteger(version),
            [KnownNames.R] = new CosInteger(revision),
            [KnownNames.Length] = new CosInteger(keyLength),
        };
        if (method is not null)
        {
            dictionary[KnownNames.CF] = new CosDictionary
            {
                [StandardFilterName] = new CosDictionary
                {
                    [KnownNames.Type] = new CosName("CryptFilter"),
                    [KnownNames.CFM] = method,
                    [AuthEvent] = DocOpen,
                    [KnownNames.Length] = new CosInteger(filterLength),
                },
            };
            dictionary[KnownNames.StmF] = StandardFilterName;
            dictionary[KnownNames.StrF] = StandardFilterName;
        }

        dictionary[KnownNames.P] = new CosInteger(permissions);
        dictionary[O] = Binary(o);
        dictionary[U] = Binary(u);
        if (!encryptMetadata)
        {
            dictionary[KnownNames.EncryptMetadata] = CosBoolean.False;
        }

        return dictionary;
    }
}
