using System.Buffers;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security.Cryptography;

namespace Broadside.Security;

/// <summary>How a crypt filter decrypts (§7.6.6, Table 25; ISO/TS 32003 Table 4).</summary>
internal enum CryptMethod
{
    /// <summary>No decryption (the <c>Identity</c> crypt filter, Table 26).</summary>
    Identity,

    /// <summary>RC4 with a per-object key (Algorithm 1; <c>V</c> 1-3, CFM <c>V2</c>).</summary>
    Rc4,

    /// <summary>AES-128-CBC with a per-object key (Algorithm 1 with "sAlT"; CFM <c>AESV2</c>).</summary>
    AesV2,

    /// <summary>AES-256-CBC with the file key (Algorithm 1.A; CFM <c>AESV3</c>).</summary>
    AesV3,

    /// <summary>AES-256-GCM with the file key (ISO/TS 32003 §5.2; CFM <c>AESV4</c>).</summary>
    AesV4,

    /// <summary>
    /// A crypt filter the credentials do not unlock: a public-key filter without the reader among its recipients (§7.6.6), or an
    /// <c>AuthEvent /EFOpen</c> filter when the document opened without the user password (§7.6.5, Table 25). Its data stays
    /// encrypted.
    /// </summary>
    Locked,
}

/// <summary>A crypt filter of the document: its method and key.</summary>
/// <param name="Name">The name it has in <c>CF</c>, or <c>Identity</c>, or the name the document's V 1-3 encryption is given.</param>
/// <param name="Method">How it decrypts.</param>
/// <param name="Key">The key: the file encryption key, or the filter's own.</param>
internal sealed record CryptFilter(CosName Name, CryptMethod Method, byte[] Key);

/// <summary>
/// Decrypts the strings and streams of one encrypted document as objects load (hook 2 of the object loader) and serves the
/// <c>Crypt</c> filter of the stream pipeline.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.2 (what is encrypted), §7.6.3 (Algorithms 1 and 1.A), §7.6.6 (crypt filters), §7.4.10 (the <c>Crypt</c> filter);
/// ISO/TS 32003 §5.2 (AES-GCM). Every string is decrypted with <c>StrF</c> and every stream with <c>StmF</c>, an embedded file with
/// <c>EFF</c> (§7.6.2 Table 20), except: strings in the encryption dictionary (it loads before this hook is set), the trailer's
/// <c>ID</c> and <c>AuthCode</c> (the trailer never loads through the loader), strings inside streams and object-stream members (the
/// container is decrypted as a whole; members never reach this hook), cross-reference streams, the <c>Contents</c> string of a
/// signature dictionary, and metadata streams when <c>EncryptMetadata</c> is false.
/// </para>
/// <para>
/// A stream whose <c>Filter</c> starts with <c>Crypt</c> is decrypted here too, with the crypt filter its <c>DecodeParms</c> name
/// (default <c>Identity</c>) and that filter's key used as is (§7.4.10), not with <c>StmF</c>; the pipeline's <c>Crypt</c> stage then
/// passes the already decrypted data through. So <see cref="CosStream.EncodedData"/> of every loaded stream holds plaintext,
/// decrypted when it is first read and kept (issue #45: loading a stream object neither reads nor decrypts its data). For an AES-128
/// crypt filter whose data does not decrypt with the key as is, Algorithm 1's per-object key (what Table 25 describes for
/// <c>AESV2</c>, and what qpdf uses) is tried before the data is reported damaged.
/// </para>
/// <para>
/// Damaged ciphertext is repaired the way pdf.js does, with a diagnostic: AES data too short for an IV and a block decrypts to nothing,
/// a trailing partial block is dropped, an invalid PKCS#5 pad is kept; AES-GCM data that fails authentication decrypts to nothing.
/// Decryption never marks anything dirty (ADR 0004). Thread-safe: the key material is immutable and every call has its own cipher.
/// </para>
/// </remarks>
internal sealed class DocumentDecryptor : IObjectDecryptor, ICryptFilterHandler
{
    private static readonly CosName None = new("None");
    private static readonly CosName V2 = new("V2");
    private static readonly CosName AESV4 = new("AESV4");
    private static readonly CosName Metadata = new("Metadata");
    private static readonly CosName EmbeddedFile = new("EmbeddedFile");
    private static readonly CosName Sig = new("Sig");
    private static readonly CosName DocTimeStamp = new("DocTimeStamp");
    private static readonly CosName StandardFilterName = new("StdCF");

