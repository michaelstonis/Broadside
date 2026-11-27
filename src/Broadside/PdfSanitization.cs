namespace Broadside;

/// <summary>
/// What <see cref="PdfDocument.Sanitize(PdfSanitizeOptions)"/> removed, and the save options that write the result so that nothing
/// removed stays in the file.
/// </summary>
/// <remarks>
/// The removed objects are gone from the document model, but an incremental update (ISO 32000-2 §7.5.6) appends to the original
/// bytes, which still hold them: save with <see cref="CreateSaveOptions"/>, a full rewrite that drops the objects sanitizing left
/// unreachable.
/// </remarks>
public sealed class PdfSanitization
{
    private readonly bool _metadataRemoved;

    internal PdfSanitization(IReadOnlyList<string> removed, bool metadataRemoved)
    {
        Removed = removed;
        _metadataRemoved = metadataRemoved;
    }

    /// <summary>
    /// Gets what was removed, one line per kind with its count (such as <c>JavaScript actions: 3</c>), in the order the kinds were
    /// first met, and the consequences: <c>signatures invalidated</c> for a signed document, and a note when a PDF/A-3 claim no
    /// longer holds. Empty when the document had nothing to remove.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.6, §12.7.3, §7.11.4, §14.13, §14.3, §8.11, §9.3.6, §12.3.4 and §12.8.</remarks>
    public IReadOnlyList<string> Removed { get; }

    /// <summary>
    /// Creates the options of the save that writes the sanitized document: a full rewrite, never an incremental update, that removes
    /// the objects sanitizing left unreachable (scripts, files, metadata streams, old content streams); when metadata was removed, the
    /// save does not record a modification date either, so no Info dictionary or XMP packet is written again.
    /// </summary>
    /// <returns>New options, which the caller may change further.</returns>
    /// <remarks>ISO 32000-2 §7.5.6: an incremental update keeps the earlier revision, and so everything removed, in the file.</remarks>
    public PdfSaveOptions CreateSaveOptions() => new PdfSaveOptions()
        .WithIncrementalUpdate(false)
        .WithRemoveUnreachableObjects(true)
        .WithUpdateModificationDate(!_metadataRemoved);
}
