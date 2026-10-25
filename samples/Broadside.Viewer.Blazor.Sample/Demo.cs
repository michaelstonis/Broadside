using System.Net;
using Broadside.Viewer.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>A gallery document, bundled with the app: pdf.js's TraceMonkey paper (real world), hand-written test corpus files and the sample's signed file.</summary>
/// <param name="File">The address relative to the app.</param>
/// <param name="Title">What the gallery shows.</param>
/// <param name="Remote">Whether to open it through HTTP range requests (first page first) instead of downloading it whole.</param>
/// <param name="Note">What to tell the user when it opens: single-feature test files can look blank or clipped on purpose.</param>
/// <param name="Panel">The panel that shows its feature (its name in lower case), opened with it.</param>
/// <param name="Mirror">
/// Where to download it when the site does not bundle it (the server answers 404): the documentation site leaves out TraceMonkey, whose
/// licence does not cover redistribution, and reads it from pdf.js's own copy instead (see README.md, "Documents and licences").
/// </param>
public sealed record GalleryEntry(string File, string Title, bool Remote = false, string? Note = null, string? Panel = null, string? Mirror = null);

/// <summary>
/// The demo's state, shared by the shell and every panel: the open document (a <see cref="DocumentSession"/> the app owns and hands to
/// the <see cref="PdfViewer"/>), where it came from, whether it was edited, and the viewer. Panels change it through these methods
/// and the shell re-renders on <see cref="Changed"/>.
/// </summary>
public sealed class Demo(HttpClient http, NavigationManager navigation, IJSRuntime js) : IAsyncDisposable
{
    /// <summary>The gallery, in the order the menu shows it; the first entry opens at start.</summary>
    public static readonly GalleryEntry[] Gallery =
    [
        new("documents/tracemonkey.pdf", "TraceMonkey paper (real world)", Mirror: "https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf"),
        new("documents/layout-two-column-tagged.pdf", "Two columns (tagged)"),
        new("documents/outline-full.pdf", "Outline", Note: "This file has an outline and no page content, so its page is blank (as in every viewer): its bookmarks are in the Outline panel.", Panel: "outline"),
        new("documents/acroform-fields.pdf", "Form fields"),
        new("documents/tagged-structure.pdf", "Tagged structure"),
        new("documents/layout-tables.pdf", "Tables"),
        new("documents/signed.pdf", "Signed document"),
        new("documents/info-dictionary.pdf", "Document information"),
        new("documents/annotations-link.pdf", "Links", Note: "This page has no content, only a link near its top left: point at it or tap it (the link is listed in the Annotate panel).", Panel: "annotate"),
        new("documents/shading-radial-cases.pdf", "Shadings", Note: "The cone in the middle of the second row is cut flat at top and bottom by its cell's clip, as in other renderers."),
        new("documents/transparency-blend-modes.pdf", "Transparency"),
        new("documents/page-rotation.pdf", "Rotated pages"),
        new("documents/linearized.pdf", "Linearized (range requests)", Remote: true),
    ];

    /// <summary>
    /// Standard 14 fonts are bundled, so documents that do not embed Helvetica, Times or Courier render like a desktop viewer. Tiles are
    /// drawn by <see cref="DemoTileBackend"/>: the software rasterizer, or WebGL when the View panel selects it.
    /// </summary>
    public static readonly ViewerOptions Options = new ViewerOptions()
        .UseEngine(new PdfEngine(new PdfOptions().UseStandard14Fonts().UseBrowserImageDecoder()))
        .UseBackend(static () => new DemoTileBackend());

    /// <summary>
    /// The options for one document: <see cref="Options"/>, plus the render worker's renderer when the worker runs (#357), so content
    /// tiles are drawn off the page's thread (the session disposes the renderer).
    /// </summary>
    private static ViewerOptions OptionsFor(Func<IRemoteTileRenderer?> worker) =>
        Renderers.UseWebGL || worker() is not { } renderer
            ? Options
            : new ViewerOptions().UseEngine(Options.Engine!).UseBackend(Options.BackendFactory!).WithIntent(Options.Intent).UseRemoteRenderer(renderer);

