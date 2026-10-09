using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>An action: what happens when an outline item, a link or another trigger is activated. A live view over the action dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.6.2, Table 196. <see cref="ActionType"/> is the <c>S</c> entry. The types this version models are
/// <see cref="PdfGoToAction"/> (§12.6.4.2) and <see cref="PdfUriAction"/> (§12.6.4.8); every other type reads as a
/// <see cref="PdfUnknownAction"/>, kept as it is and never performed (parse-and-preserve), and gains its own view as it is modelled.
/// The <c>Next</c> chain (PDF 1.2) is reachable through <see cref="Dictionary"/>.
/// </para>
/// <para>Actions are data: reading one never performs it.</para>
/// </remarks>
public abstract class PdfAction
{
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

    private protected PdfDocument Document { get; }

    /// <summary>Gets the reference diagnostics name: the action's own, or the object holding it.</summary>
    private protected CosReference? Owner { get; private init; }

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
            "URI" => new PdfUriAction(document, dictionary, reference, type) { Owner = diagnosticReference },
            _ => new PdfUnknownAction(document, dictionary, reference, type) { Owner = diagnosticReference },
        };
        action.Check();
        return action;
    }

    /// <summary>Reports, once when the action is read, what its type's entries lack.</summary>
    private protected virtual void Check()
    {
    }

    private protected void Report(string message) => Report(Document, message, Owner);

    private static void Report(PdfDocument document, string message, CosReference? reference) =>
        document.DiagnosticSink.Report(DiagnosticCodes.ActionInvalid, DiagnosticSeverity.Warning, message, objectReference: reference);
}
