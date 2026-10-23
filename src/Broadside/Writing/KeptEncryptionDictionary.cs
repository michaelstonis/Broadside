using Broadside.Filters;
using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>
/// The encryption dictionary a complete file writes when it keeps the document's encryption and the document's dictionary holds a
/// reference: a new dictionary of direct values that reads as the document's did.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.6.2: the values of the entries of Table 20 shall not be encrypted, and the strings of the encryption dictionary
/// shall be direct objects. A reader resolves the entries before it can decrypt anything, so a reference in the dictionary that
/// reaches an object the save encrypts, or puts in an encrypted object stream (§7.5.7), makes the saved file unreadable (issue #390).
/// </para>
/// <para>
/// Each entry a security handler reads (Tables 20, 21, 25, 27; ISO/TS 32004 Table 2) is resolved once, as the reader resolves it,
/// and written when it has the type the reader takes; otherwise it is left out, which the reader reads the same way, except where
/// an invalid entry reads differently from an absent one: <c>Filter</c> becomes the handler that opened the document, <c>V</c> its
/// version, <c>Length</c> its key length and <c>EFF</c> <c>Identity</c>. A string through a reference is kept only where the handler
/// read it before decryption was installed (the standard handler's <c>O</c>, <c>U</c>, <c>OE</c>, <c>UE</c> and <c>Perms</c>, the
/// public-key handler's <c>Recipients</c>): any other may have been decrypted since, and the dictionary is written unencrypted. Any
/// other entry is kept only when it is direct and holds no reference: a reference may reach the document's content. Nothing is
/// followed past one reference, so the output is no larger than the entries the handler reads. A kept integrity MAC whose
/// <c>KDFSalt</c> is not a direct string refuses the save.
/// </para>
/// <para>
/// The new dictionary is written as a new object only the trailer's <c>Encrypt</c> refers to; the document's own dictionary stays an
/// ordinary object for whatever else refers to it (a damaged file's page tree, say), encrypted like any other.
/// </para>
/// </remarks>
internal static class KeptEncryptionDictionary
{
    private static readonly CosName V = new("V");
    private static readonly CosName AuthEvent = new("AuthEvent");
    private static readonly CosName Recipients = new("Recipients");
    private static readonly CosName KdfSalt = new("KDFSalt");
    private static readonly CosName None = new("None");
    private static readonly CosName[] Numbers = [KnownNames.R, KnownNames.P];
    private static readonly CosName[] Strings = [new("O"), new("U"), new("OE"), new("UE"), new("Perms")];

    /// <summary>
    /// Returns the dictionary a complete file writes for <paramref name="security"/>'s encryption dictionary, or
    /// <see langword="null"/> when that dictionary holds no reference and is written as it is.
    /// </summary>
    /// <param name="security">The document's security, as its handler authenticated it.</param>
    /// <param name="resolve">Resolves a reference once, as the reader does.</param>
    /// <param name="keepsIntegrity">Whether the save computes the document's integrity MAC again, which needs its <c>KDFSalt</c>.</param>
    /// <returns>The new dictionary, or <see langword="null"/>.</returns>
    /// <exception cref="NotSupportedException">The MAC is kept and the <c>KDFSalt</c> is not a direct string.</exception>
    public static CosDictionary? Rebuild(PdfSecurity security, Func<CosObject?, CosObject> resolve, bool keepsIntegrity)
    {
        CosDictionary source = security.EncryptionDictionary;
        if (!HoldsReference(source))
        {
            return null;
        }

        var written = new CosDictionary
        {
            [FilterNames.Filter] = resolve(Get(source, FilterNames.Filter)) as CosName ?? security.Handler,
            [V] = resolve(Get(source, V)) is CosInteger { Value: >= 0 and <= int.MaxValue } version ? version : new CosInteger(security.Version),
        };
        CopyName(source, written, KnownNames.SubFilter, resolve);
        foreach (CosName key in Numbers)
        {
            CopyNumber(source, written, key, resolve);
        }

        if (source.ContainsKey(KnownNames.Length) && !CopyNumber(source, written, KnownNames.Length, resolve) && security.KeyLength > 0)
        {
            // Present but not a number reads as the default 40 bits, without the 128-bit retry an absent one gets (Table 20).
            written[KnownNames.Length] = new CosInteger(security.KeyLength);
        }

        // A string an entry refers to is copied only when the handler read it while authenticating, before decryption was installed,
        // so the copy holds the bytes of the file; any other may have been decrypted since.
        bool standard = security.Handler.Equals(KnownNames.Standard);
        foreach (CosName key in Strings)
        {
            if (String(source, key, resolve, byReference: standard) is { } text)
            {
                written[key] = text;
            }
        }

        if (String(source, KdfSalt, resolve, byReference: false) is { } salt)
        {
            written[KdfSalt] = salt;
        }
        else if (keepsIntegrity && source.ContainsKey(KdfSalt))
        {
            throw new NotSupportedException(
                "The document's integrity MAC needs the KDFSalt of its encryption dictionary, which is not a direct string (ISO 32000-2 §7.6.2: the strings of the encryption dictionary shall be direct); save it with new encryption (PdfSaveOptions.Encryption) or without encryption (PdfSaveOptions.RemoveEncryption).");
        }

        CopyBoolean(source, written, KnownNames.EncryptMetadata, resolve);
        CopyName(source, written, KnownNames.StmF, resolve);
        CopyName(source, written, KnownNames.StrF, resolve);
        if (source.ContainsKey(KnownNames.EFF) && !CopyName(source, written, KnownNames.EFF, resolve))
        {
            // Present but not a name selects no crypt filter (Identity); absent, embedded files take StmF's.
            written[KnownNames.EFF] = FilterNames.Identity;
        }

        CopyRecipients(source, written, resolve, byReference: !standard);
        if (resolve(Get(source, KnownNames.CF)) is CosDictionary filters)
        {
            var copies = new CosDictionary();
            foreach (KeyValuePair<CosName, CosObject> filter in filters)
            {
                copies[filter.Key] = CryptFilter(resolve(filter.Value) as CosDictionary, resolve, byReference: !standard);
            }

            written[KnownNames.CF] = copies;
        }

        CopyOthers(source, written);
        return written;
    }

