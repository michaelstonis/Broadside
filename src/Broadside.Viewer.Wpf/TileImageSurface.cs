using Broadside.Rendering;

namespace Broadside.Viewer.Wpf;

/// <summary>
/// The view's <see cref="IRenderSurface"/>: each presented tile becomes an immutable image (a frozen WPF bitmap in the view, made by
/// the factory the controller is given) that every frame draws. Evicted overlay tiles stay drawable for a moment, so an annotation or
/// form edit (which evicts only the overlay tiles it touches) never blinks: the old overlay shows until its replacement is presented.
/// </summary>
/// <remarks>
/// Thread-safe for the scheduler: <see cref="Present(Tile, Bitmap)"/> and <see cref="Evict(Tile)"/> may run on workers (a background
/// session presents where the tile finished), so they make the image there (a frozen bitmap can cross threads) and only queue the
/// change; the UI thread applies the queue in <see cref="Drain"/> before it composes, and every other member is for the UI thread.
/// Images are managed objects the garbage collector releases: nothing here is disposed.
/// </remarks>
internal sealed class TileImageSurface : IRenderSurface
{
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMilliseconds(750);
    private readonly Lock _inboxGate = new();
    private readonly Func<Bitmap, object> _createImage;
    private List<(Tile Tile, object? Image)> _inbox = [];
    private List<(Tile Tile, object? Image)> _applying = [];
    private int _presentedCount;
    private int _generation;
    private readonly Dictionary<Tile, object> _images = [];
    private readonly Dictionary<Tile, (object Image, long Since)> _stale = [];
    private readonly List<Tile> _expired = [];
    private readonly TimeProvider _time;

    public TileImageSurface(TimeProvider time, Func<Bitmap, object> createImage)
    {
        _time = time;
        _createImage = createImage;
    }

    /// <summary>Raised after a tile is presented, on the thread that presents it (a worker in background mode).</summary>
    public event EventHandler? Presented;

    public int PresentedCount => Volatile.Read(ref _presentedCount);

    public int Count => _images.Count;

    public IReadOnlyDictionary<Tile, (object Image, long Since)> Stale => _stale;

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

    private void Present(Tile tile, Bitmap pixels, int generation)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (generation != Volatile.Read(ref _generation))
        {
            return; // a closed session's late tile: not even copied
        }

        object image = _createImage(pixels);
        lock (_inboxGate)
        {
            if (generation != _generation)
            {
                return; // the surface was cleared while the image was made
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

        foreach ((Tile tile, object? image) in _applying)
        {
            if (image is null)
            {
                Remove(tile);
            }
            else
            {
                _stale.Remove(tile);
                _images[tile] = image;
            }
        }

        _applying.Clear();
    }

    private void Remove(Tile tile)
    {
        if (!_images.Remove(tile, out object? image))
        {
            return;
        }

        if ((tile.Layers & DisplayLayers.Content) == 0)
        {
            _stale[tile] = (image, _time.GetTimestamp());
        }
    }

    public bool TryGetImage(Tile tile, out object? image) => _images.TryGetValue(tile, out image);

    /// <summary>Drops stale overlay tiles older than their lifetime (all of them with <paramref name="all"/>); returns whether any remain.</summary>
    public bool ExpireStale(bool all = false)
    {
        if (_stale.Count == 0)
        {
            return false;
        }

        foreach ((Tile tile, (object _, long since)) in _stale)
        {
            if (all || _time.GetElapsedTime(since) >= StaleLifetime)
            {
                _expired.Add(tile);
            }
        }

        foreach (Tile tile in _expired)
        {
            _stale.Remove(tile);
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
            _inbox.Clear();
        }

        _images.Clear();
        _stale.Clear();
    }

    /// <summary>One session's view of the surface: its presents and evictions carry the generation it was created in.</summary>
    private sealed class SessionSurface(TileImageSurface owner, int generation) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels) => owner.Present(tile, pixels, generation);

        public void Evict(Tile tile) => owner.Evict(tile, generation);
    }
}
