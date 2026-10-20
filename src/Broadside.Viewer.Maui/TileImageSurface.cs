using Broadside.Rendering;
using SkiaSharp;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The view's <see cref="IRenderSurface"/>: each presented tile becomes an immutable Skia image that the GPU canvas uploads once and
/// composes every frame. Evicted overlay tiles stay drawable for a moment, so an annotation or form edit (which evicts only the
/// overlay tiles it touches) never blinks: the old overlay shows until its replacement is presented.
/// </summary>
/// <remarks>
/// Thread-safe for the scheduler (#357): <see cref="Present(Tile, Bitmap)"/> and <see cref="Evict(Tile)"/> may run on workers (a background session
/// presents where the tile finished), so they make the Skia image there and only queue the change; the UI thread applies the queue in
/// <see cref="Drain"/> before it composes, and every other member is for the UI thread.
/// </remarks>
internal sealed class TileImageSurface : IRenderSurface
{
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMilliseconds(750);
    private readonly Lock _inboxGate = new();
    private List<(Tile Tile, SKImage? Image)> _inbox = [];
    private List<(Tile Tile, SKImage? Image)> _applying = [];
    private int _presentedCount;
    private int _generation;
    private readonly Dictionary<Tile, SKImage> _images = [];
    private readonly Dictionary<Tile, (SKImage Image, long Since)> _stale = [];
    private readonly List<Tile> _expired = [];
    private readonly TimeProvider _time;

    public TileImageSurface(TimeProvider time) => _time = time;

    /// <summary>Raised after a tile is presented, on the thread that presents it (a worker in background mode).</summary>
    public event EventHandler? Presented;

    public int PresentedCount => Volatile.Read(ref _presentedCount);

    /// <summary>Images no longer on the surface; the controller hands them to the <see cref="FramePresenter"/>, which releases them.</summary>
    public List<SKImage> Retired { get; } = [];

    public IReadOnlyDictionary<Tile, (SKImage Image, long Since)> Stale => _stale;

    /// <summary>Gets the surface a session's scheduler presents to: what it presents after <see cref="Clear"/> is dropped, never shown.</summary>
    public IRenderSurface ForCurrentSession()
    {
        lock (_inboxGate)
        {
            return new SessionSurface(this, _generation);
        }
    }

    public void Present(Tile tile, Bitmap pixels) => Present(tile, pixels, Volatile.Read(ref _generation));

    public void Evict(Tile tile) => Evict(tile, Volatile.Read(ref _generation));

    private unsafe void Present(Tile tile, Bitmap pixels, int generation)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (generation != Volatile.Read(ref _generation))
        {
            return; // a closed session's late tile (#357): not even copied
        }

        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKImage image;
        fixed (byte* source = pixels.Pixels)
        {
            image = SKImage.FromPixelCopy(info, (IntPtr)source, pixels.Stride);
        }

        lock (_inboxGate)
        {
            if (generation != _generation)
            {
                image.Dispose(); // the surface was cleared while the image was made
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

        foreach ((Tile tile, SKImage? image) in _applying)
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

    private void Add(Tile tile, SKImage image)
    {
        if (_images.Remove(tile, out SKImage? old))
        {
            Retired.Add(old);
        }

        if (_stale.Remove(tile, out (SKImage Image, long Since) stale))
        {
            Retired.Add(stale.Image);
        }

        _images.Add(tile, image);
    }

    private void Remove(Tile tile)
    {
        if (!_images.Remove(tile, out SKImage? image))
        {
            return;
        }

        if ((tile.Layers & DisplayLayers.Content) == 0)
        {
            if (_stale.Remove(tile, out (SKImage Image, long Since) older))
            {
                Retired.Add(older.Image);
            }

            _stale.Add(tile, (image, _time.GetTimestamp()));
        }
        else
        {
            Retired.Add(image);
        }
    }

    public bool TryGetImage(Tile tile, out SKImage? image) => _images.TryGetValue(tile, out image);

    /// <summary>Whether <paramref name="image"/> is an overlay tile's (resident or stale); tests and diagnostics.</summary>
    internal bool IsOverlay(SKImage image)
    {
        foreach ((Tile tile, SKImage resident) in _images)
        {
            if (ReferenceEquals(resident, image))
            {
                return (tile.Layers & DisplayLayers.Content) == 0;
            }
        }

        foreach ((SKImage stale, long _) in _stale.Values)
        {
            if (ReferenceEquals(stale, image))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops stale overlay tiles older than their lifetime (all of them with <paramref name="all"/>); returns whether any remain.</summary>
    public bool ExpireStale(bool all = false)
    {
        if (_stale.Count == 0)
        {
            return false;
        }

        foreach ((Tile tile, (SKImage _, long since)) in _stale)
        {
            if (all || _time.GetElapsedTime(since) >= StaleLifetime)
            {
                _expired.Add(tile);
            }
        }

        foreach (Tile tile in _expired)
        {
            _stale.Remove(tile, out (SKImage Image, long Since) entry);
            Retired.Add(entry.Image);
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
        foreach (SKImage image in _images.Values)
        {
            Retired.Add(image);
        }

        _images.Clear();
        ExpireStale(all: true);
    }

    /// <summary>One session's view of the surface: its presents and evictions carry the generation it was created in.</summary>
    private sealed class SessionSurface(TileImageSurface owner, int generation) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels) => owner.Present(tile, pixels, generation);

        public void Evict(Tile tile) => owner.Evict(tile, generation);
    }
}
