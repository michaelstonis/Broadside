using System.Diagnostics;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Decides which tiles a viewer draws and when: visible tiles first (each visible page's low-resolution placeholder, then its sharp
/// tiles), then the tiles near the viewport, then the tiles ahead in the scroll direction; cancels work the viewport has left; keeps
/// the tiles on the surface, the tiles being drawn and the document's render caches within one memory budget.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33 viewer capabilities 1 (tiles from cached display lists), 3 (render-ahead with cancellation) and 13 (one memory budget).
/// ISO 32000-2 §10.7.1 (rendering through display lists).
/// </para>
/// <para>
/// The shell calls <see cref="Update"/> whenever the viewport changes and <see cref="RunFrame"/> once per frame (for example from
/// <c>requestAnimationFrame</c>) while it returns <see langword="true"/>. With a cooperative session (the browser, one thread) the
/// frame does the work itself, in slices of the session's <see cref="ViewerOptions.SliceBudget"/>, until the frame's budget is spent:
/// at most one display-list build runs per frame, and only while no slow build has run in it yet, because a build cannot be
/// interrupted, so a frame overruns its budget by at most one build. With a background session the frame only plans: it queues up to
/// <see cref="TileSchedulerOptions.MaxConcurrency"/> tile jobs on the thread pool and returns at once, and each job loads its page,
/// builds or takes its display list, renders and presents on workers, so <see cref="IRenderSurface.Present"/> and
/// <see cref="IRenderSurface.Evict"/> are then called on worker threads too (#357): the UI thread only draws what the surface holds.
/// </para>
/// <para>
/// A tile of a lower priority starts only once every wanted tile of a higher one is on the surface (or failed), so an off-screen tile
/// never renders before a visible one. A render the viewport has left is cancelled and never reaches the surface. The tiles on the
/// surface are kept (scrolling back shows them without rendering) until the budget needs their bytes: tiles no longer wanted go first,
/// least recently wanted first, then the document's caches are trimmed (<see cref="PdfDocument.TrimCaches"/>), then wanted tiles of a
/// lower priority than the one being started. The surface drops evicted tiles in <see cref="IRenderSurface.Evict"/>.
/// </para>
/// <para>
/// Use from one thread (the UI thread); the background renders it starts take its lock to report back. Disposing it cancels its
/// renders; it does not close the session or clear the surface.
/// </para>
/// </remarks>
public sealed class TileScheduler : IDisposable
{
    private const long SlowBuildTicks = TimeSpan.TicksPerMillisecond;

    private readonly DocumentSession _session;
    private readonly IRenderSurface _surface;
    private readonly Lock _gate = new();
    private readonly bool _cooperative;
    private readonly IRemoteTileRenderer? _remote;
    private int _remoteRunning;
    private readonly int _tileSize;
    private readonly long _budget;
    private readonly double _nearMargin;
    private readonly double _renderAhead;
    private readonly double _placeholderScale;
    private readonly int _maxConcurrency;
    private readonly Action<TileSchedulerEvent>? _observer;
    private readonly bool _separateOverlays;
    private DisplayLayers _layers = DisplayLayers.All;
    private readonly Dictionary<Tile, TileJob> _jobs = [];
    private List<TileJob> _wanted = [];
    private List<TileJob> _previous = [];
    private Tile[] _tiles = new Tile[64];
    private VisiblePage[] _pages = new VisiblePage[16];
    private VisiblePage[] _framePages = new VisiblePage[16];
    private readonly List<TileJob> _stale = [];
    private double _plannedZoom = double.NaN;
    private bool _sweepStale;
    private PageLayout _layout;
    private Viewport _viewport;
    private bool _hasViewport;
    private bool _replan;
    private int _direction = 1;
    private long _generation;
    private long _residentBytes;
    private long _drawingBytes;
    private long _peakBytes;
    private long _pendingTrim = -1;
    private int _residentCount;
    private int _running;
    private long _started;
    private long _presented;
    private long _cancelled;
    private long _evicted;
    private long _longestFrame;
    private bool _stopped;
    private bool _blocked;

    /// <summary>Initializes a new instance of the <see cref="TileScheduler"/> class.</summary>
    /// <param name="session">The session that renders the tiles.</param>
    /// <param name="layout">Where the pages are (<see cref="PageLayout.Create"/>); replace it with <see cref="SetLayout"/>.</param>
    /// <param name="surface">Where the tiles go; it keeps them until <see cref="IRenderSurface.Evict"/>.</param>
    /// <param name="options">The tile size, render-ahead and memory budget, or <see langword="null"/> for defaults.</param>
    /// <remarks>Spec #33 viewer capabilities 1, 3 and 13.</remarks>
    public TileScheduler(DocumentSession session, PageLayout layout, IRenderSurface surface, TileSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(surface);
        options ??= new TileSchedulerOptions();
        _session = session;
        _layout = layout;
        _surface = surface;
        _cooperative = session.IsCooperative;
        _remote = session.RemoteRenderer;
        _tileSize = options.TileSize;
        _budget = options.MemoryBudget;
        _nearMargin = options.NearMargin;
        _renderAhead = options.RenderAhead;
        _placeholderScale = options.PlaceholderScale;
        _maxConcurrency = options.MaxConcurrency;
        _observer = options.Observer;
        _separateOverlays = options.SeparateOverlays;
    }

    /// <summary>
    /// Gets the layers the tiles show (<see cref="DisplayLayers.All"/> by default). With
    /// <see cref="TileSchedulerOptions.SeparateOverlays"/>, the content layer and the annotation and form-field layers are separate
    /// tiles, the overlays transparent and drawn over the content tile of the same region.
    /// </summary>
    /// <remarks>Spec #33 viewer capability 6.</remarks>
    public DisplayLayers Layers
    {
        get
        {
            lock (_gate)
            {
                return _layers;
            }
        }
    }

    /// <summary>
    /// Shows or hides layers: tiles of layers no longer shown leave the surface at once, and the next frames draw only the tiles the
    /// new layers add. With <see cref="TileSchedulerOptions.SeparateOverlays"/>, toggling the annotation or form-field layer redraws only
    /// the overlay tiles; the content tiles stay.
    /// </summary>
    /// <param name="layers">The layers to show.</param>
    /// <exception cref="ObjectDisposedException">The scheduler is disposed.</exception>
    /// <remarks>Spec #33 viewer capability 6; ISO 32000-2 §12.5 (annotations drawn over the page content).</remarks>
    public void SetLayers(DisplayLayers layers)
    {
        layers &= DisplayLayers.All;
        List<CancellationTokenSource>? cancel = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            if (layers == _layers)
            {
                return;
            }

            _layers = layers;
            _replan = true;
            foreach (TileJob job in _jobs.Values)
            {
                if (!IsShown(job.Tile.Layers))
                {
                    _stale.Add(job);
                }
            }

            foreach (TileJob job in _stale)
            {
                Drop(job, ref cancel);
            }

            _stale.Clear();
        }

        CancelAll(cancel);
    }

    /// <summary>
    /// Redraws the tiles of a page that show <paramref name="area"/> in <paramref name="layers"/>, after an edit changed what they show
    /// (an annotation added, moved or removed, a field's value). A tile on the surface that is still wanted stays there, and in
    /// <see cref="GetFrame"/>, until its redraw replaces it (<see cref="TileEventKind.Invalidated"/>), so the edit never shows a blank
    /// frame; one no longer wanted leaves the surface; renders in progress restart.
    /// </summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="area">The changed area in the page's default user space.</param>
    /// <param name="layers">The layers that changed (<see cref="DisplayLayers.Annotations"/> for an annotation edit).</param>
    /// <returns>How many tiles were dropped.</returns>
    /// <exception cref="ObjectDisposedException">The scheduler is disposed.</exception>
    /// <remarks>
    /// Spec #33 viewer capability 6. Annotations drawn with NoZoom or NoRotate (ISO 32000-2 §12.5.3) leave their rectangle: invalidate
    /// the whole page (<see cref="Invalidate(int, DisplayLayers)"/>) for them.
    /// </remarks>
    public int Invalidate(int pageIndex, PdfRectangle area, DisplayLayers layers) => Invalidate(pageIndex, area, layers, wholePage: false);

    /// <summary>Redraws every tile of a page that shows any of <paramref name="layers"/>.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="layers">The layers that changed.</param>
    /// <returns>How many tiles were dropped.</returns>
    /// <exception cref="ObjectDisposedException">The scheduler is disposed.</exception>
    /// <remarks>Spec #33 viewer capability 6.</remarks>
    public int Invalidate(int pageIndex, DisplayLayers layers) => Invalidate(pageIndex, default, layers, wholePage: true);

    private int Invalidate(int pageIndex, PdfRectangle area, DisplayLayers layers, bool wholePage)
    {
        List<CancellationTokenSource>? cancel = null;
        int dropped;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            PdfPage? page = null;
            foreach (TileJob job in _jobs.Values)
            {
                Tile tile = job.Tile;
                if (tile.PageIndex != pageIndex || (tile.Layers & layers) == 0 || job.State is not (TileState.Resident or TileState.Drawing))
                {
                    continue;
                }

                if (!wholePage)
                {
                    page ??= _session.Document.Pages[pageIndex];
                    if (!Shows(DocumentSession.GetTileTransform(page, tile), tile.Region, area))
                    {
                        continue;
                    }
                }

                _stale.Add(job);
            }

            dropped = _stale.Count;
            foreach (TileJob job in _stale)
            {
                Refresh(job, ref cancel);
            }

            _stale.Clear();
        }

        CancelAll(cancel);
        return dropped;

        // Whether the area's device bounds, widened by the anti-aliasing and region margins, touch the tile's pixels.
        static bool Shows(in Matrix transform, PixelRectangle region, PdfRectangle area)
        {
            PathPoint a = transform.Transform(area.Left, area.Bottom);
            PathPoint b = transform.Transform(area.Right, area.Bottom);
            PathPoint c = transform.Transform(area.Right, area.Top);
            PathPoint d = transform.Transform(area.Left, area.Top);
            const double margin = 2;
            double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - margin;
            double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + margin;
            double top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - margin;
            double bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + margin;
            return right > 0 && bottom > 0 && left < region.Width && top < region.Height;
        }
    }

    /// <summary>
    /// Redraws an out-of-date tile: one still wanted keeps its old pixels on the surface (and in <see cref="GetFrame"/>) until the redraw
    /// replaces them, so an edit never shows a blank frame; a render in progress restarts, keeping the old pixels it was replacing.
    /// </summary>
    private void Refresh(TileJob job, ref List<CancellationTokenSource>? cancel)
    {
        if (job.State == TileState.Resident && job.Generation == _generation)
        {
            job.State = TileState.New;
            job.OldPixels = true;
            Trace(TileEventKind.Invalidated, job);
        }
        else if (job.IsDrawing && job.OldPixels)
        {
            job.OldPixels = false;
            Cancel(job, ref cancel);
            var redraw = new TileJob(this, job.Tile) { Generation = job.Generation, Priority = job.Priority, IsPlaceholder = job.IsPlaceholder, Bytes = job.Bytes, OldPixels = true };
            _jobs[job.Tile] = redraw;
            _replan = true;
        }
        else
        {
            Drop(job, ref cancel);
        }
    }

    /// <summary>Takes a tile off the surface (or stops its render); a tile still wanted renders again, after a replan for a stopped one.</summary>
    private void Drop(TileJob job, ref List<CancellationTokenSource>? cancel)
    {
        ForgetOldPixels(job);
        if (job.State == TileState.Resident)
        {
            Evict(job);
        }
        else if (job.IsDrawing)
        {
            Cancel(job, ref cancel);
            _replan = true;
        }
        else if (job.Generation != _generation)
        {
            _jobs.Remove(job.Tile);
        }
    }

    /// <summary>Takes the pixels an invalidated tile still shows off the surface: its redraw will not replace them.</summary>
    private void ForgetOldPixels(TileJob job)
    {
        if (!job.OldPixels)
        {
            return;
        }

        job.OldPixels = false;
        _surface.Evict(job.Tile);
        _residentBytes -= job.Bytes;
        _residentCount--;
        _evicted++;
        Trace(TileEventKind.Evicted, job);
    }

    private bool IsShown(DisplayLayers layers) =>
        _separateOverlays
            ? layers == (_layers & DisplayLayers.Content) || layers == (_layers & ~DisplayLayers.Content)
            : layers == _layers;

    /// <summary>Gets the layout the tiles are cut from.</summary>
    public PageLayout Layout
    {
        get
        {
            lock (_gate)
            {
                return _layout;
            }
        }
    }

    /// <summary>Gets what the scheduler holds and has done.</summary>
    public TileSchedulerStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                int pending = 0;
                foreach (TileJob job in _wanted)
                {
                    pending += job.IsResolved ? 0 : 1;
                }

                return new TileSchedulerStatistics(
                    _budget,
                    AccountedBytes(),
                    _peakBytes,
                    _residentBytes,
                    _residentCount,
                    pending,
                    _started,
                    _presented,
                    _cancelled,
                    _evicted,
                    TimeSpan.FromTicks(_longestFrame));
            }
        }
    }

    /// <summary>Gets a value indicating whether every wanted tile is on the surface (or failed), so frames have nothing to do.</summary>
    public bool IsIdle
    {
        get
        {
            lock (_gate)
            {
                return !HasWork();
            }
        }
    }

    /// <summary>
    /// Sets the viewport: works out the wanted tiles in priority order and cancels the renders of tiles no longer wanted. The scroll
    /// direction (for look-ahead) is the direction of the last change along the layout's strip.
    /// </summary>
    /// <param name="viewport">The viewport, in device pixels (<see cref="Viewport.Zoom"/> becomes the tiles' scale).</param>
    /// <exception cref="ArgumentOutOfRangeException">The viewport is empty or its zoom is not positive and finite.</exception>
    /// <exception cref="ObjectDisposedException">The scheduler is disposed.</exception>
    /// <remarks>Spec #33 viewer capability 3. Allocates nothing once the tiles it names are known.</remarks>
    public void Update(in Viewport viewport)
    {
        CheckViewport(viewport);
        List<CancellationTokenSource>? cancel;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            if (_hasViewport && !_replan && viewport == _viewport)
            {
                return;
            }

            if (_hasViewport)
            {
                double delta = _layout.Mode == PageLayoutMode.Horizontal ? viewport.X - _viewport.X : viewport.Y - _viewport.Y;
                _direction = delta > 0 ? 1 : delta < 0 ? -1 : _direction;
            }

            _viewport = viewport;
            _hasViewport = true;
            cancel = Plan();
        }

        CancelAll(cancel);
    }

    /// <summary>
    /// Replaces the layout (after a mode, rotation or page-size change): tiles already on the surface stay valid when their page's
    /// size is unchanged. The next <see cref="Update"/> or <see cref="RunFrame"/> works out the wanted tiles again.
    /// </summary>
    /// <param name="layout">The new layout.</param>
    /// <remarks>
    /// Spec #33 viewer capability 3. A remote document can show page 1 before its other pages' sizes are known: lay out placeholder
    /// sizes, then set the real layout as <see cref="DocumentSession.GetPageMetricsAsync"/> delivers them.
    /// </remarks>
    public void SetLayout(PageLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            _layout = layout;
            _replan = true;
        }
    }

    /// <summary>
    /// Does one frame's work: with a cooperative session, draws wanted tiles in priority order until <paramref name="budget"/> is
    /// spent; with a background session, starts renders up to the concurrency limit.
    /// </summary>
    /// <param name="budget">The time the frame may take; <see cref="Timeout.InfiniteTimeSpan"/> works until nothing is left.</param>
    /// <returns>
    /// <see langword="true"/> when work remains that another frame can do, so the shell should run one; <see langword="false"/> when
    /// every wanted tile is on the surface, or the rest waits for room in the budget (call again after <see cref="Update"/>).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not infinite.</exception>
    /// <remarks>Spec #33 viewer capabilities 1 and 3. Allocates nothing per frame beyond the tiles it starts.</remarks>
    public bool RunFrame(TimeSpan budget)
    {
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "A frame budget must not be negative.");
        }

        long start = Stopwatch.GetTimestamp();
        List<CancellationTokenSource>? cancel = null;
        bool more;
        lock (_gate)
        {
            if (_stopped || !_hasViewport)
            {
                return false;
            }

            if (_replan)
            {
                cancel = Plan();
            }

            _blocked = false;

            if (_cooperative)
            {
                RunCooperative(start, budget);
            }
            else
            {
                StartBackground();
            }

            if (_sweepStale)
            {
                SweepStale();
            }

            _longestFrame = Math.Max(_longestFrame, Stopwatch.GetElapsedTime(start).Ticks);
            // Work the budget cannot make room for waits for the viewport to move (or, on workers, for running renders to end).
            more = HasWork() && (!_blocked || _running > 0);
        }

        CancelAll(cancel);
        return more;
    }

    /// <summary>
    /// Lists what a shell draws for <paramref name="viewport"/> from what is on the surface now, before any render for it completes:
    /// each visible page's paper, then every tile on the surface that shows in the viewport, stretched or shrunk from its own scale to
    /// the viewport's zoom, the sharpest last. A zoom step shows at once this way (scaled tiles, never a blank page) while sharp tiles
    /// render at the settled scale.
    /// </summary>
    /// <param name="viewport">The viewport, which may differ from the one given to <see cref="Update"/> (a zoom in progress).</param>
    /// <param name="destination">Receives the parts in drawing order.</param>
    /// <returns>How many parts the frame has; when more than fit, call again with a larger span (nothing is sorted until they fit).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The viewport is empty or its zoom is not positive and finite.</exception>
    /// <remarks>
    /// Spec #33 viewer capability 2. Tiles drawn at another layout rotation are left out. Allocates nothing once its page buffer has grown
    /// to the viewport.
    /// </remarks>
    public int GetFrame(in Viewport viewport, Span<FramePart> destination)
    {
        CheckViewport(viewport);
        lock (_gate)
        {
            int pages = VisiblePagesOf(viewport);
            int count = 0;
            for (int i = 0; i < pages; i++)
            {
                VisiblePage page = _framePages[i];
                PixelRectangle whole = _layout.GetPagePixels(page.PageIndex, viewport.Zoom);
                var paper = new FramePart(
                    FramePartKind.Paper,
                    new Tile(page.PageIndex, viewport.Zoom, whole) { Rotation = _layout.Rotation },
                    new LayoutRectangle(page.X, page.Y, whole.Width, whole.Height));
                Add(destination, ref count, paper);
            }

            foreach (TileJob job in _jobs.Values)
            {
                Tile tile = job.Tile;
                if ((job.State != TileState.Resident && !job.OldPixels) || tile.Rotation != _layout.Rotation || IndexOfPage(tile.PageIndex, pages) is not int at)
                {
                    continue;
                }

                VisiblePage page = _framePages[at];
                double factor = viewport.Zoom / tile.Scale;
                var to = new LayoutRectangle(
                    page.X + (tile.Region.X * factor),
                    page.Y + (tile.Region.Y * factor),
                    tile.Region.Width * factor,
                    tile.Region.Height * factor);
                if (to.Right <= 0 || to.Bottom <= 0 || to.X >= viewport.Width || to.Y >= viewport.Height)
                {
                    continue;
                }

                Add(destination, ref count, new FramePart(tile.Scale == viewport.Zoom ? FramePartKind.Sharp : FramePartKind.Scaled, tile, to));
            }

            if (count <= destination.Length)
            {
                destination[pages..count].Sort(DrawingOrder);
            }

            return count;
        }

        static void Add(Span<FramePart> destination, ref int count, in FramePart part)
        {
            if (count < destination.Length)
            {
                destination[count] = part;
            }

            count++;
        }
    }

    /// <summary>Cancels every render; tiles already on the surface stay there.</summary>
    public void Dispose()
    {
        List<CancellationTokenSource>? cancel = null;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            foreach (TileJob job in _wanted)
            {
                if (job.IsDrawing)
                {
                    Cancel(job, ref cancel);
                }
            }

            _wanted.Clear();
        }

        CancelAll(cancel);
    }

    private static readonly Comparison<FramePart> DrawingOrder = static (a, b) =>
    {
        // Farthest from the frame's zoom first (the stretch is the destination's width over the tile's), so the sharpest end on top.
        int byDistance = Distance(b).CompareTo(Distance(a));
        if (byDistance != 0)
        {
            return byDistance;
        }

        int byPage = a.Tile.PageIndex.CompareTo(b.Tile.PageIndex);
        int byScale = a.Tile.Scale.CompareTo(b.Tile.Scale);
        int byRow = a.Tile.Region.Y.CompareTo(b.Tile.Region.Y);
        int byLayers = ((int)a.Tile.Layers).CompareTo((int)b.Tile.Layers);
        return byPage != 0 ? byPage : byScale != 0 ? byScale : byRow != 0 ? byRow : a.Tile.Region.X.CompareTo(b.Tile.Region.X) is int byColumn and not 0 ? byColumn : byLayers;

        static double Distance(in FramePart part) => part.Kind == FramePartKind.Sharp ? 0 : Math.Abs(Math.Log(part.Destination.Width / part.Tile.Region.Width));
    };

    private static void CheckViewport(in Viewport viewport)
    {
        if (!(viewport.Width > 0) || !(viewport.Height > 0) || !(viewport.Zoom > 0) || double.IsInfinity(viewport.Zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "A viewport needs a positive size and a positive, finite zoom.");
        }
    }

    private static long BytesOf(in Tile tile) => (long)tile.Region.Width * tile.Region.Height * 4;

    private static void CancelAll(List<CancellationTokenSource>? cancel)
    {
        if (cancel is null)
        {
            return;
        }

        foreach (CancellationTokenSource source in cancel)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The render finished meanwhile.
            }
        }
    }

    private long AccountedBytes() => _residentBytes + _drawingBytes + _session.Document.CachedBytes;

    private bool HasWork()
    {
        foreach (TileJob job in _wanted)
        {
            if (!job.IsResolved)
            {
                return true;
            }
        }

        return false;
    }

    // ---- Planning ----

    private List<CancellationTokenSource>? Plan()
    {
        _replan = false;
        _generation++;
        (_previous, _wanted) = (_wanted, _previous);
        _wanted.Clear();

        Viewport viewport = _viewport;
        _sweepStale |= viewport.Zoom != _plannedZoom;
        _plannedZoom = viewport.Zoom;
        AddPlaceholders(viewport, TilePriority.Visible);
        AddTiles(viewport, 0, TilePriority.Visible);
        if (_nearMargin > 0)
        {
            AddTiles(viewport, _nearMargin, TilePriority.Near);
        }

        if (_renderAhead > 0)
        {
            bool horizontal = _layout.Mode == PageLayoutMode.Horizontal;
            double extent = horizontal ? viewport.Width : viewport.Height;
            double offset = _direction > 0 ? extent : -extent * _renderAhead;
            Viewport ahead = horizontal
                ? viewport with { X = viewport.X + offset, Width = viewport.Width * _renderAhead }
                : viewport with { Y = viewport.Y + offset, Height = viewport.Height * _renderAhead };
            AddPlaceholders(ahead, TilePriority.Ahead);
            AddTiles(ahead, 0, TilePriority.Ahead);
        }

        // Whatever the new viewport no longer wants: renders stop, unstarted entries go, tiles on the surface stay until evicted.
        List<CancellationTokenSource>? cancel = null;
        foreach (TileJob job in _previous)
        {
            if (job.Generation == _generation)
            {
                continue;
            }

            if (job.IsDrawing)
            {
                Cancel(job, ref cancel);
            }
            else if (job.State is TileState.New or TileState.Failed)
            {
                ForgetOldPixels(job);
                _jobs.Remove(job.Tile);
            }
        }

        _previous.Clear();
        return cancel;
    }

    private void AddTiles(in Viewport viewport, double margin, TilePriority priority)
    {
        int count = _layout.GetVisibleTiles(viewport, _tileSize, _tiles, margin);
        if (count > _tiles.Length)
        {
            _tiles = new Tile[Math.Max(count, _tiles.Length * 2)];
            count = _layout.GetVisibleTiles(viewport, _tileSize, _tiles, margin);
        }

        DisplayLayers content = _separateOverlays ? _layers & DisplayLayers.Content : _layers;
        DisplayLayers overlay = _separateOverlays ? _layers & ~DisplayLayers.Content : DisplayLayers.None;
        for (int i = 0; i < count; i++)
        {
            if (content != DisplayLayers.None)
            {
                Want(_tiles[i] with { Layers = content }, priority, placeholder: false);
            }

            if (overlay != DisplayLayers.None)
            {
                Want(_tiles[i] with { Layers = overlay }, priority, placeholder: false);
            }
        }
    }

    private void AddPlaceholders(in Viewport viewport, TilePriority priority)
    {
        // Placeholders stand in for the content: with separate overlays they are content-only, so toggling an overlay never redraws one.
        DisplayLayers layers = _separateOverlays ? _layers & DisplayLayers.Content : _layers;
        if (!(_placeholderScale > 0) || _placeholderScale >= viewport.Zoom || layers == DisplayLayers.None)
        {
            return;
        }

        int count = _layout.GetVisiblePages(viewport, _pages);
        if (count > _pages.Length)
        {
            _pages = new VisiblePage[Math.Max(count, _pages.Length * 2)];
            count = _layout.GetVisiblePages(viewport, _pages);
        }

        for (int i = 0; i < count; i++)
        {
            int page = _pages[i].PageIndex;
            var tile = new Tile(page, _placeholderScale, _layout.GetPagePixels(page, _placeholderScale)) { Rotation = _layout.Rotation, Layers = layers };
            Want(tile, priority, placeholder: true);
        }
    }

    private void Want(in Tile tile, TilePriority priority, bool placeholder)
    {
        if (tile.Region.Width <= 0 || tile.Region.Height <= 0)
        {
            return;
        }

        if (!_jobs.TryGetValue(tile, out TileJob? job))
        {
            job = new TileJob(this, tile);
            _jobs.Add(tile, job);
        }
        else if (job.Generation == _generation)
        {
            return; // already wanted at a higher priority
        }

        job.Generation = _generation;
        job.Priority = priority;
        job.IsPlaceholder = placeholder;
        _wanted.Add(job);
    }

    /// <summary>The priority of the first wanted tile not yet resolved: nothing of a lower priority may start.</summary>
    private TilePriority Gate()
    {
        foreach (TileJob job in _wanted)
        {
            if (!job.IsResolved)
            {
                return job.Priority;
            }
        }

        return TilePriority.Ahead;
    }

    // ---- Memory ----

    /// <summary>Makes room for <paramref name="bytes"/> more pixels for a tile of <paramref name="priority"/>, or says there is none.</summary>
    private bool Reserve(long bytes, TilePriority priority)
    {
        if (bytes > _budget)
        {
            _blocked = true;
            return false;
        }

        while (_residentBytes + _drawingBytes + bytes + _session.Document.CachedBytes > _budget)
        {
            if (EvictOne(unwantedOnly: true, priority))
            {
                continue;
            }

            long room = _budget - _residentBytes - _drawingBytes - bytes;
            if (room >= 0)
            {
                _session.Document.TrimCaches(room);
                return true;
            }

            if (!EvictOne(unwantedOnly: false, priority))
            {
                _blocked = true;
                return false;
            }
        }

        return true;
    }

    /// <summary>After the caches grew (a build) or pixels moved: evicts unwanted tiles, then trims the caches, to fit the budget.</summary>
    private void Enforce()
    {
        while (AccountedBytes() > _budget && EvictOne(unwantedOnly: true, TilePriority.Visible))
        {
        }

        long room = _budget - _residentBytes - _drawingBytes;
        if (_session.Document.CachedBytes > room)
        {
            if (_cooperative)
            {
                _session.Document.TrimCaches(Math.Max(0, room));
            }
            else
            {
                // A worker presenting a tile trims after it leaves the lock, so the UI thread's frame never waits on the trim.
                _pendingTrim = _pendingTrim < 0 ? Math.Max(0, room) : Math.Min(_pendingTrim, Math.Max(0, room));
            }
        }

        _peakBytes = Math.Max(_peakBytes, AccountedBytes());
    }

    /// <summary>
    /// Evicts one tile from the surface: the least recently wanted of the tiles no longer wanted, or else (unless
    /// <paramref name="unwantedOnly"/>) the last wanted tile of a priority lower than <paramref name="priority"/>.
    /// </summary>
    private bool EvictOne(bool unwantedOnly, TilePriority priority)
    {
        TileJob? victim = null;
        foreach (TileJob job in _jobs.Values)
        {
            if (job.State == TileState.Resident && job.Generation != _generation && (victim is null || job.Generation < victim.Generation))
            {
                victim = job;
            }
        }

        if (victim is null && !unwantedOnly)
        {
            for (int i = _wanted.Count - 1; i >= 0 && victim is null; i--)
            {
                TileJob job = _wanted[i];
                if (job.Priority <= priority)
                {
                    break;
                }

                if (job.State == TileState.Resident)
                {
                    victim = job;
                }
            }
        }

        if (victim is null)
        {
            return false;
        }

        Evict(victim);
        return true;
    }

    /// <summary>Takes a tile off the surface.</summary>
    private void Evict(TileJob victim)
    {
        _surface.Evict(victim.Tile);
        _residentBytes -= victim.Bytes;
        _residentCount--;
        _evicted++;
        if (victim.Generation == _generation)
        {
            victim.State = TileState.New; // still wanted: drawn again when its turn comes
        }
        else
        {
            _jobs.Remove(victim.Tile);
        }

        Trace(TileEventKind.Evicted, victim);
    }

    /// <summary>
    /// After a zoom, once every visible tile at the new scale is on the surface (or failed), evicts the tiles of other scales on the
    /// visible pages that are no longer wanted: they are covered, and only <see cref="GetFrame"/> mid-zoom had a use for them.
    /// </summary>
    private void SweepStale()
    {
        foreach (TileJob job in _wanted)
        {
            if (job.Priority == TilePriority.Visible && !job.IsResolved)
            {
                return;
            }
        }

        _sweepStale = false;
        int pages = VisiblePagesOf(_viewport);
        foreach (TileJob job in _jobs.Values)
        {
            if (job.State == TileState.Resident && job.Generation != _generation && job.Tile.Scale != _viewport.Zoom && IndexOfPage(job.Tile.PageIndex, pages) is not null)
            {
                _stale.Add(job);
            }
        }

        foreach (TileJob job in _stale)
        {
            Evict(job);
        }

        _stale.Clear();
    }

    private int VisiblePagesOf(in Viewport viewport)
    {
        int count = _layout.GetVisiblePages(viewport, _framePages);
        if (count > _framePages.Length)
        {
            _framePages = new VisiblePage[Math.Max(count, _framePages.Length * 2)];
            count = _layout.GetVisiblePages(viewport, _framePages);
        }

        return count;
    }

    private int? IndexOfPage(int pageIndex, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_framePages[i].PageIndex == pageIndex)
            {
                return i;
            }
        }

        return null;
    }

    private void Trace(TileEventKind kind, TileJob job) =>
        _observer?.Invoke(new TileSchedulerEvent(kind, job.Tile, job.Priority, job.IsPlaceholder, AccountedBytes()));

    // ---- Cooperative frames ----

    private void RunCooperative(long start, TimeSpan budget)
    {
        long budgetTicks = budget == Timeout.InfiniteTimeSpan ? long.MaxValue : budget.Ticks;
        TimeSpan slice = _session.SliceBudget;
        bool built = false;
        while (true)
        {
            long remaining = budgetTicks - Stopwatch.GetElapsedTime(start).Ticks;
            if (remaining <= 0)
            {
                return;
            }

            TileJob? job = NextCooperative();
            if (job is null)
            {
                return;
            }

            if (job.State == TileState.New)
            {
                if (!StartCooperative(job))
                {
                    return; // no room within the budget: wait for the viewport to free some
                }

                continue;
            }

            if (job.Rendering is null)
            {
                if (built)
                {
                    return; // one slow build per frame
                }

                long before = Stopwatch.GetTimestamp();
                BeginDrawing(job);
                built = Stopwatch.GetElapsedTime(before).Ticks >= SlowBuildTicks;
                continue;
            }

            TimeSpan step = budgetTicks == long.MaxValue
                ? slice
                : TimeSpan.FromTicks(slice == Timeout.InfiniteTimeSpan ? remaining : Math.Min(slice.Ticks, remaining));
            Draw(job, step);
        }
    }

    /// <summary>The first wanted tile, in priority order and within the gate, that can make progress on this thread now.</summary>
    private TileJob? NextCooperative()
    {
        TilePriority gate = Gate();
        foreach (TileJob job in _wanted)
        {
            if (job.Priority > gate)
            {
                return null;
            }

            if (job.State == TileState.New && IsRemote(job) && _remoteRunning >= _remote!.MaxConcurrency)
            {
                continue; // the remote renderer is busy: draw a tile it would not take meanwhile
            }

            if (job.State == TileState.New || (job.State == TileState.Drawing && job.Load is null && !job.IsRemote))
            {
                return job;
            }
        }

        return null;
    }

    private bool StartCooperative(TileJob job)
    {
        long bytes = BytesOf(job.Tile);
        if (!Reserve(bytes, job.Priority))
        {
            return false;
        }

        if (IsRemote(job))
        {
            StartRemote(job, bytes);
            _ = RunBackgroundAsync(job); // posts the tile and returns; it is presented in a later frame
            return true;
        }

        CancellationTokenSource operation;
        try
        {
#pragma warning disable CA2000 // owned by the job; Release hands it back to the session, which disposes it
            operation = _session.Enter(CancellationToken.None);
#pragma warning restore CA2000
        }
        catch (ObjectDisposedException)
        {
            _stopped = true; // the session closed
            return false;
        }

        job.Operation = operation;
        job.Bytes = bytes;
        job.State = TileState.Drawing;
        _drawingBytes += bytes;
        _started++;
        Trace(TileEventKind.Started, job);

        ValueTask<PdfPage> load;
        try
        {
            load = _session.LoadPageAsync(job.Tile.PageIndex, operation.Token);
        }
#pragma warning disable CA1031 // a page that fails to load fails its tiles, not the viewer
        catch (Exception)
#pragma warning restore CA1031
        {
            Fail(job);
            return true;
        }

        if (load.IsCompletedSuccessfully)
        {
            job.Page = load.Result;
        }
        else
        {
            job.Load = AwaitPageAsync(job, load.AsTask());
        }

        return true;
    }

    /// <summary>Waits for a remote page (or its resources) without blocking the frame; the tile continues in a later frame.</summary>
    private async Task AwaitPageAsync(TileJob job, Task<PdfPage> load)
    {
        PdfPage? page = null;
        bool failed = false;
        try
        {
            page = await load.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031 // reported as a failed tile
        catch (Exception)
#pragma warning restore CA1031
        {
            failed = true;
        }

        lock (_gate)
        {
            job.Load = null;
            if (job.State != TileState.Drawing)
            {
                // Cancelled meanwhile: the operation outlived the cancellation only to see this load end.
                if (job.Operation is not null)
                {
                    _session.Exit(job.Operation);
                    job.Operation = null;
                }

                return;
            }

            if (page is null)
            {
                if (failed)
                {
                    Fail(job);
                }
                else
                {
                    Release(job);
                    job.State = TileState.New;
                }

                return;
            }

            job.Page = page;
        }
    }

    /// <summary>Takes the page's list (built now unless the document caches it) and starts drawing into a pooled bitmap.</summary>
    private void BeginDrawing(TileJob job)
    {
        CancellationToken token = job.Operation!.Token;
        DisplayList list;
        try
        {
            list = _session.CreateDisplayList(job.Page!, job.Tile.Layers, token);
        }
        catch (PdfRangeNotLoadedException) when (!job.Prefetched)
        {
            // A remote resource outside the page's prefetched closure: fetch the closure in the background, draw in a later frame.
            job.Prefetched = true;
            job.Load = AwaitPageAsync(job, PrefetchAsync(job.Page!, token));
            return;
        }
        catch (OperationCanceledException)
        {
            Cancel(job);
            return;
        }
#pragma warning disable CA1031 // a page that fails to build fails its tiles, not the viewer
        catch (Exception)
#pragma warning restore CA1031
        {
            Fail(job);
            return;
        }

        Enforce(); // the build may have grown the caches
        if (list.OperationCount == 0 && (job.Tile.Layers & DisplayLayers.Content) == 0)
        {
            // An overlay with nothing on it (a page without annotations): resident without pixels, never drawn or composed (#357).
            ForgetOldPixels(job);
            Release(job);
            job.Bytes = 0;
            MakeResident(job);
            return;
        }

        job.Target = new Bitmap(job.Tile.Region.Width, job.Tile.Region.Height);
        _session.AddPixelBytes(job.Bytes);
        if ((job.Tile.Layers & DisplayLayers.Content) != 0)
        {
            job.Target.Pixels.Fill(byte.MaxValue); // the paper: opaque white, premultiplied BGRA
        }

        job.Backend = _session.RentBackend();
        try
        {
            job.Rendering = job.Backend.Begin(list, job.Target, DocumentSession.GetTileTransform(job.Page!, job.Tile), token);
        }
#pragma warning disable CA1031 // reported as a failed tile
        catch (Exception)
#pragma warning restore CA1031
        {
            Fail(job);
        }
    }

    private static async Task<PdfPage> PrefetchAsync(PdfPage page, CancellationToken token)
    {
        await page.PrefetchAsync(token).ConfigureAwait(false);
        return page;
    }

    private void Draw(TileJob job, TimeSpan slice)
    {
        bool done;
        try
        {
            done = job.Rendering!.RenderSlice(slice);
        }
        catch (OperationCanceledException)
        {
            Cancel(job);
            return;
        }
#pragma warning disable CA1031 // reported as a failed tile
        catch (Exception)
#pragma warning restore CA1031
        {
            Fail(job);
            return;
        }

        if (!done)
        {
            return;
        }

        // The rendering is disposed after the tile is presented (by Release, before the bitmap): a tile its backend left on the device
        // (ADR 0012) is read back for a surface that reads pixels, and dropped before the bitmap is released (#380).
        try
        {
            if (!_surface.AcceptsDevicePixels)
            {
                job.Rendering.ReadBack();
            }

            _surface.Present(job.Tile, job.Target!);
        }
        catch (Exception)
        {
            Fail(job);
            throw;
        }

        _session.CountTile(presented: true);
        Release(job);
        MakeResident(job);
    }

    /// <summary>Returns what a cooperative render holds: its rendering, backend, bitmap and session operation.</summary>
    private void Release(TileJob job)
    {
        job.Rendering?.Dispose();
        job.Rendering = null;
        if (job.Backend is not null)
        {
            _session.ReturnBackend(job.Backend);
            job.Backend = null;
        }

        if (job.Target is not null)
        {
            job.Target.Dispose();
            job.Target = null;
            _session.AddPixelBytes(-job.Bytes);
        }

        if (job.Operation is not null && job.Load is null)
        {
            _session.Exit(job.Operation);
            job.Operation = null;
        }

        if (job.State == TileState.Drawing)
        {
            _drawingBytes -= job.Bytes;
        }

        job.Page = null;
    }

    private void MakeResident(TileJob job)
    {
        job.State = TileState.Resident;
        if (job.OldPixels)
        {
            job.OldPixels = false; // the redraw replaced the old pixels under the same tile, already counted
        }
        else
        {
            _residentBytes += job.Bytes;
            _residentCount++;
        }

        _presented++;
        Enforce();
        Trace(TileEventKind.Presented, job);
    }

    /// <summary>A cooperative render failed: its resources go back and the tile is not tried again while it stays wanted.</summary>
    private void Fail(TileJob job)
    {
        Release(job);
        ForgetOldPixels(job);
        job.State = TileState.Failed;
        Trace(TileEventKind.Failed, job);
    }

    private void Cancel(TileJob job)
    {
        List<CancellationTokenSource>? ignored = null;
        Cancel(job, ref ignored);
    }

    /// <summary>Stops a render the viewport has left; a background render's token is cancelled by the caller after the lock is released.</summary>
    private void Cancel(TileJob job, ref List<CancellationTokenSource>? cancel)
    {
        ForgetOldPixels(job);
        if (_cooperative && !job.IsRemote)
        {
            if (job.Load is not null)
            {
                job.Operation?.Cancel(); // the pending load ends and its continuation leaves the session
            }

            Release(job);
            _session.CountTile(presented: false);
        }
        else
        {
            if (job.Cancellation is not null)
            {
                (cancel ??= []).Add(job.Cancellation);
            }

            // The reserved pixels stay counted until the render has actually stopped (FinishBackground).
        }

        job.State = TileState.Cancelled;
        _cancelled++;
        if (_jobs.TryGetValue(job.Tile, out TileJob? current) && current == job)
        {
            _jobs.Remove(job.Tile);
        }

        Trace(TileEventKind.Cancelled, job);
    }

    // ---- Background frames ----

    private void StartBackground()
    {
        TilePriority gate = Gate();
        foreach (TileJob job in _wanted)
        {
            if (_running >= _maxConcurrency || job.Priority > gate)
            {
                return;
            }

            if (job.State != TileState.New)
            {
                continue;
            }

            long bytes = BytesOf(job.Tile);
            if (!Reserve(bytes, job.Priority))
            {
                return;
            }

            if (IsRemote(job))
            {
                if (_remoteRunning >= _remote!.MaxConcurrency)
                {
                    continue;
                }

                StartRemote(job, bytes);
            }
            else
            {
                job.Bytes = bytes;
                job.State = TileState.Drawing;
                job.Cancellation = new CancellationTokenSource();
                job.Reserved = true;
                _drawingBytes += bytes;
                _running++;
                _started++;
                Trace(TileEventKind.Started, job);
            }

            // The whole job (page load, list build, render, present) runs on workers: the frame's thread only plans (#357).
            ThreadPool.UnsafeQueueUserWorkItem(job, preferLocal: false);
        }
    }

    private bool IsRemote(TileJob job) => _remote is { } remote && remote.CanRender(job.Tile);

    /// <summary>Starts a tile the remote renderer draws: counted like a background render until it is presented or stops.</summary>
    private void StartRemote(TileJob job, long bytes)
    {
        job.IsRemote = true;
        job.Bytes = bytes;
        job.State = TileState.Drawing;
        job.Cancellation = new CancellationTokenSource();
        job.Reserved = true;
        _drawingBytes += bytes;
        _running++;
        _remoteRunning++;
        _started++;
        Trace(TileEventKind.Started, job);
    }

    private async Task RunBackgroundAsync(TileJob job)
    {
        bool failed = false;
        try
        {
            await (job.IsRemote
                ? _remote!.RenderAsync(job.Tile, job, job.Cancellation!.Token)
                : _session.RenderTileAsync(job.Tile, job, job.Cancellation!.Token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
            // The session closed.
        }
#pragma warning disable CA1031 // reported as a failed tile
        catch (Exception)
#pragma warning restore CA1031
        {
            failed = true;
        }

        lock (_gate)
        {
            _stopped |= _session.IsClosed;
            _running--;
            if (job.IsRemote)
            {
                _remoteRunning--;
            }

            if (job.Reserved)
            {
                job.Reserved = false;
                _drawingBytes -= job.Bytes;
            }

            if (job.State == TileState.Drawing || job.State == TileState.Presenting)
            {
                if (failed)
                {
                    ForgetOldPixels(job);
                    job.State = TileState.Failed;
                    Trace(TileEventKind.Failed, job);
                }
                else
                {
                    // Stopped by the session closing, or given back by a remote renderer that could not draw it: nothing reached the
                    // surface, so it starts again (drawn here when the remote renderer now declines it).
                    job.State = TileState.New;
                    job.IsRemote = false;
                }
            }
        }

        job.Cancellation!.Dispose();
    }

    /// <summary>A background overlay had nothing to draw: it is resident without pixels, unless it was cancelled first.</summary>
    private void PresentEmpty(TileJob job)
    {
        lock (_gate)
        {
            if (job.State != TileState.Drawing || _stopped)
            {
                return;
            }

            ForgetOldPixels(job);
            job.Reserved = false;
            _drawingBytes -= job.Bytes;
            job.Bytes = 0;
            MakeResident(job);
        }
    }

    /// <summary>A background render finished: the tile reaches the surface unless it was cancelled first.</summary>
    private void PresentBackground(TileJob job, Bitmap pixels)
    {
        lock (_gate)
        {
            if (job.State != TileState.Drawing || _stopped)
            {
                return; // cancelled: never reaches the surface
            }

            job.State = TileState.Presenting;
        }

        _surface.Present(job.Tile, pixels);
        long trim;
        lock (_gate)
        {
            job.Reserved = false;
            _drawingBytes -= job.Bytes;
            MakeResident(job);
            trim = _pendingTrim;
            _pendingTrim = -1;
        }

        if (trim >= 0)
        {
            _session.Document.TrimCaches(trim); // safe while other threads render
        }
    }

    private enum TileState
    {
        New,
        Drawing,
        Presenting,
        Resident,
        Failed,
        Cancelled,
    }

    /// <summary>One wanted tile and its render. Also the surface a background render presents through, so a cancelled render is recognised.</summary>
    private sealed class TileJob(TileScheduler scheduler, Tile tile) : IRenderSurface, IThreadPoolWorkItem, IEmptyTileSink
    {
        public Tile Tile { get; } = tile;

        public TileState State { get; set; }

        public long Generation { get; set; }

        public TilePriority Priority { get; set; }

        public bool IsPlaceholder { get; set; }

        public long Bytes { get; set; }

        public bool Reserved { get; set; }

        public bool Prefetched { get; set; }

        /// <summary>Gets or sets whether the remote renderer draws the tile (a background-style job in either mode).</summary>
        public bool IsRemote { get; set; }

        /// <summary>Gets or sets whether the surface still shows this tile's pixels from before an invalidation (counted as resident).</summary>
        public bool OldPixels { get; set; }

        public CancellationTokenSource? Cancellation { get; set; }

        public CancellationTokenSource? Operation { get; set; }

        public Task? Load { get; set; }

        public PdfPage? Page { get; set; }

        public Bitmap? Target { get; set; }

        public ITileBackend? Backend { get; set; }

        public ITileRender? Rendering { get; set; }

        public bool IsResolved => State is TileState.Resident or TileState.Failed;

        public bool IsDrawing => State is TileState.Drawing;

        public void Present(Tile presented, Bitmap pixels) => scheduler.PresentBackground(this, pixels);

        public void PresentEmpty(Tile presented) => scheduler.PresentEmpty(this);

        /// <summary>Runs a background job on the worker that took it.</summary>
        public void Execute() => _ = scheduler.RunBackgroundAsync(this);
    }
}
