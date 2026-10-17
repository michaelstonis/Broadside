using Broadside.Rendering;
using SkiaSharp;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The view's <see cref="IRenderSurface"/>: each presented tile becomes an immutable Skia image that the GPU canvas uploads once and
/// composes every frame. Evicted overlay tiles stay drawable for a moment, so an annotation or form edit (which evicts only the
/// overlay tiles it touches) never blinks: the old overlay shows until its replacement is presented.
/// </summary>
internal sealed class TileImageSurface : IRenderSurface
{
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromMilliseconds(750);
    private readonly Dictionary<Tile, SKImage> _images = [];
    private readonly Dictionary<Tile, (SKImage Image, long Since)> _stale = [];
    private readonly List<Tile> _expired = [];
    private readonly TimeProvider _time;

    public TileImageSurface(TimeProvider time) => _time = time;

    /// <summary>Raised after a tile is presented (on the thread that presents it: the session's awaiting context).</summary>
    public event EventHandler? Presented;

    public int PresentedCount { get; private set; }

    /// <summary>Images no longer on the surface; the controller hands them to the <see cref="FramePresenter"/>, which releases them.</summary>
    public List<SKImage> Retired { get; } = [];

    public IReadOnlyDictionary<Tile, (SKImage Image, long Since)> Stale => _stale;

    public unsafe void Present(Tile tile, Bitmap pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var info = new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        SKImage image;
        fixed (byte* source = pixels.Pixels)
        {
            image = SKImage.FromPixelCopy(info, (IntPtr)source, pixels.Stride);
        }

        if (_images.Remove(tile, out SKImage? old))
        {
            Retired.Add(old);
        }

        if (_stale.Remove(tile, out (SKImage Image, long Since) stale))
        {
            Retired.Add(stale.Image);
        }

        _images.Add(tile, image);
        PresentedCount++;
        Presented?.Invoke(this, EventArgs.Empty);
    }

    public void Evict(Tile tile)
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
        foreach (SKImage image in _images.Values)
        {
            Retired.Add(image);
        }

        _images.Clear();
        ExpireStale(all: true);
    }
}
