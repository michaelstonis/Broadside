namespace Broadside;

/// <summary>One XMP property, structure field, array item or qualifier: a name and a value of one of the forms XMP allows.</summary>
/// <remarks>
/// <para>
/// ISO 16684-1 §6.3 and §7.5-7.8. A simple value's text is kept exactly, white-space included (§7.2); only the convenience getters of
/// <see cref="XmpPacket"/> interpret it. Array items are named <c>rdf:li</c>. The <c>xml:lang</c> qualifier is <see cref="Language"/>;
/// the other qualifiers (written with <c>rdf:value</c>, §7.8) are <see cref="Qualifiers"/>.
/// </para>
/// <para>Immutable: a snapshot of the packet it was read from.</para>
/// </remarks>
public sealed class XmpProperty
{
    internal XmpProperty(
        string namespaceName,
        string name,
        XmpPropertyKind kind,
        string? value,
        string? language,
        IReadOnlyList<XmpProperty> items,
        IReadOnlyList<XmpProperty> fields,
        IReadOnlyList<XmpProperty> qualifiers)
    {
        NamespaceName = namespaceName;
        Name = name;
        Kind = kind;
        Value = value;
        Language = language;
        Items = items;
        Fields = fields;
        Qualifiers = qualifiers;
    }

    /// <summary>Gets the namespace name of the property's name, such as <see cref="XmpNamespaces.DublinCore"/>.</summary>
    public string NamespaceName { get; }

    /// <summary>Gets the local name of the property, such as <c>title</c>; <c>li</c> for an array item.</summary>
    public string Name { get; }

    /// <summary>Gets the form of the value.</summary>
    public XmpPropertyKind Kind { get; }

    /// <summary>Gets the text of a simple value exactly as written; <see langword="null"/> for a structure or an array.</summary>
    public string? Value { get; }

    /// <summary>Gets the <c>xml:lang</c> qualifier, such as <c>x-default</c> or <c>de</c>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 16684-1 §7.8 and §8.2.2.4 (language alternatives).</remarks>
    public string? Language { get; }

    /// <summary>Gets the items of an array, in order; empty for any other form.</summary>
    public IReadOnlyList<XmpProperty> Items { get; }

    /// <summary>Gets the fields of a structure, in the order written; empty for any other form.</summary>
    public IReadOnlyList<XmpProperty> Fields { get; }

    /// <summary>Gets the qualifiers other than <c>xml:lang</c>.</summary>
    public IReadOnlyList<XmpProperty> Qualifiers { get; }

    /// <summary>Returns the structure field with the given name.</summary>
    /// <param name="namespaceName">The field's namespace name.</param>
    /// <param name="name">The field's local name.</param>
    /// <returns>The first field with that name, or <see langword="null"/>.</returns>
    public XmpProperty? GetField(string namespaceName, string name) => Find(Fields, namespaceName, name);

    /// <summary>Returns the first property of <paramref name="properties"/> with the given name.</summary>
    internal static XmpProperty? Find(IReadOnlyList<XmpProperty> properties, string namespaceName, string name)
    {
        foreach (XmpProperty property in properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal) && string.Equals(property.NamespaceName, namespaceName, StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }
}
