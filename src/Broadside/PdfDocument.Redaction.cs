using Broadside.Redaction;

namespace Broadside;

/// <content>Applying the Redact annotations of a document (issue #624).</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Applies every Redact annotation in the document with the default options, then removes the annotations: the content under each
    /// one's areas is removed and its overlay drawn in its place.
    /// </summary>
    /// <returns>What was removed, and the diagnostics recorded.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.23 (Table 195). As <see cref="Redact(PdfRedactionOptions?)"/>.</remarks>
    public PdfRedactionResult Redact() => Redact((PdfRedactionOptions?)null);

    /// <summary>
    /// Applies every Redact annotation in the document, then removes the annotations: the content under each one's areas is removed and
    /// its overlay drawn in its place.
    /// </summary>
    /// <param name="options">
    /// What to do with images, annotations and fields under the areas; <see langword="null"/> for the defaults. Each annotation draws
    /// its own overlay (<c>RO</c>, <c>IC</c>, <c>OverlayText</c>), so the options' overlay settings are not used.
    /// </param>
    /// <returns>What was removed, and the diagnostics recorded.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for a fallback to a coarser removal; the document is then unchanged.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.5.6.23 (Table 195), applied as <see cref="PdfPage.Redact(IReadOnlyList{Graphics.Quad}, PdfRedactionOptions?)"/> describes. A signed document is redacted too; its
    /// signatures then no longer verify, which a <c>RedactionInvalidatesSignatures</c> diagnostic records.
    /// </para>
    /// <para>
    /// The removed content stays recoverable from the original bytes until the document is saved with
    /// <see cref="PdfRedactionResult.CreateSaveOptions"/>: never save a redacted document as an incremental update.
    /// </para>
    /// </remarks>
    public PdfRedactionResult Redact(PdfRedactionOptions? options) => RedactionSession.ApplyAnnotations(this, options);
}
