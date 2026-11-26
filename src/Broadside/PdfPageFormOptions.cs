namespace Broadside;

/// <summary>
/// Options for <see cref="PdfDocument.CreateFormXObject(PdfPage, PdfPageFormOptions)"/>: which box of the page becomes the form, and
/// what of the page goes with it.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.10.1 and §14.11.2. The defaults make a form that shows the page's crop box as a viewer shows it, rotated, without
/// annotations and without ties to the source's structure tree.
/// </remarks>
public sealed class PdfPageFormOptions
{
    private PdfPageBoundary _boundary = PdfPageBoundary.CropBox;

    /// <summary>Gets or sets the page boundary that becomes the form's bounding box. The default is <see cref="PdfPageBoundary.CropBox"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined boundary.</exception>
    /// <remarks>ISO 32000-2 §14.11.2: the box is intersected with the media box (§14.11.2.1).</remarks>
    public PdfPageBoundary Boundary
    {
        get => _boundary;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a page boundary.");
            }

            _boundary = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the form's matrix applies the page's <c>Rotate</c>, so the form shows the page as a viewer
    /// does. The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31 (<c>Rotate</c>).</remarks>
    public bool ApplyRotation { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the normal appearances of the page's visible, printable annotations are drawn into the
    /// form, as <see cref="PdfDocument.Flatten(PdfFlattenOptions)"/> draws them. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.5.5 and §12.5.3 (the <c>Print</c> flag set, <c>Hidden</c> clear). The page's annotations are not changed.</remarks>
    public bool IncludeAnnotations { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the form keeps the page's ties to its structure tree: its <c>StructParents</c> and the
    /// <c>MCID</c> entries of its marked content. The default is <see langword="false"/>: both are left out, for a form drawn inside
    /// an artifact or a figure.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.7.5.2 and §14.7.5.4. Keep them only to attach the form to a structure tree of the caller's own.</remarks>
    public bool KeepStructure { get; set; }

    /// <summary>Sets <see cref="Boundary"/>.</summary>
    /// <param name="boundary">The page boundary.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="boundary"/> is not a defined boundary.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfPageFormOptions WithBoundary(PdfPageBoundary boundary)
    {
        Boundary = boundary;
        return this;
    }

    /// <summary>Sets <see cref="ApplyRotation"/>.</summary>
    /// <param name="apply">Whether the page's rotation is applied.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.7.3.3, Table 31.</remarks>
    public PdfPageFormOptions WithRotation(bool apply = true)
    {
        ApplyRotation = apply;
        return this;
    }

    /// <summary>Sets <see cref="IncludeAnnotations"/>.</summary>
    /// <param name="include">Whether annotation appearances are drawn into the form.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.5.5.</remarks>
    public PdfPageFormOptions WithAnnotations(bool include = true)
    {
        IncludeAnnotations = include;
        return this;
    }

    /// <summary>Sets <see cref="KeepStructure"/>.</summary>
    /// <param name="keep">Whether the ties to the structure tree are kept.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.7.5.</remarks>
    public PdfPageFormOptions WithStructure(bool keep = true)
    {
        KeepStructure = keep;
        return this;
    }

    /// <summary>A key for the options' values, for memoizing forms.</summary>
    internal int Key => ((int)_boundary << 3) | (ApplyRotation ? 1 : 0) | (IncludeAnnotations ? 2 : 0) | (KeepStructure ? 4 : 0);
}
