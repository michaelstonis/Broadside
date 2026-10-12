namespace Broadside.Structure;

/// <summary>A structure type in a namespace: one step of a role-mapping chain, or the standard type an element resolves to.</summary>
/// <remarks>ISO 32000-2 §14.7.3 (structure types), §14.7.4 (namespaces), §14.8.6.2 (role maps and namespaces).</remarks>
public sealed class PdfStructureType : IEquatable<PdfStructureType>
{
    /// <summary>Creates a structure type.</summary>
    /// <param name="name">The type name, as in a structure element's <c>S</c> entry.</param>
    /// <param name="namespace">The namespace the name is in.</param>
    public PdfStructureType(string name, PdfStructureNamespace @namespace)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(@namespace);
        Name = name;
        Namespace = @namespace;
    }

    /// <summary>Gets the type name.</summary>
    public string Name { get; }

    /// <summary>Gets the namespace the name is in.</summary>
    public PdfStructureNamespace Namespace { get; }

    /// <summary>Gets a value indicating whether the type is a standard type of its namespace.</summary>
    /// <remarks>ISO 32000-2 §14.8.4, §14.8.6; see <see cref="PdfStructureNamespace.IsStandardType(string)"/>.</remarks>
    public bool IsStandard => Namespace.IsStandardType(Name);

    /// <inheritdoc/>
    public bool Equals(PdfStructureType? other) => other is not null && Name == other.Name && Namespace.Equals(other.Namespace);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfStructureType);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Name, Namespace);

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Namespace.Name})";
}
