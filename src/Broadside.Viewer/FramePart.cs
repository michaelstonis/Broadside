namespace Broadside.Viewer;

/// <summary>What a <see cref="FramePart"/> draws.</summary>
/// <remarks>Spec #33 viewer capability 2 (progressive zoom).</remarks>
public enum FramePartKind
{
    /// <summary>A visible page's paper (opaque white): drawn first, so a page with no tile on the surface yet still shows, never a gap.</summary>
    Paper,

    /// <summary>A tile on the surface drawn at a zoom other than its own (stretched or shrunk): a placeholder, or a tile from before a zoom.</summary>
    Scaled,

    /// <summary>A tile on the surface at the frame's own zoom, drawn pixel for pixel.</summary>
    Sharp,
}

/// <summary>
/// One draw of a frame (<see cref="TileScheduler.GetFrame"/>): a page's paper, or a tile already on the surface, and where it goes in the
/// viewport.
/// </summary>
/// <param name="Kind">What it draws.</param>
/// <param name="Tile">
/// The tile on the surface to draw (as <see cref="IRenderSurface.Present"/> received it), stretched from its whole bitmap onto
/// <paramref name="Destination"/>; for <see cref="FramePartKind.Paper"/>, the page's whole render at the frame's zoom (nothing to fetch).
/// </param>
/// <param name="Destination">Where it goes, in viewport pixels; it may reach past the viewport's edges, which the shell clips.</param>
/// <remarks>
/// Spec #33 viewer capability 2. Parts come in drawing order (later parts over earlier): every page's paper, then the scaled tiles
/// farthest from the frame's zoom first, then the sharp tiles, so the sharpest pixels end on top.
/// </remarks>
public readonly record struct FramePart(FramePartKind Kind, Tile Tile, LayoutRectangle Destination);
