namespace Broadside;

/// <summary>How <see cref="PdfPage.Resize"/> fits a page's content into its new size.</summary>
/// <remarks>The content is the page's crop box (ISO 32000-2 §14.11.2); it is centered in the new size.</remarks>
public enum PdfContentFit
{
    /// <summary>Scaled uniformly to fit entirely, centered; the unused space is left blank.</summary>
    Fit,

    /// <summary>Scaled uniformly to cover the whole new size, centered; what falls outside is cropped.</summary>
    Fill,

    /// <summary>Scaled independently across and down to the new size exactly, which may distort it.</summary>
    Stretch,

    /// <summary>Not scaled, centered; a smaller size crops it.</summary>
    None,
}