    private readonly CryptFilter _strings;
    private readonly CryptFilter _streams;
    private readonly CryptFilter _embeddedFiles;
    private readonly Dictionary<CosName, CryptFilter> _named;
    private readonly DiagnosticSink _diagnostics;
    private readonly Func<CosObject?, CosObject> _resolve;

    private DocumentDecryptor(
        CryptFilter strings,
        CryptFilter streams,
        CryptFilter embeddedFiles,
        Dictionary<CosName, CryptFilter> named,
        bool encryptMetadata,
        DiagnosticSink diagnostics,
        Func<CosObject?, CosObject> resolve)
    {
        _strings = strings;
        _streams = streams;
        _embeddedFiles = embeddedFiles;
        _named = named;
        EncryptMetadata = encryptMetadata;
        _diagnostics = diagnostics;
        _resolve = resolve;
    }

    /// <summary>Gets a value indicating whether metadata streams are encrypted (Table 21).</summary>
    public bool EncryptMetadata { get; }

    /// <summary>Gets the crypt filter strings are decrypted with.</summary>
    public CryptFilter Strings => _strings;

    /// <summary>Gets the crypt filter streams are decrypted with by default.</summary>
    public CryptFilter Streams => _streams;

    /// <summary>Reads the crypt filters of an encryption dictionary (Table 20, §7.6.6) for the key a handler returned.</summary>
    /// <param name="encryption">The encryption dictionary.</param>
    /// <param name="version">Its <c>V</c>.</param>
    /// <param name="result">
    /// What the handler returned; <see langword="null"/> for a document opened without authentication (only its <c>AuthEvent
    /// /EFOpen</c> crypt filters are encrypted, <see cref="DocumentSecurity"/>): every crypt filter of <c>CF</c> is then locked.
    /// </param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="resolve">Resolves references without decryption.</param>
    /// <returns>The decryptor.</returns>
    /// <exception cref="PdfEncryptionNotSupportedException">A method the document uses for its strings or streams is unknown.</exception>
    public static DocumentDecryptor Create(
        CosDictionary encryption,
        int version,
        SecurityHandlerResult? result,
        DiagnosticSink diagnostics,
        Func<CosObject?, CosObject> resolve)
    {
        byte[] fileKey = result?.FileEncryptionKey.ToArray() ?? [];
        var identity = new CryptFilter(FilterNames.Identity, CryptMethod.Identity, []);
        switch (version)
        {
            case 1 or 2 or 3:
                {
                    if (version == 3)
                    {
                        diagnostics.Report(
                            DiagnosticCodes.EncryptionVersionInvalid,
                            DiagnosticSeverity.Warning,
                            "V 3 (an unpublished algorithm) shall not appear in a conforming file; it is read as V 2, RC4 with the Length key.");
                    }

                    var rc4 = new CryptFilter(StandardFilterName, CryptMethod.Rc4, fileKey);
                    return new DocumentDecryptor(rc4, rc4, rc4, [], encryptMetadata: true, diagnostics, resolve);
                }

            case 4 or 5 or 6:
                {
                    var named = new Dictionary<CosName, CryptFilter>();
                    var unsupported = new Dictionary<CosName, string>();
                    if (resolve(encryption.TryGetValue(KnownNames.CF, out CosObject? cf) ? cf : null) is CosDictionary filters)
                    {
                        for (int index = 0; index < filters.Count; index++)
                        {
                            (CosName name, CosObject entry) = filters.GetAt(index);
                            if (name.Equals(FilterNames.Identity))
                            {
                                continue; // Table 20: standard crypt filter names in CF shall be ignored.
                            }

                            byte[] key = result?.CryptFilterKeys is { } keys && keys.TryGetValue(name, out ReadOnlyMemory<byte> own) ? own.ToArray() : fileKey;
                            if (key.Length == 0)
                            {
                                // Not authorized (§7.6.6) or not authenticated (Table 25 EFOpen): what names it stays encrypted.
                                named[name] = new CryptFilter(name, CryptMethod.Locked, []);
                                continue;
                            }

                            if (ReadFilter(name, resolve(entry) as CosDictionary, version, key, diagnostics, resolve, out string? problem) is { } filter)
                            {
                                named[name] = filter;
                            }
                            else
                            {
                                unsupported[name] = problem!;
                            }
                        }
                    }

                    CryptFilter Select(CosName key, CosName? fallback)
                    {
                        CosName? name = resolve(encryption.TryGetValue(key, out CosObject? entry) ? entry : null) as CosName ?? fallback;
                        if (name is null || name.Equals(FilterNames.Identity))
                        {
                            return identity;
                        }

                        if (named.TryGetValue(name, out CryptFilter? filter))
                        {
                            return filter;
                        }

                        if (unsupported.TryGetValue(name, out string? problem))
                        {
                            throw new PdfEncryptionNotSupportedException(PdfEncryptionNotSupportedReason.Algorithm, problem);
                        }

                        diagnostics.Report(
                            DiagnosticCodes.CryptFilterMissing,
                            DiagnosticSeverity.Error,
                            $"{key.Value} names the crypt filter /{name.Value}, which the CF dictionary does not define; that data is left as it is.");
                        return identity;
                    }

                    CryptFilter streams = Select(KnownNames.StmF, null);
                    CryptFilter strings = Select(KnownNames.StrF, null);
                    CryptFilter embedded = encryption.ContainsKey(KnownNames.EFF) ? Select(KnownNames.EFF, null) : streams;
                    // Table 21 puts EncryptMetadata in the encryption dictionary; Table 27 in the crypt filter StmF names (public-key handlers).
                    CosObject flag = resolve(encryption.TryGetValue(KnownNames.EncryptMetadata, out CosObject? flagEntry) ? flagEntry : null);
                    if (flag is CosNull
                        && resolve(encryption.TryGetValue(KnownNames.StmF, out CosObject? stmF) ? stmF : null) is CosName streamFilter
                        && resolve(cf) is CosDictionary definitions
                        && resolve(definitions.TryGetValue(streamFilter, out CosObject? definition) ? definition : null) is CosDictionary streamFilterDictionary)
                    {
                        flag = resolve(streamFilterDictionary.TryGetValue(KnownNames.EncryptMetadata, out CosObject? filterFlag) ? filterFlag : null);
                    }

                    bool encryptMetadata = flag is not CosBoolean { Value: false };
                    return new DocumentDecryptor(strings, streams, embedded, named, encryptMetadata, diagnostics, resolve);
                }

            default:
                throw new PdfEncryptionNotSupportedException(
                    PdfEncryptionNotSupportedReason.Algorithm,
                    string.Create(CultureInfo.InvariantCulture, $"The encryption algorithm V {version} is not defined."));
        }
    }

