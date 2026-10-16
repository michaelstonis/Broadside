using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Where a <see cref="DocumentSession"/> puts finished tiles: the surface abstraction each UI shell implements over its own canvas,
/// image or texture (a Blazor canvas, a WPF writeable bitmap, a GPU texture), and <see cref="HeadlessSurface"/> in tests.
/// </summary>
/// <remarks>
/// Spec #33 story 54. <see cref="Present"/> is called on the context that awaited the render (the UI thread when the shell has a
/// synchronization context), only for tiles that completed: a cancelled tile never reaches the surface.
/// </remarks>
public interface IRenderSurface
{
    /// <summary>Takes the pixels of one finished tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="pixels">
    /// Its pixels, premultiplied BGRA, <see cref="Tile.Region"/>'s size. Valid only during the call: the session returns the buffer to
    /// its pool afterwards, so copy what you keep.
    /// </param>
    void Present(Tile tile, Bitmap pixels);

    /// <summary>
    /// Drops a tile presented earlier: the <see cref="TileScheduler"/> calls it to keep the tiles a surface holds within its memory
    /// budget, and counts the tile's pixels as freed once it returns. Called on the thread that runs the scheduler's frames.
    /// </summary>
    /// <param name="tile">A tile passed to <see cref="Present"/> before and not dropped since.</param>
    /// <remarks>Spec #33 viewer capability 13. The default does nothing, for surfaces that keep no tiles.</remarks>
    void Evict(Tile tile)
    {
    }
}
