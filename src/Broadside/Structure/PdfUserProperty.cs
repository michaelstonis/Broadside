using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>One user property of a structure element.</summary>
/// <remarks>ISO 32000-2 §14.7.6.4, Table 362. A live view: each property reads its entry from <see cref="Dictionary"/> when called.</remarks>
public sealed class PdfUserProperty
{
    private readonly PdfDocument _document;

    internal PdfUserProperty(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the user property dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the property's name (<c>N</c>); empty when the entry has since been removed or is not a text string.</summary>
    /// <remarks>ISO 32000-2 Table 362 (required).</remarks>
    public string Name => ViewReading.Text(_document, Dictionary, StructureNames.N) ?? string.Empty;

    /// <summary>Gets the property's value (<c>V</c>), of any type; the null object when the entry has since been removed.</summary>
    /// <remarks>ISO 32000-2 Table 362 (required).</remarks>
    public CosObject Value => ViewReading.Get(_document, Dictionary, StructureNames.V) ?? CosNull.Instance;

    /// <summary>Gets the formatted value (<c>F</c>) to show instead of <see cref="Value"/>, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 362.</remarks>
    public string? FormattedValue => ViewReading.Text(_document, Dictionary, StructureNames.F);

    /// <summary>Gets a value indicating whether the property should be hidden (<c>H</c>; default false).</summary>
    /// <remarks>ISO 32000-2 Table 362.</remarks>
    public bool IsHidden => ViewReading.Boolean(_document, Dictionary, StructureNames.H) ?? false;
}
