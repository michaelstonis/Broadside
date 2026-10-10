using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>The clipping path narrowed. Valid only during the <see cref="ContentProcessor.IntersectClip"/> callback.</summary>
/// <remarks>
/// ISO 32000-2 §8.5.4: reported after the painting operator that ends a path object with <c>W</c> or <c>W*</c> (also <c>n</c>);
/// for text, at the <c>ET</c> of a text object with a clipping rendering mode (§9.3.6); for a form XObject, its bounding box
/// (§8.10.1). <see cref="ContentContext.State"/> already holds the new <see cref="Handle"/>.
/// </remarks>
public readonly ref struct ClipEvent
{
    /// <summary>Gets the handle of the new clip node, now the state's <see cref="GraphicsState.ClipHandle"/>.</summary>
    public int Handle { get; internal init; }

    /// <summary>Gets the handle of the clip it narrows.</summary>
    public int ParentHandle { get; internal init; }

    /// <summary>Gets what the clip is intersected with.</summary>
    public ClipKind Kind { get; internal init; }

    /// <summary>Gets the rule that decides what is inside <see cref="Path"/>.</summary>
    public FillRule Rule { get; internal init; }

    /// <summary>Gets the clipping region's outline in user space: the path for <see cref="ClipKind.Path"/>, the rectangle for <see cref="ClipKind.Rectangle"/>.</summary>
    public PathView Path { get; internal init; }

    /// <summary>Gets the CTM the path is in.</summary>
    public Matrix Ctm { get; internal init; }

    /// <summary>Gets the glyphs whose outlines form the region for <see cref="ClipKind.Text"/> (§9.3.6); empty for other kinds.</summary>
    public ReadOnlySpan<TextClipGlyph> Glyphs { get; internal init; }
}
