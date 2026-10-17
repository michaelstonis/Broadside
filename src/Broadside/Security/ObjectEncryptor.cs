using System.Buffers;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Security.Cryptography;

namespace Broadside.Security;

/// <summary>
/// Encrypts the strings and streams of the indirect objects a save writes, under the number and generation each is written with: the
/// counterpart of <see cref="DocumentDecryptor"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.2 (what is encrypted), §7.6.3 (Algorithms 1 and 1.A), §7.6.6 (crypt filters), §7.4.10 (the <c>Crypt</c> filter);
/// ISO/TS 32003 §5.2 (AES-GCM). Every string is encrypted with <c>StrF</c>, every stream with <c>StmF</c> and an embedded file with
/// <c>EFF</c>, except what §7.6.2 exempts and the decryptor leaves alone: the encryption dictionary itself, cross-reference streams,
/// the <c>Contents</c> string of a signature dictionary and, when <c>EncryptMetadata</c> is false, metadata streams. The trailer and
/// the members of object streams never reach it (the object stream is encrypted as a whole).
/// </para>
/// <para>
/// The document is never changed: an object that holds anything to encrypt is copied, its strings replaced by encrypted hexadecimal
/// strings and its stream data by the encrypted bytes, and the copy is what the writer serializes. AES data gets a random IV and PKCS#5
/// padding (§7.6.3.1), AES-GCM data a random 12-byte nonce and its tag.
/// </para>
/// <para>
/// A stream whose first filter is <c>Crypt</c> keeps it when the encryption is the document's own and names a filter it defines; it is
/// then encrypted with that filter's key as is (§7.4.10), AES-128 with Algorithm 1's object key as qpdf writes it (the decryptor reads
/// both). Under any other encryption, or none, the <c>Crypt</c> filter is dropped and the stream is encrypted like any other. Data under
/// a crypt filter the document's credentials did not unlock (<see cref="CryptMethod.Locked"/>) is still ciphertext and is written as it
/// is, which only the document's own encryption allows.
/// </para>
/// </remarks>
internal sealed class ObjectEncryptor
{
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName EmbeddedFile = new("EmbeddedFile");

    private readonly CryptFilter _strings;
    private readonly CryptFilter _streams;
    private readonly CryptFilter _embeddedFiles;
    private readonly IReadOnlyDictionary<CosName, CryptFilter>? _named;
    private readonly bool _encryptMetadata;
    private readonly Func<CosObject?, CosObject> _resolve;

    private ObjectEncryptor(
        CryptFilter strings,
        CryptFilter streams,
        CryptFilter embeddedFiles,
        IReadOnlyDictionary<CosName, CryptFilter>? named,
        bool encryptMetadata,
        CosDictionary? encryptionDictionary,
        Func<CosObject?, CosObject> resolve)
    {
        _strings = strings;
        _streams = streams;
        _embeddedFiles = embeddedFiles;
        _named = named;
        _encryptMetadata = encryptMetadata;
        EncryptionDictionary = encryptionDictionary;
        _resolve = resolve;
    }

    /// <summary>Gets the encryption dictionary, which is written unencrypted and never in an object stream (§7.5.7); <see langword="null"/> when the output is not encrypted.</summary>
    public CosDictionary? EncryptionDictionary { get; }

    /// <summary>Encrypts with the key and crypt filters a document was opened with (an encrypted document re-saved as it is).</summary>
    /// <param name="decryptor">The document's decryptor.</param>
    /// <param name="encryptionDictionary">The document's encryption dictionary.</param>
    /// <param name="resolve">Resolves references.</param>
    /// <returns>The encryptor.</returns>
    public static ObjectEncryptor Keep(DocumentDecryptor decryptor, CosDictionary encryptionDictionary, Func<CosObject?, CosObject> resolve) =>
        new(decryptor.Strings, decryptor.Streams, decryptor.EmbeddedFiles, decryptor.Named, decryptor.EncryptMetadata, encryptionDictionary, resolve);

