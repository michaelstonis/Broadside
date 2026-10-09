using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// An image painted into the unit square of user space: an image XObject through <c>Do</c>, or an inline image. Valid only during
/// the <see cref="ContentProcessor.PaintImage"/> callback.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.9.4 (image space maps to the unit square, then the CTM), §8.9.5 (image XObjects), §8.9.7 (inline images).
/// Declared with the content interpreter's processor surface (issue #55); reported from issue #56, with the decoded image model of
/// issue #60. Members are added, never changed.
/// </remarks>
public readonly ref struct ImageEvent
{
    /// <summary>Gets the image XObject's stream, or <see langword="null"/> for an inline image.</summary>
    public CosStream? Stream { get; internal init; }

    /// <summary>Gets the reference to the image XObject, when it is an indirect object.</summary>
    public CosReference? Reference { get; internal init; }

    /// <summary>Gets the resource name <c>Do</c> used; empty for an inline image.</summary>
    public ReadOnlySpan<byte> ResourceName { get; internal init; }

    /// <summary>Gets a value indicating whether this is an inline image.</summary>
    public bool IsInline { get; internal init; }

    /// <summary>Gets an inline image's dictionary as written (abbreviated keys included); default for an image XObject.</summary>
    public ContentOperand InlineDictionary { get; internal init; }

    /// <summary>Gets an inline image's data, still encoded; empty for an image XObject.</summary>
    public ReadOnlySpan<byte> InlineData { get; internal init; }

    /// <summary>Gets a value indicating whether the image is a stencil mask painted with the fill colour (§8.9.6.2).</summary>
    public bool IsStencil { get; internal init; }

    /// <summary>Gets the CTM: the unit square in user space maps through it.</summary>
    public Matrix Ctm { get; internal init; }

    /// <summary>Gets the image's <c>StructParent</c> (§14.7.5.4), or <see langword="null"/>.</summary>
    public int? StructParent { get; internal init; }

    /// <summary>Gets a value indicating whether optional content hides the image (§8.11.3.1).</summary>
    public bool IsHidden { get; internal init; }
}
