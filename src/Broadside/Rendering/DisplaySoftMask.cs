using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A soft mask in a display list: the per-point shape or opacity the paints that follow (or a group's result) are composited with,
/// derived from a transparency group. Valid only during the call that hands it over; <see cref="IsNone"/> when there is no mask.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.5 and §11.6.5.1, Table 142. The mask's group is <see cref="Content"/>, a display list of its own in mask space
/// (the coordinate system of the CTM at the <c>gs</c> that set the mask, §11.6.5.1), holding one transparency group
/// (<see cref="IRenderingBackend.DrawGroup"/>) with the group XObject's attributes and content. To get the mask values, replay it
/// with <see cref="Matrix"/> before the backend's own mapping of default user space to the device:
/// </para>
/// <list type="bullet">
/// <item><see cref="PdfSoftMaskType.Alpha"/> (§11.5.2): over a transparent backdrop; the value at each point is the resulting alpha.</item>
/// <item><see cref="PdfSoftMaskType.Luminosity"/> (§11.5.3): over an opaque backdrop of the colour <see cref="Backdrop"/> in
/// <see cref="ColorSpace"/>; the value is the luminosity of the resulting colour (for device spaces the gray the colour converts
/// to, Y = 0.30 R + 0.59 G + 0.11 B for RGB; for CIE-based spaces the Y of CIE 1931 XYZ).</item>
/// </list>
/// <para>
/// The value then passes through <see cref="TransferFunction"/> (clamped to 0..1). Outside the group's bounding box the value is the
/// transfer function of 0 (Alpha) or of the backdrop's luminosity (Luminosity), which the replay over the backdrop gives by itself.
/// <see cref="IsShape"/> says whether the values are shape (fm) or opacity (qm) in the compositing formulas (§11.6.4.3, AIS); the
/// two differ only inside knockout groups and where a group's shape is kept.
/// </para>
/// <para>
/// The list carries no transfer function of the page into the group (§11.7.5.2: transfer applies to final colours): the group runs
/// from the initial graphics state.
/// </para>
/// </remarks>
public readonly ref struct DisplaySoftMask
{
    private readonly float[]? _backdrop;

    internal DisplaySoftMask(in DisplaySoftMaskEntry entry)
    {
        Subtype = entry.Subtype;
        Content = entry.Content;
        Matrix = entry.Matrix;
        ColorSpace = entry.ColorSpace;
        _backdrop = entry.Backdrop;
        DefaultColorSpaces = entry.DefaultColorSpaces;
        Conversion = entry.Conversion;
        TransferFunction = entry.TransferFunction;
        IsShape = entry.IsShape;
        HasBoundingBox = entry.HasBoundingBox;
        BoundingBox = entry.BoundingBox;
    }

    /// <summary>Gets a value indicating whether there is no mask: the SMask None, painting unmasked (mask values 1 everywhere).</summary>
    /// <remarks>ISO 32000-2 §11.6.4.3: None removes any existing mask from the graphics state.</remarks>
    public bool IsNone => Content is null;

    /// <summary>Gets how the mask values derive from the group: its alpha, or its luminosity over the backdrop.</summary>
    /// <remarks>ISO 32000-2 §11.6.5.1, Table 142 (<c>S</c>).</remarks>
    public PdfSoftMaskType Subtype { get; }

    /// <summary>
    /// Gets the mask's group as a display list in mask space: one <see cref="IRenderingBackend.DrawGroup"/> (isolated, knockout and
    /// colour space from the group's attributes, opacity 1, Normal) holding the group XObject's content, clipped to its bounding box.
    /// Empty when the group paints nothing. <see langword="null"/> only when <see cref="IsNone"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 Table 142 (<c>G</c>) and §11.6.6. Built once per mask in a page's list, however many paints use it.</remarks>
    public DisplayList Content { get; }

    /// <summary>
    /// Gets the transform from mask space, where <see cref="Content"/> and <see cref="BoundingBox"/> are, to the page's default user
    /// space: the CTM at the <c>gs</c> that set the mask, independent of the transform of the paints it masks.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.6.5.1: the group's Matrix concatenated with the CTM at the moment the mask is established.</remarks>
    public Matrix Matrix { get; }

    /// <summary>
    /// Gets the colour space of <see cref="Backdrop"/>, the group colour space of a luminosity mask; <see langword="null"/> for an
    /// alpha mask.
    /// </summary>
    /// <remarks>ISO 32000-2 Table 145 (<c>CS</c>, required for the group of a luminosity mask) and Table 142 (<c>BC</c>).</remarks>
    public PdfColorSpace? ColorSpace { get; }

    /// <summary>
    /// Gets the backdrop colour of a luminosity mask, components in <see cref="ColorSpace"/>: the mask's <c>BC</c>, or the space's
    /// initial colour (black) when it is absent or does not fit the space. Empty for an alpha mask.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.5.3 and Table 142 (<c>BC</c>).</remarks>
    public ReadOnlySpan<float> Backdrop => _backdrop;

    /// <summary>Gets the default colour spaces of the group's resources, which replace a device <see cref="ColorSpace"/>.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.6.</remarks>
    public PdfDefaultColorSpaces DefaultColorSpaces { get; }

    /// <summary>Gets the conversion for <see cref="Backdrop"/> (the initial graphics state's intent, target RGB).</summary>
    public ColorConversion Conversion { get; }

    /// <summary>
    /// Gets the transfer function from the group's alpha or luminosity (0 to 1) to the mask value (clamped to 0 to 1), or
    /// <see langword="null"/> for the identity.
    /// </summary>
    /// <remarks>ISO 32000-2 Table 142 (<c>TR</c>).</remarks>
    public PdfFunction? TransferFunction { get; }

    /// <summary>
    /// Gets a value indicating whether the mask values are shape (fm) rather than opacity (qm): the alpha source parameter where the
    /// mask applies.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.6.4.3 and Table 57 (<c>AIS</c>).</remarks>
    public bool IsShape { get; }

    /// <summary>Gets a value indicating whether <see cref="BoundingBox"/> bounds the group (a group XObject has a usable <c>BBox</c>).</summary>
    public bool HasBoundingBox { get; }

    /// <summary>
    /// Gets the bounds, in mask space, of the group's bounding box (its <c>BBox</c> mapped by its <c>Matrix</c>), outside which the
    /// mask value is constant (§11.6.5.1). Meaningful only when <see cref="HasBoundingBox"/>.
    /// </summary>
    public PdfRectangle BoundingBox { get; }
}