    /// <inheritdoc/>
    public CosObject Decrypt(CosObject value, in ObjectLoadContext context)
    {
        if (context.Origin != ObjectOrigin.FileBody)
        {
            return value;
        }

        CosReference id = context.Reference;
        switch (value)
        {
            case CosString text:
                return DecryptString(text, id);
            case CosStream stream:
                DecryptStream(stream, id);
                return stream;
            case CosDictionary dictionary:
                DecryptStrings(dictionary, id);
                return dictionary;
            case CosArray array:
                DecryptStrings(array, id);
                return array;
            default:
                return value;
        }
    }

    /// <inheritdoc/>
    public bool IsLocked(CosDictionary streamDictionary) => SelectStreamFilter(streamDictionary, out _)?.Method == CryptMethod.Locked;

    /// <inheritdoc/>
    /// <remarks>The loader decrypted the stream already (see the class remarks); a known crypt filter passes the data through.</remarks>
    public bool TryDecrypt(CosName cryptFilterName, ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context)
    {
        if (!cryptFilterName.Equals(FilterNames.Identity) && !_named.ContainsKey(cryptFilterName))
        {
            return false;
        }

        output.Write(data);
        return true;
    }

    /// <summary>Decrypts data with a crypt filter, outside any object (tests, fuzzing, and the integrity check's own needs).</summary>
    /// <param name="filter">The crypt filter.</param>
    /// <param name="data">The encrypted data.</param>
    /// <param name="id">The object the data belongs to.</param>
    /// <returns>The plaintext.</returns>
    public ReadOnlyMemory<byte> Apply(CryptFilter filter, ReadOnlySpan<byte> data, CosReference id) => Apply(filter, data, id, perObjectKey: true);

