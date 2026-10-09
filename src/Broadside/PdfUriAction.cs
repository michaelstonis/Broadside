using System.Diagnostics.CodeAnalysis;
using System.Text;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>A URI action: resolves a uniform resource identifier, typically a hypertext link.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.8, Table 210 (PDF 1.1). The URI is untrusted data from the file: the library never resolves it, and any scheme
/// (<c>javascript:</c> included) is returned as written. A relative URI is relative to the catalog's <c>URI</c> dictionary's <c>Base</c>
/// (Table 211, <see cref="PdfDocument.UriBase"/>); <see cref="ResolveUri"/> combines the two without fetching anything.
/// </remarks>
public sealed class PdfUriAction : PdfAction
{
    internal PdfUriAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Uri;

    /// <summary>Gets the URI, decoded as UTF-8, or <see langword="null"/> when it is absent or not a string.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.6.4.8, Table 210: an ASCII string "encoded in UTF-8". Bytes that are not UTF-8 are read through PDFDocEncoding and
    /// a UTF-16 byte order mark as UTF-16, each with a <c>UriInvalid</c> diagnostic; a URI written as a name is read as its bytes, with
    /// the same diagnostic. The value as stored is <see cref="UriObject"/>.
    /// </remarks>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "The file may hold a relative or malformed URI (relative to the catalog URI Base); the text is returned as stored.")]
    public string? Uri
    {
        get
        {
            switch (Get(NavigationNames.Uri))
            {
                case CosString text:
                    string uri = DecodeUri(text.Bytes, out bool repaired);
                    if (repaired)
                    {
                        Report(DiagnosticCodes.UriInvalid, "A URI action's URI is not UTF-8; it is read as UTF-16 or PDFDocEncoding.");
                    }

                    return uri;
                case CosName name:
                    Report(DiagnosticCodes.UriInvalid, "A URI action's URI shall be a string; it is a name, read as its bytes.");
                    return DecodeUri(name.Bytes, out _);
                case null:
                    return null;
                default:
                    ReportEntry(NavigationNames.Uri, "an ASCII string");
                    return null;
            }
        }
    }

    /// <summary>Gets the <c>URI</c> entry as stored (resolved): normally a string; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.8, Table 210.</remarks>
    public CosObject? UriObject => Get(NavigationNames.Uri);

    /// <summary>Gets a value indicating whether the mouse position is appended to the URI when a link is clicked. Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.8, Table 210; ignored for outline items and the document's open action.</remarks>
    public bool IsMap => ReadBoolean(NavigationNames.IsMap) ?? false;

    /// <summary>
    /// Returns the URI as an absolute <see cref="System.Uri"/>: <see cref="Uri"/> itself when it is absolute, else resolved against the
    /// document's <see cref="PdfDocument.UriBase"/>. Nothing is fetched.
    /// </summary>
    /// <returns>The absolute URI; <see langword="null"/> when there is no URI, or it is relative and the document has no base URI, or it is not a URI at all.</returns>
    /// <remarks>ISO 32000-2 §12.6.4.8, Tables 210 and 211 (RFC 3986 reference resolution). The result is untrusted data from the document.</remarks>
    public System.Uri? ResolveUri()
    {
        if (Uri is not { } text)
        {
            return null;
        }

        if (System.Uri.TryCreate(text, UriKind.Absolute, out System.Uri? absolute))
        {
            return absolute;
        }

        return Document.UriBase is { } baseUri && System.Uri.TryCreate(baseUri, text, out System.Uri? resolved) ? resolved : null;
    }

    /// <summary>Decodes URI bytes: UTF-8 when valid, else UTF-16 after a byte order mark, else PDFDocEncoding (<paramref name="repaired"/> set).</summary>
    internal static string DecodeUri(ReadOnlySpan<byte> bytes, out bool repaired)
    {
        repaired = false;
        if (!bytes.StartsWith("\uFEFF"u8) && !bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) && System.Text.Unicode.Utf8.IsValid(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        repaired = !bytes.StartsWith("﻿"u8);
        return bytes.StartsWith("﻿"u8) ? Encoding.UTF8.GetString(bytes[3..]) : TextStringDecoder.Decode(bytes);
    }

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (Uri is null)
        {
            Report("A URI action shall have a URI string; it has none, so it goes nowhere.");
        }
    }
}
