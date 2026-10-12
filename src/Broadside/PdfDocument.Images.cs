using Broadside.Images;
using Broadside.Objects;

namespace Broadside;

/// <summary>Image XObjects (issue #60).</summary>
public sealed partial class PdfDocument
{
    /// <summary>Returns the image view over an image XObject of this document.</summary>
    /// <param name="image">An image XObject stream, or a reference to one, such as a value of a resource dictionary's <c>XObject</c> subdictionary.</param>
    /// <returns>
    /// The view; <see langword="null"/> when <paramref name="image"/> is not a stream, or is a stream whose <c>Subtype</c> names another
    /// kind of XObject. A stream without <c>Subtype</c> is read as an image (a thumbnail image has none, §12.3.4).
    /// </returns>
    /// <remarks>ISO 32000-2 §8.9.5, Table 87.</remarks>
    public PdfImage? GetImage(CosObject? image) => PdfImage.Create(this, image);
}
