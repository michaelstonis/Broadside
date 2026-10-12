using Broadside.Objects;

namespace Broadside;

/// <summary>
/// A usage application dictionary from a configuration's auto-state (<c>AS</c>) array: which groups have their states set from
/// which usage categories, and when. A live view.
/// </summary>
/// <remarks>ISO 32000-2 §8.11.4.4, Table 101.</remarks>
public sealed class PdfOptionalContentUsageApplication
{
    private readonly PdfOptionalContentProperties _properties;

    internal PdfOptionalContentUsageApplication(PdfOptionalContentProperties properties, CosDictionary dictionary)
    {
        _properties = properties;
        Dictionary = dictionary;
    }

    /// <summary>Gets the usage application dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 101.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the situation the dictionary applies to (<c>Event</c>), or <see langword="null"/> when missing or unknown.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 101 (required: <c>View</c>, <c>Print</c> or <c>Export</c>).</remarks>
    public PdfOptionalContentEvent? Event => ViewReading.Name(_properties.Document, Dictionary, FileAndLayerNames.Event)?.Value switch
    {
        "View" => PdfOptionalContentEvent.View,
        "Print" => PdfOptionalContentEvent.Print,
        "Export" => PdfOptionalContentEvent.Export,
        _ => null,
    };

    /// <summary>Gets the groups whose states are managed (<c>OCGs</c>, default empty).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 101.</remarks>
    public IReadOnlyList<PdfOptionalContentGroup> Groups => _properties.ReadGroupList(ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.OCGs));

    /// <summary>Gets the usage dictionary entries to consult (<c>Category</c>, such as <c>View</c>, <c>Zoom</c> or <c>Language</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 101 (required).</remarks>
    public IReadOnlyList<CosName> Categories => ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.Category) switch
    {
        CosArray array => [.. array.Select(_properties.Document.Resolve).OfType<CosName>()],
        CosName name => [name],
        _ => [],
    };
}