    /// <summary>Encrypts everything with one crypt filter, under a new encryption dictionary.</summary>
    /// <param name="filter">The crypt filter for strings, streams and embedded files.</param>
    /// <param name="encryptMetadata">Whether metadata streams are encrypted.</param>
    /// <param name="encryptionDictionary">The new encryption dictionary.</param>
    /// <param name="resolve">Resolves references.</param>
    /// <returns>The encryptor.</returns>
    public static ObjectEncryptor Create(CryptFilter filter, bool encryptMetadata, CosDictionary encryptionDictionary, Func<CosObject?, CosObject> resolve) =>
        new(filter, filter, filter, named: null, encryptMetadata, encryptionDictionary, resolve);

    /// <summary>Writes a decrypted document unencrypted: encrypts nothing, and drops <c>Crypt</c> filters (§7.4.10).</summary>
    /// <param name="resolve">Resolves references.</param>
    /// <returns>The encryptor.</returns>
    public static ObjectEncryptor Remove(Func<CosObject?, CosObject> resolve)
    {
        var identity = new CryptFilter(FilterNames.Identity, CryptMethod.Identity, []);
        return new(identity, identity, identity, named: null, encryptMetadata: true, encryptionDictionary: null, resolve);
    }

    /// <summary>Returns <paramref name="value"/> as it is written under <paramref name="id"/>: itself, or an encrypted copy.</summary>
    /// <param name="value">An indirect object's value.</param>
    /// <param name="id">The number and generation the object is written with (Algorithm 1 keys depend on them).</param>
    /// <returns>What to serialize.</returns>
    public CosObject Encrypt(CosObject value, CosReference id)
    {
        if (ReferenceEquals(value, EncryptionDictionary))
        {
            return value;
        }

        return value switch
        {
            CosString text => EncryptString(text, id),
            CosStream stream => EncryptStream(stream, id),
            CosDictionary dictionary => EncryptStrings(dictionary, id),
            CosArray array => EncryptStrings(array, id),
            _ => value,
        };
    }

    private static ReadOnlyMemory<byte> Apply(CryptFilter filter, ReadOnlySpan<byte> data, CosReference id, bool perObjectKey)
    {
        switch (filter.Method)
        {
            case CryptMethod.Rc4:
                {
                    byte[] output = new byte[data.Length];
                    Rc4.Transform(perObjectKey ? DocumentDecryptor.ObjectKey(filter.Key, id, aes: false) : filter.Key, data, output);
                    return output;
                }

            case CryptMethod.AesV2:
                return EncryptCbc(perObjectKey ? DocumentDecryptor.ObjectKey(filter.Key, id, aes: true) : filter.Key, data);
            case CryptMethod.AesV3:
                return EncryptCbc(filter.Key, data);
            case CryptMethod.AesV4:
                {
                    const int overhead = AesGcmCipher.NonceSize + AesGcmCipher.TagSize;
                    byte[] output = new byte[data.Length + overhead];
                    Span<byte> nonce = output.AsSpan(0, AesGcmCipher.NonceSize);
                    RandomNumberGenerator.Fill(nonce);
                    AesGcmCipher.Encrypt(filter.Key, nonce, data, output.AsSpan(AesGcmCipher.NonceSize, data.Length), output.AsSpan(AesGcmCipher.NonceSize + data.Length));
                    return output;
                }

            default:
                // Identity, and Locked data, which is still the ciphertext it was read as.
                return data.ToArray();
        }
    }

    /// <summary>AES-CBC with a random IV written first and PKCS#5 padding (§7.6.3.1, Algorithm 1 step e; Algorithm 1.A).</summary>
    private static byte[] EncryptCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        int pad = AesCipher.BlockSize - (data.Length % AesCipher.BlockSize);
        int body = data.Length + pad;
        byte[] output = new byte[AesCipher.BlockSize + body];
        Span<byte> iv = output.AsSpan(0, AesCipher.BlockSize);
        RandomNumberGenerator.Fill(iv);
        byte[] padded = ArrayPool<byte>.Shared.Rent(body);
        try
        {
            data.CopyTo(padded);
            padded.AsSpan(data.Length, pad).Fill((byte)pad);
            using AesCipher aes = AesCipher.Create(key);
            aes.EncryptCbc(padded.AsSpan(0, body), iv, output.AsSpan(AesCipher.BlockSize));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(padded);
        }