    /// <summary>
    /// Applies the renderer the View panel selected: the software rasterizer draws content tiles in the render worker, WebGL draws them
    /// on the page's thread (the GPU is the page's), so a switch reopens the document where it is (with any edits) or just redraws it.
    /// </summary>
    public async Task ApplyRendererAsync()
    {
        if (Session is not { } session)
        {
            return;
        }

        bool worker = !Renderers.UseWebGL && RenderWorker.IsRunning;
        if ((session.RemoteRenderer is not null) == worker)
        {
            Viewer?.Redraw();
            return;
        }

        int page = CurrentPage;
        bool edited = IsEdited;
        if (RemoteAddress is { } remote && !edited)
        {
            await OpenUrlAsync(remote.AbsoluteUri).ConfigureAwait(true);
            if (page > 0)
            {
                await GoToPageLaterAsync(page).ConfigureAwait(true);
            }

            return;
        }

        try
        {
            using var stream = new MemoryStream();
            await session.Document.SaveAsync(stream, new PdfSaveOptions(), WorkToken).ConfigureAwait(true);
            await OpenBytesAsync(Name, stream.ToArray(), Address, page).ConfigureAwait(true);
            IsEdited = edited;
        }
        catch (PdfRangeNotLoadedException)
        {
            Viewer?.Redraw(); // a remote file not loaded whole keeps its renderer
        }
    }

    /// <summary>Gets the demo of the running app (for the browser benchmarks' exports).</summary>
    public static Demo? Current { get; set; }

    private CancellationTokenSource _work = new();

    /// <summary>Raised after any change the shell shows.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the open document's session, or null.</summary>
    public DocumentSession? Session { get; private set; }

    /// <summary>Gets the open document, or null.</summary>
    public PdfDocument? Document => Session?.Document;

    /// <summary>Gets the open document's file name.</summary>
    public string Name { get; private set; } = "";

    /// <summary>Gets the address a remote document is read from through range requests, or null when the whole file is in memory.</summary>
    public Uri? RemoteAddress { get; private set; }

    /// <summary>Gets the address the open document came from (gallery file or URL), or null for a local file or a created one.</summary>
    public string? Address { get; private set; }

    /// <summary>Gets whether the open document was changed since it was opened.</summary>
    public bool IsEdited { get; private set; }

    /// <summary>Gets a number that changes whenever another document opens or the document is edited (panels reload on it).</summary>
    public int Version { get; private set; }

    /// <summary>Gets whether a document is being opened.</summary>
    public bool IsOpening { get; private set; }

    /// <summary>Gets the last message for the user (an error or a link the document asked to open).</summary>
    public string? Message { get; private set; }

    /// <summary>Gets a link the document asked to open, which the user may follow.</summary>
    public string? Link { get; private set; }

    /// <summary>Gets or sets the viewer (set by the shell once rendered).</summary>
    public PdfViewer? Viewer { get; set; }

    /// <summary>Gets or sets the viewer's last state.</summary>
    public PdfViewerState State { get; set; }

    /// <summary>Gets a token cancelled when another document opens (background work of the panels).</summary>
    public CancellationToken WorkToken => _work.Token;

    /// <summary>Gets the page thumbnails rendered so far (PNG data URLs by page index), cleared when the document changes.</summary>
    public Dictionary<int, string> Thumbnails { get; } = [];

    /// <summary>Gets the page at the centre of the viewer.</summary>
    public int CurrentPage => Math.Clamp(State.PageIndex, 0, Math.Max(0, (Session?.PageCount ?? 1) - 1));

    /// <summary>Opens a gallery document: downloaded whole (small files), or through range requests for the remote entry.</summary>
    public async Task OpenGalleryAsync(GalleryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await (entry.Remote ? OpenUrlAsync(entry.File) : RunOpenAsync(entry.File, async token =>
        {
            byte[] bytes;
            try
            {
                bytes = await http.GetByteArrayAsync(Resolve(entry.File), token).ConfigureAwait(true);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound && entry.Mirror is not null)
            {
                bytes = await http.GetByteArrayAsync(new Uri(entry.Mirror), token).ConfigureAwait(true);
            }

            return (DocumentSession.Open(bytes, OptionsFor(() => RenderWorker.Open(bytes, Options.Intent))), FileName(entry.File), null);
        })).ConfigureAwait(true);

        if (Address == entry.File && Message is null)
        {
            SuggestedPanel = entry.Panel;
            if (entry.Note is not null)
            {
                Report(entry.Note);
            }
            else
            {
                Refresh();
            }
        }
    }

    /// <summary>Gets or sets the panel the shell should open for the document just opened (taken and cleared by the shell).</summary>
    public string? SuggestedPanel { get; set; }

