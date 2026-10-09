using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// A form XObject painted by <c>Do</c>, reported when its content is entered and again when it is left. Valid only during the
/// callback that received it.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.10.1, Table 93: <c>Do</c> saves the state, concatenates <see cref="Matrix"/> to the CTM, clips to
/// <see cref="BoundingBox"/>, paints the form's content and restores the state. For a transparency group (§11.6.6) the compositing
/// parameters in effect at <c>Do</c> (blend mode, alpha, soft mask) are those of <see cref="ContentContext.State"/> during
/// <see cref="ContentProcessor.BeginForm"/>. Declared with the content interpreter's processor surface (issue #55); reported from
/// issue #56. Members are added, never changed.
/// </remarks>
public readonly ref struct FormEvent
{
    /// <summary>Gets the form XObject's stream.</summary>
    public CosStream? Stream { get; internal init; }

    /// <summary>Gets the reference to the form XObject, when it is an indirect object.</summary>
    public CosReference? Reference { get; internal init; }

    /// <summary>Gets the resource name <c>Do</c> used.</summary>
    public ReadOnlySpan<byte> ResourceName { get; internal init; }

    /// <summary>Gets the form matrix: form space to the user space at <c>Do</c> (identity when absent).</summary>
    public Matrix Matrix { get; internal init; }

    /// <summary>Gets the bounding box in form space, the form's clip.</summary>
    public PdfRectangle BoundingBox { get; internal init; }

    /// <summary>Gets the group attributes dictionary (§8.10.3, Table 94), or <see langword="null"/>.</summary>
    public CosDictionary? Group { get; internal init; }

    /// <summary>Gets the reference dictionary of a reference XObject (§8.10.4, Table 95), or <see langword="null"/>.</summary>
    public CosDictionary? ReferenceDictionary { get; internal init; }

    /// <summary>Gets the resources the form's content uses: its own, or those it inherits (§7.8.3).</summary>
    public CosDictionary? Resources { get; internal init; }

    /// <summary>Gets the form's <c>StructParent</c> (§14.7.5.4), or <see langword="null"/>.</summary>
    public int? StructParent { get; internal init; }

    /// <summary>Gets the form's <c>StructParents</c> (§14.7.5.4), or <see langword="null"/>: marked-content identifiers inside the form resolve through it.</summary>
    public int? StructParents { get; internal init; }

    /// <summary>Gets the nesting depth of the form's run.</summary>
    public int Depth { get; internal init; }
}
