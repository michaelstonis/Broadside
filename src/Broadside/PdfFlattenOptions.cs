namespace Broadside;

/// <summary>Options for <see cref="PdfDocument.Flatten(PdfFlattenOptions)"/>: which annotations are flattened, and for which intent.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.5 (appearance streams) and §12.5.3 (annotation flags). The defaults flatten the form's widgets only, as a viewer
/// shows them.
/// </remarks>
public sealed class PdfFlattenOptions
{
    private PdfFlattenIntent _intent = PdfFlattenIntent.View;

    /// <summary>
    /// Gets or sets a value indicating whether every annotation is flattened, not only the widgets of form fields. The default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.5. A pop-up annotation is removed without being drawn: it shows its parent's text only while open, an
    /// interaction a flattened page no longer has (§12.5.6.14).
    /// </remarks>
    public bool IncludeAnnotations { get; set; }

    /// <summary>
    /// Gets or sets which annotations are drawn into the content: those shown on screen or those printed. The default is
    /// <see cref="PdfFlattenIntent.View"/>. Annotations that are not drawn are removed all the same.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined intent.</exception>
    /// <remarks>ISO 32000-2 §12.5.3, Table 167.</remarks>
    public PdfFlattenIntent Intent
    {
        get => _intent;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a flatten intent.");
            }

            _intent = value;
        }
    }

    /// <summary>Sets <see cref="IncludeAnnotations"/>.</summary>
    /// <param name="include">Whether every annotation is flattened, not only widgets.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.5.</remarks>
    public PdfFlattenOptions WithAnnotations(bool include)
    {
        IncludeAnnotations = include;
        return this;
    }

    /// <summary>Sets <see cref="Intent"/>.</summary>
    /// <param name="intent">The intent.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="intent"/> is not a defined intent.</exception>
    /// <remarks>ISO 32000-2 §12.5.3, Table 167.</remarks>
    public PdfFlattenOptions WithIntent(PdfFlattenIntent intent)
    {
        Intent = intent;
        return this;
    }
}
