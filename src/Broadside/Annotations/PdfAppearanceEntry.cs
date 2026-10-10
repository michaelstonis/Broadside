using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>One entry of an appearance dictionary: a single appearance stream, or a subdictionary of streams keyed by appearance state.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.5, Table 170: "Each entry ... shall contain either a single appearance stream or an appearance subdictionary",
/// whose keys are state names such as <c>On</c> and <c>Off</c>. A state value that is not a stream is skipped with a diagnostic.
/// </remarks>
public sealed class PdfAppearanceEntry
{
    private readonly PdfAnnotation _owner;

    internal PdfAppearanceEntry(PdfAnnotation owner, CosObject value)
    {
        _owner = owner;
        Value = value;
    }

    /// <summary>Gets the entry as stored: a stream, a dictionary, or a reference to one.</summary>
    public CosObject Value { get; }

    /// <summary>Gets a value indicating whether the entry is a subdictionary of appearance states rather than one stream.</summary>
    /// <remarks>ISO 32000-2 §12.5.5.</remarks>
    public bool HasStates => _owner.Document.Resolve(Value) is CosDictionary;

    /// <summary>Gets the single appearance stream, or <see langword="null"/> when the entry is a state subdictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170.</remarks>
    public PdfFormXObject? Form => PdfFormXObject.Create(_owner.Document, Value);

    /// <summary>Gets the names of the appearance states whose value is a stream, in the subdictionary's order; empty for a single stream.</summary>
    /// <remarks>ISO 32000-2 §12.5.5.</remarks>
    public IReadOnlyList<CosName> StateNames
    {
        get
        {
            if (_owner.Document.Resolve(Value) is not CosDictionary states)
            {
                return [];
            }

            var names = new List<CosName>(states.Count);
            foreach (KeyValuePair<CosName, CosObject> state in states)
            {
                if (IsStream(state.Key, state.Value))
                {
                    names.Add(state.Key);
                }
            }

            return names;
        }
    }

    /// <summary>Returns the appearance stream of the state <paramref name="state"/>.</summary>
    /// <param name="state">The state name.</param>
    /// <returns>
    /// The form XObject; for a single-stream entry, that stream whatever the state; <see langword="null"/> when the subdictionary has no
    /// stream for the state (legal: an absent state shows nothing).
    /// </returns>
    /// <remarks>ISO 32000-2 §12.5.5.</remarks>
    public PdfFormXObject? GetAppearance(CosName state)
    {
        ArgumentNullException.ThrowIfNull(state);
        CosObject resolved = _owner.Document.Resolve(Value);
        if (resolved is CosStream)
        {
            return Form;
        }

        return resolved is CosDictionary states && states.TryGetValue(state, out CosObject? value) && IsStream(state, value)
            ? PdfFormXObject.Create(_owner.Document, value)
            : null;
    }

    private bool IsStream(CosName state, CosObject value)
    {
        switch (_owner.Document.Resolve(value))
        {
            case CosStream:
                return true;
            default:
                _owner.Report(DiagnosticCodes.AppearanceEntryInvalid, $"The appearance state {state.Value} is not a stream; it is skipped.");
                return false;
        }
    }
}
