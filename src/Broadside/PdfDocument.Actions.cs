using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <content>Actions and trigger events (issue #73): ISO 32000-2 §12.6 and the catalog's OpenAction, AA and URI entries (§7.7.2).</content>
public sealed partial class PdfDocument
{
    private static readonly CosName UriKey = new("URI");

    /// <summary>
    /// Gets the action performed when the document is opened (<c>OpenAction</c> as an action dictionary), or <see langword="null"/>
    /// when the entry is absent or is a destination (see <see cref="OpenDestination"/>).
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (PDF 1.1), and §12.6. Read from the catalog on every call.</remarks>
    public PdfAction? OpenAction
    {
        get
        {
            Catalog.TryGetValue(ActionNames.OpenAction, out CosObject? entry);
            return ReadOpenAction(entry) is CosDictionary ? PdfAction.Create(this, entry, CatalogReference) : null;
        }
    }

    /// <summary>
    /// Gets the destination shown when the document is opened (<c>OpenAction</c> as a destination array), or <see langword="null"/>
    /// when the entry is absent or is an action (see <see cref="OpenAction"/>): the top of the first page.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29, and §12.3.2. The entry shall be an array or a dictionary; a name or string is read as a named
    /// destination with an <c>OpenActionInvalid</c> diagnostic, and any other value as absent with the same diagnostic.
    /// </remarks>
    public PdfDestination? OpenDestination
    {
        get
        {
            Catalog.TryGetValue(ActionNames.OpenAction, out CosObject? entry);
            return ReadOpenAction(entry) is CosArray or CosName or CosString ? PdfDestination.Create(this, entry, isRemote: false, CatalogReference) : null;
        }
    }

    /// <summary>Gets the document's additional actions (<c>AA</c>, PDF 1.4): scripts run before and after closing, saving and printing; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29, and §12.6.3, Table 200. Read from the catalog on every call; reading never creates the entry.</remarks>
    public PdfDocumentAdditionalActions? AdditionalActions =>
        PdfAdditionalActions.Create(this, Catalog.TryGetValue(ActionNames.AA, out CosObject? aa) ? aa : null, CatalogReference, static (d, a, r, o) => new PdfDocumentAdditionalActions(d, a, r, o));

    /// <summary>
    /// Gets the base URI relative URI actions are resolved against (the catalog's <c>URI</c> dictionary's <c>Base</c>, PDF 1.1), or
    /// <see langword="null"/> when there is none or it is not an absolute URI (with a diagnostic).
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29, and §12.6.4.8, Table 211. See <see cref="PdfUriAction.ResolveUri"/>.</remarks>
    public Uri? UriBase
    {
        get
        {
            if (Resolve(Catalog.TryGetValue(UriKey, out CosObject? entry) ? entry : null) is not CosDictionary uri ||
                Resolve(uri.TryGetValue(ActionNames.Base, out CosObject? value) ? value : null) is not { } baseValue ||
                baseValue is CosNull)
            {
                return null;
            }

            if (baseValue is CosString text && Uri.TryCreate(PdfUriAction.DecodeUri(text.Bytes, out _), UriKind.Absolute, out Uri? result))
            {
                return result;
            }

            CatalogView.Report(DiagnosticCodes.CatalogEntryInvalid, "The catalog's URI dictionary's Base shall be an absolute URI string; it is ignored.");
            return null;
        }
    }

    /// <summary>Reads an action dictionary through the typed view of its type.</summary>
    /// <param name="value">The action dictionary, or a reference to it, from this document: an <c>A</c> entry, an element of <c>Next</c>, a value of an additional-actions dictionary.</param>
    /// <param name="ownerReference">The indirect object holding <paramref name="value"/>, for diagnostics when the action is direct.</param>
    /// <returns>
    /// The action; <see langword="null"/> when <paramref name="value"/> is absent or resolves to null, or, with an <c>ActionInvalid</c>
    /// diagnostic, when it is not a dictionary with an <c>S</c> name.
    /// </returns>
    /// <remarks>
    /// ISO 32000-2 §12.6.2 and §12.6.4. The entry point for actions held by objects the document model does not type yet (link,
    /// screen and widget annotations, form fields). The action's own deviations are reported now, so strict mode throws here.
    /// </remarks>
    public PdfAction? GetAction(CosObject? value, CosReference? ownerReference = null) => PdfAction.Create(this, value, ownerReference);

    /// <summary>Reads an annotation's additional-actions dictionary (its <c>AA</c> entry).</summary>
    /// <param name="value">The <c>AA</c> dictionary, or a reference to it.</param>
    /// <param name="ownerReference">The annotation, for diagnostics when the dictionary is direct.</param>
    /// <returns>The view; <see langword="null"/> when absent, or, with an <c>AdditionalActionsInvalid</c> diagnostic, not a dictionary.</returns>
    /// <remarks>
    /// ISO 32000-2 §12.6.3, Table 197. For a widget merged with its form field the same dictionary also holds Table 199's field triggers:
    /// read it with <see cref="GetFieldAdditionalActions"/> too, since <c>C</c> means calculate there.
    /// </remarks>
    public PdfAnnotationAdditionalActions? GetAnnotationAdditionalActions(CosObject? value, CosReference? ownerReference = null) =>
        PdfAdditionalActions.Create(this, value, ownerReference, static (d, a, r, o) => new PdfAnnotationAdditionalActions(d, a, r, o));

    /// <summary>Reads a form field's additional-actions dictionary (its <c>AA</c> entry, PDF 1.3).</summary>
    /// <param name="value">The <c>AA</c> dictionary, or a reference to it.</param>
    /// <param name="ownerReference">The field, for diagnostics when the dictionary is direct.</param>
    /// <returns>The view; <see langword="null"/> when absent, or, with an <c>AdditionalActionsInvalid</c> diagnostic, not a dictionary.</returns>
    /// <remarks>ISO 32000-2 §12.6.3, Table 199. The field's own entry: <c>AA</c> is not inheritable (§12.7.4.1, Table 226).</remarks>
    public PdfFieldAdditionalActions? GetFieldAdditionalActions(CosObject? value, CosReference? ownerReference = null) =>
        PdfAdditionalActions.Create(this, value, ownerReference, static (d, a, r, o) => new PdfFieldAdditionalActions(d, a, r, o));

    /// <summary>Resolves the catalog's OpenAction, reporting a value of a type Table 29 does not allow.</summary>
    private CosObject ReadOpenAction(CosObject? entry)
    {
        CosObject value = Resolve(entry);
        if (value is not (CosNull or CosArray or CosDictionary))
        {
            _diagnostics.Report(
                DiagnosticCodes.OpenActionInvalid,
                DiagnosticSeverity.Warning,
                value is CosName or CosString
                    ? "The catalog's OpenAction shall be a destination array or an action dictionary; it is a name or string, read as a named destination."
                    : "The catalog's OpenAction shall be a destination array or an action dictionary; it is ignored.",
                objectReference: entry as CosReference ?? CatalogReference);
        }

        return value;
    }
}
