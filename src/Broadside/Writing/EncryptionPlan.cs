using System.Security.Cryptography;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security;

namespace Broadside.Writing;

/// <summary>What a save does about encryption: keep the document's, write new encryption, write it unencrypted, or nothing.</summary>
/// <remarks>
/// ISO 32000-2 §7.6. Decided before anything is written, so a save that cannot keep its promise throws without writing a byte.
/// </remarks>
internal sealed class EncryptionPlan
{
    private static readonly CosName AuthCode = new("AuthCode");
    private static readonly CosName KdfSalt = new("KDFSalt");

    /// <summary>How deep a kept encryption dictionary is copied direct: past it a value is written as null (<see cref="Direct"/>).</summary>
    private const int MaxDirectDepth = 32;

    /// <summary>How many values a direct copy of a kept encryption dictionary holds at most: past it a value is written as null.</summary>
    private const int MaxDirectValues = 4096;

    private readonly DocumentDecryptor? _sourceDecryptor;
    private readonly bool _keepsSourceEncryption;
    private readonly CosDictionary? _sourceDictionary;

    private EncryptionPlan(
        ObjectEncryptor? encryptor,
        bool replacesDictionary,
        CosDictionary? newDictionary,
        CosString? firstIdentifier,
        IReadOnlyList<VersionedFeature> features,
        IntegrityCodeWriter? integrity,
        DocumentDecryptor? sourceDecryptor,
        bool keepsSourceEncryption,
        CosDictionary? sourceDictionary = null)
    {
        Encryptor = encryptor;
        ReplacesDictionary = replacesDictionary;
        NewDictionary = newDictionary;
        FirstIdentifier = firstIdentifier;
        Features = features;
        Integrity = integrity;
        _sourceDecryptor = sourceDecryptor;
        _keepsSourceEncryption = keepsSourceEncryption;
        _sourceDictionary = sourceDictionary;
    }

    /// <summary>Gets what each written object becomes, or <see langword="null"/> when objects are written as they are (and may be copied).</summary>
    public ObjectEncryptor? Encryptor { get; }

    /// <summary>Gets a value indicating whether the source's <c>Encrypt</c> and <c>AuthCode</c> trailer entries, and its encryption dictionary, are dropped.</summary>
    public bool ReplacesDictionary { get; }

    /// <summary>Gets the new encryption dictionary, written as a new indirect object the trailer's <c>Encrypt</c> refers to.</summary>
    public CosDictionary? NewDictionary { get; }

    /// <summary>Gets the first file identifier the keys were derived from (Algorithm 2), or <see langword="null"/> to keep the usual one.</summary>
    public CosString? FirstIdentifier { get; }

    /// <summary>
    /// Gets the encryption the save writes as features of the version table (<see cref="VersionedFeatures"/>): new encryption's
    /// algorithm, kept AES-GCM encryption (ISO/TS 32003) and an integrity MAC (ISO/TS 32004), whose extensions the catalog shall
    /// declare; empty for none.
    /// </summary>
    /// <remarks>
    /// Other kept encryption declares nothing: the document's version already covers it, or it is declared by a developer extension
    /// of an earlier version (Adobe's AES-256 for PDF 1.7, for example), which the save keeps.
    /// </remarks>
    public IReadOnlyList<VersionedFeature> Features { get; }

    /// <summary>
    /// Gets the composer of the PDF MAC token the saved file's trailer carries in a new <c>AuthCode</c> dictionary (ISO/TS 32004
    /// §5.2.3, §6.5.1), or <see langword="null"/> when the file carries none. The source's <c>AuthCode</c> is then not repeated.
    /// </summary>
    public IntegrityCodeWriter? Integrity { get; }

    /// <summary>Whether a trailer entry belongs to the source's encryption: <c>Encrypt</c>, or the ISO/TS 32004 <c>AuthCode</c>.</summary>
    /// <param name="key">The trailer key.</param>
    /// <returns><see langword="true"/> for <c>Encrypt</c> and <c>AuthCode</c>.</returns>
    public static bool IsEncryptionEntry(CosName key) => key.Equals(KnownNames.Encrypt) || key.Equals(AuthCode);

    /// <summary>Whether the save repeats an entry of the source's trailer as it is.</summary>
    /// <param name="key">The trailer key.</param>
    /// <returns>
    /// <see langword="false"/> for the encryption entries a replaced or dropped encryption takes along, and for an <c>AuthCode</c> the
    /// save writes anew.
    /// </returns>
    public bool KeepsTrailerEntry(CosName key) =>
        !(ReplacesDictionary && IsEncryptionEntry(key)) && !(Integrity is not null && key.Equals(AuthCode));

