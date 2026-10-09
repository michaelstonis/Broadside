using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>A shading painted by <c>sh</c>. Valid only during the <see cref="ContentProcessor.PaintShading"/> callback.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.2, Table 76: the shading fills the current clipping path, in user space mapped by the CTM. Fills with a
/// shading pattern carry no event of their own: the paint event's state holds the pattern colour. Declared with the content
/// interpreter's processor surface (issue #55); reported from issue #79, which adds the resolved shading model. Members are added,
/// never changed. The shading's <see cref="PdfShading.Background"/> is ignored here (Table 76): only shading patterns paint it.
/// </remarks>
public readonly ref struct ShadingEvent
{
    /// <summary>Gets the shading dictionary or stream (§8.7.4.5, Tables 77 to 83).</summary>
    public CosObject? Shading { get; internal init; }

    /// <summary>
    /// Gets the resolved shading, in user space (its coordinates are mapped by <see cref="Ctm"/>); <see langword="null"/> when the
    /// object is not a shading. A model with <see cref="PdfShading.IsValid"/> <see langword="false"/> paints nothing.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.7.4.3 to §8.7.4.5.</remarks>
    public PdfShading? Model { get; internal init; }

    /// <summary>Gets the resource name <c>sh</c> used.</summary>
    public ReadOnlySpan<byte> ResourceName { get; internal init; }

    /// <summary>Gets the CTM.</summary>
    public Matrix Ctm { get; internal init; }

    /// <summary>Gets a value indicating whether optional content hides the shading (§8.11.3.1).</summary>
    public bool IsHidden { get; internal init; }
}
