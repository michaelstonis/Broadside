using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>An additional-actions dictionary: the actions triggered by events other than activation. A live view.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.3, Tables 197 to 200. The same key means different events for different owners (<c>C</c> is a page's close but a
/// field's calculate), so the dictionary is typed by its owner: <see cref="PdfAnnotationAdditionalActions"/>,
/// <see cref="PdfPageAdditionalActions"/>, <see cref="PdfFieldAdditionalActions"/> and <see cref="PdfDocumentAdditionalActions"/>.
/// <see cref="Actions"/> lists every entry, triggers the table does not define included. Reading never writes to the dictionary.
/// </remarks>
public abstract class PdfAdditionalActions
{
    private protected PdfAdditionalActions(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosReference? owner)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
        Owner = reference ?? owner;
    }

    /// <summary>Gets the additional-actions dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.6.3.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the dictionary, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets every trigger and its action, in dictionary order; an entry that is not an action is skipped with a diagnostic.</summary>
    /// <remarks>ISO 32000-2 §12.6.3.</remarks>
    public IReadOnlyList<KeyValuePair<CosName, PdfAction>> Actions
    {
        get
        {
            var actions = new List<KeyValuePair<CosName, PdfAction>>(Dictionary.Count);
            foreach (KeyValuePair<CosName, CosObject> entry in Dictionary)
            {
                if (PdfAction.Create(Document, entry.Value, Owner) is { } action)
                {
                    actions.Add(new(entry.Key, action));
                }
            }

            return actions;
        }
    }

    private protected PdfDocument Document { get; }

    private protected CosReference? Owner { get; }

    /// <summary>Returns the action for <paramref name="trigger"/>, or <see langword="null"/> when there is none.</summary>
    /// <param name="trigger">The key, for example <c>O</c> or <c>WC</c>.</param>
    /// <returns>The action; <see langword="null"/> when absent or, with a diagnostic, not an action dictionary.</returns>
    /// <remarks>ISO 32000-2 §12.6.3.</remarks>
    public PdfAction? GetAction(CosName trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return Dictionary.TryGetValue(trigger, out CosObject? value) ? PdfAction.Create(Document, value, Owner) : null;
    }

    /// <summary>Reads an <c>AA</c> entry: absent or null reads as none; another type than a dictionary as none with a diagnostic.</summary>
    internal static T? Create<T>(PdfDocument document, CosObject? value, CosReference? owner, Func<PdfDocument, CosDictionary, CosReference?, CosReference?, T> create)
        where T : PdfAdditionalActions
    {
        switch (document.Resolve(value))
        {
            case CosNull:
                return null;
            case CosDictionary dictionary:
                return create(document, dictionary, value as CosReference, owner);
            default:
                document.DiagnosticSink.Report(
                    DiagnosticCodes.AdditionalActionsInvalid,
                    DiagnosticSeverity.Warning,
                    "An AA entry shall be an additional-actions dictionary; it is ignored.",
                    objectReference: value as CosReference ?? owner);
                return null;
        }
    }

    private protected PdfAction? Get(string trigger) => GetAction(new CosName(trigger));
}

/// <summary>The additional actions of an annotation: cursor, mouse button, focus and page events.</summary>
/// <remarks>ISO 32000-2 §12.6.3, Table 197 (PDF 1.2; PO, PC, PV and PI PDF 1.5).</remarks>
public sealed class PdfAnnotationAdditionalActions : PdfAdditionalActions
{
    internal PdfAnnotationAdditionalActions(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosReference? owner)
        : base(document, dictionary, reference, owner)
    {
    }

    /// <summary>Gets the action when the cursor enters the annotation's active area (<c>E</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? CursorEnter => Get("E");

    /// <summary>Gets the action when the cursor exits the annotation's active area (<c>X</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? CursorExit => Get("X");

    /// <summary>Gets the action when the mouse button is pressed inside the active area (<c>D</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? MouseDown => Get("D");