    /// <summary>Opens a document from an address through HTTP range requests: page 1 of a linearized file shows before the rest arrives.</summary>
    public Task OpenUrlAsync(string address) => RunOpenAsync(address, async token =>
    {
        Uri uri = Resolve(address);
        DocumentSession session = await DocumentSession.OpenAsync(http, uri, OptionsFor(() => RenderWorker.Open(uri, Options.Intent)), token).ConfigureAwait(true);
        return (session, FileName(uri.AbsolutePath), uri);
    });

    /// <summary>Opens a document from bytes (a local file, a created document, or a remote one downloaded whole).</summary>
    public async Task OpenBytesAsync(string name, byte[] bytes, string? address = null, int page = 0)
    {
        await RunOpenAsync(address, _ => Task.FromResult<(DocumentSession, string, Uri?)>((DocumentSession.Open(bytes, OptionsFor(() => RenderWorker.Open(bytes, Options.Intent))), name, null)))
            .ConfigureAwait(true);
        if (page > 0)
        {
            await GoToPageLaterAsync(page).ConfigureAwait(true);
        }
    }

    /// <summary>Downloads the whole remote document and reopens it from memory, so every panel can read all of it.</summary>
    public async Task LoadWholeAsync()
    {
        if (RemoteAddress is not { } address)
        {
            return;
        }

        int page = CurrentPage;
        byte[] bytes;
        try
        {
            bytes = await http.GetByteArrayAsync(address, WorkToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // shown to the user
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            Report($"The document could not be downloaded: {exception.Message}");
            return;
        }

        await OpenBytesAsync(Name, bytes, Address, page).ConfigureAwait(true);
    }

    /// <summary>Records an edit of the open document.</summary>
    public void MarkEdited()
    {
        IsEdited = true;
        Thumbnails.Clear();
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the open document (with every edit) and hands the file to the browser as a download.</summary>
    public async Task DownloadAsync(PdfSaveOptions? options = null, string? name = null)
    {
        if (Document is not { } document)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream();
            await document.SaveAsync(stream, options ?? new PdfSaveOptions(), WorkToken).ConfigureAwait(true);
            await DownloadAsync(name ?? Name, stream.ToArray()).ConfigureAwait(true);
        }
        catch (PdfRangeNotLoadedException)
        {
            Report("Saving needs the whole file: download it first (Document › Load whole file).");
        }
#pragma warning disable CA1031 // shown to the user
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Report($"The document could not be saved: {exception.Message}");
        }
    }

    /// <summary>Hands bytes to the browser as a file download.</summary>
    public ValueTask DownloadAsync(string name, byte[] bytes) => js.InvokeVoidAsync("broadsideDemo.download", name, bytes);

    /// <summary>Shows a message (or clears it with null).</summary>
    public void Report(string? message, string? link = null)
    {
        Message = message;
        Link = link;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="Changed"/> (the viewer's state moved).</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _work.CancelAsync().ConfigureAwait(true);
        _work.Dispose();
        if (Session is not null)
        {
            await Session.DisposeAsync().ConfigureAwait(true);
        }
    }

    private async Task RunOpenAsync(string? address, Func<CancellationToken, Task<(DocumentSession Session, string Name, Uri? Remote)>> open)
    {
        await _work.CancelAsync().ConfigureAwait(true);
        _work.Dispose();
        _work = new CancellationTokenSource();
        CancellationToken token = _work.Token;
        IsOpening = true;
        RenderStats.MarkOpen();
        Message = null;
        Link = null;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            (DocumentSession session, string name, Uri? remote) = await open(token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                await session.DisposeAsync().ConfigureAwait(true);
                return;
            }

            DocumentSession? previous = Session;
            Session = session;
            Name = name;
            Address = address;
            RemoteAddress = remote;
            IsEdited = false;
            Thumbnails.Clear();
            State = default;
            Version++;
            Changed?.Invoke(this, EventArgs.Empty);

            // The viewer moves to the new session when the shell re-renders; the old one closes after that.
            if (previous is not null)
            {
                await Task.Yield();
                await previous.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // shown to the user
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Message = $"The document could not be opened: {exception.Message}";
        }
        finally
        {
            IsOpening = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task GoToPageLaterAsync(int page)
    {
        // The viewer opens the new session in its next render; wait until it reports the page count.
        for (int i = 0; i < 100 && State.PageCount == 0; i++)
        {
            await Task.Delay(20).ConfigureAwait(true);
        }

        Viewer?.GoToPage(page);
    }

    private Uri Resolve(string address) => new(new Uri(navigation.BaseUri), address);

    private static string FileName(string path)
    {
        string name = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        return name.Length == 0 ? "document.pdf" : name;
    }
}
