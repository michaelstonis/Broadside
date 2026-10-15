using Broadside.Images;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>Placing image XObjects (issue #153).</summary>
public sealed partial class PdfCanvas
{
    /// <summary>Paints an image XObject of this document into the unit square of user space (<c>Do</c>).</summary>
    /// <param name="image">
    /// The image: <see cref="PdfDocument.CreateImageFromPng"/>, <see cref="PdfDocument.CreateImageFromJpeg"/>,
    /// <see cref="PdfDocument.CreateImage(ReadOnlySpan{byte}, PdfImageLayout, PdfImageCompression)"/>, or an image XObject read from
    /// the document.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="image"/> belongs to another document, or is not an indirect image XObject (an inline image, or a direct stream).</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.8 (<c>Do</c>, Table 86) and §8.9.4: the image's samples fill the square from (0, 0) to (1, 1), its first row
    /// at the top; transform user space first to give it a size and place (<see cref="Transform"/>), or use the overload that does.
    /// The image is named in the <c>XObject</c> resources (<c>Im1</c>, ...); drawing it again, here or on another page, refers to
    /// the same object.
    /// </remarks>
    public void DrawImage(PdfImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfNotAtPageLevel();
        _writer.Name(ImageName(image)).Operator("Do");
    }

    /// <summary>Paints an image XObject of this document into a rectangle of user space (<c>q w 0 0 h x y cm /Im Do Q</c>).</summary>
    /// <param name="image">The image, as for <see cref="DrawImage(PdfImage)"/>.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The bottom edge.</param>
    /// <param name="width">The width (negative mirrors the image horizontally).</param>
    /// <param name="height">The height (negative mirrors it vertically).</param>
    /// <exception cref="ArgumentException"><paramref name="image"/> belongs to another document or is not an indirect image XObject, or a value is not finite.</exception>
    /// <exception cref="InvalidOperationException">A path is being built, or the canvas is disposed.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.9.4: maps the unit square to the rectangle with <c>cm</c> inside its own <c>q</c> and <c>Q</c>
    /// (§8.4.4), so the transformation does not outlast the call.
    /// </remarks>
    public void DrawImage(PdfImage image, double x, double y, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfNotAtPageLevel();
        ThrowIfNotFinite(x, y, width, height);
        CosName name = ImageName(image);
        _writer.Operator("q");
        _writer.Number(width).Number(0).Number(0).Number(height).Number(x).Number(y).Operator("cm");
        _writer.Name(name).Operator("Do");
        _writer.Operator("Q");
    }

    /// <summary>The name of the image in the <c>XObject</c> resources (§7.8.3), added when it has none.</summary>
    private CosName ImageName(PdfImage image)
    {
        if (image.IsInline || image.Reference is not { } reference)
        {
            throw new ArgumentException("An image painted with Do shall be an image XObject, an indirect stream (ISO 32000-2 §8.9.5).", nameof(image));
        }

        if (_stencil && !image.IsStencil)
        {
            throw new InvalidOperationException(
                "An uncoloured tiling pattern's cell shall paint only image masks, not images with colours of their own (ISO 32000-2 §8.7.3.3).");
        }

        if (!ReferenceEquals(image.Document, _document))
        {
            throw new ArgumentException("The image belongs to another document.", nameof(image));
        }

        return _resources.Name(CanvasResources.XObject, "Im", reference);
    }
}