    /// <summary>
    /// Returns what a complete file writes for <paramref name="value"/>, an object the save keeps: the direct copy of the document's
    /// encryption dictionary when <paramref name="value"/> is that dictionary and the save keeps it with references in it
    /// (<see cref="Direct"/>), else <paramref name="value"/> itself.
    /// </summary>
    /// <param name="value">An object the save writes, or the value of the trailer's <c>Encrypt</c> entry.</param>
    /// <returns>What to write.</returns>
    public CosObject Written(CosObject value) =>
        _sourceDictionary is not null && ReferenceEquals(value, _sourceDictionary) ? Encryptor!.EncryptionDictionary! : value;

    /// <summary>Decides what a save does about encryption.</summary>
    /// <param name="loader">The document's loader.</param>
    /// <param name="security">The document's security, or <see langword="null"/> for a document read as not encrypted.</param>
    /// <param name="options">The save options.</param>
    /// <param name="incremental">Whether the save is an incremental update, which keeps the document's encryption.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">
    /// The options both set and remove encryption, set password and public-key encryption together, set encryption the algorithm
    /// cannot write, or set public-key encryption without a recipient.
    /// </exception>
    /// <exception cref="PdfEncryptionNotSupportedException">Public-key encryption in a browser, which has no CMS.</exception>
    /// <exception cref="NotSupportedException">
    /// The save would change an incremental update's encryption, or keep an integrity MAC it cannot compute again (a document opened
    /// without its file encryption key, or whose encryption dictionary has no 32-byte <c>KDFSalt</c> or a <c>V</c> below 5). Never for
    /// a document read as not encrypted (<paramref name="security"/> <see langword="null"/>) unless new encryption is asked of an
    /// incremental update.
    /// </exception>
    public static EncryptionPlan Create(ObjectLoader loader, PdfSecurity? security, PdfSaveOptions options, bool incremental)
    {
        if (options.Encryption is not null && options.RemoveEncryption)
        {
            throw new ArgumentException("A save cannot both set new encryption and remove encryption.", nameof(options));
        }

        if (options.PublicKeyEncryption is not null && (options.Encryption is not null || options.RemoveEncryption))
        {
            throw new ArgumentException("A save cannot set public-key encryption together with password encryption or with removing encryption.", nameof(options));
        }

        CosDictionary trailer = loader.CrossReference.Trailer;
        bool hasEncrypt = trailer.ContainsKey(KnownNames.Encrypt);
        DocumentDecryptor? decryptor = security is null ? null : loader.Hooks.Decryptor as DocumentDecryptor;
        if (incremental && (options.Encryption is not null || (options.RemoveEncryption && security is not null)))
        {
            throw new NotSupportedException(
                "An incremental update keeps the document's encryption (ISO 32000-2 §7.5.6: the earlier revisions stay as written); save a complete file to encrypt, re-encrypt or decrypt it.");
        }

        if (options.PublicKeyEncryption is { } publicKey)
        {
            if (incremental)
            {
                throw new NotSupportedException(
                    "An incremental update keeps the document's encryption (ISO 32000-2 §7.5.6: the earlier revisions stay as written); save a complete file to encrypt it for certificate recipients.");
            }

            // The public-key file key does not hash the file identifier (§7.6.5.3); the source's first one is kept, or a random one written.
            byte[] identifier = trailer.TryGetValue(KnownNames.ID, out CosObject? ids) && loader.Resolve(ids) is CosArray { Count: >= 1 } idArray && loader.Resolve(idArray[0]) is CosString firstId
                ? firstId.Bytes.ToArray()
                : RandomNumberGenerator.GetBytes(16);
            PublicKeyEncryption recipients = PublicKeyEncryption.Create(publicKey);

            // ISO/TS 32004 §6.4: with adbe.pkcs7.s5 the MAC's key material is the key of the crypt filter DefaultCryptFilter.
            IntegrityCodeWriter? recipientsMac = recipients.KdfSalt is { } recipientsSalt ? new IntegrityCodeWriter(recipients.Filter.Key, recipientsSalt) : null;
            return new EncryptionPlan(
                ObjectEncryptor.Create(recipients.Filter, recipients.EncryptMetadata, recipients.Dictionary, loader.Resolve),
                replacesDictionary: true,
                recipients.Dictionary,
                new CosString(identifier, hexadecimal: true),
                recipientsMac is null ? [VersionedFeatures.Aes256Encryption] : [VersionedFeatures.Aes256Encryption, VersionedFeatures.IntegrityProtection],
                recipientsMac,
                decryptor,
                keepsSourceEncryption: false);
        }

        if (options.Encryption is { } encryption)
        {
            byte[] documentId = trailer.TryGetValue(KnownNames.ID, out CosObject? id) && loader.Resolve(id) is CosArray { Count: >= 1 } ids && loader.Resolve(ids[0]) is CosString first
                ? first.Bytes.ToArray()
                : RandomNumberGenerator.GetBytes(16);
            StandardEncryption created = StandardEncryption.Create(encryption, documentId);
            VersionedFeature algorithm = encryption.Algorithm switch
            {
                PdfEncryptionAlgorithm.Rc4 => VersionedFeatures.Rc4Encryption,
                PdfEncryptionAlgorithm.Aes128 => VersionedFeatures.Aes128Encryption,
                PdfEncryptionAlgorithm.Aes256Gcm => VersionedFeatures.AesGcmEncryption,
                _ => VersionedFeatures.Aes256Encryption,
            };
            IntegrityCodeWriter? mac = created.KdfSalt is { } salt ? new IntegrityCodeWriter(created.Filter.Key, salt) : null;
            return new EncryptionPlan(
                ObjectEncryptor.Create(created.Filter, created.EncryptMetadata, created.Dictionary, loader.Resolve),
                replacesDictionary: true,
                created.Dictionary,
                new CosString(documentId, hexadecimal: true),
                mac is null ? [algorithm] : [algorithm, VersionedFeatures.IntegrityProtection],
                mac,
                decryptor,
                keepsSourceEncryption: false);
        }

        if (security is null || decryptor is null)
        {
            // Not encrypted, or an Encrypt entry that is not an encryption dictionary (EncryptDictionaryInvalid: the document was
            // read, and IsEncrypted reports it, as not encrypted): nothing to decrypt or encrypt, so the save is the one an
            // unencrypted document gets. The damaged entry is dropped from a complete file, and from an incremental update asked to
            // remove encryption; any other update repeats it, so the file reads back the way it was read.
            return new EncryptionPlan(null, replacesDictionary: hasEncrypt && (!incremental || options.RemoveEncryption), null, null, [], null, null, keepsSourceEncryption: false);
        }

        if (options.RemoveEncryption)
        {
            return new EncryptionPlan(ObjectEncryptor.Remove(loader.Resolve), replacesDictionary: true, null, null, [], null, decryptor, keepsSourceEncryption: false);
        }

        // ISO/TS 32004 §6.5.1: the revision the save writes is the newest and unsigned, so its token stands alone in the trailer and
        // covers the whole file. The document's own token (standalone or attached to a signature) is replaced whatever its status:
        // the saver holds the file encryption key the MAC proves knowledge of (§6.4, NOTE 2).
        IntegrityCodeWriter? integrity = security.Integrity != PdfIntegrityStatus.None || security.RequiresIntegrityCode
            ? KeptIntegrity(security, decryptor, loader.Resolve)
            : null;
        var features = new List<VersionedFeature>(2);
        if (security.Version >= 6)
        {
            features.Add(VersionedFeatures.AesGcmEncryption);
        }

        if (integrity is not null)
        {
            features.Add(VersionedFeatures.IntegrityProtection);
        }

        // The keys of revisions 2-4 hash the first file identifier as the reader took it (an empty one when the trailer has none).
        CosString firstIdentifier = trailer.TryGetValue(KnownNames.ID, out CosObject? identifiers) && identifiers is CosArray { Count: >= 1 } array && array[0] is CosString kept
            ? kept
            : new CosString([], hexadecimal: true);
        // §7.6.2: the values of the dictionary's entries shall not be encrypted, and the reader resolves them before it can decrypt
        // anything. A reference in it reaches an object the save would encrypt, or put in an encrypted object stream (§7.5.7), so a
        // complete file writes the dictionary direct (issue #390). An incremental update writes it only when it changed, as it is.
        CosDictionary source = security.EncryptionDictionary;
        CosDictionary written = incremental ? source : Direct(source, loader.Resolve);
        return new EncryptionPlan(
            ObjectEncryptor.Keep(decryptor, written, loader.Resolve),
            replacesDictionary: false,
            null,
            firstIdentifier,
            features,
            integrity,
            decryptor,
            keepsSourceEncryption: true,
            ReferenceEquals(written, source) ? null : source);
    }

