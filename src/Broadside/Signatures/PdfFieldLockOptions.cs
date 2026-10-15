using Broadside.Forms;

namespace Broadside.Signatures;

/// <summary>
/// The form fields a signature locks: once it is signed, a change to their values invalidates it. Written as the signature field's
/// lock dictionary and copied into the signature's FieldMDP transform parameters.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.5, Tables 235 and 236 (signature field lock dictionary, PDF 1.5; its <c>P</c>, PDF 2.0), and §12.8.2.4,
/// Table 259 (FieldMDP: "the Action and Fields entries in the transform parameters dictionary shall be copied from the corresponding
/// fields in the signature field lock dictionary").
/// </remarks>
public sealed class PdfFieldLockOptions
{
    private PdfFieldLockOptions(PdfSignatureFieldLockAction action, IReadOnlyList<string> fieldNames)
    {
        Action = action;
        FieldNames = fieldNames;
    }

    /// <summary>Gets which fields are locked: all of them, those <see cref="FieldNames"/> names, or all but those.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>Action</c>.</remarks>
    public PdfSignatureFieldLockAction Action { get; }

    /// <summary>
    /// Gets the fully qualified names of the fields included or excluded; a name locks the field and every field under it. Empty for
    /// <see cref="PdfSignatureFieldLockAction.All"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>Fields</c>; §12.7.4.2 (field names).</remarks>
    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>
    /// Gets the changes the document permits once the signature is signed, or <see langword="null"/> (the default) to leave them as
    /// they are. Permissions can only be narrowed: a value wider than those already in effect is ignored. PDF 2.0.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>P</c>.</remarks>
    public PdfCertificationPermissions? Permissions { get; private init; }

    /// <summary>Locks every field of the document.</summary>
    /// <returns>The lock.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>Action</c> <c>All</c>.</remarks>
    public static PdfFieldLockOptions All() => new(PdfSignatureFieldLockAction.All, []);

    /// <summary>Locks the fields <paramref name="fieldNames"/> names, and the fields under them.</summary>
    /// <param name="fieldNames">Fully qualified field names.</param>
    /// <returns>The lock.</returns>
    /// <exception cref="ArgumentException">A name is null or empty.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>Action</c> <c>Include</c>.</remarks>
    public static PdfFieldLockOptions Include(params IEnumerable<string> fieldNames) => new(PdfSignatureFieldLockAction.Include, Names(fieldNames));

    /// <summary>Locks every field except those <paramref name="fieldNames"/> names and the fields under them.</summary>
    /// <param name="fieldNames">Fully qualified field names.</param>
    /// <returns>The lock.</returns>
    /// <exception cref="ArgumentException">A name is null or empty.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>Action</c> <c>Exclude</c>.</remarks>
    public static PdfFieldLockOptions Exclude(params IEnumerable<string> fieldNames) => new(PdfSignatureFieldLockAction.Exclude, Names(fieldNames));

    /// <summary>Returns this lock with <see cref="Permissions"/> set.</summary>
    /// <param name="permissions">The changes permitted once signed, or <see langword="null"/> to leave them as they are.</param>
    /// <returns>A new lock.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236, <c>P</c> (PDF 2.0).</remarks>
    public PdfFieldLockOptions WithPermissions(PdfCertificationPermissions? permissions)
    {
        if (permissions is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(permissions), value, "The permissions shall be 1, 2 or 3 (Table 236).");
        }

        return new(Action, FieldNames) { Permissions = permissions };
    }

    private static string[] Names(IEnumerable<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        string[] names = [.. fieldNames];
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A locked field name is null or empty.", nameof(fieldNames));
            }
        }

        return names;
    }
}
