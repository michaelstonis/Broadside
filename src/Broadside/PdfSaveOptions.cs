namespace Broadside;

/// <summary>Options for one save: how the written file is laid out.</summary>
/// <remarks>
/// ISO 32000-2 §7.5. Separate from <see cref="PdfOptions"/>, which configures reading, because a document can be saved several times
/// in different layouts. The defaults write a classic cross-reference table, which every reader supports.
/// </remarks>
public sealed class PdfSaveOptions
{
    private PdfCrossReferenceLayout _crossReferenceLayout = PdfCrossReferenceLayout.Table;

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
