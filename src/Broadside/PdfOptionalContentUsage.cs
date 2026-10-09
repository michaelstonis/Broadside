using Broadside.Objects;

namespace Broadside;

/// <summary>The usage dictionary of an optional content group: what its content is for. A live view; exposed as data.</summary>
/// <remarks>
/// ISO 32000-2 §8.11.4.4, Table 100. The entries drive automatic state changes only through the auto-state (<c>AS</c>) entries of a
/// configuration (<see cref="PdfOptionalContentProperties.ApplyAutoStates"/>); reading them changes nothing.
/// </remarks>
public sealed class PdfOptionalContentUsage
{
    private readonly PdfDocument _document;

    internal PdfOptionalContentUsage(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the usage dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the application that created the group (<c>CreatorInfo</c> <c>Creator</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public string? Creator => Sub(FileAndLayerNames.CreatorInfo) is { } info ? ViewReading.Text(_document, info, FileAndLayerNames.Creator) : null;

    /// <summary>Gets the kind of content per the creator (<c>CreatorInfo</c> <c>Subtype</c>, such as <c>Artwork</c> or <c>Technical</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public CosName? CreatorSubtype => Sub(FileAndLayerNames.CreatorInfo) is { } info ? ViewReading.Name(_document, info, FileAndLayerNames.Subtype) : null;

    /// <summary>Gets the language and locale of the content (<c>Language</c> <c>Lang</c>, such as <c>es-MX</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100, and §14.9.2.</remarks>
    public string? Language => Sub(FileAndLayerNames.Language) is { } language ? ViewReading.Text(_document, language, FileAndLayerNames.Lang) : null;

    /// <summary>Gets a value indicating whether the group is preferred on a partial language match (<c>Language</c> <c>Preferred</c> ON). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public bool LanguagePreferred => Sub(FileAndLayerNames.Language) is { } language && State(language, FileAndLayerNames.Preferred) == true;

    /// <summary>Gets the recommended state when exporting (<c>Export</c> <c>ExportState</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public bool? ExportState => Sub(FileAndLayerNames.Export) is { } export ? State(export, FileAndLayerNames.ExportState) : null;

    /// <summary>Gets the minimum magnification at which the group is ON (<c>Zoom</c> <c>min</c>, default 0), or <see langword="null"/> without a <c>Zoom</c> entry.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public double? ZoomMin => Sub(FileAndLayerNames.Zoom) is { } zoom ? ViewReading.Number(_document, zoom, FileAndLayerNames.Min) ?? 0 : null;

    /// <summary>Gets the magnification below which the group is ON (<c>Zoom</c> <c>max</c>), or <see langword="null"/> when absent (infinity).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public double? ZoomMax => Sub(FileAndLayerNames.Zoom) is { } zoom ? ViewReading.Number(_document, zoom, FileAndLayerNames.Max) : null;

    /// <summary>Gets the kind of printed content (<c>Print</c> <c>Subtype</c>, such as <c>Trapping</c> or <c>Watermark</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public CosName? PrintSubtype => Sub(FileAndLayerNames.Print) is { } print ? ViewReading.Name(_document, print, FileAndLayerNames.Subtype) : null;

    /// <summary>Gets the state when printing (<c>Print</c> <c>PrintState</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public bool? PrintState => Sub(FileAndLayerNames.Print) is { } print ? State(print, FileAndLayerNames.PrintState) : null;

    /// <summary>Gets the state when the document is first opened (<c>View</c> <c>ViewState</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100. Applied only through an auto-state entry with the <c>View</c> category.</remarks>
    public bool? ViewState => Sub(FileAndLayerNames.View) is { } view ? State(view, FileAndLayerNames.ViewState) : null;

    /// <summary>Gets how <see cref="UserNames"/> is interpreted (<c>User</c> <c>Type</c>: <c>Ind</c>, <c>Ttl</c> or <c>Org</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public CosName? UserType => Sub(FileAndLayerNames.User) is { } user ? ViewReading.Name(_document, user, KnownNames.Type) : null;

    /// <summary>Gets the individuals, titles or organisations the content is for (<c>User</c> <c>Name</c>, a text string or an array).</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public IReadOnlyList<string> UserNames => Sub(FileAndLayerNames.User) is { } user
        ? ViewReading.Get(_document, user, FileAndLayerNames.Name) switch
        {
            CosString name => [name.DecodeText()],
            CosArray names => [.. names.Select(_document.Resolve).OfType<CosString>().Select(name => name.DecodeText())],
            _ => [],
        }
        : [];

    /// <summary>Gets the kind of pagination artifact (<c>PageElement</c> <c>Subtype</c>: <c>HF</c>, <c>FG</c>, <c>BG</c> or <c>L</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.4, Table 100.</remarks>
    public CosName? PageElement => Sub(FileAndLayerNames.PageElement) is { } element ? ViewReading.Name(_document, element, FileAndLayerNames.Subtype) : null;

    private CosDictionary? Sub(CosName key) => ViewReading.Get(_document, Dictionary, key) as CosDictionary;

    private bool? State(CosDictionary dictionary, CosName key) => ViewReading.Name(_document, dictionary, key)?.Value switch
    {
        "ON" => true,
        "OFF" => false,
        _ => null,
    };
}
