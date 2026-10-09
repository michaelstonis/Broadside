using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>
/// One node of the clipping path: the region it intersects the clip with, and the node it narrows. Following
/// <see cref="ParentHandle"/> to 0 gives every region the current clip is the intersection of. Valid until the run ends.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.5.4: the clipping path only shrinks, by intersection; <c>Q</c> restores an earlier one. The path is in the user
/// space that was current when the clip was set, mapped by <see cref="Ctm"/>.
/// </remarks>
public readonly ref struct ClipView
{
    /// <summary>Gets the handle of this node; 0 for the initial clipping path.</summary>
    public int Handle { get; internal init; }

    /// <summary>Gets the handle of the clip this node intersects; 0 for the initial clipping path, and for the initial node itself.</summary>
    public int ParentHandle { get; internal init; }

    /// <summary>Gets what the node intersects with.</summary>
    public ClipKind Kind { get; internal init; }

    /// <summary>Gets the rule that decides what is inside <see cref="Path"/>.</summary>
    public FillRule Rule { get; internal init; }

    /// <summary>Gets the clipping region's outline, in the user space of <see cref="Ctm"/>; empty for <see cref="ClipKind.Initial"/>.</summary>
    public PathView Path { get; internal init; }

    /// <summary>Gets the CTM when the clip was set.</summary>
    public Matrix Ctm { get; internal init; }
}
