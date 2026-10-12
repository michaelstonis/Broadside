namespace Broadside;

/// <summary>Options for one save: how the written file is laid out, how its objects are numbered and which objects it holds.</summary>
/// <remarks>
/// ISO 32000-2 §7.5. Separate from <see cref="PdfOptions"/>, which configures reading, because a document can be saved several times
/// in different layouts. The defaults write a classic cross-reference table, which every reader supports.
/// </remarks>
public sealed class PdfSaveOptions
{
    private PdfCrossReferenceLayout _crossReferenceLayout = PdfCrossReferenceLayout.Table;
    private PdfObjectNumbering _objectNumbering = PdfObjectNumbering.Automatic;

    /// <summary>
    /// Gets or sets how the file stores its cross-reference information and objects. The default is
    /// <see cref="PdfCrossReferenceLayout.Table"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.7, §7.5.8. See <see cref="PdfCrossReferenceLayout"/> for the combinations and their versions.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined layout.</exception>
    public PdfCrossReferenceLayout CrossReferenceLayout
    {
        get => _crossReferenceLayout;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a cross-reference layout.");
            }

            _crossReferenceLayout = value;
        }
    }

    /// <summary>
    /// Gets or sets how the written objects are numbered. The default, <see cref="PdfObjectNumbering.Automatic"/>, keeps the
    /// document's numbers unless they are too large or too sparse to write compactly.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.3.10, §7.5.4. See <see cref="PdfObjectNumbering"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined numbering.</exception>
    public PdfObjectNumbering ObjectNumbering
    {
        get => _objectNumbering;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not an object numbering.");
            }

            _objectNumbering = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether objects the trailer does not reach are left out of the written file (garbage collection).
    /// The default is <see langword="false"/>: every object the cross-reference information lists is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.5 and §7.3.10. An object is reachable when a chain of indirect references leads to it from the trailer: from the
    /// catalog (<c>Root</c>), the document information dictionary (<c>Info</c>) or any other trailer entry. A page removed from the page
    /// tree, an annotation removed from its page's <c>Annots</c>, and everything only they referred to, are not written. An object
    /// reached only as a stream's indirect <c>Length</c> is not written either; the stream gets a direct length (§7.3.8.2).
    /// </para>
    /// <para>
    /// Objects the library does not interpret (JavaScript, XFA, 3D, rich media, multimedia) are kept like any other object when they
    /// are reachable, with their data unchanged. With <see cref="PdfObjectNumbering.Automatic"/>, a dropped object's number becomes a
    /// free entry with its generation raised by one (§7.5.4).
    /// </para>
    /// </remarks>
    public bool RemoveUnreachableObjects { get; set; }

    /// <summary>Sets <see cref="ObjectNumbering"/>.</summary>
    /// <param name="numbering">The numbering.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numbering"/> is not a defined numbering.</exception>
    /// <remarks>ISO 32000-2 §7.3.10, §7.5.4.</remarks>
    public PdfSaveOptions WithObjectNumbering(PdfObjectNumbering numbering)
    {
        ObjectNumbering = numbering;
        return this;
    }

    /// <summary>Sets <see cref="RemoveUnreachableObjects"/>.</summary>
    /// <param name="remove">Whether objects the trailer does not reach are left out.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.5, §7.3.10.</remarks>
    public PdfSaveOptions WithRemoveUnreachableObjects(bool remove)
    {
        RemoveUnreachableObjects = remove;
        return this;
    }

    /// <summary>Sets <see cref="CrossReferenceLayout"/>.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layout"/> is not a defined layout.</exception>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.7, §7.5.8.</remarks>
    public PdfSaveOptions WithCrossReferenceLayout(PdfCrossReferenceLayout layout)
    {
        CrossReferenceLayout = layout;
        return this;
    }
}
