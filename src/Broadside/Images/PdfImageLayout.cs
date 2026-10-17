using Broadside.Graphics;

namespace Broadside.Images;

/// <summary>The shape of a buffer of image samples: its size, colour space and bits per component.</summary>
/// <param name="Width">The width in samples (pixels), at least 1.</param>
/// <param name="Height">The height in samples (rows), at least 1.</param>
/// <param name="ColorSpace">
/// The colour space of the samples: a device space, or a space of the document the image is created in (any but Pattern; an
/// Indexed space from <see cref="PdfDocument.CreateIndexedColorSpace"/> takes palette indices).
/// </param>
/// <param name="BitsPerComponent">The bits per sample: 1, 2, 4, 8 or 16 (16 is PDF 1.5).</param>
/// <remarks>
/// ISO 32000-2 §8.9.2 and §8.9.3: the samples are stored row by row from the top, each row starting on a byte boundary, the
/// components of a pixel together, MSB first; a 16-bit sample takes two bytes, high byte first. A row is therefore
/// ceil(Width × components × BitsPerComponent / 8) bytes.
/// </remarks>
public readonly record struct PdfImageLayout(int Width, int Height, PdfColorSpace ColorSpace, int BitsPerComponent = 8)
{
    /// <summary>Returns the number of bytes of one row of samples of <paramref name="components"/> components.</summary>
    internal long Stride(int components) => (((long)Width * components * BitsPerComponent) + 7) / 8;
}
