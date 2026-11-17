using System.Buffers;
using Broadside.Rendering;

namespace Broadside.Viewer.Uno;

/// <summary>
/// One presented tile's pixels (premultiplied BGRA, rows packed) and the image the view made of them. The view uploads the pixels
/// into its image once (<see cref="Image"/>) and may then let them go (<see cref="DetachPixels"/>).
/// </summary>
internal sealed class TilePixels
{
    private byte[]? _pixels;

    public TilePixels(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    /// <summary>Gets the pixels, or an empty span once they were detached or released.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels is { } pixels ? pixels.AsSpan(0, Stride * Height) : default;

    /// <summary>Gets or sets the view's image of these pixels (made on the UI thread when first drawn).</summary>
    public object? Image { get; set; }

    /// <summary>Gets whether the tile left the surface.</summary>
    public bool IsReleased { get; private set; }

    /// <summary>Copies a rendered tile on the thread that presents it.</summary>
    public static TilePixels Copy(Bitmap bitmap)
    {
        int stride = bitmap.Width * 4;
        byte[] pixels = ArrayPool<byte>.Shared.Rent(Math.Max(stride * bitmap.Height, 1));
        ReadOnlySpan<byte> source = bitmap.Pixels;
        if (bitmap.Stride == stride)
        {
            source[..(stride * bitmap.Height)].CopyTo(pixels);
        }
        else
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                bitmap.GetRow(y)[..stride].CopyTo(pixels.AsSpan(y * stride, stride));
            }
        }

        return new TilePixels(bitmap.Width, bitmap.Height, pixels);
    }

    /// <summary>Returns the pixel buffer once the view's image holds them.</summary>
    public void DetachPixels()
    {
        if (Interlocked.Exchange(ref _pixels, null) is { } pixels)
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>The tile left the surface: its buffer goes back to the pool and its image is dropped.</summary>
    public void Release()
    {
        IsReleased = true;
        DetachPixels();
        (Image as IDisposable)?.Dispose();
        Image = null;
    }
}

/// <summary>
/// The view's <see cref="IRenderSurface"/>: each presented tile is copied into a <see cref="TilePixels"/>, which the view turns into a
/// bitmap image when it first shows it. Evicted overlay tiles stay drawable for a moment, so an annotation or form edit (which
/// evicts only the overlay tiles it touches) never blinks: the old overlay shows until its replacement is presented.
/// </summary>
/// <remarks>
/// Thread-safe for the scheduler: <see cref="Present(Tile, Bitmap)"/> and <see cref="Evict(Tile)"/> may run on workers (a background
/// session presents where the tile finished), so they copy the pixels there and only queue the change; the UI thread applies the queue
/// in <see cref="Drain"/> before it composes, and every other member is for the UI thread.
/// </remarks>
internal sealed class TileSurface : IRenderSurface
{
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMilliseconds(750);
    private readonly Lock _inboxGate = new();
    private List<(Tile Tile, TilePixels? Pixels)> _inbox = [];
    private List<(Tile Tile, TilePixels? Pixels)> _applying = [];
    private int _presentedCount;
    private int _generation;
    private readonly Dictionary<Tile, TilePixels> _images = [];
    private readonly Dictionary<Tile, (TilePixels Pixels, long Since)> _stale = [];
    private readonly List<Tile> _expired = [];
    private readonly TimeProvider _time;

    public TileSurface(TimeProvider time) => _time = time;

    /// <summary>Raised after a tile is presented, on the thread that presents it (a worker in background mode).</summary>
    public event EventHandler? Presented;

    public int PresentedCount => Volatile.Read(ref _presentedCount);

    /// <summary>Tiles no longer on the surface; the controller releases them once a frame without them is shown.</summary>
    public List<TilePixels> Retired { get; } = [];

    public IReadOnlyDictionary<Tile, (TilePixels Pixels, long Since)> Stale => _stale;

    /// <summary>Gets the tiles on the surface (tests and diagnostics).</summary>
    public IReadOnlyDictionary<Tile, TilePixels> Tiles => _images;

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

        TilePixels copy = TilePixels.Copy(pixels);
        lock (_inboxGate)
        {
            if (generation != _generation)
            {
                copy.Release(); // the surface was cleared while the copy was made
                return;
            }

            _inbox.Add((tile, copy));
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

        foreach ((Tile tile, TilePixels? pixels) in _applying)
        {
            if (pixels is null)
            {
                Remove(tile);
            }
            else
            {
                Add(tile, pixels);
            }
        }

        _applying.Clear();
    }

    private void Add(Tile tile, TilePixels pixels)
    {
        if (_images.Remove(tile, out TilePixels? old))
        {
            Retired.Add(old);
        }

        if (_stale.Remove(tile, out (TilePixels Pixels, long Since) stale))
        {
            Retired.Add(stale.Pixels);
        }

        _images.Add(tile, pixels);
    }

    private void Remove(Tile tile)
    {
        if (!_images.Remove(tile, out TilePixels? pixels))
        {
            return;
        }

        if ((tile.Layers & DisplayLayers.Content) == 0)
        {
            if (_stale.Remove(tile, out (TilePixels Pixels, long Since) older))
            {
                Retired.Add(older.Pixels);
            }

            _stale.Add(tile, (pixels, _time.GetTimestamp()));
        }
        else
        {
            Retired.Add(pixels);
        }
    }

    public bool TryGetImage(Tile tile, out TilePixels? pixels) => _images.TryGetValue(tile, out pixels);

    /// <summary>Drops stale overlay tiles older than their lifetime (all of them with <paramref name="all"/>); returns whether any remain.</summary>
    public bool ExpireStale(bool all = false)
    {
        if (_stale.Count == 0)
        {
            return false;
        }

        foreach ((Tile tile, (TilePixels _, long since)) in _stale)
        {
            if (all || _time.GetElapsedTime(since) >= StaleLifetime)
            {
                _expired.Add(tile);
            }
        }

        foreach (Tile tile in _expired)
        {
            _stale.Remove(tile, out (TilePixels Pixels, long Since) entry);
            Retired.Add(entry.Pixels);
        }

        _expired.Clear();
        return _stale.Count > 0;
    }

    /// <summary>Releases the retired tiles (after a frame that no longer shows them was shown).</summary>
    public void ReleaseRetired()
    {
        foreach (TilePixels pixels in Retired)
        {
            pixels.Release();
        }

        Retired.Clear();
    }

    public void Clear()
    {
        // Tiles of the session being closed that are still on their way are fenced off: they arrive with the old generation.
        lock (_inboxGate)
        {
            _generation++;
        }

        Drain();
        Retired.AddRange(_images.Values);
        _images.Clear();
        ExpireStale(all: true);
    }

    /// <summary>One session's view of the surface: its presents and evictions carry the generation it was created in.</summary>
    private sealed class SessionSurface(TileSurface owner, int generation) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels) => owner.Present(tile, pixels, generation);

        public void Evict(Tile tile) => owner.Evict(tile, generation);
    }
}