    /// <summary>
    /// Returns <paramref name="dictionary"/> when it holds no reference, else a copy in which every reference, in it and in the
    /// dictionaries and arrays it holds, is replaced by a copy of what the reference resolves to (ISO 32000-2 §7.6.2).
    /// </summary>
    /// <remarks>
    /// What cannot be direct is written as null, which reads as the entry absent (§7.3.9) and is what a security handler reads it as
    /// anyway, since none reads a stream or a page tree from the dictionary: a stream (§7.3.8: always indirect), a reference to an
    /// object being copied (a cycle), and anything past <see cref="MaxDirectDepth"/> nested values or <see cref="MaxDirectValues"/>
    /// copied ones (a damaged entry that reaches the whole document). Every value the handler read when the document was opened was
    /// resolved before decryption was installed, so the copy holds those values as the handler read them.
    /// </remarks>
    private static CosDictionary Direct(CosDictionary dictionary, Func<CosObject?, CosObject> resolve)
    {
        if (!HoldsReference(dictionary, depth: 0))
        {
            return dictionary;
        }

        int budget = MaxDirectValues;
        return (CosDictionary)DirectCopy(dictionary, resolve, [], depth: 0, ref budget);
    }

    private static bool HoldsReference(CosObject value, int depth) => depth > MaxDirectDepth || value switch
    {
        CosReference => true,
        CosDictionary dictionary => dictionary.Any(entry => HoldsReference(entry.Value, depth + 1)),
        CosArray array => array.Any(item => HoldsReference(item, depth + 1)),
        _ => false,
    };

