using System.Diagnostics;
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
/// ISO 32000-2 §10.7.1 (rendering through display lists, ADR 0006).
/// </para>
/// <para>
/// The shell calls <see cref="Update"/> whenever the viewport changes and <see cref="RunFrame"/> once per frame (for example from
/// <c>requestAnimationFrame</c>) while it returns <see langword="true"/>. With a cooperative session (the browser, one thread) the
/// frame does the work itself, in slices of the session's <see cref="ViewerOptions.SliceBudget"/>, until the frame's budget is spent:
/// at most one display-list build runs per frame, and only while no slow build has run in it yet, because a build cannot be
/// interrupted, so a frame overruns its budget by at most one build. With a background session the frame starts up to
/// <see cref="TileSchedulerOptions.MaxConcurrency"/> renders on worker threads and returns at once.
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
    private readonly int _tileSize;
    private readonly long _budget;
    private readonly double _nearMargin;
    private readonly double _renderAhead;
    private readonly double _placeholderScale;
    private readonly int _maxConcurrency;
    private readonly Action<TileSchedulerEvent>? _observer;
    private readonly Dictionary<Tile, TileJob> _jobs = [];
    private List<TileJob> _wanted = [];
    private List<TileJob> _previous = [];
    private Tile[] _tiles = new Tile[64];
    private VisiblePage[] _pages = new VisiblePage[16];
    private PageLayout _layout;
    private Viewport _viewport;
    private bool _hasViewport;
    private bool _replan;
    private int _direction = 1;
    private long _generation;
    private long _residentBytes;
    private long _drawingBytes;
    private long _peakBytes;
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
        _tileSize = options.TileSize;
        _budget = options.MemoryBudget;
        _nearMargin = options.NearMargin;
        _renderAhead = options.RenderAhead;
        _placeholderScale = options.PlaceholderScale;
        _maxConcurrency = options.MaxConcurrency;
        _observer = options.Observer;
    }

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
        if (!(viewport.Width > 0) || !(viewport.Height > 0) || !(viewport.Zoom > 0) || double.IsInfinity(viewport.Zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "A viewport needs a positive size and a positive, finite zoom.");
        }

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

            _longestFrame = Math.Max(_longestFrame, Stopwatch.GetElapsedTime(start).Ticks);
            // Work the budget cannot make room for waits for the viewport to move (or, on workers, for running renders to end).
            more = HasWork() && (!_blocked || _running > 0);
        }

        CancelAll(cancel);
        return more;
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

        for (int i = 0; i < count; i++)
        {
            Want(_tiles[i], priority, placeholder: false);
        }
    }

    private void AddPlaceholders(in Viewport viewport, TilePriority priority)
    {
        if (!(_placeholderScale > 0) || _placeholderScale >= viewport.Zoom)
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
            var tile = new Tile(page, _placeholderScale, _layout.GetPagePixels(page, _placeholderScale)) { Rotation = _layout.Rotation };
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
            _session.Document.TrimCaches(Math.Max(0, room));
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
        return true;
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

            if (job.State == TileState.New || (job.State == TileState.Drawing && job.Load is null))
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

        job.Rendering.Dispose();
        job.Rendering = null;
        try
        {
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
        _residentBytes += job.Bytes;
        _residentCount++;
        _presented++;
        Enforce();
        Trace(TileEventKind.Presented, job);
    }

    /// <summary>A cooperative render failed: its resources go back and the tile is not tried again while it stays wanted.</summary>
    private void Fail(TileJob job)
    {
        Release(job);
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
        if (_cooperative)
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

            job.Bytes = bytes;
            job.State = TileState.Drawing;
            job.Cancellation = new CancellationTokenSource();
            job.Reserved = true;
            _drawingBytes += bytes;
            _running++;
            _started++;
            Trace(TileEventKind.Started, job);
            _ = RunBackgroundAsync(job);
        }
    }

    private async Task RunBackgroundAsync(TileJob job)
    {
        bool failed = false;
        try
        {
            await _session.RenderTileAsync(job.Tile, job, job.Cancellation!.Token).ConfigureAwait(false);
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
            if (job.Reserved)
            {
                job.Reserved = false;
                _drawingBytes -= job.Bytes;
            }

            if (job.State == TileState.Drawing || job.State == TileState.Presenting)
            {
                if (failed)
                {
                    job.State = TileState.Failed;
                    Trace(TileEventKind.Failed, job);
                }
                else
                {
                    job.State = TileState.New; // stopped by the session closing; nothing reached the surface
                }
            }
        }

        job.Cancellation!.Dispose();
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
        lock (_gate)
        {
            job.Reserved = false;
            _drawingBytes -= job.Bytes;
            MakeResident(job);
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
    private sealed class TileJob(TileScheduler scheduler, Tile tile) : IRenderSurface
    {
        public Tile Tile { get; } = tile;

        public TileState State { get; set; }

        public long Generation { get; set; }

        public TilePriority Priority { get; set; }

        public bool IsPlaceholder { get; set; }

        public long Bytes { get; set; }

        public bool Reserved { get; set; }

        public bool Prefetched { get; set; }

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
    }
}
