using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>A path object being painted. Valid only during the <see cref="ContentProcessor.PaintPath"/> callback.</summary>
/// <remarks>
/// ISO 32000-2 §8.5.3, Table 59. The path is in user space; <see cref="ContentContext.State"/> holds the CTM, line parameters and
/// colours in effect, and the clipping path before any <c>W</c> of this path object takes effect: a clip given with
/// <see cref="PendingClip"/> changes the clipping path only after this paint, through <see cref="ContentProcessor.IntersectClip"/>
/// (§8.5.4), so a path stroked with <c>W S</c> is not clipped by itself.
/// </remarks>
public readonly ref struct PathEvent
{
    /// <summary>Gets the path, exactly as constructed (see <see cref="PathView"/> for the painting rules a renderer applies).</summary>
    public PathView Path { get; internal init; }

    /// <summary>Gets what is painted; <see cref="PathPaint.None"/> for <c>n</c>.</summary>
    public PathPaint Paint { get; internal init; }

    /// <summary>Gets the rule for the fill; <see cref="Graphics.FillRule.NonZero"/> when nothing is filled.</summary>
    public FillRule FillRule { get; internal init; }

    /// <summary>Gets a value indicating whether the operator closed the last subpath before painting: <c>s</c>, <c>b</c>, <c>b*</c>.</summary>
    /// <remarks>The closing segment is already in <see cref="Path"/>.</remarks>
    public bool ClosedBeforePaint { get; internal init; }

    /// <summary>Gets the rule of the <c>W</c> or <c>W*</c> in this path object, or <see langword="null"/> when there is none.</summary>
    public FillRule? PendingClip { get; internal init; }

    /// <summary>Gets a value indicating whether optional content hides the path (§8.11.3.1); reported only to processors that ask for <see cref="ContentEvents.HiddenContent"/>.</summary>
    public bool IsHidden { get; internal init; }
}