        return output;
    }

    private CosString EncryptString(CosString text, CosReference id) =>
        _strings.Method is CryptMethod.Identity or CryptMethod.Locked
            ? text
            : CosString.FromOwnedBytes(Apply(_strings, text.Bytes, id, perObjectKey: true).ToArray(), hexadecimal: true);

    private CosDictionary EncryptStrings(CosDictionary dictionary, CosReference id)
    {
        bool signature = DocumentDecryptor.IsSignature(dictionary);
        var copy = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            copy[entry.Key] = entry.Value switch
            {
                CosString text when !(signature && entry.Key.Equals(KnownNames.Contents)) => EncryptString(text, id),
                CosDictionary inner => EncryptStrings(inner, id),
                CosArray inner => EncryptStrings(inner, id),
                _ => entry.Value,
            };
        }

        return copy;
    }

    private CosArray EncryptStrings(CosArray array, CosReference id)
    {
        var copy = new CosArray();
        foreach (CosObject item in array)
        {
            copy.Add(item switch
            {
                CosString text => EncryptString(text, id),
                CosDictionary inner => EncryptStrings(inner, id),
                CosArray inner => EncryptStrings(inner, id),
                _ => item,
            });
        }

        return copy;
    }

    private CosStream EncryptStream(CosStream stream, CosReference id)
    {
        CosDictionary source = stream.Dictionary;
        if (DocumentDecryptor.IsType(source, KnownNames.XRef))
        {
            return stream; // §7.5.8.2: cross-reference streams are never encrypted.
        }

        CosDictionary dictionary = EncryptStrings(source, id);
        CryptFilter? filter;
        bool perObjectKey = true;
        bool named = DocumentDecryptor.TryReadCryptFilterName(source, _resolve, out CosName? name);
        if (named && _named is not null)
        {
            // The document's own encryption: the named crypt filter, its key used as is (§7.4.10), AES-128 with the object key as
            // qpdf writes it; a name CF does not define was not decrypted either, so its data is written as it was read.
            filter = name!.Equals(FilterNames.Identity) ? null : _named.GetValueOrDefault(name);
            perObjectKey = filter?.Method == CryptMethod.AesV2;
        }
        else
        {
            if (named)
            {
                RemoveCryptFilter(dictionary);
            }

            filter = DocumentDecryptor.IsType(source, Metadata) && !_encryptMetadata
                ? null
                : DocumentDecryptor.IsType(source, EmbeddedFile) ? _embeddedFiles : _streams;
        }

        ReadOnlyMemory<byte> data = stream.EncodedData;
        return new CosStream(dictionary, filter is null ? data : Apply(filter, data.Span, id, perObjectKey));
    }

    /// <summary>Drops a leading <c>Crypt</c> filter and its parameters (§7.4.10), and <c>Filter</c> and <c>DecodeParms</c> once empty.</summary>
    private void RemoveCryptFilter(CosDictionary dictionary)
    {
        CosObject filters = _resolve(dictionary.TryGetValue(FilterNames.Filter, out CosObject? filter) ? filter : null);
        if (filters is not CosArray { Count: > 1 } list)
        {
            dictionary.Remove(FilterNames.Filter);
            dictionary.Remove(FilterNames.DecodeParms);
            return;
        }

        dictionary[FilterNames.Filter] = new CosArray(list.Skip(1));
        if (_resolve(dictionary.TryGetValue(FilterNames.DecodeParms, out CosObject? entry) ? entry : null) is CosArray { Count: > 0 } parameters)
        {
            dictionary[FilterNames.DecodeParms] = new CosArray(parameters.Skip(1));
        }
        else
        {
            dictionary.Remove(FilterNames.DecodeParms);
        }
    }
}
