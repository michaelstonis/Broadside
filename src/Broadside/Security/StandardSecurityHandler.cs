using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security.Cryptography;

namespace Broadside.Security;

/// <summary>The standard password-based security handler, revisions 2 to 7. Registered by default.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.4: Algorithms 2, 3, 6 and 7 for revisions 2 to 4 (RC4 and AES-128, MD5 key derivation), Algorithms 2.A, 2.B,
/// 11, 12 and 13 for revision 6 (AES-256), the same for revision 7 (ISO/TS 32003, AES-GCM: its password algorithms are those of
/// revision 6), and the deprecated revision 5 (an Adobe extension: SHA-256 instead of Algorithm 2.B) for reading only.
/// </para>
/// <para>
/// Without a password the default (empty) user password is tried (§7.6.4.1); a document it does not open throws
/// <see cref="PdfPasswordException"/> with <see cref="PdfPasswordFailure.Required"/>. A password is tried as the user password, then
/// as the owner password; one that is neither throws <see cref="PdfPasswordFailure.Incorrect"/>. The owner password grants
/// <see cref="PdfPermissions.All"/>.
/// </para>
/// <para>
/// Repairs, each recorded as a diagnostic: <c>O</c> and <c>U</c> longer than the revision's length are truncated and shorter ones
/// padded with zeros; a <c>Length</c> outside 40 to 128 bits is clamped (one that looks like a byte count is read as one); for a
/// revision 3 or 4 key shorter than 128 bits whose owner password fails Algorithm 3 as written, the variant that rehashes only the
/// first <c>n</c> bytes in step c (which PDFBox writes) is tried; a revision 6 <c>Perms</c> that disagrees with <c>P</c> wins over it
/// (it is the tamper-resistant copy, ISO/TS 32004 §5.1.2 NOTE 2).
/// </para>
/// </remarks>
public sealed class StandardSecurityHandler : ISecurityHandler
{
    /// <summary>The 32-byte padding string of Algorithm 2 step a (§7.6.4.3.2).</summary>
    internal static ReadOnlySpan<byte> PasswordPadding =>
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    private static readonly CosName StandardName = new("Standard");
    private static readonly CosName V = new("V");
    private static readonly CosName R = new("R");
    private static readonly CosName O = new("O");
    private static readonly CosName U = new("U");
    private static readonly CosName OE = new("OE");
    private static readonly CosName UE = new("UE");
    private static readonly CosName P = new("P");
    private static readonly CosName Perms = new("Perms");
    private static readonly CosName LengthName = new("Length");
    private static readonly CosName EncryptMetadata = new("EncryptMetadata");

    /// <summary>Gets <c>Standard</c>, the name of the built-in password-based handler (Table 20).</summary>
    public CosName Filter => StandardName;

    /// <summary>Gets no formats: the standard handler is chosen by <c>Filter</c> only.</summary>
    public IReadOnlyCollection<CosName> SubFilters => [];

    /// <summary>Computes the permissions a user has from the <c>P</c> entry, the way the revision defines them (Table 22).</summary>
    /// <param name="rawPermissions">The <c>P</c> entry.</param>
    /// <param name="revision">The revision <c>R</c>.</param>
    /// <returns>The permissions.</returns>
    /// <remarks>ISO 32000-2 §7.6.4.2, Table 22.</remarks>
    public static PdfPermissions UserPermissions(int rawPermissions, int revision)
    {
        var bits = (PdfPermissions)rawPermissions;
        bool print = bits.HasFlag(PdfPermissions.Print);
        bool modify = bits.HasFlag(PdfPermissions.Modify);
        bool annotate = bits.HasFlag(PdfPermissions.Annotate);
        PdfPermissions permissions = bits & (PdfPermissions.Print | PdfPermissions.Modify | PdfPermissions.Extract | PdfPermissions.Annotate);
        if (revision < 3)
        {
            // Revision 2 controls bits 3-6 only; what later revisions split out follows the bit that covered it.
            permissions |= (annotate ? PdfPermissions.FillForms : 0) | (modify ? PdfPermissions.Assemble : 0) | (print ? PdfPermissions.PrintHighQuality : 0);
            return permissions;
        }

        if (annotate || bits.HasFlag(PdfPermissions.FillForms))
        {
            permissions |= PdfPermissions.FillForms;
        }

        if (bits.HasFlag(PdfPermissions.Assemble))
        {
            permissions |= PdfPermissions.Assemble;
        }

        if (print && bits.HasFlag(PdfPermissions.PrintHighQuality))
        {
            permissions |= PdfPermissions.PrintHighQuality;
        }

        return permissions;
    }

