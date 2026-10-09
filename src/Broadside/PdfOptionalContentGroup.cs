using Broadside.Objects;

namespace Broadside;

/// <summary>An optional content group: a collection of graphics that can be made visible or invisible. A live view over its dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §8.11.2.1 (PDF 1.5), Table 96. A group's state (ON or OFF) is not stored in the file; it lives in a
/// <see cref="PdfOptionalContentState"/>. Only groups listed in the optional content properties' <c>OCGs</c> array are optional
/// content (§8.11.3.2). Its <c>Metadata</c> entry is reported by <c>PdfDocument.EnumerateObjectMetadata</c>.
/// </remarks>
public sealed class PdfOptionalContentGroup
{
    private readonly PdfDocument _document;

    internal PdfOptionalContentGroup(PdfDocument document, CosDictionary dictionary, CosReference? reference, int ordinal)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
        Ordinal = ordinal;
    }

    /// <summary>Gets the optional content group dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1, Table 96.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the group (groups shall be indirect objects), or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the name of the group for a user interface (<c>Name</c>); empty when missing.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1, Table 96 (required).</remarks>
    public string Name => ViewReading.Text(_document, Dictionary, FileAndLayerNames.Name) ?? string.Empty;

    /// <summary>Gets the intended uses of the group (<c>Intent</c>, a name or an array of names): <c>View</c> when absent.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1, Table 96, and §8.11.2.3. <c>View</c> and <c>Design</c> are defined; second-class names may appear.</remarks>
    public IReadOnlyList<CosName> Intents => ReadIntents(_document, Dictionary);

    /// <summary>Gets the usage dictionary describing the nature of the group's content (<c>Usage</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.1, Table 96, and §8.11.4.4, Table 100.</remarks>
    public PdfOptionalContentUsage? Usage =>
        ViewReading.Get(_document, Dictionary, FileAndLayerNames.Usage) is CosDictionary usage ? new PdfOptionalContentUsage(_document, usage) : null;

    /// <summary>Gets the group's position in the optional content properties' group list.</summary>
    internal int Ordinal { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>Reads an <c>Intent</c> entry (a name or an array of names); <c>View</c> when absent or unusable.</summary>
    internal static CosName[] ReadIntents(PdfDocument document, CosDictionary dictionary) => ViewReading.Get(document, dictionary, FileAndLayerNames.Intent) switch
    {
        CosName name => [name],
        CosArray array => [.. array.Select(document.Resolve).OfType<CosName>()],
        _ => [FileAndLayerNames.View],
    };
}