    /// <summary>Gets the action when the mouse button is released inside the active area (<c>U</c>); the annotation's <c>A</c> takes precedence over it.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? MouseUp => Get("U");

    /// <summary>Gets the action when the annotation receives the input focus (<c>Fo</c>; widget annotations only).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? Focus => Get("Fo");

    /// <summary>Gets the action when the annotation loses the input focus (<c>Bl</c>; widget annotations only).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? Blur => Get("Bl");

    /// <summary>Gets the action when the page holding the annotation is opened (<c>PO</c>, PDF 1.5), after the page's own open action.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? PageOpen => Get("PO");

    /// <summary>Gets the action when the page holding the annotation is closed (<c>PC</c>, PDF 1.5), before the page's own close action.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? PageClose => Get("PC");

    /// <summary>Gets the action when the page holding the annotation becomes visible (<c>PV</c>, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? PageVisible => Get("PV");

    /// <summary>Gets the action when the page holding the annotation is no longer visible (<c>PI</c>, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 197.</remarks>
    public PdfAction? PageInvisible => Get("PI");
}

/// <summary>The additional actions of a page: opening and closing it.</summary>
/// <remarks>ISO 32000-2 §12.6.3, Table 198 (PDF 1.2).</remarks>
public sealed class PdfPageAdditionalActions : PdfAdditionalActions
{
    internal PdfPageAdditionalActions(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosReference? owner)
        : base(document, dictionary, reference, owner)
    {
    }

    /// <summary>Gets the action when the page is opened (<c>O</c>), after the document's open action.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 198.</remarks>
    public PdfAction? Open => Get("O");

    /// <summary>Gets the action when the page is closed (<c>C</c>), before any other page is opened.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 198.</remarks>
    public PdfAction? Close => Get("C");
}

/// <summary>The additional actions of a form field: keystroke, format, validate and calculate scripts.</summary>
/// <remarks>ISO 32000-2 §12.6.3, Table 199 (PDF 1.3). Not defined for button fields.</remarks>
public sealed class PdfFieldAdditionalActions : PdfAdditionalActions
{
    internal PdfFieldAdditionalActions(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosReference? owner)
        : base(document, dictionary, reference, owner)
    {
    }

    /// <summary>Gets the action when the user changes a character or a list selection (<c>K</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 199.</remarks>
    public PdfAction? Keystroke => Get("K");

    /// <summary>Gets the action before the field's value is formatted for display (<c>F</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 199.</remarks>
    public PdfAction? Format => Get("F");

    /// <summary>Gets the action when the field's value changes, to validate it (<c>V</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 199.</remarks>
    public PdfAction? Validate => Get("V");

    /// <summary>Gets the action that recalculates the field's value when another field changes (<c>C</c>), in the order of the form's <c>CO</c>.</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 199, and §12.7.3, Table 224.</remarks>
    public PdfAction? Calculate => Get("C");
}

/// <summary>The additional actions of the document: before and after closing, saving and printing.</summary>
/// <remarks>ISO 32000-2 §12.6.3, Table 200 (PDF 1.4).</remarks>
public sealed class PdfDocumentAdditionalActions : PdfAdditionalActions
{
    internal PdfDocumentAdditionalActions(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosReference? owner)
        : base(document, dictionary, reference, owner)
    {
    }

    /// <summary>Gets the action before the document closes (<c>WC</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 200.</remarks>
    public PdfAction? WillClose => Get("WC");

    /// <summary>Gets the action before the document is saved (<c>WS</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 200.</remarks>
    public PdfAction? WillSave => Get("WS");

    /// <summary>Gets the action after the document is saved (<c>DS</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 200.</remarks>
    public PdfAction? DidSave => Get("DS");

    /// <summary>Gets the action before the document is printed (<c>WP</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 200.</remarks>
    public PdfAction? WillPrint => Get("WP");

    /// <summary>Gets the action after the document is printed (<c>DP</c>).</summary>
    /// <remarks>ISO 32000-2 §12.6.3, Table 200.</remarks>
    public PdfAction? DidPrint => Get("DP");
}
