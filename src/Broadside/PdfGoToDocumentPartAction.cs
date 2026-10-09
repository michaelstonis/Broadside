using Broadside.Objects;

namespace Broadside;

/// <summary>A go-to-document-part action: changes the view to the start page of a document part (DPart).</summary>
/// <remarks>ISO 32000-2 §12.6.4.5, Table 206 (PDF 2.0), and §14.12 (document parts, exposed raw).</remarks>
public sealed class PdfGoToDocumentPartAction : PdfAction
{
    internal PdfGoToDocumentPartAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.GoToDp;

    /// <summary>Gets the DPart dictionary to go to (<c>Dp</c>, required, an indirect reference), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.5, Table 206, and §14.12, Table 409.</remarks>
    public CosDictionary? DocumentPart => ReadDictionary(ActionNames.Dp);

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(ActionNames.Dp))
        {
            Report("A GoToDp action shall have a Dp entry naming a DPart; it has none, so it goes nowhere.");
        }
    }
}
