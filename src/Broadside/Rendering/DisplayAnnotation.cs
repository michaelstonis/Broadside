using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// An annotation as a display list draws it: where it sits on the page, whether it keeps its size and orientation on screen, and
/// how its appearance is composited with what is below it. A backend receives it in <see cref="IRenderingBackend.BeginAnnotation"/>;
/// the appearance's paints follow, until <see cref="IRenderingBackend.EndAnnotation"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.5.5 (Algorithm "Appearance streams"): the paints between the two calls are the annotation's appearance stream,
/// already placed on <see cref="Rectangle"/> in default user space (the matrix <c>AA</c> is their transform) and clipped to the
/// appearance's bounding box.
/// </para>
/// <para>
/// §12.5.3, Table 167: <see cref="NoZoom"/> and <see cref="NoRotate"/> ask the backend to pivot the appearance about the upper-left
/// corner of <see cref="Rectangle"/> (Figure 78) so that it keeps the size it has at a magnification of one device pixel per unit
/// of default user space, and the upright orientation of the unrotated page, whatever the device transform's scale and rotation.
/// </para>
/// <para>
/// §12.5.5, last paragraphs, and §11.6.6: the appearance is a transparency group (non-isolated and non-knockout unless its
/// <c>Group</c> says otherwise) composited with the page and earlier annotations using the annotation's <c>ca</c>
/// (<see cref="Opacity"/>; §11.6.4.4: the nonstroking alpha constant applies to a group's result) and <c>BM</c>
/// (<see cref="BlendMode"/>), with no soft mask. This follows that algorithm over §12.5.2 Table 166's note that <c>CA</c>,
/// <c>ca</c> and <c>BM</c> are not used when an appearance stream exists (owner decision 4 of the Phase 3 plan).
/// </para>
/// </remarks>
public readonly record struct DisplayAnnotation
{
    internal DisplayAnnotation(
        PdfAnnotationKind kind,
        CosReference? reference,
        PdfRectangle rectangle,
        bool noZoom,
        bool noRotate,
        double opacity,
        BlendMode blendMode,
        bool isolated,
        bool knockout)
    {
        Kind = kind;
        Reference = reference;
        Rectangle = rectangle;
        NoZoom = noZoom;
        NoRotate = noRotate;
        Opacity = opacity;
        BlendMode = blendMode;
        Isolated = isolated;
        Knockout = knockout;
    }

    /// <summary>Gets the annotation's type; <see cref="PdfAnnotationKind.Widget"/> for a form field's widget.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.1, Table 171.</remarks>
    public PdfAnnotationKind Kind { get; }

    /// <summary>Gets the annotation dictionary's reference, or <see langword="null"/> for a direct annotation.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the annotation rectangle (<c>Rect</c>, normalized) in default user space.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public PdfRectangle Rectangle { get; }

    /// <summary>Gets a value indicating whether the appearance keeps its size on screen whatever the magnification.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.3, Table 167 (<c>NoZoom</c>, bit 4); a text annotation behaves as if it were set (§12.5.6.4). Always
    /// <see langword="false"/> in a list built for <see cref="DisplayIntent.Print"/>.
    /// </remarks>
    public bool NoZoom { get; }

    /// <summary>Gets a value indicating whether the appearance keeps its upright orientation on screen whatever the rotation.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.5.3, Table 167 (<c>NoRotate</c>, bit 5), and Figure 78; a text annotation behaves as if it were set
    /// (§12.5.6.4). Always <see langword="false"/> in a list built for <see cref="DisplayIntent.Print"/>.
    /// </remarks>
    public bool NoRotate { get; }

    /// <summary>Gets the opacity the appearance group is composited with: the annotation's <c>ca</c>, or its <c>CA</c> without one, within 0 to 1.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, §12.5.5 and §11.6.4.4.</remarks>
    public double Opacity { get; }

    /// <summary>Gets the blend mode the appearance group is composited with (<c>BM</c>, PDF 2.0).</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, §12.5.5 and §11.3.5.</remarks>
    public BlendMode BlendMode { get; }

    /// <summary>Gets a value indicating whether the appearance group is isolated (its <c>Group</c> dictionary's <c>I</c>; false without one).</summary>
    /// <remarks>ISO 32000-2 §12.5.5 and §11.6.6, Table 145.</remarks>
    public bool Isolated { get; }

    /// <summary>Gets a value indicating whether the appearance group is a knockout group (its <c>Group</c> dictionary's <c>K</c>; false without one).</summary>
    /// <remarks>ISO 32000-2 §12.5.5 and §11.6.6, Table 145.</remarks>
    public bool Knockout { get; }
}