    /// <inheritdoc/>
    public SecurityHandlerResult Authenticate(SecurityHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        CosDictionary dictionary = context.EncryptionDictionary;
        int revision = ReadInteger(context, dictionary, R) ?? throw Fail(context, "The standard encryption dictionary has no R (revision) entry.");
        if (revision is < 2 or > 7)
        {
            throw new PdfEncryptionNotSupportedException(
                PdfEncryptionNotSupportedReason.Algorithm,
                string.Create(CultureInfo.InvariantCulture, $"Revision {revision} of the standard security handler is not defined."));
        }

        int version = ReadInteger(context, dictionary, V) ?? DefaultVersion(context, revision);
        int rawPermissions = ReadInteger(context, dictionary, P) ?? throw Fail(context, "The standard encryption dictionary has no P (permissions) entry.");
        PdfPassword? password = context.Credentials as PdfPassword;
        if (revision == 5)
        {
            context.Report(
                DiagnosticCodes.EncryptionRevisionInvalid,
                DiagnosticSeverity.Warning,
                "Revision 5 of the standard security handler shall not be used (Table 21: a deprecated Adobe extension); it is read as such.");
        }

        return revision <= 4
            ? AuthenticateLegacy(context, dictionary, password, version, revision, rawPermissions)
            : AuthenticateModern(context, dictionary, password, revision, rawPermissions);
    }

    /// <summary>Algorithm 2.B (§7.6.4.3.4): the revision 6 hash, written into the first 32 bytes of <paramref name="output"/>.</summary>
    /// <param name="input">The password, a salt and (owner) the 48-byte <c>U</c>.</param>
    /// <param name="password">The UTF-8 password.</param>
    /// <param name="userKey">The 48-byte <c>U</c> when checking the owner password; otherwise empty.</param>
    /// <param name="output">At least 32 bytes.</param>
    internal static void HashRevision6(ReadOnlySpan<byte> input, ReadOnlySpan<byte> password, ReadOnlySpan<byte> userKey, Span<byte> output)
    {
        Span<byte> k = stackalloc byte[64];
        int kLength = SHA256.HashData(input, k);
        int maxSequence = password.Length + 64 + userKey.Length;
        byte[] k1 = ArrayPool<byte>.Shared.Rent(maxSequence * 64);
        byte[] e = ArrayPool<byte>.Shared.Rent(maxSequence * 64);
        using AesCipher aes = AesCipher.Create(k[..16]);
        try
        {
            for (int round = 1; ; round++)
            {
                // a) K1 = 64 repetitions of password ‖ K ‖ U.
                int sequence = password.Length + kLength + userKey.Length;
                Span<byte> first = k1.AsSpan(0, sequence);
                password.CopyTo(first);
                k[..kLength].CopyTo(first[password.Length..]);
                userKey.CopyTo(first[(password.Length + kLength)..]);
                int length = sequence * 64;
                for (int offset = sequence; offset < length; offset += sequence)
                {
                    first.CopyTo(k1.AsSpan(offset, sequence));
                }

                // b) E = AES-128-CBC(key K[0..16], IV K[16..32], no padding), one cipher re-keyed each round.
                aes.SetKey(k[..16]);
                aes.EncryptCbc(k1.AsSpan(0, length), k[16..32], e);

                // c) The first 16 bytes of E as a big-endian integer, modulo 3: since 256 ≡ 1 (mod 3), the sum of the bytes.
                int sum = 0;
                for (int i = 0; i < 16; i++)
                {
                    sum += e[i];
                }

                // d) The next K.
                ReadOnlySpan<byte> encrypted = e.AsSpan(0, length);
                kLength = (sum % 3) switch
                {
                    0 => SHA256.HashData(encrypted, k),
                    1 => SHA384.HashData(encrypted, k),
                    _ => SHA512.HashData(encrypted, k),
                };

                // e)-f) After round 63 (the 64th), stop once the last byte of E is at most the round number - 32.
                if (round >= 64 && e[length - 1] <= round - 32)
                {
                    break;
                }
            }

            k[..32].CopyTo(output);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(k1);
            ArrayPool<byte>.Shared.Return(e);
        }
    }

