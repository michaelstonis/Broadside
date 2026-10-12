using Broadside.Objects;

namespace Broadside;

/// <summary>A hide action: hides or shows annotations by setting or clearing their Hidden flags.</summary>
/// <remarks>ISO 32000-2 §12.6.4.11, Table 214 (PDF 1.2).</remarks>
public sealed class PdfHideAction : PdfAction
{
    internal PdfHideAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Hide;

    /// <summary>
    /// Gets the annotations to hide or show (<c>T</c>, required): annotation dictionaries, and fully qualified field names whose widget
    /// annotations are meant. A single target reads as a list of one; an element of another type is skipped with a diagnostic.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.6.4.11, Table 214.</remarks>
    public IReadOnlyList<PdfActionTarget> Targets =>
        ReadTargets(ActionNames.T, "an annotation dictionary, a text string or an array of them") ?? [];

    /// <summary>Gets whether to hide (<see langword="true"/>, the default) or show (<see langword="false"/>) the annotations (<c>H</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.11, Table 214.</remarks>
    public bool Hide => ReadBoolean(ActionNames.H) ?? true;

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(ActionNames.T))
        {
            Report("A hide action shall have a T entry naming the annotations; it has none.");
        }
    }
}
