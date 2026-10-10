using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>One user property of a structure element.</summary>
/// <remarks>ISO 32000-2 §14.7.6.4, Table 362.</remarks>
public sealed class PdfUserProperty
{
    internal PdfUserProperty(CosDictionary dictionary, string name, CosObject value, string? formattedValue, bool isHidden)
    {
        Dictionary = dictionary;
        Name = name;
        Value = value;
        FormattedValue = formattedValue;
        IsHidden = isHidden;
    }

    /// <summary>Gets the user property dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the property's name (<c>N</c>).</summary>
    /// <remarks>ISO 32000-2 Table 362 (required).</remarks>
    public string Name { get; }

    /// <summary>Gets the property's value (<c>V</c>), of any type.</summary>
    /// <remarks>ISO 32000-2 Table 362 (required).</remarks>
    public CosObject Value { get; }

    /// <summary>Gets the formatted value (<c>F</c>) to show instead of <see cref="Value"/>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 362.</remarks>
    public string? FormattedValue { get; }

    /// <summary>Gets a value indicating whether the property should be hidden (<c>H</c>; default false).</summary>
    /// <remarks>ISO 32000-2 Table 362.</remarks>
    public bool IsHidden { get; }
}
