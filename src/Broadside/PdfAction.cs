using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>An action: what happens when an outline item, a link or another trigger is activated. A live view over the action dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.6.2, Table 196. <see cref="ActionType"/> is the <c>S</c> entry and <see cref="Kind"/> the type it names in Table 201
/// (§12.6.4.1); each type has its own sealed view, and a type the table does not list reads as a <see cref="PdfUnknownAction"/>. The
/// <c>Next</c> entry (PDF 1.2) is <see cref="Next"/>, one level; <see cref="EnumerateActionTree"/> walks the whole tree in execution
/// order, safely when it is a graph.
/// </para>
/// <para>
/// Actions are data: reading one never performs it, and the library has no way to perform one. Scripts (<see cref="PdfJavaScriptAction"/>,
/// <see cref="PdfRenditionAction"/>), multimedia (<see cref="PdfSoundAction"/>, <see cref="PdfMovieAction"/>,
/// <see cref="PdfRichMediaExecuteAction"/>) and 3D views (<see cref="PdfGoTo3DViewAction"/>) are out of scope: they are exposed as data
/// and preserved byte for byte when the document is saved, with an <see cref="DiagnosticSeverity.Information"/> diagnostic
/// (<c>ActionOutOfScope</c>) when one is read. Views never write to the dictionaries they read.
/// </para>
/// <para>
/// Diagnostics are lazy: an action's missing or malformed entries are reported when the action is first read through a view, so in strict
/// mode the property or method that reads an invalid action throws, never <see cref="PdfDocument.Open(string)"/>.
/// </para>
/// </remarks>
public abstract class PdfAction
{
    /// <summary>The default depth limit of <see cref="EnumerateActionTree"/>: how many <c>Next</c> levels below an action it follows.</summary>
    public const int DefaultMaxTreeDepth = 64;

