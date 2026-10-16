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
}
