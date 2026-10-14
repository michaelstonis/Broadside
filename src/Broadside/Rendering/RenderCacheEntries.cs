using System.Runtime.CompilerServices;
using Broadside.Caching;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// A decoded image XObject (with its masks) or stencil mask in the document's image cache (issue #103), or the knowledge that it
/// cannot be painted (both <see langword="null"/>; its reason is already a diagnostic).
/// </summary>
/// <param name="epoch">The mutation epoch read before decoding.</param>
/// <param name="image">The image, for a key that is not a stencil.</param>
/// <param name="mask">The mask, for a stencil key.</param>
internal sealed class DisplayImageEntry(long epoch, DisplayImage? image, DisplayImageMask? mask) : EpochEntry(epoch)
{
    /// <summary>The bytes charged for an entry beyond what it holds.</summary>
    public const long Overhead = 64;

    /// <summary>Gets the image; <see langword="null"/> for a stencil or an image that cannot be painted.</summary>
    public DisplayImage? Image { get; } = image;

    /// <summary>Gets the stencil mask; <see langword="null"/> for an image or a mask that cannot be painted.</summary>
    public DisplayImageMask? Mask { get; } = mask;

    /// <inheritdoc/>
    public override long Bytes { get; } = Overhead + (image?.RetainedBytes ?? 0) + (mask?.RetainedBytes ?? 0);
}

/// <summary>
/// A glyph outline in the document's glyph cache (issue #103): the path a font program gave for a glyph id, empty when it gave none.
/// A program never changes once parsed (a changed font file parses into a new program), so the entry is always current.
/// </summary>
/// <param name="verbs">The path's verbs.</param>
/// <param name="points">The path's points.</param>
internal sealed class GlyphOutlineEntry(PathVerb[] verbs, PathPoint[] points) : IBudgetedEntry
{
    /// <summary>The bytes charged for an entry beyond its path.</summary>
    public const long Overhead = 64;

    /// <summary>Gets the outline in glyph space.</summary>
    public PathView Path => new(verbs, points);

    /// <inheritdoc/>
    public long Bytes { get; } = Overhead + ((long)verbs.Length * Unsafe.SizeOf<PathVerb>()) + ((long)points.Length * Unsafe.SizeOf<PathPoint>());

    /// <inheritdoc/>
    public bool IsCurrent => true;

    /// <summary>Returns the bytes an entry for <paramref name="path"/> would be charged, before copying it.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The bytes.</returns>
    public static long BytesOf(PathView path) =>
        Overhead + ((long)path.Verbs.Length * Unsafe.SizeOf<PathVerb>()) + ((long)path.Points.Length * Unsafe.SizeOf<PathPoint>());
}

/// <summary>A page's display list in the document's display-list cache (issue #103), current while the document is unchanged.</summary>
/// <param name="epoch">The mutation epoch read before building.</param>
/// <param name="list">The list.</param>
internal sealed class DisplayListEntry(long epoch, DisplayList list) : EpochEntry(epoch)
{
    /// <summary>The bytes charged for an entry beyond its list.</summary>
    public const long Overhead = 64;

    /// <summary>Gets the list.</summary>
    public DisplayList List { get; } = list;

    /// <inheritdoc/>
    public override long Bytes { get; } = Overhead + list.RetainedBytes;
}
