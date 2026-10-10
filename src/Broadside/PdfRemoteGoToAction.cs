using Broadside.Objects;

namespace Broadside;

/// <summary>A remote go-to action: changes the view to a destination in another PDF file.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.3, Table 203 (PDF 1.1). The destination belongs to the other file: an explicit destination names a 0-based
/// page number there, and a named destination is looked up there, so <see cref="PdfDestination.IsRemote"/> is true and nothing is
/// resolved against this document. The library never opens the other file.
/// </remarks>
public sealed class PdfRemoteGoToAction : PdfAction
{
    internal PdfRemoteGoToAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.GoToR;

    /// <summary>Gets the file holding the destination (<c>F</c>, required), or <see langword="null"/> when absent or invalid.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.3, Table 203, and §7.11. The file name is untrusted data from the document.</remarks>
    public PdfFileSpecification? File => ReadFileSpecification(NavigationNames.F);

    /// <summary>Gets the destination in the other file (<c>D</c>, required), or <see langword="null"/>; an explicit one names a 0-based page number.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.3, Table 203, and §12.3.2.</remarks>
    public PdfDestination? Destination => ReadDestination(NavigationNames.D, isRemote: true);

    /// <summary>Gets the structure destination in the other file (<c>SD</c>, PDF 2.0), whose first element is a structure element ID; it should take precedence over <see cref="Destination"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.3, Table 203, and §12.3.2.3.</remarks>
    public PdfExplicitDestination? StructureDestination => ReadDestination(NavigationNames.SD, isRemote: true) as PdfExplicitDestination;

    /// <summary>Gets whether to open the other file in a new window (<c>NewWindow</c>, PDF 1.2); <see langword="null"/> when absent: the processor's preference.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.3, Table 203.</remarks>
    public bool? NewWindow => ReadBoolean(ActionNames.NewWindow);

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(NavigationNames.F))
        {
            Report("A remote go-to action shall have an F entry naming the file; it has none.");
        }

        if (!Dictionary.ContainsKey(NavigationNames.D))
        {
            Report("A remote go-to action shall have a D entry; it has none, so it goes nowhere.");
        }
    }
}
