using Broadside.Objects;

namespace Broadside;

/// <summary>A set-OCG-state action: turns optional content groups on or off, or toggles them.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.13, Table 217 (PDF 1.5). <see cref="Changes"/> lists the <c>State</c> array's changes in order;
/// <see cref="ApplyTo"/> computes the states the action would leave, without changing the document.
/// </remarks>
public sealed class PdfSetOcgStateAction : PdfAction
{
    internal PdfSetOcgStateAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.SetOcgState;

    /// <summary>Gets the <c>State</c> array (required) as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.13, Table 217.</remarks>
    public CosArray? State => Read(ActionNames.State);

    /// <summary>
    /// Gets the changes of the <c>State</c> array, left to right: each group with the state name before it. A group before any state
    /// name, an unknown name and its groups, and an element that is neither a name nor a dictionary are skipped with a diagnostic.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.6.4.13, Table 217. A group may appear more than once; each change applies in order.</remarks>
    public IReadOnlyList<PdfOcgStateChange> Changes
    {
        get
        {
            var changes = new List<PdfOcgStateChange>();
            if (State is not { } state)
            {
                return changes;
            }

            PdfOcgStateOperation? operation = null;
            bool reported = false;
            foreach (CosObject element in state)
            {
                switch (Document.Resolve(element))
                {
                    case CosName name:
                        operation = name.Value switch
                        {
                            "ON" => PdfOcgStateOperation.On,
                            "OFF" => PdfOcgStateOperation.Off,
                            "Toggle" => PdfOcgStateOperation.Toggle,
                            _ => null,
                        };
                        reported |= operation is null && Invalid();
                        break;
                    case CosDictionary group when operation is { } current:
                        changes.Add(new PdfOcgStateChange(current, group, element as CosReference));
                        break;
                    default:
                        reported |= Invalid();
                        break;
                }
            }

            return changes;

            bool Invalid()
            {
                if (!reported)
                {
                    ReportEntry(ActionNames.State, "an array of ON, OFF or Toggle names each followed by optional content group dictionaries");
                }

                return true;
            }
        }
    }

    /// <summary>Gets whether radio-button relationships between groups are preserved (<c>PreserveRB</c>); <see langword="true"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.13, Table 217, and §8.11.4.3, Table 99 (RBGroups).</remarks>
    public bool PreserveRadioButtons => ReadBoolean(ActionNames.PreserveRB) ?? true;

    /// <summary>Returns the states <paramref name="state"/> becomes when this action's changes are applied to it. The document is not changed.</summary>
    /// <param name="state">The current states of the document's optional content groups.</param>
    /// <returns>The new states; <paramref name="state"/> itself when the action has no <c>State</c> array.</returns>
    /// <remarks>ISO 32000-2 §12.6.4.13, Table 217, through <see cref="PdfOptionalContentState.Apply"/>.</remarks>
    public PdfOptionalContentState ApplyTo(PdfOptionalContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return State is { } array ? state.Apply(array, PreserveRadioButtons) : state;
    }

    /// <inheritdoc/>
    private protected override void Check()
    {
        if (!Dictionary.ContainsKey(ActionNames.State))
        {
            Report("A set-OCG-state action shall have a State array; it has none, so it does nothing.");
        }
    }

    private CosArray? Read(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosArray array:
                return array;
            default:
                ReportEntry(key, "an array");
                return null;
        }
    }
}

/// <summary>One change of a set-OCG-state action: a group and what happens to it.</summary>
/// <remarks>ISO 32000-2 §12.6.4.13, Table 217.</remarks>
public sealed class PdfOcgStateChange
{
    internal PdfOcgStateChange(PdfOcgStateOperation operation, CosDictionary group, CosReference? groupReference)
    {
        Operation = operation;
        Group = group;
        GroupReference = groupReference;
    }

    /// <summary>Gets what happens to the group: on, off or toggled.</summary>
    public PdfOcgStateOperation Operation { get; }

    /// <summary>Gets the optional content group dictionary (§8.11.2.1).</summary>
    public CosDictionary Group { get; }

    /// <summary>Gets the indirect reference to <see cref="Group"/>, or <see langword="null"/>.</summary>
    public CosReference? GroupReference { get; }
}