    /// <summary>A crypt filter dictionary (§7.6.6 Table 25, §7.6.5.2 Table 27); one that is not a dictionary reads as method None.</summary>
    private static CosDictionary CryptFilter(CosDictionary? source, Func<CosObject?, CosObject> resolve, bool byReference)
    {
        var written = new CosDictionary();
        if (source is null)
        {
            written[KnownNames.CFM] = None;
            return written;
        }

        CopyName(source, written, KnownNames.Type, resolve);
        CopyName(source, written, KnownNames.CFM, resolve);
        CopyName(source, written, AuthEvent, resolve);
        CopyNumber(source, written, KnownNames.Length, resolve);
        CopyBoolean(source, written, KnownNames.EncryptMetadata, resolve);
        CopyRecipients(source, written, resolve, byReference);
        CopyOthers(source, written);
        return written;
    }

    /// <summary>
    /// <c>Recipients</c>: a string, or an array whose strings are kept (Table 27; the reader skips what is not a string); through
    /// references only when <paramref name="byReference"/>.
    /// </summary>
    private static void CopyRecipients(CosDictionary source, CosDictionary written, Func<CosObject?, CosObject> resolve, bool byReference)
    {
        CosObject? entry = Get(source, Recipients);
        switch (entry is CosReference && byReference ? resolve(entry) : entry)
        {
            case CosString recipient:
                written[Recipients] = recipient;
                break;
            case CosArray recipients:
                written[Recipients] = new CosArray(recipients
                    .Select(item => item is CosReference && byReference ? resolve(item) : item)
                    .OfType<CosString>());
                break;
        }
    }

    /// <summary>A string entry: direct, or through one reference when <paramref name="byReference"/>.</summary>
    private static CosString? String(CosDictionary source, CosName key, Func<CosObject?, CosObject> resolve, bool byReference) =>
        Get(source, key) switch
        {
            CosString text => text,
            CosReference reference when byReference => resolve(reference) as CosString,
            _ => null,
        };

    /// <summary>Keeps the entries no handler of Broadside reads (a registered handler may) that are direct and hold no reference.</summary>
    private static void CopyOthers(CosDictionary source, CosDictionary written)
    {
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            if (!IsRead(entry.Key) && !HoldsReference(entry.Value))
            {
                written[entry.Key] = entry.Value;
            }
        }
    }

    private static bool IsRead(CosName key) =>
        key.Equals(FilterNames.Filter) || key.Equals(KnownNames.SubFilter) || key.Equals(V) || key.Equals(KnownNames.Length)
        || key.Equals(KnownNames.EncryptMetadata) || key.Equals(KnownNames.StmF) || key.Equals(KnownNames.StrF) || key.Equals(KnownNames.EFF)
        || key.Equals(KnownNames.CF) || key.Equals(KnownNames.Type) || key.Equals(KnownNames.CFM) || key.Equals(AuthEvent) || key.Equals(Recipients)
        || key.Equals(KdfSalt) || Array.IndexOf(Numbers, key) >= 0 || Array.IndexOf(Strings, key) >= 0;

    private static bool CopyNumber(CosDictionary source, CosDictionary written, CosName key, Func<CosObject?, CosObject> resolve) =>
        Copy(source, written, key, resolve, static value => value is CosInteger or CosReal);

    private static bool CopyName(CosDictionary source, CosDictionary written, CosName key, Func<CosObject?, CosObject> resolve) =>
        Copy(source, written, key, resolve, static value => value is CosName);

    private static bool CopyBoolean(CosDictionary source, CosDictionary written, CosName key, Func<CosObject?, CosObject> resolve) =>
        Copy(source, written, key, resolve, static value => value is CosBoolean);

    /// <summary>Copies the entry, resolved once, when it has the type the reader takes; names, numbers and booleans are never encrypted.</summary>
    private static bool Copy(CosDictionary source, CosDictionary written, CosName key, Func<CosObject?, CosObject> resolve, Func<CosObject, bool> accepts)
    {
        CosObject value = resolve(Get(source, key));
        if (!accepts(value))
        {
            return false;
        }

        written[key] = value;
        return true;
    }

    private static CosObject? Get(CosDictionary dictionary, CosName key) => dictionary.TryGetValue(key, out CosObject? value) ? value : null;

    /// <summary>Whether <paramref name="value"/> is a reference or holds one, at any depth of its direct dictionaries and arrays.</summary>
    private static bool HoldsReference(CosObject value)
    {
        var pending = new Stack<CosObject>();
        pending.Push(value);
        while (pending.TryPop(out CosObject? item))
        {
            switch (item)
            {
                case CosReference:
                    return true;
                case CosDictionary dictionary:
                    foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
                    {
                        pending.Push(entry.Value);
                    }

                    break;
                case CosArray array:
                    foreach (CosObject element in array)
                    {
                        pending.Push(element);
                    }

                    break;
            }
        }

        return false;
    }
}
