using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// Renders the viewer's content tiles in a Web Worker (#357): a second .NET runtime, booted from the app's own framework files, holds
/// its own copy of each document, builds its display lists and rasterizes its tiles, and hands each tile back as a transferable
/// <c>ImageBitmap</c>. The page's thread then only plans frames, presents finished tiles and draws the annotation and form-field
/// overlays (so edits never wait for the worker and never reach its copy). It needs no cross-origin isolation, so it works on any
/// static host; where Web Workers are missing, or the app does not serve the worker, the viewer draws every tile itself.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33 viewer capability 1 (#357). Strictly opt-in: the library never starts the worker by itself. It costs a second
/// .NET runtime (boot time and memory, see docs/benchmarks/viewer-render-worker.md), so an app that wants it does all three of: serve
/// it from <c>Main</c>, start it with <see cref="StartAsync"/>, and give each session a renderer with
/// <see cref="ViewerOptions.UseRemoteRenderer"/>. Without them every tile renders on the page's thread, as before.
/// </para>
/// <para>The app's <c>Main</c> runs in the worker too, and must start with:</para>
/// <code>
/// if (RenderWorker.IsWorker)
/// {
///     RenderWorker.Serve(() =&gt; new PdfEngine(options)); // the engine the page opens documents with
///     return;
/// }
/// </code>
/// <para>
/// Then <see cref="StartAsync"/> starts the worker once, and each document opened from bytes gets a renderer with
/// <see cref="Open(ReadOnlySpan{byte}, ViewerOptions)"/> (<c>options.UseRemoteRenderer(RenderWorker.Open(bytes, options)!)</c>) or, for a
/// document read through range requests, <see cref="Open(Uri, ViewerOptions)"/>; the worker then renders with the options' intent and
/// decodes images as they say (at the resolution they are drawn at, by default). Password-protected documents are drawn on the page's
/// thread.
/// </para>
/// <para>
/// The worker decodes JPEG images with the browser's own decoder before it draws a tile (<see cref="BrowserImageDecoder"/>: WebCodecs
/// <c>ImageDecoder</c>, or <c>createImageBitmap</c> with a read back where the browser has no <c>ImageDecoder</c>, as in Safari). The
/// engine of a worker given none has it on; an engine the app gives needs <c>UseBrowserImageDecoder()</c> in its options.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class RenderWorker
{
    private const string WorkerModule = "broadside-render-worker";
    private static readonly Dictionary<int, PdfDocument> Documents = [];
    private static readonly Dictionary<int, (int Document, TaskCompletionSource<JSObject?> Pending)> Jobs = [];
    private static readonly HashSet<int> Opening = [];
    private static readonly HashSet<int> ClosedWhileOpening = [];
    private static readonly Dictionary<int, WorkerTileRenderer> Renderers = [];
    private static Func<PdfEngine>? _engine;
    private static Task<bool>? _start;
    private static readonly Dictionary<double, SoftwareTileBackend> Backends = [];
    private static byte[] _rgba = [];
    private static int _nextDocument;
    private static int _nextJob;

    /// <summary>Gets whether this runtime is the render worker (the app's <c>Main</c> then calls <see cref="Serve"/> and returns).</summary>
    public static bool IsWorker => JSHost.GlobalThis.GetPropertyAsBoolean("__broadsideRenderWorker");

    /// <summary>Gets whether the worker is running and serving tiles (after <see cref="StartAsync"/> succeeded).</summary>
    public static bool IsRunning { get; private set; }

    /// <summary>Serves tiles from this runtime, the worker's: documents are opened with the engine <paramref name="engine"/> creates.</summary>
    /// <param name="engine">Creates the engine each document opens with (fonts, CMaps and options as the page's own).</param>
    /// <exception cref="InvalidOperationException">This runtime is not the render worker.</exception>
    public static void Serve(Func<PdfEngine> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!IsWorker)
        {
            throw new InvalidOperationException("RenderWorker.Serve runs in the render worker only (check RenderWorker.IsWorker first).");
        }

        _engine = engine;
    }

    /// <summary>
    /// Starts the render worker once (later calls share the first): it boots the app's runtime in a Web Worker and waits until the app's
    /// <c>Main</c> has called <see cref="Serve"/>.
    /// </summary>
    /// <param name="timeout">How long to wait for the worker, or <see langword="null"/> for 60 seconds.</param>
    /// <returns><see langword="true"/> when tiles can be rendered in the worker; otherwise the viewer draws them itself.</returns>
    public static Task<bool> StartAsync(TimeSpan? timeout = null)
    {
        if (IsWorker)
        {
            return Task.FromResult(false);
        }

        return _start ??= StartCoreAsync(timeout ?? TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Sends a document to the worker and returns the renderer a session opened from the same bytes uses for its content tiles, or
    /// <see langword="null"/> when the worker is not running.
    /// </summary>
    /// <param name="document">The document's bytes, as the page's session opens them.</param>
    /// <param name="intent">The display intent the session renders with.</param>
    /// <returns>The renderer (the session owns and disposes it), or <see langword="null"/>.</returns>
    /// <remarks>Images are decoded at the resolution they are drawn at, keeping 2 samples per device pixel; <see cref="Open(ReadOnlySpan{byte}, ViewerOptions)"/> follows the session's options.</remarks>
    public static IRemoteTileRenderer? Open(ReadOnlySpan<byte> document, DisplayIntent intent) =>
        Open(document, intent, RenderOptions.DefaultImageSamplesPerPixel);

    /// <summary>
    /// Sends a document to the worker and returns the renderer a session opened from the same bytes with <paramref name="options"/>
    /// uses for its content tiles, or <see langword="null"/> when the worker is not running. The worker renders with the options'
    /// display intent and decodes images as they say (<see cref="ViewerOptions.ScaledImageDecode"/>,
    /// <see cref="ViewerOptions.ImageSamplesPerPixel"/>).
    /// </summary>
    /// <param name="document">The document's bytes, as the page's session opens them.</param>
    /// <param name="options">The options the session opens with.</param>
    /// <returns>The renderer (the session owns and disposes it), or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §8.9.4: reduced images are for display only.</remarks>
    public static IRemoteTileRenderer? Open(ReadOnlySpan<byte> document, ViewerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Open(document, options.Intent, SamplesPerPixel(options));
    }

    /// <summary>
    /// Has the worker open a document from an address through HTTP range requests and returns the renderer a session opened from the
    /// same address with <paramref name="options"/> uses, or <see langword="null"/> when the worker is not running. The worker renders
    /// with the options' display intent and decodes images as they say (<see cref="ViewerOptions.ScaledImageDecode"/>,
    /// <see cref="ViewerOptions.ImageSamplesPerPixel"/>).
    /// </summary>
    /// <param name="address">The document's absolute address (the worker resolves nothing against the page).</param>
    /// <param name="options">The options the session opens with.</param>
    /// <returns>The renderer (the session owns and disposes it), or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §8.9.4: reduced images are for display only.</remarks>
    public static IRemoteTileRenderer? Open(Uri address, ViewerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Open(address, options.Intent, SamplesPerPixel(options));
    }

    /// <summary>The samples per pixel the worker reduces images to for these options, or 0 for their own resolution.</summary>
    private static double SamplesPerPixel(ViewerOptions options) => options.ScaledImageDecode ? options.ImageSamplesPerPixel : 0;

    private static WorkerTileRenderer? Open(ReadOnlySpan<byte> document, DisplayIntent intent, double samplesPerPixel)
    {
        if (!IsRunning)
        {
            return null;
        }

        int id = ++_nextDocument;
        var renderer = new WorkerTileRenderer(id, samplesPerPixel);
        Renderers[id] = renderer;
        // The worker only reads the bytes (copied out of this runtime's heap before the call returns).
        ViewerInterop.WorkerOpen(id, (int)intent, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(document), document.Length));
        return renderer;
    }

    /// <summary>
    /// Has the worker open a document from an address through HTTP range requests (as <see cref="DocumentSession.OpenAsync(HttpClient, Uri, ViewerOptions?, CancellationToken)"/>
    /// does on the page) and returns the renderer a session opened from the same address uses, or <see langword="null"/> when the
    /// worker is not running.
    /// </summary>
    /// <param name="address">The document's absolute address (the worker resolves nothing against the page).</param>
    /// <param name="intent">The display intent the session renders with.</param>
    /// <returns>The renderer (the session owns and disposes it), or <see langword="null"/>.</returns>
    public static IRemoteTileRenderer? Open(Uri address, DisplayIntent intent) => Open(address, intent, RenderOptions.DefaultImageSamplesPerPixel);

    private static WorkerTileRenderer? Open(Uri address, DisplayIntent intent, double samplesPerPixel)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!IsRunning || !address.IsAbsoluteUri)
        {
            return null;
        }

        int id = ++_nextDocument;
        var renderer = new WorkerTileRenderer(id, samplesPerPixel);
        Renderers[id] = renderer;
        ViewerInterop.WorkerOpenAddress(id, (int)intent, address.AbsoluteUri);
        return renderer;
    }

    private static async Task<bool> StartCoreAsync(TimeSpan timeout)
    {
        await ViewerInterop.ImportAsync().ConfigureAwait(true);
        string status = await ViewerInterop.StartWorker((int)timeout.TotalMilliseconds).ConfigureAwait(true);
        IsRunning = status == "ready";
        if (!IsRunning)
        {
            Console.WriteLine($"Broadside render worker not used ({status}): tiles render on the page's thread.");
        }

        return IsRunning;
    }

    // ---- The page's side ----

    /// <summary>Renders a tile in the worker: its image, or <see langword="null"/> when the worker gave the tile back (it failed).</summary>
    internal static async Task<JSObject?> RenderAsync(int document, Tile tile, double samplesPerPixel, CancellationToken token)
    {
        if (!IsRunning)
        {
            return null;
        }

        int job = ++_nextJob;
        var pending = new TaskCompletionSource<JSObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Jobs[job] = (document, pending);
        PixelRectangle r = tile.Region;
        ViewerInterop.WorkerRender(job, document, tile.PageIndex, tile.Scale, tile.Rotation, r.X, r.Y, r.Width, r.Height, (int)tile.Layers, samplesPerPixel);
        try
        {
            using CancellationTokenRegistration cancel = token.Register(static state => ViewerInterop.WorkerCancel((int)state!), job);
            return await pending.Task.WaitAsync(token).ConfigureAwait(true);
        }
        finally
        {
            Jobs.Remove(job);
        }
    }

    internal static void CloseRenderer(int document)
    {
        Renderers.Remove(document);
        ViewerInterop.WorkerClose(document);
    }

    [JSExport]
    internal static void OnOpened(int document, bool opened, string? message)
    {
        if (!opened && Renderers.TryGetValue(document, out WorkerTileRenderer? renderer))
        {
            renderer.IsBroken = true; // later tiles draw on the page's thread
            GiveBack(document); // and so do the tiles already sent for it
            Console.WriteLine($"Broadside render worker could not open the document ({message}): tiles render on the page's thread.");
        }
    }

    [JSExport]
    internal static void OnRendered(int job, JSObject image)
    {
        if (Jobs.Remove(job, out (int Document, TaskCompletionSource<JSObject?> Pending) entry))
        {
            entry.Pending.TrySetResult(image);
        }
        else
        {
            image.Dispose(); // cancelled meanwhile
        }
    }

    /// <summary>The worker stopped after it had started (#357): every tile goes back to the page's thread from now on.</summary>
    [JSExport]
    internal static void OnWorkerFailed(string message)
    {
        IsRunning = false;
        foreach (WorkerTileRenderer renderer in Renderers.Values)
        {
            renderer.IsBroken = true;
        }

        GiveBack(null);
        Console.WriteLine($"Broadside render worker stopped ({message}): tiles render on the page's thread.");
    }

    /// <summary>Completes the outstanding tiles (of one document, or all) without an image: the scheduler draws them itself.</summary>
    private static void GiveBack(int? document)
    {
        foreach ((int job, (int Document, TaskCompletionSource<JSObject?> Pending) entry) in Jobs.ToArray())
        {
            if (document is null || entry.Document == document)
            {
                Jobs.Remove(job);
                entry.Pending.TrySetResult(null);
            }
        }
    }

    [JSExport]
    internal static void OnFailed(int job, string message)
    {
        if (Jobs.Remove(job, out (int Document, TaskCompletionSource<JSObject?> Pending) entry))
        {
            entry.Pending.TrySetException(new InvalidOperationException("The render worker failed to draw the tile: " + message));
        }
    }

    [JSExport]
    internal static void OnCancelled(int job)
    {
        if (Jobs.Remove(job, out (int Document, TaskCompletionSource<JSObject?> Pending) entry))
        {
            entry.Pending.TrySetCanceled();
        }
    }

    // ---- The worker's side ----

    [JSExport]
    internal static bool IsServing() => _engine is not null;

    [JSExport]
    internal static void OpenDocument(int document, int intent, byte[] bytes)
    {
        PdfEngine engine = _engine?.Invoke() ?? DefaultEngine;
        Documents[document] = engine.Open(bytes);
        DocumentIntents[document] = (DisplayIntent)intent;
    }

    [JSExport]
    internal static void CloseDocument(int document)
    {
        if (Opening.Contains(document))
        {
            ClosedWhileOpening.Add(document); // disposed when its open completes
        }

        if (Documents.Remove(document, out PdfDocument? closed))
        {
            closed.Dispose();
        }

        DocumentIntents.Remove(document);
        Remote.Remove(document);
    }

    [JSExport]
    internal static async Task OpenAddress(int document, int intent, string address)
    {
        PdfEngine engine = _engine?.Invoke() ?? DefaultEngine;
        _http ??= new HttpClient();
        Opening.Add(document);
        PdfDocument opened;
        try
        {
            opened = await engine.OpenAsync(_http, new Uri(address)).ConfigureAwait(true);
        }
        finally
        {
            Opening.Remove(document);
        }

        if (ClosedWhileOpening.Remove(document))
        {
            opened.Dispose(); // its renderer was disposed while the range requests ran
            return;
        }

        Documents[document] = opened;
        DocumentIntents[document] = (DisplayIntent)intent;
        Remote.Add(document);
    }

    [JSExport]
    internal static async Task Render(int job, int document, int pageIndex, double scale, int rotation, int x, int y, int width, int height, int layers, double samplesPerPixel)
    {
        PdfDocument pdf = Documents[document];
        bool remote = Remote.Contains(document);
        PdfPage page = remote ? await pdf.Pages.PrefetchAsync(pageIndex, CancellationToken.None).ConfigureAwait(true) : pdf.Pages[pageIndex];
        DisplayList list;
        try
        {
            list = page.CreateDisplayList((DisplayLayers)layers, DocumentIntents.GetValueOrDefault(document), new ContentOptions());
        }
        catch (PdfRangeNotLoadedException) when (remote)
        {
            // A resource outside the page's prefetched closure: fetch the whole closure, then build again (as the session does).
            await page.PrefetchAsync(CancellationToken.None).ConfigureAwait(true);
            list = page.CreateDisplayList((DisplayLayers)layers, DocumentIntents.GetValueOrDefault(document), new ContentOptions());
        }

        Matrix transform = new RenderOptions().WithScale(scale).WithRotation(rotation).GetDeviceTransform(page) * Matrix.CreateTranslation(-x, -y);
        using var target = new Bitmap(width, height);
        if (((DisplayLayers)layers & DisplayLayers.Content) != 0)
        {
            target.Pixels.Fill(byte.MaxValue); // the paper: opaque white, premultiplied BGRA
        }
        else
        {
            target.Pixels.Clear();
        }

        // The page's decode setting (issue #428): reduced images at its samples per pixel, or 0 for their own resolution.
        if (!Backends.TryGetValue(samplesPerPixel, out SoftwareTileBackend? backend))
        {
            backend = samplesPerPixel >= 1 ? new SoftwareTileBackend(true, samplesPerPixel) : new SoftwareTileBackend(false, RenderOptions.DefaultImageSamplesPerPixel);
            Backends[samplesPerPixel] = backend;
        }

        // The browser's own JPEG decoder decodes the tile's images first, in this worker (#373): ImageDecoder, or createImageBitmap
        // with a read back where the browser has no ImageDecoder (Safari), whose cost stays off the page's thread here.
        await BrowserImageDecoder.InitializeAsync().ConfigureAwait(true);
        await backend.PrefetchImagesAsync(list, transform, width, height, CancellationToken.None).ConfigureAwait(true);
        using (ITileRender render = backend.Begin(list, target, transform, CancellationToken.None))
        {
            render.RenderSlice(Timeout.InfiniteTimeSpan);
        }

        int length = width * height * 4;
        if (_rgba.Length < length)
        {
            _rgba = new byte[length];
        }

        Span<byte> rgba = _rgba.AsSpan(0, length);
        for (int row = 0; row < height; row++)
        {
            CanvasSurface.ToStraightRgba(target.GetRow(row)[..(width * 4)], rgba.Slice(row * width * 4, width * 4));
        }

        Deliver(job, width, height, rgba);
    }

    private static readonly Dictionary<int, DisplayIntent> DocumentIntents = [];
    private static PdfEngine? _defaultEngine;

    /// <summary>The engine of a worker whose app gave none: the browser's own JPEG decoder on, like the page's viewer.</summary>
    private static PdfEngine DefaultEngine => _defaultEngine ??= new PdfEngine(new PdfOptions().UseBrowserImageDecoder());
    private static readonly HashSet<int> Remote = [];
    private static HttpClient? _http;

    [JSImport("deliver", WorkerModule)]
    private static partial void Deliver(int job, int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
}

