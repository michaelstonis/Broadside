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

    private readonly DocumentDecryptor? _sourceDecryptor;
    private readonly bool _keepsSourceEncryption;

    private EncryptionPlan(
        ObjectEncryptor? encryptor,
        bool replacesDictionary,
        CosDictionary? newDictionary,
        CosString? firstIdentifier,
        IReadOnlyList<VersionedFeature> features,
        IntegrityCodeWriter? integrity,
        DocumentDecryptor? sourceDecryptor,
        bool keepsSourceEncryption)
    {
        Encryptor = encryptor;
        ReplacesDictionary = replacesDictionary;
        NewDictionary = newDictionary;
        FirstIdentifier = firstIdentifier;
        Features = features;
        Integrity = integrity;
        _sourceDecryptor = sourceDecryptor;
        _keepsSourceEncryption = keepsSourceEncryption;
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

    /// <summary>Decides what a save does about encryption.</summary>
    /// <param name="loader">The document's loader.</param>
    /// <param name="security">The document's security, or <see langword="null"/> for a document read as not encrypted.</param>
    /// <param name="options">The save options.</param>
    /// <param name="incremental">Whether the save is an incremental update, which keeps the document's encryption.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">The options both set and remove encryption, or set encryption the algorithm cannot write.</exception>
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

        CosDictionary trailer = loader.CrossReference.Trailer;
        bool hasEncrypt = trailer.ContainsKey(KnownNames.Encrypt);
        DocumentDecryptor? decryptor = security is null ? null : loader.Hooks.Decryptor as DocumentDecryptor;
        if (incremental && (options.Encryption is not null || (options.RemoveEncryption && security is not null)))
        {
            throw new NotSupportedException(
                "An incremental update keeps the document's encryption (ISO 32000-2 §7.5.6: the earlier revisions stay as written); save a complete file to encrypt, re-encrypt or decrypt it.");
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
        return new EncryptionPlan(
            ObjectEncryptor.Keep(decryptor, security.EncryptionDictionary, loader.Resolve),
            replacesDictionary: false,
            null,
            firstIdentifier,
            features,
            integrity,
            decryptor,
            keepsSourceEncryption: true);
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
