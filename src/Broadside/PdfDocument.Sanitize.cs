using Broadside.Sanitizing;

namespace Broadside;

/// <content>Sanitizing a document before it is passed on (issue #648).</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Removes what is active or hidden in the document with the default options: scripts and the actions that reach outside it
    /// (all but URI), the XFA form, embedded and associated files, metadata, thumbnails and the content of layers that are off.
    /// </summary>
    /// <returns>What was removed, and the save options that write the result.</returns>
    /// <exception cref="InvalidOperationException">The document is encrypted and was opened with the user password, not the owner's.</exception>
    /// <remarks>ISO 32000-2 §12.6, §12.7.3, §7.11.4, §14.13, §14.3, §8.11 and §12.3.4; as <see cref="Sanitize(PdfSanitizeOptions)"/>.</remarks>
    public PdfSanitization Sanitize() => Sanitize(new PdfSanitizeOptions());

    /// <summary>Removes what <paramref name="options"/> names from the document: actions, the XFA form, files, metadata, thumbnails, hidden content and comments.</summary>
    /// <param name="options">What to remove.</param>
    /// <returns>What was removed, one line per kind, and the save options that write the result.</returns>
    /// <exception cref="InvalidOperationException">The document is encrypted and was opened with the user password, not the owner's.</exception>
    /// <remarks>
    /// <para>
    /// Actions (ISO 32000-2 §12.6.2 to §12.6.4): every place an action can be is visited, the catalog's <c>OpenAction</c> and
    /// <c>AA</c>, pages' <c>AA</c>, annotations' <c>A</c>, <c>PA</c> and <c>AA</c>, form fields' <c>AA</c>, outline items' <c>A</c>,
    /// and every <c>Next</c> sequence below them. A removed action is unlinked from its owner or from its predecessor's <c>Next</c>,
    /// and the actions after it take its place. A link annotation left without an action or destination is removed; an additional
    /// actions dictionary keeps its triggers whose actions stay. An action with no <c>S</c>, or of a type Table 201 does not define,
    /// is removed. The name dictionary's <c>JavaScript</c> tree goes with <see cref="PdfSanitizeActions.JavaScript"/> (§7.7.4).
    /// </para>
    /// <para>
    /// Content (§8.11.3, §9.3.6): each page's content, each form XObject it uses and each annotation appearance is rewritten once,
    /// through a <see cref="Content.ContentEditor"/>. Hidden content stops being drawn but keeps its effect on the graphics state:
    /// a hidden text-showing operator becomes a move of the same length, a hidden path is ended without painting (its clip stays),
    /// and hidden images, forms and shadings are removed.
    /// </para>
    /// <para>
    /// Sanitizing a signed document invalidates its signatures, which <see cref="PdfSanitization.Removed"/> notes as
    /// <c>signatures invalidated</c>; the signature fields stay. Removal never raises the document's version. Malformed entries where
    /// actions live are removed, not repaired. Save the result with <see cref="PdfSanitization.CreateSaveOptions"/>: an incremental
    /// update keeps everything removed in the file. A mutation: no read may run concurrently with it.
    /// </para>
    /// </remarks>
    public PdfSanitization Sanitize(PdfSanitizeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Security is { Access: PdfAccessLevel.User })
        {
            throw new InvalidOperationException("Sanitizing changes the document, which needs owner access: open it with the owner password (ISO 32000-2 §7.6.4).");
        }

        return DocumentSanitizer.Run(this, options);
    }
}