/// <summary>The page's handle on one document in the render worker: content tiles go there, overlays stay on the page.</summary>
[SupportedOSPlatform("browser")]
internal sealed class WorkerTileRenderer(int document, double samplesPerPixel) : IRemoteTileRenderer
{
    private bool _disposed;

    public bool IsBroken { get; set; }

    // One tile rendering and one waiting, so the worker never idles between them.
    public int MaxConcurrency => 2;

    public bool CanRender(in Tile tile) => !_disposed && !IsBroken && (tile.Layers & ~DisplayLayers.Content) == 0;

    public async Task RenderAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();
        if (await RenderWorker.RenderAsync(document, tile, samplesPerPixel, cancellationToken).ConfigureAwait(true) is not { } image)
        {
            // Given back (the worker stopped, or could not open the document): completing without presenting hands the tile to the
            // scheduler, which draws it on the page's thread now that CanRender declines.
            IsBroken = true;
            return;
        }

        using (image)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var placeholder = new Bitmap(tile.Region.Width, tile.Region.Height);
            WorkerPresentation.Hold(placeholder, image);
            try
            {
                surface.Present(tile, placeholder);
            }
            finally
            {
                WorkerPresentation.Release(placeholder);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            RenderWorker.CloseRenderer(document);
        }
    }
}

/// <summary>Hands a worker's tile to <see cref="CanvasSurface"/> through <see cref="IRenderSurface.Present"/>: the bitmap stands in for it.</summary>
[SupportedOSPlatform("browser")]
internal static class WorkerPresentation
{
    private static Bitmap? _held;
    private static JSObject? _image;

    public static void Hold(Bitmap placeholder, JSObject image)
    {
        _held = placeholder;
        _image = image;
    }

    public static bool TryTake(Bitmap pixels, out JSObject? image)
    {
        image = ReferenceEquals(_held, pixels) ? _image : null;
        return image is not null;
    }

    public static void Release(Bitmap placeholder)
    {
        if (ReferenceEquals(_held, placeholder))
        {
            _held = null;
            _image = null;
        }
    }
}
