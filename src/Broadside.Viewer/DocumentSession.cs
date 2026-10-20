using Broadside.Content;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// One open document in a viewer: owns the document, its engine and options and the tile backends (the backend choice), caches page
/// geometry, builds display lists off the UI thread with cancellation and renders tiles onto an <see cref="IRenderSurface"/>.
/// Framework-neutral: every UI shell (Blazor, WPF, MAUI, Avalonia) drives the same session.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.7.3.3 (pages), §14.11.2 (the crop box is displayed) and §10.7.1 (rendering through display lists, ADR 0006).
/// Spec #33 story 54 and viewer capabilities 1, 2 and 5.
/// </para>
/// <para>
/// Every operation is asynchronous and never blocks its caller: in <see cref="ViewerThreading.Background"/> mode the work runs on
/// thread-pool workers; in <see cref="ViewerThreading.Cooperative"/> mode (the browser) it runs on the caller's thread in slices with
/// yields between them. Finished tiles are presented on the context that awaited the render. Display lists come from the document's
/// own cache (<see cref="PdfOptions.DisplayListCacheBudget"/>), so the tiles of one page at any zoom share one list. Pages of a
/// remote document (<see cref="OpenAsync(HttpClient, Uri)"/>) are prefetched before use, which is
/// how a browser reaches them (<see cref="PdfPageCollection.PrefetchAsync"/>).
/// </para>
/// <para>
/// Closing (<see cref="Dispose"/> or <see cref="DisposeAsync"/>) cancels pending work; once it has stopped, the session disposes its
/// backends and the document, and every pooled tile buffer is back in its pool. <see cref="DisposeAsync"/> completes then.
/// Safe to use from several threads; the document must not be edited while the session reads it.
/// </para>
/// </remarks>
public sealed class DocumentSession : IDisposable, IAsyncDisposable
{
    private readonly bool _remote;
    private readonly bool _cooperative;
    private readonly TimeSpan _sliceBudget;
    private readonly DisplayIntent _intent;
    private readonly Func<ITileBackend> _backendFactory;
    private readonly Func<CancellationToken, ValueTask> _yield;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _closing = new();
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _slots;
    private readonly Stack<ITileBackend> _idleBackends = new();
    private readonly PageMetrics?[] _metrics;
    private readonly Dictionary<(int Page, DisplayLayers Layers), PendingBuild> _builds = [];
    private readonly DocumentGate? _reads;
    private int _pending;
    private bool _closed;
    private bool _cancelIssued;
    private bool _releasing;
    private long _pixelBytes;
    private long _presented;
    private long _cancelled;
    private int _liveBackends;

    private DocumentSession(PdfDocument document, ViewerOptions options, bool remote)
    {
        Document = document;
        _remote = remote;
        _cooperative = options.IsCooperative;
        _sliceBudget = options.SliceBudget;
        _intent = options.Intent;
        _backendFactory = options.BackendFactory ?? (static () => new SoftwareTileBackend());
        _yield = options.Yield ?? YieldAsync;
        _slots = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        MaxConcurrency = options.MaxConcurrency;
        _metrics = new PageMetrics?[document.Pages.Count];
        _reads = _cooperative ? null : new DocumentGate();
        RemoteRenderer = options.RemoteRenderer;
    }

    /// <summary>Gets the document. The session owns it and disposes it when it closes.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the renderer that draws the tiles it accepts outside the viewer's thread, if the options selected one.</summary>
    /// <remarks>Spec #33 viewer capability 1 (#357), ADR 0013. A shell keeps overlays in tiles of their own when it is set.</remarks>
    public IRemoteTileRenderer? RemoteRenderer { get; }

    /// <summary>Gets whether the document is read through range requests (pages are prefetched before use).</summary>
    internal bool IsRemote => _remote;

    /// <summary>Gets whether the session works on its caller's thread in slices (the browser) instead of on thread-pool workers.</summary>
    internal bool IsCooperative => _cooperative;