    private static SecurityHandlerResult AuthenticateLegacy(
        SecurityHandlerContext context,
        CosDictionary dictionary,
        PdfPassword? password,
        int version,
        int revision,
        int rawPermissions)
    {
        int keyLength = LegacyKeyLength(context, dictionary, version, revision);
        bool encryptMetadata = version < 4 || revision < 4 || (ReadBoolean(context, dictionary, EncryptMetadata) ?? true);
        byte[] owner = ReadEntry(context, dictionary, O, 32, required: true)!;
        byte[] user = ReadEntry(context, dictionary, U, 32, required: true)!;
        byte[] documentId = context.DocumentId.Span.ToArray();
        var legacy = new LegacyKeys(revision, keyLength, owner, user, rawPermissions, documentId, encryptMetadata);
        if (TryLegacyPasswords(context, legacy, password, revision, rawPermissions) is { } result)
        {
            return result;
        }

        // Table 20: Length is optional and defaults to 40 bits. A writer that left it out for a 128-bit key (qpdf's test file
        // bad-encryption-length.pdf; qpdf assumes 128 bits whenever Length is missing) is read by trying 128 bits once 40 bits
        // authenticate nothing.
        if (revision >= 3 && version is 2 or 3 && !dictionary.ContainsKey(LengthName))
        {
            var longer = new LegacyKeys(revision, 16, owner, user, rawPermissions, documentId, encryptMetadata);
            if (TryLegacyPasswords(context, longer, password, revision, rawPermissions) is { } repaired)
            {
                context.Report(
                    DiagnosticCodes.EncryptionKeyLengthInvalid,
                    DiagnosticSeverity.Warning,
                    "The encryption dictionary has no Length entry, so the key is 40 bits long (Table 20), but only a 128-bit key authenticates; it is read as 128 bits.");
                return repaired;
            }
        }

        throw new PdfPasswordException(password is null || password.IsEmpty ? PdfPasswordFailure.Required : PdfPasswordFailure.Incorrect);
    }

    /// <summary>Algorithms 6 and 7 for every encoding of <paramref name="password"/>: the user password first, then the owner password.</summary>
    /// <returns>The result, or <see langword="null"/> when no encoding authenticates.</returns>
    private static SecurityHandlerResult? TryLegacyPasswords(
        SecurityHandlerContext context,
        LegacyKeys legacy,
        PdfPassword? password,
        int revision,
        int rawPermissions)
    {
        PdfPermissions userPermissions = UserPermissions(rawPermissions, revision);
        foreach (byte[] candidate in PasswordEncoding.Legacy(password))
        {
            if (legacy.TryUser(candidate) is { } userKey)
            {
                return new SecurityHandlerResult(userKey, PdfAccessLevel.User, userPermissions, rawPermissions) { Revision = revision };
            }

            // The empty string is a password like any other: Algorithm 7 does not exclude it.
            if (legacy.TryOwner(candidate, context) is { } ownerKey)
            {
                return new SecurityHandlerResult(ownerKey, PdfAccessLevel.Owner, PdfPermissions.All, rawPermissions) { Revision = revision };
            }
        }

        return null;
    }

    private static SecurityHandlerResult AuthenticateModern(
        SecurityHandlerContext context,
        CosDictionary dictionary,
        PdfPassword? password,
        int revision,
        int rawPermissions)
    {
        byte[] owner = ReadEntry(context, dictionary, O, 48, required: true)!;
        byte[] user = ReadEntry(context, dictionary, U, 48, required: true)!;
        byte[] ownerEncryption = ReadEntry(context, dictionary, OE, 32, required: true)!;
        byte[] userEncryption = ReadEntry(context, dictionary, UE, 32, required: true)!;
        byte[]? permissionsEntry = ReadEntry(context, dictionary, Perms, 16, required: revision >= 6);
        bool encryptMetadata = ReadBoolean(context, dictionary, EncryptMetadata) ?? true;

        List<byte[]> candidates = revision == 5 ? PasswordEncoding.Utf8(password) : PasswordEncoding.Unicode(password);
        foreach (byte[] candidate in candidates)
        {
            PdfAccessLevel? access = null;
            byte[]? key = TryModernUser(revision, candidate, user, userEncryption);
            if (key is not null)
            {
                access = PdfAccessLevel.User;
            }
            else if ((key = TryModernOwner(revision, candidate, owner, user, ownerEncryption)) is not null)
            {
                // An empty owner password is a password like any other (Algorithms 9 and 12; pdf.js test file pr6531_2.pdf).
                access = PdfAccessLevel.Owner;
            }

            if (access is { } level)
            {
                int permissions = permissionsEntry is null
                    ? rawPermissions
                    : ValidatePermissions(context, key!, permissionsEntry, rawPermissions, encryptMetadata);
                PdfPermissions granted = level == PdfAccessLevel.Owner ? PdfPermissions.All : UserPermissions(permissions, revision);
                return new SecurityHandlerResult(key, level, granted, permissions) { Revision = revision };
            }
        }

        throw new PdfPasswordException(password is null || password.IsEmpty ? PdfPasswordFailure.Required : PdfPasswordFailure.Incorrect);
    }

