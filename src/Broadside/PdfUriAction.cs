using System.Diagnostics.CodeAnalysis;
using System.Text;
using Broadside.Objects;

namespace Broadside;

/// <summary>A URI action: resolves a uniform resource identifier, typically a hypertext link.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.8, Table 210 (PDF 1.1). The URI is untrusted data from the file: the library never resolves it. A relative URI
/// is relative to the catalog's <c>URI</c> dictionary's <c>Base</c> (Table 211), which this view does not apply.
/// </remarks>
public sealed class PdfUriAction : PdfAction
{
    internal PdfUriAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <summary>Gets the URI, the <c>URI</c> string decoded as UTF-8, or <see langword="null"/> when it is absent or not a string.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.8, Table 210: an ASCII string "encoded in UTF-8".</remarks>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The file may hold a relative or malformed URI (relative to the catalog URI Base); the text is returned as stored.")]
    public string? Uri => Dictionary.TryGetValue(NavigationNames.Uri, out CosObject? uri) && Document.Resolve(uri) is CosString text
        ? Encoding.UTF8.GetString(text.Bytes)
        : null;

    /// <summary>Gets a value indicating whether the mouse position is appended to the URI when a link is clicked. Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.8, Table 210; ignored for outline items and the document's open action.</remarks>
    public bool IsMap => Dictionary.TryGetValue(NavigationNames.IsMap, out CosObject? isMap) && Document.Resolve(isMap) is CosBoolean { Value: true };

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (Uri is null)
        {
            Report("A URI action shall have a URI string; it has none, so it goes nowhere.");
        }
    }
}
