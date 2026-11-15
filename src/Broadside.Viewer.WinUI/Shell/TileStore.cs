using System.Buffers;
using Broadside.Rendering;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// The tile scheduler's surface: resident tile images by tile, plus overlay tiles kept on screen (stale) after their eviction until
/// replacements arrive, so form edits never blink.
/// </summary>
/// <remarks>
/// A background session presents and evicts on worker threads, concurrently, because tiles render off the UI thread: a present copies the pixels into a pooled
/// buffer and queues it; <see cref="Drain"/> on the UI thread turns the queued pixels into the host's images (a <c>WriteableBitmap</c>
/// can only be made there) and applies evictions in order. Each session presents through <see cref="ForCurrentSession"/>, so tiles a
/// closed session's workers present late never reach the next document.
/// </remarks>
internal sealed class TileStore : IRenderSurface
{
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMilliseconds(750);
    private readonly Lock _inboxGate = new();
    private readonly ITileImageFactory _factory;
    private readonly TimeProvider _time;
    private readonly Dictionary<Tile, ITileImage> _images = [];
    private readonly Dictionary<Tile, (ITileImage Image, long Since)> _stale = [];
    private readonly List<Tile> _expired = [];
    private List<Pending> _inbox = [];
    private List<Pending> _applying = [];
    private int _presentedCount;
    private int _generation;

    public TileStore(ITileImageFactory factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
    }

    /// <summary>Raised after a tile was queued (on the thread that presented it).</summary>
    public event EventHandler? Presented;

    /// <summary>Gets how many tiles were presented in all.</summary>
    public int PresentedCount => Volatile.Read(ref _presentedCount);

    /// <summary>Gets the evicted overlay tiles still shown until their replacements arrive.</summary>
    public IReadOnlyDictionary<Tile, (ITileImage Image, long Since)> Stale => _stale;

    /// <summary>Gets how many tile images are resident.</summary>
    public int Count => _images.Count;

    /// <summary>A surface for the session about to open: what it presents after <see cref="Clear"/> is dropped.</summary>
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

        // The bitmap goes back to the scheduler's pool when this returns: copy its rows, tightly packed.
        int rowBytes = pixels.Width * 4;
        int length = rowBytes * pixels.Height;
        byte[] copy = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        ReadOnlySpan<byte> source = pixels.Pixels;
        for (int y = 0; y < pixels.Height; y++)
        {
            source.Slice(y * pixels.Stride, rowBytes).CopyTo(copy.AsSpan(y * rowBytes, rowBytes));
        }

        lock (_inboxGate)
        {
            if (generation != _generation)
            {
                ArrayPool<byte>.Shared.Return(copy); // the store was cleared while the pixels were copied
                return;
            }

            _inbox.Add(new Pending(tile, copy, pixels.Width, pixels.Height));
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
                _inbox.Add(new Pending(tile, null, 0, 0));
            }
        }
    }

    /// <summary>Applies queued presents and evictions in order (UI thread).</summary>
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

        try
        {
            foreach (Pending pending in _applying)
            {
                if (pending.Pixels is null)
                {
                    Remove(pending.Tile);
                    continue;
                }

                ITileImage image = _factory.Create(pending.Width, pending.Height, pending.Pixels.AsSpan(0, pending.Width * pending.Height * 4), pending.Width * 4);
                Add(pending.Tile, image);
            }
        }
        finally
        {
            foreach (Pending pending in _applying)
            {
                if (pending.Pixels is not null)
                {
                    ArrayPool<byte>.Shared.Return(pending.Pixels);
                }
            }

            _applying.Clear();
        }
    }

    private void Add(Tile tile, ITileImage image)
    {
        _images.Remove(tile);
        _stale.Remove(tile);
        _images.Add(tile, image);
    }

    private void Remove(Tile tile)
    {
        if (!_images.Remove(tile, out ITileImage? image))
        {
            return;
        }

        if ((tile.Layers & DisplayLayers.Content) == 0)
        {
            // An overlay tile stays drawable until its replacement arrives (or it expires).
            _stale[tile] = (image, _time.GetTimestamp());
        }
    }

    public bool TryGetImage(Tile tile, out ITileImage? image) => _images.TryGetValue(tile, out image);

    /// <summary>Whether an image is an overlay tile's (resident or stale); tests and diagnostics.</summary>
    internal bool IsOverlay(ITileImage image)
    {
        foreach ((Tile tile, ITileImage resident) in _images)
        {
            if (ReferenceEquals(resident, image))
            {
                return (tile.Layers & DisplayLayers.Content) == 0;
            }
        }

        foreach ((ITileImage stale, long _) in _stale.Values)
        {
            if (ReferenceEquals(stale, image))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops stale overlay tiles older than their lifetime (all of them when <paramref name="all"/>); returns whether any is left.</summary>
    public bool ExpireStale(bool all = false)
    {
        if (_stale.Count == 0)
        {
            return false;
        }

        foreach ((Tile tile, (ITileImage _, long since)) in _stale)
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

    /// <summary>Drops every tile and fences off tiles still on their way from the closed session.</summary>
    public void Clear()
    {
        lock (_inboxGate)
        {
            _generation++;
            foreach (Pending pending in _inbox)
            {
                if (pending.Pixels is not null)
                {
                    ArrayPool<byte>.Shared.Return(pending.Pixels);
                }
            }

            _inbox.Clear();
        }

        _images.Clear();
        _stale.Clear();
    }

    private readonly record struct Pending(Tile Tile, byte[]? Pixels, int Width, int Height);

    private sealed class SessionSurface(TileStore owner, int generation) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels) => owner.Present(tile, pixels, generation);

        public void Evict(Tile tile) => owner.Evict(tile, generation);
    }
}
