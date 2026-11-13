namespace Broadside.Images;

/// <summary>Whether an image's data carries its own opacity, and how: the value of the image dictionary's <c>SMaskInData</c>.</summary>
/// <remarks>ISO 32000-2 §8.9.5.1, Table 87 (PDF 1.5): meaningful only for JPEG 2000 data (§7.4.9).</remarks>
public enum PdfSoftMaskInData
{
    /// <summary>No opacity is taken from the data (<c>SMaskInData</c> 0, or absent or invalid).</summary>
    None = 0,

    /// <summary>The data's opacity channel is the image's soft mask (<c>SMaskInData</c> 1).</summary>
    Alpha = 1,

    /// <summary>The data's opacity channel is the image's soft mask and the colors are premultiplied by it (<c>SMaskInData</c> 2).</summary>
    PremultipliedAlpha = 2,
}
