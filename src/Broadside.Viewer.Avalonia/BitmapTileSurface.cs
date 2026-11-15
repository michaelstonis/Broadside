using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Broadside.Rendering;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using PixelSize = Avalonia.PixelSize;
using TileBitmap = Broadside.Rendering.Bitmap;

namespace Broadside.Viewer.Avalonia;

/// <summary>
/// The viewer's <see cref="IRenderSurface"/>: each presented tile is copied into its own Avalonia <see cref="WriteableBitmap"/>
/// (premultiplied BGRA, the rasterizer's own layout, so the copy is a row-by-row block copy) that the control's render pass draws
/// every frame. Evicted overlay tiles stay drawable for a moment, so an annotation or form edit (which evicts only the overlay tiles
/// it touches) never blinks: the old overlay shows until its replacement is presented.
/// </summary>
/// <remarks>
/// Thread-safe for the scheduler: <see cref="Present(Tile, TileBitmap)"/> and <see cref="Evict(Tile)"/> run on workers in a background
/// session (rendering happens off the UI thread), so they make the bitmap there and only queue the change; the UI thread applies the
/// queue in <see cref="Drain"/> before it composes, and every other member is for the UI thread. A bitmap no longer on the surface is
/// disposed at once: Avalonia's renderer keeps its own reference to a bitmap a recorded frame still draws.
/// </remarks>
internal sealed class BitmapTileSurface : IRenderSurface
{
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMilliseconds(750);
    private static readonly Vector Dpi = new(96, 96);
    private readonly Lock _inboxGate = new();
    private readonly Dictionary<Tile, WriteableBitmap> _images = [];
    private readonly Dictionary<Tile, (WriteableBitmap Image, long Since)> _stale = [];
    private readonly List<Tile> _expired = [];
    private readonly TimeProvider _time;
    private List<(Tile Tile, WriteableBitmap? Image)> _inbox = [];
    private List<(Tile Tile, WriteableBitmap? Image)> _applying = [];
    private int _presentedCount;
    private int _generation;

    public BitmapTileSurface(TimeProvider time) => _time = time;

    /// <summary>Raised after a tile is presented, on the thread that presents it (a worker in background mode).</summary>
    public event EventHandler? Presented;

    public int PresentedCount => Volatile.Read(ref _presentedCount);

    public int Count => _images.Count;

    public IReadOnlyDictionary<Tile, (WriteableBitmap Image, long Since)> Stale => _stale;

    /// <summary>Gets the surface a session's scheduler presents to: what it presents after <see cref="Clear"/> is dropped, never shown.</summary>
    public IRenderSurface ForCurrentSession()
    {
        lock (_inboxGate)
        {
            return new SessionSurface(this, _generation);
        }
    }

    public void Present(Tile tile, TileBitmap pixels) => Present(tile, pixels, Volatile.Read(ref _generation));

    public void Evict(Tile tile) => Evict(tile, Volatile.Read(ref _generation));

    private unsafe void Present(Tile tile, TileBitmap pixels, int generation)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (generation != Volatile.Read(ref _generation) || pixels.Width <= 0 || pixels.Height <= 0)
        {
            return; // a closed session's late tile: not even copied
        }

        var image = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (ILockedFramebuffer target = image.Lock())
        {
            int rowBytes = pixels.Width * 4;
            for (int y = 0; y < pixels.Height; y++)
            {
                pixels.GetRow(y)[..rowBytes].CopyTo(new Span<byte>((byte*)target.Address + ((long)y * target.RowBytes), rowBytes));
            }
        }

        lock (_inboxGate)
        {
            if (generation != _generation)
            {
                image.Dispose(); // the surface was cleared while the bitmap was made
                return;
            }

            _inbox.Add((tile, image));
        }

        Interlocked.Increment(ref _presentedCount);
        Presented?.Invoke(this, EventArgs.Empty);
    }

    private void Evict(Tile tile, int generation)
    {
        lock (_inboxGate)
        {
            if (generation == _generation)
            {
                _inbox.Add((tile, null));
            }
        }
    }

    /// <summary>Applies the tiles presented and evicted since the last call, in order (UI thread).</summary>
    public void Drain()
    {
        lock (_inboxGate)
        {
            if (_inbox.Count == 0)
            {
                return;
            }

            (_inbox, _applying) = (_applying, _inbox);
        }

        foreach ((Tile tile, WriteableBitmap? image) in _applying)
        {
            if (image is null)
            {
                Remove(tile);
            }
            else
            {
                Add(tile, image);
            }
        }

        _applying.Clear();
    }

    private void Add(Tile tile, WriteableBitmap image)
    {
        if (_images.Remove(tile, out WriteableBitmap? old))
        {
            old.Dispose();
        }

        if (_stale.Remove(tile, out (WriteableBitmap Image, long Since) stale))
        {
            stale.Image.Dispose();
        }

        _images.Add(tile, image);
    }

    private void Remove(Tile tile)
    {
        if (!_images.Remove(tile, out WriteableBitmap? image))
        {
            return;
        }

        if ((tile.Layers & DisplayLayers.Content) == 0)
        {
            if (_stale.Remove(tile, out (WriteableBitmap Image, long Since) older))
            {
                older.Image.Dispose();
            }

            _stale.Add(tile, (image, _time.GetTimestamp()));
        }
        else
        {
            image.Dispose();
        }
    }

    public bool TryGetImage(Tile tile, out AvaloniaBitmap? image)
    {
        bool found = _images.TryGetValue(tile, out WriteableBitmap? bitmap);
        image = bitmap;
        return found;
    }

    /// <summary>Drops stale overlay tiles older than their lifetime (all of them with <paramref name="all"/>); returns whether any remain.</summary>
    public bool ExpireStale(bool all = false)
    {
        if (_stale.Count == 0)
        {
            return false;
        }

        foreach ((Tile tile, (WriteableBitmap _, long since)) in _stale)
        {
            if (all || _time.GetElapsedTime(since) >= StaleLifetime)
            {
                _expired.Add(tile);
            }
        }

        foreach (Tile tile in _expired)
        {
            _stale.Remove(tile, out (WriteableBitmap Image, long Since) entry);
            entry.Image.Dispose();
        }

        _expired.Clear();
        return _stale.Count > 0;
    }

    public void Clear()
    {
        // Tiles of the session being closed that are still on their way are fenced off: they arrive with the old generation.
        lock (_inboxGate)
        {
            _generation++;
        }

        Drain();
        foreach (WriteableBitmap image in _images.Values)
        {
            image.Dispose();
        }

        _images.Clear();
        ExpireStale(all: true);
    }

    /// <summary>One session's view of the surface: its presents and evictions carry the generation it was created in.</summary>
    private sealed class SessionSurface(BitmapTileSurface owner, int generation) : IRenderSurface
    {
        public void Present(Tile tile, TileBitmap pixels) => owner.Present(tile, pixels, generation);

        public void Evict(Tile tile) => owner.Evict(tile, generation);
    }
}