    /// <summary>Gets the longest piece of drawing work run between yields in cooperative mode.</summary>
    internal TimeSpan SliceBudget => _sliceBudget;

    /// <summary>Gets the most tiles drawn at once.</summary>
    internal int MaxConcurrency { get; }

    /// <summary>
    /// Gets the backends the session has created and not yet disposed (rented, idle or drawing). Each holds pooled scratch buffers
    /// until it is disposed, so this is zero once a closed session has released everything it took from the pools.
    /// </summary>
    internal int LiveBackends => Volatile.Read(ref _liveBackends);

    /// <summary>Gets the number of pages.</summary>
    public int PageCount => _metrics.Length;

    /// <summary>Gets a value indicating whether the session has been closed.</summary>
    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    /// <summary>Gets what the session is doing and holding right now.</summary>
    public SessionStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new SessionStatistics(_pending, Interlocked.Read(ref _pixelBytes), Interlocked.Read(ref _presented), Interlocked.Read(ref _cancelled));
            }
        }
    }

    /// <summary>Opens the document at a path with default options.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static DocumentSession Open(string path) => Open(path, null);

    /// <summary>Opens a document from a stream with default options; the caller keeps the stream open for the session's lifetime.</summary>
    /// <param name="stream">The stream; readable and seekable.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static DocumentSession Open(Stream stream) => Open(stream, null);

    /// <summary>Opens a document held in memory with default options.</summary>
    /// <param name="bytes">The file's bytes, which must not change while the session is open.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static DocumentSession Open(ReadOnlyMemory<byte> bytes) => Open(bytes, null);

    /// <summary>Opens a document from a stream asynchronously with default options.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static Task<DocumentSession> OpenAsync(Stream stream) => OpenAsync(stream, null, CancellationToken.None);

    /// <summary>Opens a remote document over HTTP range requests with default options, first page first.</summary>
    /// <param name="httpClient">The client the requests go through.</param>
    /// <param name="uri">The document's address.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5 and Annex F. Spec #33 viewer capability 5.</remarks>
    public static Task<DocumentSession> OpenAsync(HttpClient httpClient, Uri uri) => OpenAsync(httpClient, uri, null, CancellationToken.None);

    /// <summary>Opens the document at a path.</summary>
    /// <param name="path">The file.</param>
    /// <param name="options">The session's options, or <see langword="null"/> for defaults.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static DocumentSession Open(string path, ViewerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(path);
        options ??= new ViewerOptions();
        PdfEngine engine = EngineOf(options);
        PdfDocument document = options.Credentials is { } credentials ? engine.Open(path, credentials) : engine.Open(path);
        return new DocumentSession(document, options, remote: false);
    }

    /// <summary>Opens a document from a stream, which the caller keeps open for the session's lifetime and disposes after it.</summary>
    /// <param name="stream">The stream; readable and seekable.</param>
    /// <param name="options">The session's options, or <see langword="null"/> for defaults.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static DocumentSession Open(Stream stream, ViewerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new ViewerOptions();
        PdfEngine engine = EngineOf(options);
        PdfDocument document = options.Credentials is { } credentials ? engine.Open(stream, credentials) : engine.Open(stream);
        return new DocumentSession(document, options, remote: false);
    }

    /// <summary>Opens a document held in memory.</summary>
    /// <param name="bytes">The file's bytes, which must not change while the session is open.</param>
    /// <param name="options">The session's options, or <see langword="null"/> for defaults.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static DocumentSession Open(ReadOnlyMemory<byte> bytes, ViewerOptions? options)
    {
        options ??= new ViewerOptions();
        PdfEngine engine = EngineOf(options);
        PdfDocument document = options.Credentials is { } credentials ? engine.Open(bytes, credentials) : engine.Open(bytes);
        return new DocumentSession(document, options, remote: false);
    }

    /// <summary>
    /// Opens a document from a stream asynchronously (a browser file or a network stream): a stream that cannot seek is read into memory
    /// without blocking.
    /// </summary>
    /// <param name="stream">The stream, which the caller keeps open for the session's lifetime when it can seek.</param>
    /// <param name="options">The session's options, or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5.</remarks>
    public static async Task<DocumentSession> OpenAsync(Stream stream, ViewerOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new ViewerOptions();
        PdfEngine engine = EngineOf(options);
        PdfDocument document;
        if (options.Credentials is { } credentials)
        {
            var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            document = engine.Open(copy.GetBuffer().AsMemory(0, (int)copy.Length), credentials);
        }
        else
        {
            document = await engine.OpenAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        return new DocumentSession(document, options, remote: false);
    }

    /// <summary>
    /// Opens a remote document over HTTP range requests, first page first: pages are fetched as they are needed, and before each use
    /// the session prefetches what the page needs, so this works where synchronous fetching is impossible (the browser).
    /// </summary>
    /// <param name="httpClient">The client the requests go through.</param>
    /// <param name="uri">The document's address.</param>
    /// <param name="options">The session's options, or <see langword="null"/> for defaults.</param>
    /// <param name="cancellationToken">Stops the open.</param>
    /// <returns>The session; the caller closes it.</returns>
    /// <remarks>ISO 32000-2 §7.5 and Annex F (linearized files). Spec #33 viewer capability 5.</remarks>
    public static async Task<DocumentSession> OpenAsync(HttpClient httpClient, Uri uri, ViewerOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(uri);
        options ??= new ViewerOptions();
        PdfDocument document = await EngineOf(options).OpenAsync(httpClient, uri, cancellationToken).ConfigureAwait(false);
        return new DocumentSession(document, options, remote: true);
    }

    /// <summary>Returns a page's cached geometry, if it has been loaded.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="metrics">The geometry, when known.</param>
    /// <returns><see langword="true"/> when the page's geometry is cached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page does not exist.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public bool TryGetPageMetrics(int pageIndex, out PageMetrics metrics)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        lock (_gate)
        {
            if (_metrics[pageIndex] is { } known)
            {
                metrics = known;
                return true;
            }
        }

        metrics = default;
        return false;
    }

    /// <summary>Returns a page's geometry, loading the page first if needed (a remote page is prefetched), and caches it.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="cancellationToken">Stops the load.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page does not exist.</exception>
    /// <exception cref="ObjectDisposedException">The session is closed.</exception>
    /// <remarks>ISO 32000-2 §7.7.3.3 and §14.11.2.</remarks>
    public async ValueTask<PageMetrics> GetPageMetricsAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        if (TryGetPageMetrics(pageIndex, out PageMetrics metrics))
        {
            return metrics;
        }

        CancellationTokenSource operation = Enter(cancellationToken);
        try
        {
            await LoadPageAsync(pageIndex, operation.Token).ConfigureAwait(false);
            TryGetPageMetrics(pageIndex, out metrics);
            return metrics;
        }
        finally
        {
            Exit(operation);
        }
    }

    /// <summary>
    /// Returns a page's display list, built off the UI thread (or after a yield in cooperative mode) and kept in the document's cache,
    /// so every tile of the page at every zoom replays the same list.
    /// </summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="layers">The layers the list holds.</param>
    /// <param name="cancellationToken">Stops the build at its next check (every 1,024 operators and inside long image decodes).</param>
    /// <returns>The list.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled or the session closed.</exception>
    /// <exception cref="ObjectDisposedException">The session is closed.</exception>
    /// <remarks>ISO 32000-2 §8.2 and §12.5 (layers). ADR 0006.</remarks>
    public async ValueTask<DisplayList> GetDisplayListAsync(int pageIndex, DisplayLayers layers = DisplayLayers.All, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource operation = Enter(cancellationToken);
        try
        {
            PdfPage page = await LoadPageAsync(pageIndex, operation.Token).ConfigureAwait(false);
            return await BuildAsync(pageIndex, page, layers, operation.Token).ConfigureAwait(false);
        }
        finally
        {
            Exit(operation);
        }
    }

    /// <summary>Returns the tile that covers a whole page at a scale.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="scale">Pixels per point.</param>
    /// <param name="rotation">Rotation added to the page's own, a multiple of 90 degrees.</param>
    /// <param name="cancellationToken">Stops loading the page.</param>
    /// <returns>The tile, its region the whole page's pixels.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public async ValueTask<Tile> GetPageTileAsync(int pageIndex, double scale, int rotation = 0, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource operation = Enter(cancellationToken);
        try
        {
            PdfPage page = await LoadPageAsync(pageIndex, operation.Token).ConfigureAwait(false);
            PixelRectangle region = new RenderOptions().WithScale(scale).WithRotation(rotation).GetPixelRectangle(page);
            return new Tile(pageIndex, scale, region) { Rotation = rotation };
        }
        finally
        {
            Exit(operation);
        }
    }

    /// <summary>
    /// Renders one tile from the page's cached display list and presents it to <paramref name="surface"/> on the awaiting context. A
    /// render cancelled by its token or by closing the session never reaches the surface.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="surface">Where the finished pixels go.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>A task that completes once the tile has been presented.</returns>
    /// <exception cref="ArgumentException">The tile's region is empty or its scale is not positive.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled or the session closed.</exception>
    /// <exception cref="ObjectDisposedException">The session is closed.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.2 and §10.7.1. Spec #33 viewer capabilities 1 and 2.</remarks>
    public async Task RenderTileAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (tile.Region.Width <= 0 || tile.Region.Height <= 0 || !(tile.Scale > 0) || double.IsInfinity(tile.Scale))
        {
            throw new ArgumentException("A tile needs a non-empty region and a positive, finite scale.", nameof(tile));
        }

        CancellationTokenSource operation = Enter(cancellationToken);
        CancellationToken token = operation.Token;
        bool slot = false;
        ITileBackend? backend = null;
        try
        {
            PdfPage page = await LoadPageAsync(tile.PageIndex, token).ConfigureAwait(true);
            DisplayList list = await BuildAsync(tile.PageIndex, page, tile.Layers, token).ConfigureAwait(true);
            Matrix transform = GetTileTransform(page, tile);
            if (list.OperationCount == 0 && (tile.Layers & DisplayLayers.Content) == 0 && surface is IEmptyTileSink empty)
            {
                // An overlay with nothing on it: the scheduler keeps it without pixels (#357).
                token.ThrowIfCancellationRequested();
                empty.PresentEmpty(tile);
                Interlocked.Increment(ref _presented);
                return;
            }

            await _slots.WaitAsync(token).ConfigureAwait(true);
            slot = true;
            backend = RentBackend();
            await DrawAndPresentAsync(tile, surface, backend, list, transform, token).ConfigureAwait(true);
            Interlocked.Increment(ref _presented);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Interlocked.Increment(ref _cancelled);
            throw;
        }
        finally
        {
            if (backend is not null)
            {
                ReturnBackend(backend);
            }

            if (slot)
            {
                _slots.Release();
            }

            Exit(operation);
        }
    }

    /// <summary>Renders a whole page at a scale as one tile and presents it to <paramref name="surface"/>.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="scale">Pixels per point.</param>
    /// <param name="surface">Where the finished pixels go.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <returns>The tile presented.</returns>
    /// <remarks>ISO 32000-2 §10.7.1 and §14.11.2.</remarks>
    public async Task<Tile> RenderPageAsync(int pageIndex, double scale, IRenderSurface surface, CancellationToken cancellationToken = default)
    {
        Tile tile = await GetPageTileAsync(pageIndex, scale, 0, cancellationToken).ConfigureAwait(true);
        await RenderTileAsync(tile, surface, cancellationToken).ConfigureAwait(true);
        return tile;
    }

    /// <summary>
    /// Starts an edit of <see cref="Document"/> on the calling thread (the UI thread): in <see cref="ViewerThreading.Background"/> mode it
    /// stops the reads that workers have in progress (page loads, display-list builds and page-model loads, which start again after
    /// the edit) and holds new ones until the returned scope is disposed. Edit the document only inside such a scope while the session
    /// reads it; <see cref="AnnotationLayer"/>'s edits take one themselves.
    /// </summary>
    /// <returns>The edit's scope; dispose it when the edit is done (then invalidate the tiles it touched).</returns>
    /// <exception cref="InvalidOperationException">Another thread is editing the document.</exception>
    /// <remarks>
    /// Spec #33 viewer capability 1 (#357): the thread-safety contract, concurrent reads and renders, single-threaded mutation. Tile
    /// replays read only the finished display list and go on. In <see cref="ViewerThreading.Cooperative"/> mode every read runs on the
    /// caller's thread, so the scope does nothing.
    /// </remarks>
    public IDisposable BeginEdit()
    {
        if (_reads is not { } reads)
        {
            return EditScope.None;
        }

        reads.BeginEdit();
        return new EditScope(reads.EndEdit);
    }

    /// <summary>
    /// Reads the document off the caller's thread: on a worker in <see cref="ViewerThreading.Background"/> mode, outside any edit
    /// (<see cref="BeginEdit"/>); on the caller's thread in cooperative mode.
    /// </summary>
    internal async ValueTask<T> ReadAsync<T>(Func<CancellationToken, T> work, CancellationToken cancellationToken)
    {
        if (_reads is not { } reads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return work(cancellationToken);
        }

        CancellationTokenSource operation = Enter(cancellationToken);
        try
        {
            return await reads.ReadAsync(work, operation.Token).ConfigureAwait(false);
        }
        finally
        {
            Exit(operation);
        }
    }

    /// <summary>Closes the session: cancels pending work; the backends and the document are released once that work has stopped.</summary>
    public void Dispose() => _ = CloseAsync();

    /// <summary>Closes the session and completes once pending work has stopped and every resource is released.</summary>
    /// <returns>A task that completes when the session is fully released.</returns>
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private static PdfEngine EngineOf(ViewerOptions options) =>
        options.Engine ?? new PdfEngine(options.DocumentOptions ?? new PdfOptions());

    private static async ValueTask YieldAsync(CancellationToken cancellationToken) => await Task.Yield();

    private async Task DrawAndPresentAsync(Tile tile, IRenderSurface surface, ITileBackend backend, DisplayList list, Matrix transform, CancellationToken token)
    {
        using var target = new Bitmap(tile.Region.Width, tile.Region.Height);
        long bytes = (long)target.Stride * target.Height;
        Interlocked.Add(ref _pixelBytes, bytes);
        try
        {
            if ((tile.Layers & DisplayLayers.Content) != 0)
            {
                target.Pixels.Fill(byte.MaxValue); // the paper: opaque white, premultiplied BGRA
            }

            if (_cooperative)
            {
                using ITileRender rendering = backend.Begin(list, target, transform, token);
                while (!rendering.RenderSlice(_sliceBudget))
                {
                    await _yield(token).ConfigureAwait(true);
                }
            }
            else
            {
                await Task.Run(
                    () =>
                    {
                        using ITileRender rendering = backend.Begin(list, target, transform, token);
                        rendering.RenderSlice(Timeout.InfiniteTimeSpan);
                    },
                    token).ConfigureAwait(true);
            }

            // A render cancelled after its last slice still never reaches the surface.
            token.ThrowIfCancellationRequested();
            surface.Present(tile, target);
        }
        finally
        {
            Interlocked.Add(ref _pixelBytes, -bytes);
        }
    }

    internal async ValueTask<PdfPage> LoadPageAsync(int pageIndex, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        PdfPage page;
        if (_remote)
        {
            page = await Document.Pages.PrefetchAsync(pageIndex, token).ConfigureAwait(false);
        }
        else if (_reads is not { } reads)
        {
            page = Document.Pages[pageIndex];
        }
        else if (reads.TryEnterRead())
        {
            // No edit in progress: the page object is read here, so requests for a page arrive at its list build together and share it.
            try
            {
                page = Document.Pages[pageIndex];
            }
            finally
            {
                reads.ExitRead();
            }
        }
        else
        {
            // An edit is in progress: read after it, on a worker.
            page = await ReadPageAsync(reads, pageIndex, token).ConfigureAwait(false);
        }
        lock (_gate)
        {
            _metrics[pageIndex] ??= PageMetrics.Of(pageIndex, page);
        }

        return page;
    }

    // The lambdas live in methods of their own: a lambda that captures a parameter makes the compiler allocate its closure on every
    // call of the method that declares the parameter, so LoadPageAsync and BuildCoreAsync would allocate even in cooperative mode.
    private Task<PdfPage> ReadPageAsync(DocumentGate reads, int pageIndex, CancellationToken token) =>
        reads.ReadAsync(_ => Document.Pages[pageIndex], token);

    private Task<DisplayList> BuildOnWorkerAsync(PdfPage page, DisplayLayers layers, CancellationToken token) =>
        _reads!.ReadAsync(read => page.CreateDisplayList(layers, _intent, new ContentOptions().WithCancellation(read)), token);

    /// <summary>Builds (or takes from the document's cache) a page's list on the calling thread: the tile scheduler's cooperative path.</summary>
    internal DisplayList CreateDisplayList(PdfPage page, DisplayLayers layers, CancellationToken token) =>
        page.CreateDisplayList(layers, _intent, new ContentOptions().WithCancellation(token));

    /// <summary>The device transform that draws <paramref name="tile"/>'s region of <paramref name="page"/> at the bitmap's origin.</summary>
    internal static Matrix GetTileTransform(PdfPage page, in Tile tile) =>
        new RenderOptions().WithScale(tile.Scale).WithRotation(tile.Rotation).GetDeviceTransform(page)
            * Matrix.CreateTranslation(-tile.Region.X, -tile.Region.Y);

    /// <summary>Counts pixels a tile render holds outside the session (the scheduler's cooperative renders).</summary>
    internal void AddPixelBytes(long bytes) => Interlocked.Add(ref _pixelBytes, bytes);

    /// <summary>Counts a tile presented (or cancelled) by the scheduler's cooperative path.</summary>
    internal void CountTile(bool presented) => Interlocked.Increment(ref presented ? ref _presented : ref _cancelled);

    private async ValueTask<DisplayList> BuildAsync(int pageIndex, PdfPage page, DisplayLayers layers, CancellationToken token)
    {
        // Requests for the same list while it is being built share one build (the tiles of a page arrive together). The build stops
        // when every requester has stopped waiting, or when the session closes.
        (int, DisplayLayers) key = (pageIndex, layers);
        PendingBuild? build;
        bool start = false;
        lock (_gate)
        {
            if (!_builds.TryGetValue(key, out build))
            {
                build = new PendingBuild(_closing.Token);
                _builds.Add(key, build);
                start = true;
            }

            build.Waiters++;
        }

        try
        {
            if (start)
            {
                _ = RunBuildAsync(key, page, layers, build);
            }

            return await build.Completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            bool abandon;
            lock (_gate)
            {
                abandon = --build.Waiters == 0 && !build.Completion.Task.IsCompleted;
                if (abandon && _builds.TryGetValue(key, out PendingBuild? current) && current == build)
                {
                    _builds.Remove(key);
                }
            }

            if (abandon)
            {
                try
                {
                    await build.Cancellation.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // The build finished meanwhile.
                }
            }
        }
    }

    private async Task RunBuildAsync((int, DisplayLayers) key, PdfPage page, DisplayLayers layers, PendingBuild build)
    {
        try
        {
            build.Completion.TrySetResult(await BuildCoreAsync(page, layers, build.Cancellation.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException exception)
        {
            build.Completion.TrySetCanceled(exception.CancellationToken);
        }
#pragma warning disable CA1031 // every failure reaches the requesters through the shared task
        catch (Exception exception)
#pragma warning restore CA1031
        {
            build.Completion.TrySetException(exception);
        }
        finally
        {
            lock (_gate)
            {
                if (_builds.TryGetValue(key, out PendingBuild? current) && current == build)
                {
                    _builds.Remove(key);
                }
            }

            build.Dispose();
        }
    }

    private async ValueTask<DisplayList> BuildCoreAsync(PdfPage page, DisplayLayers layers, CancellationToken token)
    {
        ContentOptions options = new ContentOptions().WithCancellation(token);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (_cooperative)
                {
                    await _yield(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return page.CreateDisplayList(layers, _intent, options);
                }

                // A read: an edit stops the build and it starts again after the edit.
                return await BuildOnWorkerAsync(page, layers, token).ConfigureAwait(false);
            }
            catch (PdfRangeNotLoadedException) when (_remote && attempt == 0)
            {
                // A resource outside the page's prefetched closure (a shared object the hints did not list): fetch the page's whole
                // closure without blocking, then build again.
                await page.PrefetchAsync(token).ConfigureAwait(false);
            }
        }
    }

    internal CancellationTokenSource Enter(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _pending++;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(_closing.Token, cancellationToken);
    }

    internal void Exit(CancellationTokenSource operation)
    {
        operation.Dispose();
        bool release;
        lock (_gate)
        {
            _pending--;
            release = _cancelIssued && _pending == 0;
        }

        if (release)
        {
            Release();
        }
    }

    internal ITileBackend RentBackend()
    {
        lock (_gate)
        {
            if (_idleBackends.TryPop(out ITileBackend? idle))
            {
                return idle;
            }
        }

        ITileBackend created = _backendFactory();
        Interlocked.Increment(ref _liveBackends);
        return created;
    }

    internal void ReturnBackend(ITileBackend backend)
    {
        lock (_gate)
        {
            if (!_closed)
            {
                _idleBackends.Push(backend);
                return;
            }
        }

        DisposeBackend(backend);
    }

    private void DisposeBackend(ITileBackend backend)
    {
        backend.Dispose();
        Interlocked.Decrement(ref _liveBackends);
    }

    private async Task CloseAsync()
    {
        bool release;
        lock (_gate)
        {
            if (_closed)
            {
                release = false;
            }
            else
            {
                _closed = true;
                release = true;
            }
        }

        if (!release)
        {
            await _released.Task.ConfigureAwait(false);
            return;
        }

        await _closing.CancelAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _cancelIssued = true;
            release = _pending == 0;
        }

        if (release)
        {
            Release();
        }

        await _released.Task.ConfigureAwait(false);
    }

    private void Release()
    {
        // Runs once, after Close has cancelled: by Close when nothing is pending, else by the last pending operation's Exit.
        ITileBackend[] backends;
        lock (_gate)
        {
            if (_releasing)
            {
                return;
            }

            _releasing = true;
            backends = [.. _idleBackends];
            _idleBackends.Clear();
        }

        foreach (ITileBackend idle in backends)
        {
            DisposeBackend(idle);
        }

        _slots.Dispose();
        _closing.Dispose();
        _reads?.Dispose();
        RemoteRenderer?.Dispose();
        Document.Dispose();
        _released.TrySetResult();
    }

    private sealed class PendingBuild(CancellationToken closing) : IDisposable
    {
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(closing);

        public TaskCompletionSource<DisplayList> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Waiters { get; set; }

        public void Dispose() => Cancellation.Dispose();
    }

    private sealed class EditScope(Action? end) : IDisposable
    {
        public static readonly EditScope None = new(null);

        private Action? _end = end;

        public void Dispose() => Interlocked.Exchange(ref _end, null)?.Invoke();
    }
}
