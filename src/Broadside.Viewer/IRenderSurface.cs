using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Where a <see cref="DocumentSession"/> puts finished tiles: the surface abstraction each UI shell implements over its own canvas,
/// image or texture (a Blazor canvas, a WPF writeable bitmap, a GPU texture), and <see cref="HeadlessSurface"/> in tests.
/// </summary>
/// <remarks>
/// Spec #33 story 54. <see cref="Present"/> is called only for tiles that completed: a cancelled tile never reaches the surface. A
/// direct <see cref="DocumentSession.RenderTileAsync"/> presents on the context that awaited it; a <see cref="TileScheduler"/> over a
/// <see cref="ViewerThreading.Background"/> session presents and evicts on worker threads, several at once, so such a surface is
/// thread-safe and hands its tiles to the UI thread itself (#357).
/// </remarks>
public interface IRenderSurface
{
    /// <summary>Takes the pixels of one finished tile; a tile presented again (redrawn after an edit) replaces its earlier pixels.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="pixels">
    /// Its pixels, premultiplied BGRA, <see cref="Tile.Region"/>'s size. Valid only during the call: the session returns the buffer to
    /// its pool afterwards, so copy what you keep.
    /// </param>
    void Present(Tile tile, Bitmap pixels);

    /// <summary>
    /// Gets a value indicating whether <see cref="Present"/> can take a tile its backend left on the device (the GPU) without reading
    /// its pixels; otherwise the session reads such a tile back into the bitmap first (<see cref="ITileRender.ReadBack"/>).
    /// </summary>
    /// <remarks>
    /// ADR 0012 (#380). The default, <see langword="false"/>, suits every surface that reads the bitmap (an image, a PNG, a copy). The
    /// Blazor canvas surface accepts device pixels: it copies a tile still in the WebGL canvas GPU to GPU, and reads the bitmap otherwise.
    /// </remarks>
    bool AcceptsDevicePixels => false;

    /// <summary>
    /// Drops a tile presented earlier: the <see cref="TileScheduler"/> calls it to keep the tiles a surface holds within its memory
    /// budget, and counts the tile's pixels as freed once it returns. Called on the thread that runs the scheduler's frames, or (in
    /// background mode) on the worker that just presented a tile.
    /// </summary>
    /// <param name="tile">A tile passed to <see cref="Present"/> before and not dropped since.</param>
    /// <remarks>Spec #33 viewer capability 13. The default does nothing, for surfaces that keep no tiles.</remarks>
    void Evict(Tile tile)
    {
    }
}
