using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Images;

/// <summary>
/// One image painted on a page: the <see cref="PdfImage"/>, where the unit square of image space lands in page space, and the colour
/// state in effect when it was painted.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.1, §8.9.4 (image coordinate system: the unit square mapped by the current transformation matrix), §8.9.5 (image XObjects),
/// §8.9.6.2 (image masks, painted with the current nonstroking colour), §8.9.7 (inline images), §8.6.5.6 (default colour spaces of the
/// current resource dictionary) and §8.6.5.8 (rendering intents).
/// </remarks>
public readonly record struct ExtractedImage
{
    /// <summary>Gets the image.</summary>
    /// <remarks>ISO 32000-2 §8.9.5 and §8.9.7.</remarks>
    public PdfImage Image { get; internal init; }

    /// <summary>Gets the resource name <c>Do</c> used to paint the image XObject, or <see langword="null"/> for an inline image.</summary>
    /// <remarks>ISO 32000-2 §8.8, Table 86 (<c>Do</c>).</remarks>
    public CosName? Name { get; internal init; }

    /// <summary>Gets the four corners of the image's unit square in page space (default user space).</summary>
    /// <remarks>ISO 32000-2 §8.9.4 and §8.3.2.3.</remarks>
    public Quad Quad { get; internal init; }

    /// <summary>Gets the smallest axis-aligned rectangle in page space that holds <see cref="Quad"/>.</summary>
    /// <remarks>ISO 32000-2 §7.9.5 and §8.9.4.</remarks>
    public PdfRectangle Bounds => Quad.Bounds;

    /// <summary>Gets the current transformation matrix when the image was painted, mapping the unit square into page space.</summary>
    /// <remarks>ISO 32000-2 §8.3.4 and §8.9.4.</remarks>
    public Matrix Ctm { get; internal init; }

    /// <summary>Gets the nonstroking colour in effect when the image was painted, which colours an image mask (<c>ImageMask true</c>).</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2.</remarks>
    public PdfColor FillColor { get; internal init; }

    /// <summary>Gets the rendering intent in effect: the image dictionary's <c>Intent</c> when valid, else the graphics state's.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.8 and §8.9.5.1, Table 87.</remarks>
    public RenderingIntent RenderingIntent { get; internal init; }

    /// <summary>Gets the default colour spaces of the resource dictionary active when the image was painted.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.6.</remarks>
    public PdfDefaultColorSpaces DefaultColorSpaces { get; internal init; }

    /// <summary>Gets what sets the image apart: inline, an image mask, hidden by optional content, or inside an <c>Artifact</c> sequence.</summary>
    /// <remarks>ISO 32000-2 §8.9.6.2, §8.9.7, §8.11.3.1 and §14.8.2.2.</remarks>
    public ExtractedImageFlags Flags { get; internal init; }
}
