using Broadside.Objects;

namespace Broadside;

/// <summary>
/// An optional content configuration: initial group states and presentation for a user interface. The default configuration
/// (<c>D</c>) or an alternate one (<c>Configs</c>). A live view over the configuration dictionary.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.11.4.3, Table 99. Defaults are returned, never written: <c>BaseState</c> ON, <c>Intent</c> View,
/// <c>ListMode</c> AllPages, <c>Locked</c> empty; <c>Order</c> and <c>RBGroups</c> are empty in the default configuration and the
/// default configuration's values in the others.
/// </remarks>
public sealed class PdfOptionalContentConfiguration
{
    private const int MaxOrderDepth = 32;

    private readonly PdfOptionalContentProperties _properties;

    internal PdfOptionalContentConfiguration(PdfOptionalContentProperties properties, CosDictionary dictionary, CosReference? reference, bool isDefault)
    {
        _properties = properties;
        Dictionary = dictionary;
        Reference = reference;
        IsDefault = isDefault;
    }

    /// <summary>Gets the configuration dictionary. For a document whose <c>D</c> entry is missing, an empty dictionary not attached to the document.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the configuration, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets a value indicating whether this is the default configuration (<c>D</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.2, Table 98.</remarks>
    public bool IsDefault { get; }

    /// <summary>Gets the name of the configuration for a user interface (<c>Name</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99.</remarks>
    public string? Name => ViewReading.Text(_properties.Document, Dictionary, FileAndLayerNames.Name);

    /// <summary>Gets the application or feature that created the configuration (<c>Creator</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99.</remarks>
    public string? Creator => ViewReading.Text(_properties.Document, Dictionary, FileAndLayerNames.Creator);

    /// <summary>Gets the state every group starts from when the configuration is applied (<c>BaseState</c>), as written; ON when absent or unknown.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99. In the default configuration it shall be ON; state computation reports and honours OFF, and reads Unchanged as ON.</remarks>
    public PdfOptionalContentBaseState BaseState => ViewReading.Name(_properties.Document, Dictionary, FileAndLayerNames.BaseState)?.Value switch
    {
        "OFF" => PdfOptionalContentBaseState.Off,
        "Unchanged" => PdfOptionalContentBaseState.Unchanged,
        _ => PdfOptionalContentBaseState.On,
    };

