using Broadside.Objects;

namespace Broadside;

/// <summary>One value of a collection item: a text string, a date string or a number, with the prefix of a subitem.</summary>
/// <remarks>ISO 32000-2 §7.11.6, Tables 46 and 47. The data type matches the schema field's subtype (§12.3.5, Table 155).</remarks>
public sealed class PdfCollectionItemValue
{
    internal PdfCollectionItemValue(CosObject data, string? prefix)
    {
        Data = data;
        Prefix = prefix;
    }

    /// <summary>Gets the value as stored, resolved: a <see cref="CosString"/> (text or date) or a <see cref="CosNumber"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.6, Table 47 (<c>D</c>).</remarks>
    public CosObject Data { get; }

    /// <summary>Gets the subitem's prefix (<c>P</c>), shown before the value and ignored when sorting, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.6, Table 47.</remarks>
    public string? Prefix { get; }

    /// <summary>Gets the value decoded as text when it is a string, else <see langword="null"/>.</summary>
    public string? Text => Data is CosString value ? value.DecodeText() : null;

    /// <summary>Gets the value when it is a number, else <see langword="null"/>.</summary>
    public double? Number => Data is CosNumber value ? value.ToDouble() : null;
}
