namespace Broadside.Viewer;

/// <summary>How a <see cref="TileScheduler"/> cuts pages into tiles, how far it renders ahead and how much memory it may use.</summary>
/// <remarks>
/// Spec #33 viewer capabilities 1, 3 and 13. Mutable and fluent like the other options objects: <c>Use*</c> selects an implementation,
/// <c>With*</c> sets a value. A scheduler copies the values when it is created.
/// </remarks>
public sealed class TileSchedulerOptions
{
    /// <summary>The default <see cref="MemoryBudget"/>: 256 MiB.</summary>
    public const long DefaultMemoryBudget = 256L * 1024 * 1024;

    /// <summary>The default <see cref="TileSize"/>: 256 pixels.</summary>
    public const int DefaultTileSize = 256;

    /// <summary>The default <see cref="PlaceholderScale"/>: a quarter of a pixel per point (18 DPI).</summary>
    public const double DefaultPlaceholderScale = 0.25;

    private int _tileSize = DefaultTileSize;
    private long _memoryBudget = DefaultMemoryBudget;
    private double _nearMargin = DefaultTileSize;
    private double _renderAhead = 1;
    private double _placeholderScale = DefaultPlaceholderScale;
    private int _maxConcurrency = Math.Max(1, Environment.ProcessorCount);

    /// <summary>Gets or sets the side of a tile in device pixels (<see cref="DefaultTileSize"/> by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 16.</exception>
    public int TileSize
    {
        get => _tileSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 16);
            _tileSize = value;
        }
    }

    /// <summary>
    /// Gets or sets the one memory budget in bytes covering the tiles the surface holds, the tiles being drawn, and the document's
    /// render caches with their display lists (<see cref="PdfDocument.CachedBytes"/>; <see cref="DefaultMemoryBudget"/> by default).
    /// Choose at least twice the pixels of a screenful of tiles.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public long MemoryBudget
    {
        get => _memoryBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _memoryBudget = value;
        }
    }

    /// <summary>Gets or sets the pixels around the viewport, on every side, whose tiles are rendered after the visible ones (one tile by default).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double NearMargin
    {
        get => _nearMargin;
        set
        {
            CheckFinite(value);
            _nearMargin = value;
        }
    }

    /// <summary>Gets or sets how many viewports beyond the visible one, in the scroll direction, are rendered last (1 by default; 0 turns look-ahead off).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double RenderAhead
    {
        get => _renderAhead;
        set
        {
            CheckFinite(value);
            _renderAhead = value;
        }
    }

    /// <summary>
    /// Gets or sets the scale, in pixels per point, of the low-resolution whole-page tile drawn before a page's sharp tiles
    /// (<see cref="DefaultPlaceholderScale"/> by default; 0 turns placeholders off). Placeholders are drawn only at zooms above it and
    /// do not depend on the zoom, so a surface can stretch them while the sharp tiles of a new zoom arrive.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double PlaceholderScale
    {
        get => _placeholderScale;
        set
        {
            CheckFinite(value);
            _placeholderScale = value;
        }
    }

    /// <summary>
    /// Gets or sets how many tiles render at once on a multi-threaded host (the processor count by default). A cooperative session
    /// draws one tile at a time.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxConcurrency = value;
        }
    }

    /// <summary>
    /// Gets or sets a receiver of the scheduler's trace: every tile started, presented, cancelled, evicted or failed, with the bytes
    /// accounted right after; <see langword="null"/> (the default) traces nothing. Called synchronously while the scheduler holds its
    /// lock, so keep it short.
    /// </summary>
    public Action<TileSchedulerEvent>? Observer { get; set; }

    /// <summary>Sets <see cref="TileSize"/>.</summary>
    /// <param name="tileSize">Device pixels.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions WithTileSize(int tileSize)
    {
        TileSize = tileSize;
        return this;
    }

    /// <summary>Sets <see cref="MemoryBudget"/>.</summary>
    /// <param name="bytes">The budget in bytes.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions WithMemoryBudget(long bytes)
    {
        MemoryBudget = bytes;
        return this;
    }

    /// <summary>Sets <see cref="NearMargin"/>.</summary>
    /// <param name="pixels">Device pixels on every side.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions WithNearMargin(double pixels)
    {
        NearMargin = pixels;
        return this;
    }

    /// <summary>Sets <see cref="RenderAhead"/>.</summary>
    /// <param name="viewports">Viewports in the scroll direction.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions WithRenderAhead(double viewports)
    {
        RenderAhead = viewports;
        return this;
    }

    /// <summary>Sets <see cref="PlaceholderScale"/>.</summary>
    /// <param name="scale">Pixels per point, or 0 for none.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions WithPlaceholderScale(double scale)
    {
        PlaceholderScale = scale;
        return this;
    }

    /// <summary>Sets <see cref="MaxConcurrency"/>.</summary>
    /// <param name="maxConcurrency">Tiles at once.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions WithMaxConcurrency(int maxConcurrency)
    {
        MaxConcurrency = maxConcurrency;
        return this;
    }

    /// <summary>Sets <see cref="Observer"/>.</summary>
    /// <param name="observer">The trace receiver.</param>
    /// <returns>These options.</returns>
    public TileSchedulerOptions UseObserver(Action<TileSchedulerEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        Observer = observer;
        return this;
    }

    private static void CheckFinite(double value)
    {
        if (!(value >= 0) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be zero or positive and finite.");
        }
    }
}
