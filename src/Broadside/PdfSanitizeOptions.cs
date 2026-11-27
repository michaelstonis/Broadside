namespace Broadside;

/// <summary>What <see cref="PdfDocument.Sanitize(PdfSanitizeOptions)"/> removes from a document before it is passed on.</summary>
/// <remarks>
/// The defaults remove everything active (scripts, launch, submit, import and remote actions, media), embedded files, XFA, metadata,
/// thumbnails and hidden layers, and keep URI links, invisible text (OCR layers) and comments. ISO 32000-2 §12.6, §12.7.3, §7.11.4,
/// §14.13, §14.3, §8.11, §9.3.6 and §12.3.4.
/// </remarks>
public sealed class PdfSanitizeOptions
{
    /// <summary>Gets or sets the kinds of action removed; <see cref="PdfSanitizeActions.Default"/> by default (every kind but URI).</summary>
    /// <remarks>ISO 32000-2 §12.6.2 to §12.6.4; §7.7.4 (the name dictionary's <c>JavaScript</c> tree).</remarks>
    public PdfSanitizeActions Actions { get; set; } = PdfSanitizeActions.Default;

    /// <summary>Gets or sets a value indicating whether the interactive form's XFA form (<c>XFA</c>) is removed, with the catalog's <c>NeedsRendering</c>; true by default.</summary>
    /// <remarks>ISO 32000-2 §12.7.3, Table 224; §7.7.2 (<c>NeedsRendering</c>).</remarks>
    public bool RemoveXfa { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether embedded and associated files are removed: the <c>EmbeddedFiles</c> name tree, FileAttachment annotations, every <c>AF</c> array, the files embedded in other file specifications (<c>EF</c>) and the catalog's <c>Collection</c>; true by default.</summary>
    /// <remarks>ISO 32000-2 §7.11.4, §7.7.4 (Table 32), §12.5.6.15, §12.3.5 and §14.13. A PDF/A-3 document's associated files (such as a ZUGFeRD invoice) go too, and its PDF/A-3 claim then no longer holds: set this to false to keep them.</remarks>
    public bool RemoveEmbeddedFiles { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether metadata is removed: the document information dictionary (<c>Info</c>), the catalog's and every object's <c>Metadata</c> stream, and every <c>PieceInfo</c>; true by default.</summary>
    /// <remarks>ISO 32000-2 §14.3.2, §14.3.3 and §14.5. <c>Producer</c> is not written again.</remarks>
    public bool RemoveMetadata { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether pages' thumbnail images (<c>Thumb</c>) are removed; true by default.</summary>
    /// <remarks>ISO 32000-2 §12.3.4.</remarks>
    public bool RemoveThumbnails { get; set; } = true;

    /// <summary>Gets or sets what is done with optional content; <see cref="PdfSanitizeOptionalContent.RemoveHidden"/> by default.</summary>
    /// <remarks>ISO 32000-2 §8.11.2 to §8.11.4.</remarks>
    public PdfSanitizeOptionalContent OptionalContent { get; set; } = PdfSanitizeOptionalContent.RemoveHidden;

    /// <summary>Gets or sets a value indicating whether text drawn in rendering mode 3 (neither filled, stroked nor added to the clip) is removed; false by default, because OCR layers over scanned pages are such text.</summary>
    /// <remarks>ISO 32000-2 §9.3.6, Table 104. Whole text-showing operators are replaced by moves of the same length, so the text after them stays where it was. Mode 7 adds to the clip and is never removed.</remarks>
    public bool RemoveInvisibleText { get; set; }

    /// <summary>Gets or sets a value indicating whether comments are removed: markup annotations (§12.5.6.2) and pop-ups; false by default.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2 and §12.5.6.14.</remarks>
    public bool RemoveComments { get; set; }

    /// <summary>Sets <see cref="Actions"/>.</summary>
    /// <param name="actions">The kinds of action removed.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="actions"/> has a bit that is not a defined kind.</exception>
    public PdfSanitizeOptions WithActions(PdfSanitizeActions actions)
    {
        if ((actions & ~PdfSanitizeActions.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actions), actions, "Not a combination of PdfSanitizeActions kinds.");
        }

        Actions = actions;
        return this;
    }

    /// <summary>Sets <see cref="RemoveXfa"/>.</summary>
    /// <param name="remove">Whether the XFA form is removed.</param>
    /// <returns>These options.</returns>
    public PdfSanitizeOptions WithRemoveXfa(bool remove)
    {
        RemoveXfa = remove;
        return this;
    }

    /// <summary>Sets <see cref="RemoveEmbeddedFiles"/>.</summary>
    /// <param name="remove">Whether embedded and associated files are removed.</param>
    /// <returns>These options.</returns>
    public PdfSanitizeOptions WithRemoveEmbeddedFiles(bool remove)
    {
        RemoveEmbeddedFiles = remove;
        return this;
    }

    /// <summary>Sets <see cref="RemoveMetadata"/>.</summary>
    /// <param name="remove">Whether metadata is removed.</param>
    /// <returns>These options.</returns>
    public PdfSanitizeOptions WithRemoveMetadata(bool remove)
    {
        RemoveMetadata = remove;
        return this;
    }

    /// <summary>Sets <see cref="RemoveThumbnails"/>.</summary>
    /// <param name="remove">Whether thumbnails are removed.</param>
    /// <returns>These options.</returns>
    public PdfSanitizeOptions WithRemoveThumbnails(bool remove)
    {
        RemoveThumbnails = remove;
        return this;
    }

    /// <summary>Sets <see cref="OptionalContent"/>.</summary>
    /// <param name="mode">What is done with optional content.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not defined.</exception>
    public PdfSanitizeOptions WithOptionalContent(PdfSanitizeOptionalContent mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a defined PdfSanitizeOptionalContent.");
        }

        OptionalContent = mode;
        return this;
    }

    /// <summary>Sets <see cref="RemoveInvisibleText"/>.</summary>
    /// <param name="remove">Whether invisible text is removed.</param>
    /// <returns>These options.</returns>
    public PdfSanitizeOptions WithRemoveInvisibleText(bool remove)
    {
        RemoveInvisibleText = remove;
        return this;
    }

    /// <summary>Sets <see cref="RemoveComments"/>.</summary>
    /// <param name="remove">Whether comments are removed.</param>
    /// <returns>These options.</returns>
    public PdfSanitizeOptions WithRemoveComments(bool remove)
    {
        RemoveComments = remove;
        return this;
    }
}
