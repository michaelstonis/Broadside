namespace Broadside.Images;

/// <summary>How an image is masked: which of its dictionary's masking entries applies.</summary>
/// <remarks>
/// ISO 32000-2 §8.9.6 and §11.6.4.3. When several are present the precedence is: <c>ImageMask</c>, then <c>SMaskInData</c> (JPEG 2000),
/// then <c>SMask</c>, then <c>Mask</c> (a stream, then an array). <c>SMask</c> overrides <c>Mask</c> (Table 87); either soft mask
/// overrides the graphics state's soft mask for this image only.
/// </remarks>
public enum PdfImageMaskKind
{
    /// <summary>No mask: every sample is painted.</summary>
    None,

    /// <summary>The image is itself a stencil mask (<c>ImageMask true</c>): it paints the current fill colour where its samples say.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2.</remarks>
    Stencil,

    /// <summary>An explicit mask: <c>Mask</c> is an image mask, possibly at another resolution (<see cref="PdfImage.Mask"/>).</summary>
    /// <remarks>ISO 32000-2 §8.9.6.3.</remarks>
    Explicit,

    /// <summary>Colour key masking: <c>Mask</c> is an array of ranges of raw sample values that are not painted (<see cref="PdfImage.ColorKey"/>).</summary>
    /// <remarks>ISO 32000-2 §8.9.6.4.</remarks>
    ColorKey,

    /// <summary>A soft-mask image: <c>SMask</c> gives each sample's opacity (<see cref="PdfImage.Mask"/>, <see cref="PdfImage.Matte"/>).</summary>
    /// <remarks>ISO 32000-2 §11.6.5.2.</remarks>
    Soft,

    /// <summary>Opacity in the JPEG 2000 data itself (<c>SMaskInData</c> 1 or 2): <see cref="DecodedImage.Alpha"/>.</summary>
    /// <remarks>ISO 32000-2 §7.4.9 and §8.9.5.1, Table 87.</remarks>
    SoftInData,
}
