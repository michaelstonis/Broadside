using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>The document's mark information dictionary: whether it is a tagged PDF and which tagged-PDF conventions it uses.</summary>
/// <remarks>
/// ISO 32000-2 §14.7.1, Table 353, and §14.8.1 (a tagged PDF shall have <c>Marked</c> true). A live view over the catalog's
/// <c>MarkInfo</c>: every flag is read when asked for and is <see langword="false"/> when the dictionary or the entry is missing. An
/// entry that is not a boolean reads as <see langword="false"/> with a <c>MarkInfoInvalid</c> diagnostic.
/// </remarks>
public sealed class PdfMarkInfo
{
    private readonly PdfDocument _document;

    internal PdfMarkInfo(PdfDocument document) => _document = document;

    /// <summary>Gets the mark information dictionary, or <see langword="null"/> when the catalog has none.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (<c>MarkInfo</c>), and Table 353.</remarks>
    public CosDictionary? Dictionary => ViewReading.Get(_document, _document.Catalog, StructureNames.MarkInfo) as CosDictionary;

    /// <summary>Gets a value indicating whether the document conforms to tagged PDF conventions (<c>Marked</c>).</summary>
    /// <remarks>ISO 32000-2 Table 353 (PDF 1.4 for tagged PDF); §14.8.1.</remarks>
    public bool Marked => Flag(StructureNames.Marked);

    /// <summary>Gets a value indicating whether structure elements carry user properties (<c>UserProperties</c>).</summary>
    /// <remarks>ISO 32000-2 Table 353 (PDF 1.6), §14.7.6.4.</remarks>
    public bool UserProperties => Flag(StructureNames.UserProperties);

    /// <summary>Gets a value indicating whether the document has tag suspects (<c>Suspects</c>; deprecated in PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 Table 353 (PDF 1.6).</remarks>
    public bool Suspects => Flag(StructureNames.Suspects);

    private bool Flag(CosName key)
    {
        if (Dictionary is not { } dictionary || ViewReading.Get(_document, dictionary, key) is not { } value)
        {
            return false;
        }

        if (value is CosBoolean flag)
        {
            return flag.Value;
        }

        _document.DiagnosticSink.Report(
            DiagnosticCodes.MarkInfoInvalid,
            Diagnostics.DiagnosticSeverity.Warning,
            $"MarkInfo {key.Value} is not a boolean (Table 353); read as false.",
            offset: null,
            _document.Catalog.TryGetValue(StructureNames.MarkInfo, out CosObject? reference) ? reference as CosReference : null);
        return false;
    }
}
