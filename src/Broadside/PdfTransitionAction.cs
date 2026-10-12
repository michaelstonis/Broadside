using Broadside.Objects;

namespace Broadside;

/// <summary>A transition action: shows the result of the previous actions of a sequence with a transition effect.</summary>
/// <remarks>ISO 32000-2 §12.6.4.15, Table 219 (PDF 1.5), and §12.4.4.2 (transition dictionaries, exposed raw).</remarks>
public sealed class PdfTransitionAction : PdfAction
{
    internal PdfTransitionAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Transition;

    /// <summary>Gets the transition dictionary (<c>Trans</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.15, Table 219, and §12.4.4.2, Table 164.</remarks>
    public CosDictionary? Transition => ReadDictionary(ActionNames.Trans);

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(ActionNames.Trans))
        {
            Report("A transition action shall have a Trans entry; it has none.");
        }
    }
}
