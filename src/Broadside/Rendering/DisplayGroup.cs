using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A transparency group in a display list: its attributes, the alpha and blend mode its result composites onto its backdrop with,
/// the area it can paint, and its content, which <see cref="Replay"/> hands to a backend. Valid only during the
/// <see cref="IRenderingBackend.DrawGroup"/> call that hands it over.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.4: the objects of a group composite with one another into a single colour, shape and alpha per point, against an
/// initial backdrop that is transparent for an isolated group (§11.4.5) and the group's backdrop otherwise; in a knockout group each
/// object composites with that initial backdrop only, replacing what earlier objects of the group painted where it covers
/// (§11.4.6). The result composites onto the backdrop as one object with <see cref="Opacity"/> and <see cref="BlendMode"/>, through
/// the clip in force where the group is painted (§8.5.4).
/// </para>
/// <para>
/// Content: the replay starts with the state the backend holds at the group, and is balanced (<see cref="IRenderingBackend.SaveState"/>
/// first, <see cref="IRenderingBackend.RestoreState"/> last), so the backend's state after it is what it was before. Inside, the
/// alphas and blend mode the content paints with arrive through <see cref="IRenderingBackend.SetCompositing"/> as for any paint; the
/// group's own <see cref="Opacity"/> and <see cref="BlendMode"/> are not set on the backend, they are here. A backend may replay the
/// content more than once (for example once per colour plane) or not at all.
/// </para>
/// </remarks>
public readonly ref struct DisplayGroup
{
    private readonly DisplayList _list;
    private readonly int _start;
    private readonly int _end;

    internal DisplayGroup(DisplayList list, in DisplayGroupEntry entry)
    {
        _list = list;
        _start = entry.Start;
        _end = entry.End;
        Kind = entry.Kind;
        IsIsolated = entry.IsIsolated;
        IsKnockout = entry.IsKnockout;
        ColorSpace = entry.ColorSpace;
        DefaultColorSpaces = entry.DefaultColorSpaces;
        Conversion = entry.Conversion;
        Opacity = entry.Opacity;
        BlendMode = entry.BlendMode;
        HasBoundingBox = entry.HasBoundingBox;
        BoundingBox = entry.BoundingBox;
        Matrix = entry.Matrix;
    }

    /// <summary>Gets where the group comes from: a group XObject, the page group, or the implicit group of a combined fill and stroke.</summary>
    /// <remarks>ISO 32000-2 §11.6.6, §11.4.7, §11.7.4.4.</remarks>
    public DisplayGroupKind Kind { get; }

    /// <summary>Gets a value indicating whether the group is isolated: its objects composite onto a transparent initial backdrop.</summary>
    /// <remarks>ISO 32000-2 §11.4.5; Table 145 (<c>I</c>). Always true for the page group (§11.4.7).</remarks>
    public bool IsIsolated { get; }

    /// <summary>Gets a value indicating whether the group is a knockout group: each object composites with the initial backdrop only.</summary>
    /// <remarks>ISO 32000-2 §11.4.6; Table 145 (<c>K</c>). Always true for the implicit group of a fill and stroke (§11.7.4.4).</remarks>
    public bool IsKnockout { get; }

    /// <summary>
    /// Gets the group colour space, the blending colour space of the group's content, or <see langword="null"/> when the group blends
    /// in its parent's (always so for a non-isolated group). The page group's space without a <c>CS</c> is the backend's.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.6, Table 145 (<c>CS</c>), §11.3.4 and §11.7.2: colours painted into the group are converted to it and
    /// composite there; device spaces are subject to <see cref="DefaultColorSpaces"/>. A space that cannot blend (Lab, Indexed,
    /// Pattern, Separation, DeviceN, an ICC profile that is not gray, RGB or CMYK) is not handed over: the group blends in its
    /// parent's space.
    /// </remarks>
    public PdfColorSpace? ColorSpace { get; }

    /// <summary>Gets the default colour spaces current where the group is painted, which replace a device <see cref="ColorSpace"/>.</summary>
    /// <remarks>ISO 32000-2 §8.6.5.6.</remarks>
    public PdfDefaultColorSpaces DefaultColorSpaces { get; }

    /// <summary>
    /// Gets the conversion the graphics state asked for where the group is painted (rendering intent and black point compensation),
    /// with <see cref="ColorConversion.Target"/> RGB: the one that converts the group's result to its parent's colour space.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.4.5 and §11.6.6 (Final compositing).</remarks>
    public ColorConversion Conversion { get; }

    /// <summary>Gets the constant alpha the group's result composites with: the nonstroking alpha (<c>ca</c>) where it is painted.</summary>
    /// <remarks>ISO 32000-2 §11.6.4.4 and §11.6.6; 1 for the page group and the implicit group of a fill and stroke.</remarks>
    public double Opacity { get; }

    /// <summary>Gets the blend mode the group's result composites with: the one where it is painted.</summary>
    /// <remarks>ISO 32000-2 §11.6.3 and §11.6.6; Normal for the page group and the implicit group of a fill and stroke.</remarks>
    public BlendMode BlendMode { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="BoundingBox"/> bounds what the content can paint; false when only the clip does
    /// (the page group, or a form without a usable <c>BBox</c>).
    /// </summary>
    public bool HasBoundingBox { get; }

    /// <summary>
    /// Gets the rectangle, in the group's space, outside which the content paints nothing: a group XObject's <c>BBox</c> (§8.10.2), or
    /// for a fill and stroke the path's bounds widened by the stroke's reach. Meaningful only when <see cref="HasBoundingBox"/>.
    /// </summary>
    public PdfRectangle BoundingBox { get; }

    /// <summary>
    /// Gets the transform from the group's space, where <see cref="BoundingBox"/> is given, to the page's default user space: the
    /// form matrix concatenated with the CTM at <c>Do</c>, or the CTM of the path. Independent of
    /// <see cref="IRenderingBackend.SetTransform"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.10.1.</remarks>
    public Matrix Matrix { get; }

    /// <summary>Calls <paramref name="backend"/> once for every operation of the group's content, in order (nested groups included).</summary>
    /// <param name="backend">The backend that draws the content, usually the one <see cref="IRenderingBackend.DrawGroup"/> was called on.</param>
    /// <remarks>ADR 0006; allocates nothing. See <see cref="DisplayList.Replay"/>.</remarks>
    public void Replay(IRenderingBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _list.ReplayRange(backend, _start, _end);
    }
}
