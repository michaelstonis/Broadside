using Broadside.Objects;

namespace Broadside;

/// <summary>A go-to action: changes the view to a destination in this document.</summary>
/// <remarks>ISO 32000-2 §12.6.4.2, Table 202 (PDF 1.1).</remarks>
public sealed class PdfGoToAction : PdfAction
{
    internal PdfGoToAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <summary>Gets the destination to show, the <c>D</c> entry: explicit or named; <see langword="null"/> when absent or invalid.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.2, Table 202, and §12.3.2.</remarks>
    public PdfDestination? Destination =>
        Dictionary.TryGetValue(NavigationNames.D, out CosObject? d) ? PdfDestination.Create(Document, d, isRemote: false, Reference ?? Owner) : null;

    /// <summary>Gets the structure destination, the <c>SD</c> entry (PDF 2.0), or <see langword="null"/>. When present it should take precedence over <see cref="Destination"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.2, Table 202, and §12.3.2.3.</remarks>
    public PdfExplicitDestination? StructureDestination =>
        Dictionary.TryGetValue(NavigationNames.SD, out CosObject? sd)
            ? PdfDestination.Create(Document, sd, isRemote: false, Reference ?? Owner) as PdfExplicitDestination
            : null;

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(NavigationNames.D))
        {
            Report("A go-to action shall have a D entry; it has none, so it goes nowhere.");
        }
    }
}