    private static CryptFilter? ReadFilter(
        CosName name,
        CosDictionary? dictionary,
        int version,
        byte[] key,
        DiagnosticSink diagnostics,
        Func<CosObject?, CosObject> resolve,
        out string? problem)
    {
        problem = null;
        CosName method = resolve(dictionary?.TryGetValue(KnownNames.CFM, out CosObject? entry) == true ? entry : null) as CosName ?? None;
        if (method.Equals(None))
        {
            diagnostics.Report(
                DiagnosticCodes.CryptFilterMethodInvalid,
                DiagnosticSeverity.Warning,
                $"The crypt filter /{name.Value} has the method None, which leaves decryption to the security handler; this handler has no method of its own, so the data is left as it is.");
            return new CryptFilter(name, CryptMethod.Identity, []);
        }

        if (method.Equals(V2))
        {
            return new CryptFilter(name, CryptMethod.Rc4, key);
        }

        if (method.Equals(KnownNames.AESV2))
        {
            if (version >= 5)
            {
                diagnostics.Report(
                    DiagnosticCodes.CryptFilterMethodInvalid,
                    DiagnosticSeverity.Warning,
                    $"The crypt filter /{name.Value} uses AESV2 with a 256-bit file key (V {version}); it is read as AESV3, as other readers do.");
                return Aes256(name, CryptMethod.AesV3, key, out problem);
            }

            return new CryptFilter(name, CryptMethod.AesV2, Fit(name, key, 16, diagnostics));
        }

        if (method.Equals(KnownNames.AESV3))
        {
            return Aes256(name, CryptMethod.AesV3, key, out problem);
        }

        if (method.Equals(AESV4))
        {
            if (version < 6)
            {
                diagnostics.Report(
                    DiagnosticCodes.CryptFilterMethodInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The crypt filter /{name.Value} uses AESV4, which goes with V 6 (ISO/TS 32003); the dictionary has V {version}. It is read as AES-GCM."));
            }

            return Aes256(name, CryptMethod.AesV4, key, out problem);
        }

        problem = $"The crypt filter /{name.Value} uses the method /{method.Value}, which this engine does not support (§7.6.6, Table 25).";
        return null;
    }

    private static CryptFilter? Aes256(CosName name, CryptMethod method, byte[] key, out string? problem)
    {
        if (key.Length != 32)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"The crypt filter /{name.Value} needs a 256-bit key; the security handler's key has {key.Length * 8} bits.");
            return null;
        }

