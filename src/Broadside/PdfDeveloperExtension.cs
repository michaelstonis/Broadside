using Broadside.Objects;

namespace Broadside;

/// <summary>One developer extension the document declares: an entry of the catalog's <c>Extensions</c> dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.12, Tables 48 and 49. Each key of the extensions dictionary other than <c>Type</c> is a registered developer
/// prefix (Annex E), such as <c>ADBE</c> or <c>ISO_</c>; its value is a developer extensions dictionary or, in PDF 2.0, an array of
/// them (ISO/TS 32001 and its siblings declare themselves as <c>/ISO_ [...]</c>). Each dictionary in an array is one extension.
/// </para>
/// <para>
/// A live view over the developer extensions dictionary. A missing or malformed <c>BaseVersion</c> or <c>ExtensionLevel</c> (both
/// required) reads as <see langword="null"/>, reported when <see cref="PdfDocument.Extensions"/> is read.
/// </para>
/// </remarks>
public sealed class PdfDeveloperExtension
{
    private static readonly CosName BaseVersionKey = new("BaseVersion");
    private static readonly CosName UrlKey = new("URL");
    private static readonly CosName ExtensionRevisionKey = new("ExtensionRevision");

    private readonly PdfDocument _document;

    internal PdfDeveloperExtension(PdfDocument document, CosName prefix, CosDictionary dictionary)
    {
        _document = document;
        Prefix = prefix;
        Dictionary = dictionary;
    }

    /// <summary>Gets the developer prefix the extension is declared under, such as <c>ADBE</c> or <c>ISO_</c>.</summary>
    /// <remarks>ISO 32000-2 §7.12.2, Table 48, and Annex E.</remarks>
    public CosName Prefix { get; }

    /// <summary>Gets the developer extensions dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.12.3, Table 49.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the PDF version the extension applies to (<c>BaseVersion</c>), or <see langword="null"/> when missing or malformed.</summary>
    /// <remarks>ISO 32000-2 §7.12.4.</remarks>
    public PdfVersion? BaseVersion => ReadBaseVersion(_document, Dictionary);

    /// <summary>Gets the developer's extension level (<c>ExtensionLevel</c>), or <see langword="null"/> when missing or not an integer.</summary>
    /// <remarks>ISO 32000-2 §7.12.5.</remarks>
    public int? ExtensionLevel => ReadExtensionLevel(_document, Dictionary);

    /// <summary>Gets the URL of the extension's documentation (<c>URL</c>, PDF 2.0), or <see langword="null"/> when absent or not an absolute URI.</summary>
    /// <remarks>ISO 32000-2 §7.12.3, Table 49. The raw string stays in <see cref="Dictionary"/>.</remarks>
    public Uri? Url => _document.Resolve(Dictionary.GetValueOrDefault(UrlKey)) is CosString url && Uri.TryCreate(url.DecodeText(), UriKind.Absolute, out Uri? uri) ? uri : null;

    /// <summary>Gets further revision information on the extension level (<c>ExtensionRevision</c>, PDF 2.0), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.12.6.</remarks>
    public string? ExtensionRevision => _document.Resolve(Dictionary.GetValueOrDefault(ExtensionRevisionKey)) is CosString revision ? revision.DecodeText() : null;

    internal static PdfVersion? ReadBaseVersion(PdfDocument document, CosDictionary dictionary) =>
        document.Resolve(dictionary.GetValueOrDefault(BaseVersionKey)) is CosName name && PdfVersion.TryParse(name.Bytes, out PdfVersion version) ? version : null;

    internal static int? ReadExtensionLevel(PdfDocument document, CosDictionary dictionary) =>
        document.Resolve(dictionary.GetValueOrDefault(KnownNames.ExtensionLevel)) is CosInteger { Value: >= int.MinValue and <= int.MaxValue } level ? (int)level.Value : null;
}