    private static CosObject DirectCopy(CosObject value, Func<CosObject?, CosObject> resolve, HashSet<CosReference> path, int depth, ref int budget)
    {
        if (depth > MaxDirectDepth || --budget < 0)
        {
            return CosNull.Instance;
        }

        switch (value)
        {
            case CosReference reference:
                if (!path.Add(reference))
                {
                    return CosNull.Instance;
                }

                CosObject resolved = DirectCopy(resolve(reference), resolve, path, depth, ref budget);
                path.Remove(reference);
                return resolved;
            case CosStream:
                return CosNull.Instance;
            case CosDictionary dictionary:
                var copy = new CosDictionary();
                foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                {
                    copy[entry.Key] = DirectCopy(entry.Value, resolve, path, depth + 1, ref budget);
                }

                return copy;
            case CosArray array:
                var items = new CosArray();
                foreach (CosObject item in array)
                {
                    items.Add(DirectCopy(item, resolve, path, depth + 1, ref budget));
                }

                return items;
            default:
                return value;
        }
    }

    /// <summary>The MAC composer for a document that keeps its encryption and has, or needs, a PDF MAC token (ISO/TS 32004).</summary>
    private static IntegrityCodeWriter KeptIntegrity(PdfSecurity security, DocumentDecryptor decryptor, Func<CosObject?, CosObject> resolve)
    {
        if (security.Version < 5)
        {
            throw new NotSupportedException(
                "The document's trailer has an AuthCode entry, which needs an encryption dictionary with V 5 or later (ISO/TS 32004 §5.2.1, Table 5); save it with new encryption (PdfSaveOptions.Encryption) or without encryption (PdfSaveOptions.RemoveEncryption).");
        }

        if (decryptor.FileEncryptionKey.Length == 0)
        {
            throw new NotSupportedException(
                "The document's integrity MAC (ISO/TS 32004 §6.4) is derived from the file encryption key, which the document was opened without; open it with its password or certificate to save it with its own encryption.");
        }

        if (resolve(security.EncryptionDictionary.TryGetValue(KdfSalt, out CosObject? salt) ? salt : null) is not CosString { Bytes.Length: 32 } kdfSalt)
        {
            throw new NotSupportedException(
                "The document's integrity MAC needs a 32-byte KDFSalt in its encryption dictionary (ISO/TS 32004 §5.1.1, Table 2), which it lacks; save it with new encryption (PdfSaveOptions.Encryption) or without encryption (PdfSaveOptions.RemoveEncryption).");
        }

        return new IntegrityCodeWriter(decryptor.FileEncryptionKey, kdfSalt.Bytes);
    }

    /// <summary>
    /// Refuses, before anything is written, a save that would have to write data the document's credentials could not decrypt
    /// (a crypt filter they do not unlock, §7.6.6) under anything but the key it was read with, at the number it was read under.
    /// </summary>
    /// <param name="objects">The objects the save writes.</param>
    /// <param name="references">The numbers they are written with, or <see langword="null"/> when numbers are kept.</param>
    /// <exception cref="NotSupportedException">Such data would be written.</exception>
    public void CheckWritable(IEnumerable<(CosReference Reference, CosObject Value)> objects, IReadOnlyDictionary<CosReference, CosReference>? references)
    {
        if (_sourceDecryptor is null)
        {
            return;
        }

        foreach ((CosReference reference, CosObject value) in objects)
        {
            if (value is CosStream stream
                && _sourceDecryptor.IsLocked(stream.Dictionary)
                && (!_keepsSourceEncryption || (references is not null && (!references.TryGetValue(reference, out CosReference? written) || !written.Equals(reference)))))
            {
                throw new NotSupportedException(
                    "The document holds data its credentials do not decrypt (a crypt filter they do not unlock, ISO 32000-2 §7.6.6), which can only be saved encrypted as it was read, under its own number; open it with credentials that unlock it.");
            }
        }
    }
}
