using Broadside.Objects;

namespace Broadside;

/// <summary>An embedded go-to action: changes the view to a destination in a PDF file embedded in this one, in its parent, or in another file's hierarchy.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.4, Table 204 (PDF 1.6). <see cref="Target"/> is the path from this document to the target document, one
/// <see cref="PdfEmbeddedTarget"/> per step; <see cref="File"/> names the target's root document when it is not this one's. The
/// destination is in the target document (<see cref="PdfDestination.IsRemote"/> is true). The library never opens the target.
/// </remarks>
public sealed class PdfEmbeddedGoToAction : PdfAction
{
    internal PdfEmbeddedGoToAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.GoToE;

    /// <summary>Gets the root document of the target relative to this document's root (<c>F</c>), or <see langword="null"/>: the same root.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 204, and §7.11.</remarks>
    public PdfFileSpecification? File => ReadFileSpecification(NavigationNames.F);

    /// <summary>Gets the destination in the target document (<c>D</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 204, and §12.3.2.</remarks>
    public PdfDestination? Destination => ReadDestination(NavigationNames.D, isRemote: true);

    /// <summary>Gets whether to open the target in a new window (<c>NewWindow</c>); <see langword="null"/> when absent: the processor's preference.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 204.</remarks>
    public bool? NewWindow => ReadBoolean(ActionNames.NewWindow);

    /// <summary>Gets the first step of the path to the target document (<c>T</c>; required when <c>F</c> is absent), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Tables 204 and 205.</remarks>
    public PdfEmbeddedTarget? Target => ReadDictionary(ActionNames.T) is { } target
        ? new PdfEmbeddedTarget(Document, target, DiagnosticReference, [Dictionary])
        : null;

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(NavigationNames.D))
        {
            Report("An embedded go-to action shall have a D entry; it has none, so it goes nowhere.");
        }

        if (!Dictionary.ContainsKey(NavigationNames.F) && !Dictionary.ContainsKey(ActionNames.T))
        {
            Report("An embedded go-to action without an F entry shall have a T target dictionary; it has neither.");
        }
    }
}