    /// <summary>Gets the groups turned ON after <see cref="BaseState"/> is applied (<c>ON</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99.</remarks>
    public IReadOnlyList<PdfOptionalContentGroup> On => _properties.ReadGroupList(ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.ON));

    /// <summary>Gets the groups turned OFF after <see cref="BaseState"/> is applied (<c>OFF</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99. A group shall not be in both <c>ON</c> and <c>OFF</c>; state computation applies OFF last.</remarks>
    public IReadOnlyList<PdfOptionalContentGroup> Off => _properties.ReadGroupList(ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.OFF));

    /// <summary>
    /// Gets the intents whose groups count when computing visibility (<c>Intent</c>, a name or an array): <c>View</c> when absent;
    /// <c>All</c> means every intent; an empty list means no group counts and all content is visible.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99, and §8.11.2.3. In the default configuration it shall be View.</remarks>
    public IReadOnlyList<CosName> Intents => PdfOptionalContentGroup.ReadIntents(_properties.Document, Dictionary);

    /// <summary>Gets the auto-state usage application dictionaries (<c>AS</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99, and §8.11.4.4, Table 101.</remarks>
    public IReadOnlyList<PdfOptionalContentUsageApplication> AutoStates => ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.AS) is CosArray array
        ? [.. array.Select(_properties.Document.Resolve).OfType<CosDictionary>().Select(item => new PdfOptionalContentUsageApplication(_properties, item))]
        : [];

    /// <summary>Gets the presentation order of the groups for a user interface (<c>Order</c>), as a tree.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99. Absent: empty in the default configuration, the default configuration's order in the others. Nesting deeper than 32 levels is cut off, and an array reached a second time (a cycle or a shared array) is read once.</remarks>
    public IReadOnlyList<PdfOptionalContentOrderItem> Order =>
        FromSelfOrDefault(FileAndLayerNames.Order) is CosArray array ? ReadOrder(array, start: 0, depth: 0, []) : [];

    /// <summary>Gets the display mode of <see cref="Order"/> (<c>ListMode</c>).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99.</remarks>
    public PdfOptionalContentListMode ListMode =>
        ViewReading.Name(_properties.Document, Dictionary, FileAndLayerNames.ListMode) is { Value: "VisiblePages" } ? PdfOptionalContentListMode.VisiblePages : PdfOptionalContentListMode.AllPages;

    /// <summary>Gets the radio-button collections (<c>RBGroups</c>): at most one group of each is ON at a time.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99. Absent: empty in the default configuration, the default configuration's in the others.</remarks>
    public IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>> RadioButtonGroups => FromSelfOrDefault(FileAndLayerNames.RBGroups) is CosArray array
        ? [.. array.Select(_properties.Document.Resolve).OfType<CosArray>().Select(_properties.ReadGroupList)]
        : [];

    /// <summary>Gets the groups a user interface shall not let the user change (<c>Locked</c>, PDF 1.6).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3, Table 99. Default empty.</remarks>
    public IReadOnlyList<PdfOptionalContentGroup> Locked => _properties.ReadGroupList(ViewReading.Get(_properties.Document, Dictionary, FileAndLayerNames.Locked));

    /// <inheritdoc/>
    public override string ToString() => Name ?? (IsDefault ? "D" : "Configuration");

    private CosObject? FromSelfOrDefault(CosName key) =>
        ViewReading.Get(_properties.Document, Dictionary, key)
        ?? (IsDefault ? null : ViewReading.Get(_properties.Document, _properties.DefaultConfigurationDictionary, key));

    private List<PdfOptionalContentOrderItem> ReadOrder(CosArray array, int start, int depth, HashSet<CosArray> visited)
    {
        var items = new List<PdfOptionalContentOrderItem>();
        if (depth >= MaxOrderDepth || !visited.Add(array))
        {
            return items;
        }

        var pendingChildren = new List<PdfOptionalContentOrderItem>();
        PdfOptionalContentGroup? pendingGroup = null;
        for (int index = start; index < array.Count; index++)
        {
            switch (_properties.Document.Resolve(array[index]))
            {
                case CosDictionary dictionary when _properties.FindGroup(dictionary) is { } group:
                    Flush(items, ref pendingGroup, pendingChildren);
                    pendingGroup = group;
                    break;
                case CosArray nested:
                    string? label = nested.Count > 0 && _properties.Document.Resolve(nested[0]) is CosString text ? text.DecodeText() : null;
                    if (label is not null)
                    {
                        Flush(items, ref pendingGroup, pendingChildren);
                        var members = ReadOrder(nested, 1, depth + 1, visited);
                        items.Add(new PdfOptionalContentOrderItem(null, label, members));
                    }
                    else if (pendingGroup is not null && pendingChildren.Count == 0)
                    {
                        pendingChildren.AddRange(ReadOrder(nested, 0, depth + 1, visited));
                    }
                    else
                    {
                        Flush(items, ref pendingGroup, pendingChildren);
                        items.Add(new PdfOptionalContentOrderItem(null, null, ReadOrder(nested, 0, depth + 1, visited)));
                    }

                    break;
            }
        }

        Flush(items, ref pendingGroup, pendingChildren);
        return items;
    }

    private static void Flush(List<PdfOptionalContentOrderItem> items, ref PdfOptionalContentGroup? pendingGroup, List<PdfOptionalContentOrderItem> pendingChildren)
    {
        if (pendingGroup is not null)
        {
            items.Add(new PdfOptionalContentOrderItem(pendingGroup, null, [.. pendingChildren]));
            pendingGroup = null;
            pendingChildren.Clear();
        }
    }
}
