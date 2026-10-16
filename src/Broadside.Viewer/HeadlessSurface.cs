using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The headless host's <see cref="IRenderSurface"/>: keeps a copy of every tile presented, in order, for tests, servers and tools
/// that drive a <see cref="DocumentSession"/> without a UI.
/// </summary>
/// <remarks>Spec #33 story 54 (headless test host). Safe to present to from several threads. Disposing it returns the copies' pooled buffers.</remarks>
public sealed class HeadlessSurface : IRenderSurface, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<PresentedTile> _tiles = [];

    /// <summary>Gets the tiles presented so far, in the order they arrived.</summary>
    public IReadOnlyList<PresentedTile> Tiles
    {
        get
        {
            lock (_gate)
            {
                return [.. _tiles];
            }
        }
    }

    /// <inheritdoc/>
    public void Present(Tile tile, Bitmap pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        Bitmap? copy = new(pixels.Width, pixels.Height);
        try
        {
            for (int y = 0; y < pixels.Height; y++)
            {
                pixels.GetRow(y).CopyTo(copy.GetRow(y));
            }

            lock (_gate)
            {
                _tiles.Add(new PresentedTile(tile, copy));
            }

            copy = null;
        }
        finally
        {
            copy?.Dispose();
        }
    }

    /// <summary>Releases every copy.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (PresentedTile presented in _tiles)
            {
                presented.Pixels.Dispose();
            }

            _tiles.Clear();
        }
    }
}

/// <summary>A tile a <see cref="HeadlessSurface"/> received and its copied pixels.</summary>
/// <param name="Tile">The tile.</param>
/// <param name="Pixels">A copy of its pixels, premultiplied BGRA, owned by the surface.</param>
/// <remarks>Spec #33 story 54.</remarks>
public sealed record PresentedTile(Tile Tile, Bitmap Pixels);
