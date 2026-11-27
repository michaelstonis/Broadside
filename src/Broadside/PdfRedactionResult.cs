using Broadside.Diagnostics;

namespace Broadside;

/// <summary>
/// What applying a redaction removed, the diagnostics it recorded, and the save options that write it so that nothing removed stays
/// in the file.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.6.23. The removed content is gone from the document model, but an incremental update (§7.5.6) appends to the
/// original bytes, which still hold it: save with <see cref="CreateSaveOptions"/>, a full rewrite that drops the objects the redaction
/// left unreachable.
/// </remarks>
public sealed class PdfRedactionResult
{
    internal PdfRedactionResult(int glyphs, int images, int paths, int annotations, int fields, IReadOnlyList<Diagnostic> diagnostics)
    {
        GlyphsRemoved = glyphs;
        ImagesChanged = images;
        PathsRemoved = paths;
        AnnotationsRemoved = annotations;
        FieldsCleared = fields;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the number of glyphs removed from text-showing operators.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int GlyphsRemoved { get; }

    /// <summary>Gets the number of images removed or rewritten.</summary>
    /// <remarks>ISO 32000-2 §8.9.</remarks>
    public int ImagesChanged { get; }

    /// <summary>Gets the number of subpaths removed or cut.</summary>
    /// <remarks>ISO 32000-2 §8.5.</remarks>
    public int PathsRemoved { get; }

    /// <summary>Gets the number of annotations removed: the Redact annotations applied and the annotations under the areas.</summary>
    /// <remarks>ISO 32000-2 §12.5.2.</remarks>
    public int AnnotationsRemoved { get; }

    /// <summary>Gets the number of form fields whose value was cleared.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.</remarks>
    public int FieldsCleared { get; }

    /// <summary>Gets the <c>RedactionDiagnostics</c> recorded: every fallback to a coarser removal, and the signatures invalidated.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.23.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Creates the options of the save that writes the redaction: a full rewrite, never an incremental update, that removes the objects
    /// the redaction left unreachable (the old content streams, images, forms and annotations).
    /// </summary>
    /// <returns>New options, which the caller may change further.</returns>
    /// <remarks>ISO 32000-2 §7.5.6: an incremental update keeps the earlier revision, and so the removed content, in the file.</remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "The save options belong to the result, as PdfAConversion.CreateSaveOptions does.")]
    public PdfSaveOptions CreateSaveOptions() => new PdfSaveOptions()
        .WithIncrementalUpdate(false)
        .WithRemoveUnreachableObjects(true);
}