    private protected PdfAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
        ActionType = actionType;
    }

    /// <summary>Gets the action dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.6.2, Table 196.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the action dictionary, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the type of action, the <c>S</c> entry, such as <c>GoTo</c> or <c>URI</c>.</summary>
    /// <remarks>ISO 32000-2 §12.6.2, Table 196, and §12.6.4.1, Table 201.</remarks>
    public CosName ActionType { get; }

    /// <summary>Gets the type of action <see cref="ActionType"/> names in Table 201, or <see cref="PdfActionKind.Unknown"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.1, Table 201.</remarks>
    public abstract PdfActionKind Kind { get; }

    /// <summary>
    /// Gets the actions performed after this one (<c>Next</c>, PDF 1.2), in order: none, the single action dictionary, or the
    /// action dictionaries of the array. Each may have a <c>Next</c> of its own; see <see cref="EnumerateActionTree"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.6.2, Table 196 and Note 1. Read from the dictionary on every call. An element that is not an action dictionary
    /// is skipped with an <c>ActionNextInvalid</c> diagnostic, as is a <c>Next</c> that is neither a dictionary nor an array.
    /// </remarks>
    public IReadOnlyList<PdfAction> Next
    {
        get
        {
            if (!Dictionary.TryGetValue(NavigationNames.Next, out CosObject? next))
            {
                return [];
            }

            switch (Document.Resolve(next))
            {
                case CosNull:
                    return [];
                case CosDictionary:
                    return Create(Document, next, DiagnosticReference) is { } single ? [single] : [];
                case CosArray array:
                    var actions = new List<PdfAction>(array.Count);
                    foreach (CosObject element in array)
                    {
                        if (Document.Resolve(element) is not CosDictionary)
                        {
                            Report(DiagnosticCodes.ActionNextInvalid, $"An element of the {ActionType.Value} action's Next array is not an action dictionary; it is skipped.");
                        }
                        else if (Create(Document, element, DiagnosticReference) is { } following)
                        {
                            actions.Add(following);
                        }
                    }

                    return actions;
                default:
                    Report(DiagnosticCodes.ActionNextInvalid, $"The {ActionType.Value} action's Next entry shall be an action dictionary or an array of them; it is ignored.");
                    return [];
            }
        }
    }

    private protected PdfDocument Document { get; }

    /// <summary>Gets the reference diagnostics name: the action's own, or the object holding it.</summary>
    private protected CosReference? Owner { get; private init; }

    /// <summary>Gets the object diagnostics about this action name: its own reference, else the object holding it.</summary>
    private protected CosReference? DiagnosticReference => Reference ?? Owner;

    /// <summary>
    /// Enumerates this action and every action after it, depth first in the order they would be performed: this action, then each action
    /// of its <see cref="Next"/> followed by that action's own tree (§12.6.2, Note 1).
    /// </summary>
    /// <param name="maxDepth">How many <c>Next</c> levels below this action to follow; deeper actions are cut with an <c>ActionTreeTooDeep</c> diagnostic.</param>
    /// <returns>The actions, read lazily as the enumeration advances.</returns>
    /// <remarks>
    /// ISO 32000-2 §12.6.2, Note 1: "self-referential actions ought not be executed more than once". Each action dictionary is yielded
    /// once, at its first position: an action reached again (shared by two branches, or a cycle) is not repeated, and a cycle, legal
    /// but anomalous, is recorded as an <c>ActionCycle</c> diagnostic of severity <see cref="DiagnosticSeverity.Information"/>. The walk
    /// is iterative and never writes to the dictionaries.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDepth"/> is negative.</exception>
    public IEnumerable<PdfAction> EnumerateActionTree(int maxDepth = DefaultMaxTreeDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        return WalkTree(maxDepth);
    }

    /// <summary>Reads an action dictionary.</summary>
    /// <param name="document">The document.</param>
    /// <param name="value">The action dictionary or a reference to it.</param>
    /// <param name="owner">The indirect object holding the action, for diagnostics when the action is direct.</param>
    /// <returns>
    /// The action; <see langword="null"/> when the value is absent, or, with a diagnostic, when it is not a dictionary or has no
    /// <c>S</c> name.
    /// </returns>
    internal static PdfAction? Create(PdfDocument document, CosObject? value, CosReference? owner)
    {
        var reference = value as CosReference;
        CosReference? diagnosticReference = reference ?? owner;
        CosObject resolved = document.Resolve(value);
        if (resolved is CosNull)
        {
            return null;
        }

        if (resolved is not CosDictionary dictionary || !dictionary.TryGetValue(NavigationNames.S, out CosObject? s) || document.Resolve(s) is not CosName type)
        {
            Report(document, "An action is not a dictionary with an S entry naming its type; it is ignored.", diagnosticReference);
            return null;
        }

        PdfAction action = type.Value switch
        {
            "GoTo" => new PdfGoToAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "GoToR" => new PdfRemoteGoToAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "GoToE" => new PdfEmbeddedGoToAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "GoToDp" => new PdfGoToDocumentPartAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Launch" => new PdfLaunchAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Thread" => new PdfThreadAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "URI" => new PdfUriAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Sound" => new PdfSoundAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Movie" => new PdfMovieAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Hide" => new PdfHideAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Named" => new PdfNamedAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "SubmitForm" => new PdfSubmitFormAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "ResetForm" => new PdfResetFormAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "ImportData" => new PdfImportDataAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "SetOCGState" => new PdfSetOcgStateAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Rendition" => new PdfRenditionAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "Trans" => new PdfTransitionAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "GoTo3DView" => new PdfGoTo3DViewAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "JavaScript" => new PdfJavaScriptAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            "RichMediaExecute" => new PdfRichMediaExecuteAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            _ => new PdfUnknownAction(document, dictionary, reference, type) { Owner = diagnosticReference },
        };
        action.Check();
        return action;
    }

    /// <summary>Reports, once when the action is read, what its type's entries lack.</summary>
    private protected virtual void Check()
    {
    }

    /// <summary>Reports a missing required entry or another "shall" the action breaks (<c>ActionInvalid</c>).</summary>
    private protected void Report(string message) => Report(Document, message, DiagnosticReference);

    /// <summary>Reports a deviation under <paramref name="code"/>.</summary>
    private protected void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: DiagnosticReference);

    /// <summary>Records that the action is out of scope: kept as data, never performed (<c>ActionOutOfScope</c>, Information).</summary>
    private protected void ReportOutOfScope(string what) =>
        Report(DiagnosticCodes.ActionOutOfScope, $"A {ActionType.Value} action is kept as data and never performed: {what} is out of scope.", DiagnosticSeverity.Information);

    /// <summary>Returns the entry for <paramref name="key"/>, resolved; <see langword="null"/> when absent or a reference to nothing.</summary>
    private protected CosObject? Get(CosName key) =>
        Dictionary.TryGetValue(key, out CosObject? value) && Document.Resolve(value) is not CosNull and var resolved ? resolved : null;

    /// <summary>Returns the entry for <paramref name="key"/> as stored when it is an indirect reference.</summary>
    private protected CosReference? GetReference(CosName key) => Dictionary.TryGetValue(key, out CosObject? value) ? value as CosReference : null;

    /// <summary>Reports an entry of the wrong type (<c>ActionEntryInvalid</c>), which is then read as absent.</summary>
    private protected void ReportEntry(CosName key, string expected) =>
        Report(DiagnosticCodes.ActionEntryInvalid, $"The {ActionType.Value} action's {key.Value} entry shall be {expected}; it is ignored.");

    /// <summary>Reads a dictionary entry; another type reads as absent with a diagnostic.</summary>
    private protected CosDictionary? ReadDictionary(CosName key) => Read<CosDictionary>(key, "a dictionary");

    /// <summary>Reads a stream entry; another type reads as absent with a diagnostic.</summary>
    private protected CosStream? ReadStream(CosName key) => Read<CosStream>(key, "a stream");

    /// <summary>Reads a string entry as stored; another type reads as absent with a diagnostic.</summary>
    private protected CosString? ReadString(CosName key) => Read<CosString>(key, "a string");

    /// <summary>Reads a name entry; another type reads as absent with a diagnostic.</summary>
    private protected CosName? ReadName(CosName key) => Read<CosName>(key, "a name");

    /// <summary>Reads a boolean entry; another type reads as absent with a diagnostic.</summary>
    private protected bool? ReadBoolean(CosName key) => Read<CosBoolean>(key, "a boolean")?.Value;

    /// <summary>Reads a text string entry (§7.9.2.2), decoded; another type reads as absent with a diagnostic.</summary>
    private protected string? ReadText(CosName key) => ReadString(key)?.DecodeText();

    /// <summary>Reads an integer entry that fits in 32 bits; anything else reads as absent with a diagnostic.</summary>
    private protected int? ReadInteger(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosInteger { Value: >= int.MinValue and <= int.MaxValue } integer:
                return (int)integer.Value;
            default:
                ReportEntry(key, "an integer");
                return null;
        }
    }

    /// <summary>Reads a number entry; anything else reads as absent with a diagnostic.</summary>
    private protected double? ReadNumber(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosNumber number:
                return number.ToDouble();
            default:
                ReportEntry(key, "a number");
                return null;
        }
    }

    /// <summary>Reads a file specification entry (§7.11): a string or a dictionary; anything else reads as absent with a diagnostic.</summary>
    private protected PdfFileSpecification? ReadFileSpecification(CosName key)
    {
        if (Get(key) is null)
        {
            return null;
        }

        PdfFileSpecification? file = PdfFileSpecification.Create(Document, Dictionary[key]);
        if (file is null)
        {
            ReportEntry(key, "a file specification (a string or a dictionary)");
        }

        return file;
    }

    /// <summary>Reads a text string or text stream entry (§7.9.2.2, §7.9.3), decoded; anything else reads as absent with a diagnostic.</summary>
    /// <remarks>A stream is decoded through its filters into new memory: the stream itself is never changed.</remarks>
    private protected string? ReadScript(CosName key)
    {
        switch (Get(key))
        {
            case null:
                return null;
            case CosString text:
                return text.DecodeText();
            case CosStream stream:
                return TextStringDecoder.Decode(Document.DecodeStream(stream).Span);
            default:
                ReportEntry(key, "a text string or a text stream");
                return null;
        }
    }

    /// <summary>Reads an annotation or field target, or an array of them (Tables 214, 239, 241); <see langword="null"/> when absent.</summary>
    private protected List<PdfActionTarget>? ReadTargets(CosName key, string expected) =>
        Dictionary.TryGetValue(key, out CosObject? value) ? PdfActionTarget.Read(Document, value, () => ReportEntry(key, expected)) : null;

    /// <summary>Reads a destination entry (§12.3.2) with the remote flag for go-to actions of other documents.</summary>
    private protected PdfDestination? ReadDestination(CosName key, bool isRemote) =>
        Dictionary.TryGetValue(key, out CosObject? value) ? PdfDestination.Create(Document, value, isRemote, DiagnosticReference) : null;

    private static void Report(PdfDocument document, string message, CosReference? reference) =>
        document.DiagnosticSink.Report(DiagnosticCodes.ActionInvalid, DiagnosticSeverity.Warning, message, objectReference: reference);

    private T? Read<T>(CosName key, string expected)
        where T : CosObject
    {
        switch (Get(key))
        {
            case null:
                return null;
            case T value:
                return value;
            default:
                ReportEntry(key, expected);
                return null;
        }
    }

    private IEnumerable<PdfAction> WalkTree(int maxDepth)
    {
        var visited = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { Dictionary };
        var onPath = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance) { Dictionary };
        var frames = new Stack<(PdfAction Action, IReadOnlyList<PdfAction> Next, int Index)>();
        yield return this;
        frames.Push((this, Next, 0));
        while (frames.TryPop(out (PdfAction Action, IReadOnlyList<PdfAction> Next, int Index) frame))
        {
            if (frame.Index >= frame.Next.Count)
            {
                onPath.Remove(frame.Action.Dictionary);
                continue;
            }

            frames.Push(frame with { Index = frame.Index + 1 });
            PdfAction child = frame.Next[frame.Index];
            if (!visited.Add(child.Dictionary))
            {
                if (onPath.Contains(child.Dictionary))
                {
                    frame.Action.Report(
                        DiagnosticCodes.ActionCycle,
                        $"The {frame.Action.ActionType.Value} action's Next leads back to an action it follows; the cycle is cut and that action is not repeated.",
                        DiagnosticSeverity.Information);
                }

                continue;
            }

            if (frames.Count > maxDepth)
            {
                frame.Action.Report(DiagnosticCodes.ActionTreeTooDeep, $"The action tree is deeper than {maxDepth} Next levels; the actions below are not read.");
                continue;
            }

            yield return child;
            onPath.Add(child.Dictionary);
            frames.Push((child, child.Next, 0));
        }
    }
}