    /// <summary>Algorithm 11, then Algorithm 2.A step e.</summary>
    private static byte[]? TryModernUser(int revision, byte[] password, byte[] user, byte[] userEncryption)
    {
        Span<byte> hash = stackalloc byte[32];
        ModernHash(revision, [.. password, .. user.AsSpan(32, 8)], password, [], hash);
        if (!CryptographicOperations.FixedTimeEquals(hash, user.AsSpan(0, 32)))
        {
            return null;
        }

        ModernHash(revision, [.. password, .. user.AsSpan(40, 8)], password, [], hash);
        return DecryptFileKey(hash, userEncryption);
    }

    /// <summary>Algorithm 12, then Algorithm 2.A step d.</summary>
    private static byte[]? TryModernOwner(int revision, byte[] password, byte[] owner, byte[] user, byte[] ownerEncryption)
    {
        Span<byte> hash = stackalloc byte[32];
        ModernHash(revision, [.. password, .. owner.AsSpan(32, 8), .. user], password, user, hash);
        if (!CryptographicOperations.FixedTimeEquals(hash, owner.AsSpan(0, 32)))
        {
            return null;
        }

        ModernHash(revision, [.. password, .. owner.AsSpan(40, 8), .. user], password, user, hash);
        return DecryptFileKey(hash, ownerEncryption);
    }

    private static void ModernHash(int revision, ReadOnlySpan<byte> input, ReadOnlySpan<byte> password, ReadOnlySpan<byte> userKey, Span<byte> output)
    {
        if (revision == 5)
        {
            SHA256.HashData(input, output);
        }
        else
        {
            HashRevision6(input, password, userKey, output);
        }
    }

    /// <summary>AES-256 in CBC mode with a zero IV and no padding over <c>OE</c> or <c>UE</c> (Algorithm 2.A steps d and e).</summary>
    private static byte[] DecryptFileKey(ReadOnlySpan<byte> intermediateKey, byte[] encryptedKey)
    {
        byte[] fileKey = new byte[32];
        using AesCipher aes = AesCipher.Create(intermediateKey);
        aes.DecryptCbc(encryptedKey.AsSpan(0, 32), stackalloc byte[16], fileKey);
        return fileKey;
    }

