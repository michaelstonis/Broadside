using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>A structure namespace: a namespace dictionary of the structure tree, or one of the namespaces the standard defines.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.4 (Table 356) and §14.8.6. A structure element without an <c>NS</c> entry is in the default namespace,
/// <see cref="Pdf17"/>, even in a PDF 2.0 file (§14.8.6.1). Two namespaces are equal when they have the same namespace name: a
/// namespace dictionary naming <c>http://iso.org/pdf2/ssn</c> equals <see cref="Pdf20"/>, whatever object it is (§14.7.4.2).
/// </para>
/// <para>
/// A view over its dictionary (ADR 0004): <see cref="Name"/>, <see cref="RoleMap"/> and <see cref="Schema"/> read it on every call.
/// The <c>Schema</c> file specification is exposed as its COS object until the file specification view (issue #76) types it.
/// </para>
/// </remarks>
public sealed class PdfStructureNamespace : IEquatable<PdfStructureNamespace>
{
    /// <summary>The namespace name of the standard structure namespace for PDF 1.7, the default namespace (§14.8.6.1).</summary>
    public const string Pdf17Name = "http://iso.org/pdf/ssn";

    /// <summary>The namespace name of the standard structure namespace for PDF 2.0 (§14.8.6.1).</summary>
    public const string Pdf20Name = "http://iso.org/pdf2/ssn";

    /// <summary>The namespace name of MathML 3.0 (§14.8.6.3).</summary>
    public const string MathMLName = "http://www.w3.org/1998/Math/MathML";

    private readonly StructureContext? _context;
    private readonly PdfStructureNamespaceKind _fixedKind;

    private PdfStructureNamespace(PdfStructureNamespaceKind kind) => _fixedKind = kind;

    internal PdfStructureNamespace(StructureContext context, CosDictionary dictionary, CosReference? reference)
    {
        _context = context;
        Dictionary = dictionary;
        Reference = reference;
        if (ViewReading.Get(context.Document, dictionary, StructureNames.NS) is not CosString)
        {
            context.Report(DiagnosticCodes.NamespaceInvalid, "A namespace dictionary has no NS text string (Table 356); its namespace name reads as empty.", reference);
        }

        if (ViewReading.Get(context.Document, dictionary, Objects.KnownNames.Type) is { } type && !StructureNames.Namespace.Equals(type))
        {
            context.Report(DiagnosticCodes.NamespaceInvalid, "A namespace dictionary's Type is not Namespace (Table 356); read as a namespace.", reference);
        }
    }

    /// <summary>Gets the standard structure namespace for PDF 1.7: the default namespace of every element without <c>NS</c>.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.1; Annex M.</remarks>
    public static PdfStructureNamespace Pdf17 { get; } = new(PdfStructureNamespaceKind.Pdf17);

    /// <summary>Gets the standard structure namespace for PDF 2.0.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.1, §14.8.4.</remarks>
    public static PdfStructureNamespace Pdf20 { get; } = new(PdfStructureNamespaceKind.Pdf20);

    /// <summary>Gets the MathML 3.0 namespace.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.3.</remarks>
    public static PdfStructureNamespace MathML { get; } = new(PdfStructureNamespaceKind.MathML);

    /// <summary>Gets the namespace dictionary, or <see langword="null"/> for <see cref="Pdf17"/>, <see cref="Pdf20"/> and <see cref="MathML"/>.</summary>
    /// <remarks>ISO 32000-2 §14.7.4.2, Table 356.</remarks>
    public CosDictionary? Dictionary { get; }

    /// <summary>Gets the indirect reference to the namespace dictionary, when it was reached through one.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the namespace name (<c>NS</c>), conventionally a URI; empty when the dictionary has none.</summary>
    /// <remarks>ISO 32000-2 §14.7.4.2, Table 356 (required, PDF 2.0).</remarks>
    public string Name => Dictionary is null
        ? _fixedKind switch
        {
            PdfStructureNamespaceKind.Pdf17 => Pdf17Name,
            PdfStructureNamespaceKind.Pdf20 => Pdf20Name,
            _ => MathMLName,
        }
        : ViewReading.Text(_context?.Document, Dictionary, StructureNames.NS) ?? string.Empty;

    /// <summary>Gets which namespace this is, from its namespace name.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.</remarks>
    public PdfStructureNamespaceKind Kind => Dictionary is null
        ? _fixedKind
        : Name switch
        {
            Pdf17Name => PdfStructureNamespaceKind.Pdf17,
            Pdf20Name => PdfStructureNamespaceKind.Pdf20,
            MathMLName => PdfStructureNamespaceKind.MathML,
            _ => PdfStructureNamespaceKind.Custom,
        };

    /// <summary>Gets a value indicating whether this is the default namespace: the one an element without an <c>NS</c> entry is in.</summary>
    /// <remarks>ISO 32000-2 §14.8.6.1. Its role mapping comes from the structure tree root's <c>RoleMap</c>, not from a <c>RoleMapNS</c>.</remarks>
    public bool IsDefault => Dictionary is null && _fixedKind == PdfStructureNamespaceKind.Pdf17;

    /// <summary>Gets the namespace's role map (<c>RoleMapNS</c>), or <see langword="null"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §14.7.4.2, Table 356: each key is a type in this namespace; each value a type name in the default namespace, or
    /// <c>[name namespace]</c> naming a type in another namespace.
    /// </remarks>
    public CosDictionary? RoleMap => Dictionary is null ? null : ViewReading.Get(_context?.Document, Dictionary, StructureNames.RoleMapNS) as CosDictionary;

    /// <summary>Gets the schema file specification (<c>Schema</c>), as its COS object, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.7.4.2, Table 356.</remarks>
    public CosObject? Schema => Dictionary is null ? null : ViewReading.Get(_context?.Document, Dictionary, StructureNames.Schema);

    /// <summary>Returns whether <paramref name="type"/> is a standard structure type of this namespace.</summary>
    /// <param name="type">The structure type name.</param>
    /// <returns>
    /// For the PDF 1.7 and PDF 2.0 namespaces, whether the type is in their set (§14.8.4, Annex M: <c>Em</c>, <c>Strong</c>,
    /// <c>Title</c>, <c>H7</c> and the other PDF 2.0 additions are not standard in PDF 1.7, and <c>Art</c>, <c>Note</c>, <c>Code</c> and
    /// the other PDF 1.7 types Annex M lists are not standard in PDF 2.0); for MathML, any non-empty name (element names are not checked
    /// against the MathML 3.0 schema); for a custom namespace, <see langword="false"/>.
    /// </returns>
    /// <remarks>ISO 32000-2 §14.8.4, §14.8.6, Annex M; ISO/TS 32005 §5.3-§5.5.</remarks>
    public bool IsStandardType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Kind switch
        {
            PdfStructureNamespaceKind.Pdf17 => StandardStructureTypes.IsPdf17(type),
            PdfStructureNamespaceKind.Pdf20 => StandardStructureTypes.IsPdf20(type),
            PdfStructureNamespaceKind.MathML => type.Length > 0,
            _ => false,
        };
    }

    /// <inheritdoc/>
    public bool Equals(PdfStructureNamespace? other) =>
        other is not null && (ReferenceEquals(this, other) || (Kind == other.Kind && (Kind != PdfStructureNamespaceKind.Custom || Name == other.Name)));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfStructureNamespace);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind == PdfStructureNamespaceKind.Custom ? HashCode.Combine(Kind, Name) : Kind.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Name;
}
