using Broadside.Objects;

namespace Broadside;

/// <summary>
/// A collection item: the values a file in a portable collection shows for each field of the collection's schema. A live view over
/// the collection item dictionary.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.11.6 (PDF 1.7), Tables 46 and 47. Each entry other than <c>Type</c> is keyed by a schema field key and holds a
/// text string, a date, a number, or a collection subitem dictionary (data <c>D</c> plus a prefix <c>P</c> that sorting ignores).
/// </remarks>
public sealed class PdfCollectionItem
{
    private readonly PdfDocument _document;

    internal PdfCollectionItem(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the collection item dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.11.6, Table 46.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the field keys the item has values for, in the order they are stored.</summary>
    /// <remarks>ISO 32000-2 §7.11.6, Table 46: every key except <c>Type</c>.</remarks>
    public IReadOnlyList<string> Keys => [.. Dictionary.Keys.Where(key => !key.Equals(KnownNames.Type)).Select(key => key.Value)];

    /// <summary>Returns the item's value for the schema field <paramref name="fieldKey"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="fieldKey">The field's key in the collection schema.</param>
    /// <returns>The value; a subitem dictionary is unwrapped into its data and prefix.</returns>
    /// <remarks>ISO 32000-2 §7.11.6, Tables 46 and 47.</remarks>
    public PdfCollectionItemValue? GetValue(string fieldKey)
    {
        ArgumentNullException.ThrowIfNull(fieldKey);
        if (ViewReading.Get(_document, Dictionary, new CosName(fieldKey)) is not { } value)
        {
            return null;
        }

        if (value is CosDictionary subitem)
        {
            return ViewReading.Get(_document, subitem, FileAndLayerNames.D) is { } data
                ? new PdfCollectionItemValue(data, ViewReading.Text(_document, subitem, FileAndLayerNames.P))
                : null;
        }

        return new PdfCollectionItemValue(value, prefix: null);
    }
}