        problem = null;
        return new CryptFilter(name, method, key);
    }

    private static byte[] Fit(CosName name, byte[] key, int length, DiagnosticSink diagnostics)
    {
        if (key.Length == length)
        {
            return key;
        }

        diagnostics.Report(
            DiagnosticCodes.EncryptionKeyLengthInvalid,
            DiagnosticSeverity.Warning,
            string.Create(CultureInfo.InvariantCulture, $"The crypt filter /{name.Value} needs a {length * 8}-bit key; the {key.Length * 8}-bit key is {(key.Length < length ? "padded with zeros" : "truncated")}."));
        byte[] fitted = new byte[length];
        key.AsSpan(0, Math.Min(length, key.Length)).CopyTo(fitted);
        return fitted;
    }

    private static bool IsType(CosDictionary dictionary, CosName type) =>
        dictionary.TryGetValue(KnownNames.Type, out CosObject? value) && type.Equals(value);

    /// <summary>A signature dictionary's <c>Contents</c> is never encrypted (§7.6.2): Type Sig or DocTimeStamp, or Contents with ByteRange.</summary>
    private static bool IsSignature(CosDictionary dictionary) =>
        IsType(dictionary, Sig) || IsType(dictionary, DocTimeStamp)
        || (dictionary.TryGetValue(KnownNames.Contents, out CosObject? contents) && contents is CosString && dictionary.TryGetValue(KnownNames.ByteRange, out CosObject? range) && range is CosArray);

    private CosString DecryptString(CosString text, CosReference id)
    {
        if (_strings.Method == CryptMethod.Identity)
        {
            return text;
        }

        if (_strings.Method == CryptMethod.Locked)
        {
            ReportLocked(_strings, id);
            return text;
        }

        ReadOnlyMemory<byte> plain = Apply(_strings, text.Bytes, id, perObjectKey: true);
        return CosString.FromOwnedBytes(plain.ToArray(), text.IsHexadecimal);
    }

    private void DecryptStrings(CosDictionary dictionary, CosReference id)
    {
        bool signature = IsSignature(dictionary);
        for (int index = 0; index < dictionary.Count; index++)
        {
            (CosName key, CosObject value) = dictionary.GetAt(index);
            switch (value)
            {
                case CosString text when !(signature && key.Equals(KnownNames.Contents)):
                    dictionary.ReplaceLoaded(index, DecryptString(text, id));
                    break;
                case CosDictionary inner:
                    DecryptStrings(inner, id);
                    break;
                case CosArray inner:
                    DecryptStrings(inner, id);
                    break;
                default:
                    break;
            }
        }
    }

    private void DecryptStrings(CosArray array, CosReference id)
    {
        for (int index = 0; index < array.Count; index++)
        {
            switch (array[index])
            {
                case CosString text:
                    array.ReplaceLoaded(index, DecryptString(text, id));
                    break;
                case CosDictionary inner:
                    DecryptStrings(inner, id);
                    break;
                case CosArray inner:
                    DecryptStrings(inner, id);
                    break;
                default:
                    break;
            }
        }
    }

    private void DecryptStream(CosStream stream, CosReference id)
    {
        CosDictionary dictionary = stream.Dictionary;
        if (IsType(dictionary, KnownNames.XRef))
        {
            return; // §7.5.8.2: cross-reference streams are never encrypted, nor their dictionaries' strings.
        }

        DecryptStrings(dictionary, id);

        // Identity, or an unknown crypt filter (the pipeline reports CryptFilterUnsupported when the stream is decoded).
        if (SelectStreamFilter(dictionary, out bool perObjectKey) is not { } filter || filter.Method == CryptMethod.Identity || stream.EncodedLength == 0)
        {
            return;
        }

        if (filter.Method == CryptMethod.Locked)
        {
            ReportLocked(filter, id);
            return;
        }

        // Decrypted when the data is first read, then kept (#45: loading a stream does not read or decrypt its data).
        bool metadata = IsType(dictionary, Metadata);
        stream.TransformLoadedData(data => DecryptStreamData(filter, data, id, perObjectKey, metadata));
    }

    private ReadOnlyMemory<byte> DecryptStreamData(CryptFilter filter, ReadOnlyMemory<byte> data, CosReference id, bool perObjectKey, bool metadata)
    {
        if (metadata && data.Span.StartsWith("<?xpacket "u8))
        {
            _diagnostics.Report(
                DiagnosticCodes.MetadataNotEncrypted,
                DiagnosticSeverity.Warning,
                "The metadata stream is plaintext XMP although EncryptMetadata is true; it is read as it is.",
                objectReference: id);
            return data;
        }

        return Apply(filter, data.Span, id, perObjectKey);
    }

    /// <summary>
    /// The crypt filter a stream's data is decrypted with: the one its <c>Crypt</c> filter names (its key used as is), else
    /// <c>EFF</c> for an embedded file and <c>StmF</c> for any other stream; <see langword="null"/> for a cross-reference stream, an
    /// unencrypted metadata stream or an unknown crypt filter.
    /// </summary>
    private CryptFilter? SelectStreamFilter(CosDictionary dictionary, out bool perObjectKey)
    {
        perObjectKey = true;
        if (IsType(dictionary, KnownNames.XRef))
        {
            return null;
        }

        if (TryReadCryptFilterName(dictionary, out CosName? cryptFilterName))
        {
            perObjectKey = false; // §7.4.10: the crypt filter's key is used as is.
            return cryptFilterName.Equals(FilterNames.Identity) ? null : _named.GetValueOrDefault(cryptFilterName);
        }

        if (IsType(dictionary, Metadata) && !EncryptMetadata)
        {
            return null;
        }

        return IsType(dictionary, EmbeddedFile) ? _embeddedFiles : _streams;
    }

    /// <summary>Records, once per object, that data encrypted with a locked crypt filter stays encrypted.</summary>
    private void ReportLocked(CryptFilter filter, CosReference id) =>
        _diagnostics.ReportOnce(
            DiagnosticCodes.CryptFilterNotAuthorized,
            DiagnosticSeverity.Information,
            $"The data is encrypted with the crypt filter /{filter.Name.Value}, which the credentials given do not unlock: a public-key filter that does not name the reader (§7.6.6), or one whose AuthEvent is EFOpen, which needs the user password when an embedded file is accessed (Table 25); it stays encrypted.",
            objectReference: id);

    /// <summary>Reads the crypt filter a stream names when its first filter is <c>Crypt</c> (§7.4.10, Table 14; default Identity).</summary>
    private bool TryReadCryptFilterName(CosDictionary dictionary, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CosName? name)
    {
        name = null;
        CosObject filters = _resolve(dictionary.TryGetValue(FilterNames.Filter, out CosObject? filter) ? filter : null);
        bool first = filters switch
        {
            CosName single => single.Equals(FilterNames.Crypt),
            CosArray { Count: > 0 } array => FilterNames.Crypt.Equals(_resolve(array[0])),
            _ => false,
        };
        if (!first)
        {
            return false;
        }

        CosObject parameters = _resolve(dictionary.TryGetValue(FilterNames.DecodeParms, out CosObject? entry) ? entry : null);
        if (parameters is CosArray { Count: > 0 } list)
        {
            parameters = _resolve(list[0]);
        }

        name = parameters is CosDictionary decodeParms && _resolve(decodeParms.TryGetValue(FilterNames.Name, out CosObject? value) ? value : null) is CosName named
            ? named
            : FilterNames.Identity;
        return true;
    }

    private ReadOnlyMemory<byte> Apply(CryptFilter filter, ReadOnlySpan<byte> data, CosReference id, bool perObjectKey)
    {
        switch (filter.Method)
        {
            case CryptMethod.Identity:
                return data.ToArray();
            case CryptMethod.Rc4:
                {
                    byte[] output = new byte[data.Length];
                    Rc4.Transform(perObjectKey ? ObjectKey(filter.Key, id, aes: false) : filter.Key, data, output);
                    return output;
                }

            case CryptMethod.AesV2:
                {
                    byte[] key = perObjectKey ? ObjectKey(filter.Key, id, aes: true) : filter.Key;
                    ReadOnlyMemory<byte> plain = DecryptCbc(key, data, out string? problem);
                    if (problem is not null && !perObjectKey)
                    {
                        ReadOnlyMemory<byte> salted = DecryptCbc(ObjectKey(filter.Key, id, aes: true), data, out string? saltedProblem);
                        if (saltedProblem is null)
                        {
                            return salted;
                        }
                    }

                    Report(problem, id);
                    return plain;
                }

            case CryptMethod.AesV3:
                {
                    ReadOnlyMemory<byte> plain = DecryptCbc(filter.Key, data, out string? problem);
                    Report(problem, id);
                    return plain;
                }

            default:
                return DecryptGcm(filter.Key, data, id);
        }
    }

    /// <summary>Algorithm 1 steps b-d: MD5 of the key, the low 3 bytes of the object number, the low 2 of the generation, "sAlT" for AES.</summary>
    private static byte[] ObjectKey(byte[] fileKey, CosReference id, bool aes)
    {
        Span<byte> input = stackalloc byte[fileKey.Length + 9];
        fileKey.CopyTo(input);
        int offset = fileKey.Length;
        input[offset] = (byte)id.ObjectNumber;
        input[offset + 1] = (byte)(id.ObjectNumber >> 8);
        input[offset + 2] = (byte)(id.ObjectNumber >> 16);
        input[offset + 3] = (byte)id.Generation;
        input[offset + 4] = (byte)(id.Generation >> 8);
        int length = offset + 5;
        if (aes)
        {
            "sAlT"u8.CopyTo(input[length..]);
            length += 4;
        }

        Span<byte> digest = stackalloc byte[Md5.HashSize];
        Md5.HashData(input[..length], digest);
        return digest[..Math.Min(fileKey.Length + 5, 16)].ToArray();
    }

    /// <summary>AES-CBC with the IV in the first 16 bytes and PKCS#5 padding (§7.6.3.1), repaired the way pdf.js repairs it.</summary>
    private static ReadOnlyMemory<byte> DecryptCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, out string? problem)
    {
        problem = null;
        if (data.Length < 32)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"AES-encrypted data shall hold a 16-byte IV and at least one 16-byte block; {data.Length} bytes decrypt to nothing.");
            return ReadOnlyMemory<byte>.Empty;
        }

        int body = (data.Length - 16) & ~15;
        if (body != data.Length - 16)
        {
            problem = "AES-encrypted data is not a whole number of 16-byte blocks; the partial block at the end is dropped.";
        }

        byte[] plain = new byte[body];
        using (AesCipher aes = AesCipher.Create(key))
        {
            aes.DecryptCbc(data.Slice(16, body), data[..16], plain);
        }

        int pad = plain[^1];
        bool valid = pad is >= 1 and <= 16;
        for (int i = 1; valid && i <= pad; i++)
        {
            valid = plain[^i] == pad;
        }

        if (!valid)
        {
            problem ??= "AES-decrypted data does not end with a valid PKCS#5 pad; it is kept whole.";
            return plain;
        }

        return plain.AsMemory(0, body - pad);
    }

    private ReadOnlyMemory<byte> DecryptGcm(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data, CosReference id)
    {
        const int overhead = AesGcmCipher.NonceSize + AesGcmCipher.TagSize;
        if (data.Length < overhead)
        {
            Report(string.Create(CultureInfo.InvariantCulture, $"AES-GCM data shall hold a 12-byte IV and a 16-byte tag; {data.Length} bytes decrypt to nothing."), id);
            return ReadOnlyMemory<byte>.Empty;
        }

        byte[] plain = new byte[data.Length - overhead];
        if (!AesGcmCipher.TryDecrypt(key, data[..AesGcmCipher.NonceSize], data[AesGcmCipher.NonceSize..^AesGcmCipher.TagSize], data[^AesGcmCipher.TagSize..], plain))
        {
            _diagnostics.Report(
                DiagnosticCodes.EncryptedDataAuthenticationFailed,
                DiagnosticSeverity.Error,
                "The AES-GCM authentication tag does not match: the data was changed or damaged after it was encrypted; it decrypts to nothing.",
                objectReference: id);
            return ReadOnlyMemory<byte>.Empty;
        }

        return plain;
    }

    private void Report(string? problem, CosReference id)
    {
        if (problem is not null)
        {
            _diagnostics.Report(DiagnosticCodes.EncryptedDataInvalid, DiagnosticSeverity.Warning, problem, objectReference: id);
        }
    }
}
