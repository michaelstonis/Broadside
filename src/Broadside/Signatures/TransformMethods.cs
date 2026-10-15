using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Signatures;

/// <summary>
/// The names and readers of signature references and their transform parameters (DocMDP and FieldMDP), and of signature field
/// lock dictionaries, shared by signing and modification detection.
/// </summary>
/// <remarks>ISO 32000-2 §12.8.1, Table 256; §12.8.2.2, Table 257; §12.8.2.4, Table 259; §12.7.5.5, Table 236; §12.8.6, Table 263.</remarks>
internal static class TransformMethods
{
    public static readonly CosName Reference = new("Reference");
    public static readonly CosName TransformMethod = new("TransformMethod");
    public static readonly CosName TransformParams = new("TransformParams");
    public static readonly CosName DocMdp = new("DocMDP");
    public static readonly CosName FieldMdp = new("FieldMDP");
    public static readonly CosName SigRef = new("SigRef");
    public static readonly CosName SigFieldLock = new("SigFieldLock");
    public static readonly CosName Perms = new("Perms");
    public static readonly CosName P = new("P");
    public static readonly CosName V = new("V");
    public static readonly CosName Data = new("Data");
    public static readonly CosName DigestMethod = new("DigestMethod");
    public static readonly CosName Version12 = new("1.2");
    public static readonly CosName All = new("All");
    public static readonly CosName Include = new("Include");
    public static readonly CosName Exclude = new("Exclude");

    /// <summary>
    /// The permissions of the signature's DocMDP signature reference (Table 257, <c>P</c>, default 2), or <see langword="null"/> when
    /// it has none. A <c>P</c> other than 1, 2 or 3 reads as 1: the most restrictive reading of an author's unreadable intent.
    /// </summary>
    public static PdfCertificationPermissions? DocMdpPermissions(CosDictionary signature, Func<CosObject?, CosObject> resolve)
    {
        if (FindReference(signature, DocMdp, resolve) is not { } reference)
        {
            return null;
        }

        return resolve(reference.GetValueOrDefault(TransformParams)) is CosDictionary parameters
            && parameters.TryGetValue(P, out CosObject? value)
            ? resolve(value) is CosInteger { Value: >= 1 and <= 3 } integer ? (PdfCertificationPermissions)integer.Value : PdfCertificationPermissions.NoChanges
            : PdfCertificationPermissions.FormFilling;
    }

    /// <summary>The fields the signature's FieldMDP signature reference locks (Table 259), or <see langword="null"/> when it has none.</summary>
    public static FieldLock? FieldMdpLock(CosDictionary signature, Func<CosObject?, CosObject> resolve) =>
        FindReference(signature, FieldMdp, resolve) is { } reference && resolve(reference.GetValueOrDefault(TransformParams)) is CosDictionary parameters
            ? ReadLock(parameters, resolve)
            : null;

    /// <summary>
    /// The fields a signature field lock dictionary (Table 236) or FieldMDP transform parameters dictionary (Table 259) lock, or
    /// <see langword="null"/> when its <c>Action</c> is missing or unknown.
    /// </summary>
    public static FieldLock? ReadLock(CosDictionary dictionary, Func<CosObject?, CosObject> resolve)
    {
        PdfSignatureFieldLockAction action = (resolve(dictionary.GetValueOrDefault(FormNames.Action)) as CosName)?.Value switch
        {
            "All" => PdfSignatureFieldLockAction.All,
            "Include" => PdfSignatureFieldLockAction.Include,
            "Exclude" => PdfSignatureFieldLockAction.Exclude,
            _ => PdfSignatureFieldLockAction.Unknown,
        };
        if (action == PdfSignatureFieldLockAction.Unknown)
        {
            return null;
        }

        string[] names = resolve(dictionary.GetValueOrDefault(FormNames.Fields)) is CosArray fields
            ? [.. fields.Select(resolve).OfType<CosString>().Select(static name => name.DecodeText())]
            : [];
        PdfCertificationPermissions? permissions = resolve(dictionary.GetValueOrDefault(P)) is CosInteger { Value: >= 1 and <= 3 } integer
            ? (PdfCertificationPermissions)integer.Value
            : null;
        return new FieldLock(action, names, permissions);
    }

    /// <summary>The name a lock action is written as (Table 236).</summary>
    public static CosName ActionName(PdfSignatureFieldLockAction action) => action switch
    {
        PdfSignatureFieldLockAction.All => All,
        PdfSignatureFieldLockAction.Include => Include,
        PdfSignatureFieldLockAction.Exclude => Exclude,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "A lock's action is All, Include or Exclude."),
    };

    /// <summary>Whether the signature dictionary is a document timestamp's (§12.8.5: <c>Type</c> <c>DocTimeStamp</c>, <c>SubFilter</c> <c>ETSI.RFC3161</c>).</summary>
    public static bool IsDocumentTimestamp(CosDictionary signature, Func<CosObject?, CosObject> resolve) =>
        resolve(signature.GetValueOrDefault(KnownNames.Type)) is CosName { Value: "DocTimeStamp" }
        || resolve(signature.GetValueOrDefault(KnownNames.SubFilter)) is CosName { Value: "ETSI.RFC3161" };

    private static CosDictionary? FindReference(CosDictionary signature, CosName method, Func<CosObject?, CosObject> resolve)
    {
        if (resolve(signature.GetValueOrDefault(Reference)) is not CosArray references)
        {
            return null;
        }

        foreach (CosObject element in references)
        {
            if (resolve(element) is CosDictionary reference && method.Equals(resolve(reference.GetValueOrDefault(TransformMethod))))
            {
                return reference;
            }
        }

        return null;
    }
}

/// <summary>The fields a signature locks (Table 236 or Table 259), and the permissions its lock grants (Table 236, <c>P</c>).</summary>
/// <param name="Action">All, Include or Exclude.</param>
/// <param name="Names">The fully qualified names included or excluded.</param>
/// <param name="Permissions">The lock's <c>P</c>, or <see langword="null"/>.</param>
internal sealed record FieldLock(PdfSignatureFieldLockAction Action, IReadOnlyList<string> Names, PdfCertificationPermissions? Permissions)
{
    /// <summary>Whether the field named <paramref name="fieldName"/> is locked: a name locks the field and every field under it.</summary>
    public bool Locks(string fieldName)
    {
        bool named = Names.Any(name => fieldName == name || (fieldName.Length > name.Length && fieldName.StartsWith(name, StringComparison.Ordinal) && fieldName[name.Length] == '.'));
        return Action switch
        {
            PdfSignatureFieldLockAction.All => true,
            PdfSignatureFieldLockAction.Include => named,
            PdfSignatureFieldLockAction.Exclude => !named,
            _ => false,
        };
    }
}