    /// <summary>Algorithm 13 (and 2.A step f): decrypt <c>Perms</c>, check "adb", compare with <c>P</c> and <c>EncryptMetadata</c>.</summary>
    private static int ValidatePermissions(SecurityHandlerContext context, byte[] fileKey, byte[] permissionsEntry, int rawPermissions, bool encryptMetadata)
    {
        Span<byte> block = stackalloc byte[16];
        using (AesCipher aes = AesCipher.Create(fileKey))
        {
            aes.DecryptEcb(permissionsEntry.AsSpan(0, 16), block);
        }

        if (!block.Slice(9, 3).SequenceEqual("adb"u8))
        {
            context.Report(
                DiagnosticCodes.EncryptionPermissionsMismatch,
                DiagnosticSeverity.Warning,
                "The Perms entry does not decrypt to a permissions block (bytes 9-11 are not \"adb\"); the P entry is used.");
            return rawPermissions;
        }

        int permissions = BinaryPrimitives.ReadInt32LittleEndian(block);
        if (permissions != rawPermissions)
        {
            context.Report(
                DiagnosticCodes.EncryptionPermissionsMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The permissions in Perms ({permissions}) shall match the P entry ({rawPermissions}); the value in Perms is used."));
        }

        if (block[8] != (encryptMetadata ? (byte)'T' : (byte)'F'))
        {
            context.Report(
                DiagnosticCodes.EncryptionPermissionsMismatch,
                DiagnosticSeverity.Warning,
                "Byte 8 of Perms does not match EncryptMetadata (Algorithm 13); EncryptMetadata is used.");
        }

        return permissions;
    }

    /// <summary>The file encryption key length <c>n</c> in bytes for revisions 2 to 4 (Table 20, Algorithm 2 step i).</summary>
    private static int LegacyKeyLength(SecurityHandlerContext context, CosDictionary dictionary, int version, int revision)
    {
        if (revision == 2 || version == 1)
        {
            return 5;
        }

        if (version >= 4)
        {
            return 16;
        }

        int? bits = ReadInteger(context, dictionary, LengthName);
        switch (bits)
        {
            case null:
                return 5;
            case >= 40 and <= 128 when bits % 8 == 0:
                return bits.Value / 8;
            case >= 5 and <= 16:
                context.Report(
                    DiagnosticCodes.EncryptionKeyLengthInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The Length entry shall be a bit count from 40 to 128; {bits} is read as a byte count."));
                return bits.Value;
            default:
                int clamped = Math.Clamp(bits.Value, 40, 128) / 8;
                context.Report(
                    DiagnosticCodes.EncryptionKeyLengthInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The Length entry shall be a multiple of 8 from 40 to 128; {bits} is read as {clamped * 8}."));
                return clamped;
        }
    }

    private static int DefaultVersion(SecurityHandlerContext context, int revision)
    {
        int version = revision switch { 2 => 1, 3 => 2, 4 => 4, 7 => 6, _ => 5 };
        context.Report(
            DiagnosticCodes.EncryptDictionaryInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The encryption dictionary has no V entry; it is read as {version}, the value revision {revision} goes with."));
        return version;
    }

    private static int? ReadInteger(SecurityHandlerContext context, CosDictionary dictionary, CosName key)
    {
        if (!dictionary.TryGetValue(key, out CosObject? entry))
        {
            return null;
        }

        switch (context.Resolve(entry))
        {
            case CosInteger integer when integer.Value is >= int.MinValue and <= uint.MaxValue:
                // P is an unsigned 32-bit quantity some writers store unsigned (§7.6.4.2).
                return unchecked((int)integer.Value);
            case CosReal real:
                context.Report(
                    DiagnosticCodes.EncryptDictionaryInvalid,
                    DiagnosticSeverity.Warning,
                    $"The encryption dictionary's {key.Value} entry shall be an integer; the real number is truncated.");
                return (int)real.Value;
            default:
                return null;
        }
    }

    private static bool? ReadBoolean(SecurityHandlerContext context, CosDictionary dictionary, CosName key) =>
        dictionary.TryGetValue(key, out CosObject? entry) && context.Resolve(entry) is CosBoolean boolean ? boolean.Value : null;

    /// <summary>Reads a byte-string entry, truncating a longer one and zero-padding a shorter one, with a diagnostic.</summary>
    private static byte[]? ReadEntry(SecurityHandlerContext context, CosDictionary dictionary, CosName key, int length, bool required)
    {
        if (!dictionary.TryGetValue(key, out CosObject? entry) || context.Resolve(entry) is not CosString value)
        {
            return required
                ? throw Fail(context, $"The standard encryption dictionary has no {key.Value} string, which the revision requires.")
                : null;
        }

        ReadOnlySpan<byte> bytes = value.Bytes;
        if (bytes.Length == length)
        {
            return bytes.ToArray();
        }

        context.Report(
            DiagnosticCodes.EncryptionEntryLengthInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The {key.Value} entry shall be {length} bytes long; it has {bytes.Length}, read as its first {length} bytes{(bytes.Length < length ? " padded with zeros" : string.Empty)}."));
        byte[] fixedLength = new byte[length];
        bytes[..Math.Min(length, bytes.Length)].CopyTo(fixedLength);
        return fixedLength;
    }

    private static DiagnosticException Fail(SecurityHandlerContext context, string message)
    {
        var diagnostic = new Diagnostic(DiagnosticCodes.EncryptDictionaryInvalid, DiagnosticSeverity.Error, message);
        context.Report(diagnostic.Code, diagnostic.Severity, diagnostic.Message);
        return new DiagnosticException(diagnostic);
    }

    /// <summary>The key computations of revisions 2 to 4 for one encryption dictionary.</summary>
    private sealed class LegacyKeys(int revision, int keyLength, byte[] owner, byte[] user, int permissions, byte[] documentId, bool encryptMetadata)
    {
        /// <summary>Algorithm 6: computes the key from a user password with Algorithm 2 and checks it against <c>U</c>.</summary>
        public byte[]? TryUser(ReadOnlySpan<byte> password)
        {
            byte[] key = FileKey(password);
            Span<byte> check = stackalloc byte[32];
            if (revision == 2)
            {
                // Algorithm 4.
                Rc4.Transform(key, PasswordPadding, check);
                return CryptographicOperations.FixedTimeEquals(check, user.AsSpan(0, 32)) ? key : null;
            }

            // Algorithm 5: compare the first 16 bytes.
            Span<byte> buffer = stackalloc byte[32 + documentId.Length];
            PasswordPadding.CopyTo(buffer);
            documentId.CopyTo(buffer[32..]);
            Md5.HashData(buffer, check);
            Span<byte> value = check[..16];
            Rc4.Transform(key, value, value);
            Span<byte> roundKey = stackalloc byte[key.Length];
            for (int i = 1; i <= 19; i++)
            {
                for (int b = 0; b < key.Length; b++)
                {
                    roundKey[b] = (byte)(key[b] ^ i);
                }

                Rc4.Transform(roundKey, value, value);
            }

            return CryptographicOperations.FixedTimeEquals(value, user.AsSpan(0, 16)) ? key : null;
        }

        /// <summary>Algorithm 7: recovers the user password from <c>O</c> with the owner password and authenticates it.</summary>
        public byte[]? TryOwner(ReadOnlySpan<byte> password, SecurityHandlerContext context)
        {
            if (TryOwner(password, rehashWholeDigest: true) is { } key)
            {
                return key;
            }

            if (revision < 3 || keyLength >= 16 || TryOwner(password, rehashWholeDigest: false) is not { } variantKey)
            {
                return null;
            }

            context.Report(
                DiagnosticCodes.OwnerPasswordKeyVariant,
                DiagnosticSeverity.Warning,
                "The O entry was computed by rehashing only the first n bytes in Algorithm 3 step c, not the whole digest; the owner password is accepted.");
            return variantKey;
        }

        private byte[]? TryOwner(ReadOnlySpan<byte> password, bool rehashWholeDigest)
        {
            // Algorithm 3 steps a-d.
            Span<byte> digest = stackalloc byte[16];
            Md5.HashData(Pad(password), digest);
            if (revision >= 3)
            {
                for (int i = 0; i < 50; i++)
                {
                    Md5.HashData(rehashWholeDigest ? digest : digest[..keyLength], digest);
                }
            }

            ReadOnlySpan<byte> rc4Key = digest[..keyLength];
            Span<byte> userPassword = stackalloc byte[32];
            if (revision == 2)
            {
                Rc4.Transform(rc4Key, owner.AsSpan(0, 32), userPassword);
            }
            else
            {
                owner.AsSpan(0, 32).CopyTo(userPassword);
                Span<byte> roundKey = stackalloc byte[keyLength];
                for (int i = 19; i >= 0; i--)
                {
                    for (int b = 0; b < keyLength; b++)
                    {
                        roundKey[b] = (byte)(rc4Key[b] ^ i);
                    }

                    Rc4.Transform(roundKey, userPassword, userPassword);
                }
            }

            // The result is the padded user password; padding a 32-byte string leaves it as is.
            return TryUser(userPassword);
        }

        /// <summary>Algorithm 2: the file encryption key from a user password.</summary>
        private byte[] FileKey(ReadOnlySpan<byte> password)
        {
            byte[] input = [.. Pad(password), .. owner.AsSpan(0, 32), 0, 0, 0, 0, .. documentId, .. revision >= 4 && !encryptMetadata ? [0xFF, 0xFF, 0xFF, 0xFF] : Array.Empty<byte>()];
            BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(64), permissions);
            Span<byte> digest = stackalloc byte[16];
            Md5.HashData(input, digest);
            if (revision >= 3)
            {
                for (int i = 0; i < 50; i++)
                {
                    Md5.HashData(digest[..keyLength], digest);
                }
            }

            return digest[..keyLength].ToArray();
        }

        private static byte[] Pad(ReadOnlySpan<byte> password)
        {
            byte[] padded = new byte[32];
            int length = Math.Min(32, password.Length);
            password[..length].CopyTo(padded);
            PasswordPadding[..(32 - length)].CopyTo(padded.AsSpan(length));
            return padded;
        }
    }
}
