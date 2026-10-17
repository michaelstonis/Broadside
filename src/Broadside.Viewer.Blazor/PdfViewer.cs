using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// A PDF viewer for Blazor WebAssembly: pages are rasterized in tiles by the managed software rasterizer on the browser's thread, in
/// slices that leave every animation frame to the browser, and drawn into a canvas; a remote <see cref="Source"/> is read through
/// HTTP range requests, so page 1 of a linearized file shows before the rest downloads. Pointer, wheel and keyboard input drive the
/// viewer core's <see cref="InputModel"/>; mouse drags over text select it (copy with the browser's copy command); links and form
/// fields work through the <see cref="AnnotationLayer"/>; and each loaded page's logical structure is mirrored into an invisible DOM
/// tree in reading order, so screen readers and the keyboard reach page text.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33 track 6 shell (issue #221), over the viewer core of issues #209 to #220. Accessibility: ISO 32000-2 §14.8 (tagged PDF)
/// and §14.9.3 (alternate descriptions) through <see cref="AccessibilityBridge"/>. Actions: ISO 32000-2 §12.6.4 — destinations inside
/// the document are followed; every action, URIs included, is reported through <see cref="OnAction"/> and never performed.
/// </para>
/// <para>
/// Keys while the viewer has focus: arrows, Page Up/Down, Space, Home/End scroll and turn pages; + and - zoom, 0 fits the width; Tab and
/// Shift+Tab move between links and form fields, Enter activates one; Ctrl/Cmd+A selects all text; Shift+arrows extend a selection;
/// N, H, T, L, G, K, F, D and P move the reading focus to the next element, heading, table, list, figure, link, form field, landmark or
/// page (Shift goes back); Escape clears the selection and the reading focus.
/// </para>
/// <para>Runs in the browser only (it imports a small JavaScript module from <c>_content/Broadside.Viewer.Blazor</c>).</para>
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed class PdfViewer : ComponentBase, IAsyncDisposable
{
    private ViewerController? _controller;
    private HttpClient? _ownedClient;
    private Uri? _openedSource;
    private DocumentSession? _openedSession;
    private bool _attached;
    private string? _error;
    private bool _wasLoading;

    /// <summary>Gets or sets the address of the document to open; a relative address is resolved against the application's base address.</summary>
    [Parameter]
    public Uri? Source { get; set; }

    /// <summary>Gets or sets a document session the host opened itself (for example from a picked file); it wins over <see cref="Source"/> and is not disposed by the viewer.</summary>
    [Parameter]
    public DocumentSession? Session { get; set; }

    /// <summary>Gets or sets the client that fetches <see cref="Source"/>; the viewer creates one when it is <see langword="null"/>.</summary>
    [Parameter]
    public HttpClient? HttpClient { get; set; }

    /// <summary>Gets or sets the viewer options a <see cref="Source"/> opens with (engine, fonts, credentials, slice budget).</summary>
    [Parameter]
    public ViewerOptions? Options { get; set; }

    /// <summary>Gets or sets the page layout (mode, cover, gaps, rotation); a change applies to the open document at once and keeps the current page in view.</summary>
    [Parameter]
    public PageLayoutOptions? LayoutOptions { get; set; }

    /// <summary>Gets or sets a value indicating whether annotations and form fields are drawn.</summary>
    [Parameter]
    public bool ShowAnnotations { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether presses on markup annotations select and move them (off: they pan or select text).</summary>
    [Parameter]
    public bool AllowAnnotationEditing { get; set; }

    /// <summary>
    /// Gets or sets an observer of the tile scheduler's events (tiles started, presented, cancelled, evicted, failed), applied when a
    /// document opens: for instrumentation such as time to the first sharp page (#353's measurements).
    /// </summary>
    [Parameter]
    public Action<TileSchedulerEvent>? TileObserver { get; set; }

    /// <summary>Gets or sets the accessible name of the viewer region.</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Gets or sets extra CSS classes for the viewer's host element (give it a size).</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Gets or sets the callback raised when the current page, the page count, the zoom or the loading state changes.</summary>
    [Parameter]
    public EventCallback<PdfViewerState> OnStateChanged { get; set; }

    /// <summary>Gets or sets the callback raised for every action a link or a widget requests; the viewer never opens URIs itself.</summary>
    [Parameter]
    public EventCallback<ViewerActionEventArgs> OnAction { get; set; }

    /// <summary>Gets or sets the callback raised when the document cannot be opened or a page cannot be loaded.</summary>
    [Parameter]
    public EventCallback<Exception> OnError { get; set; }

    /// <summary>Gets the current state.</summary>
    public PdfViewerState State => _controller?.State ?? default;

    /// <summary>
    /// Gets the text selection of the open document, or null while nothing is open: select a found match with
    /// <see cref="TextSelection.Select"/> (after <see cref="TextSelection.LoadPageAsync"/>) and the viewer draws it.
    /// </summary>
    public TextSelection? Selection => _controller?.Selection;

    /// <summary>
    /// Gets the annotation layer of the open document, or null while nothing is open: annotations added, edited or removed and form
    /// fields filled through it are redrawn at once.
    /// </summary>
    public AnnotationLayer? Annotations => _controller?.Annotations;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Scrolls to a page.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    public void GoToPage(int pageIndex) => _controller?.GoToPage(pageIndex);

    /// <summary>Goes to the next page (or spread).</summary>
    public void NextPage() => _controller?.NextPage();

    /// <summary>Goes to the previous page (or spread).</summary>
    public void PreviousPage() => _controller?.PreviousPage();

    /// <summary>Zooms to actual size, the page width or the whole page.</summary>
    /// <param name="mode">The zoom.</param>
    public void ZoomTo(ZoomMode mode) => _controller?.ZoomTo(mode);

    /// <summary>Draws every tile again, for example after the session's backend changed what it draws with.</summary>
    public void Redraw() => _controller?.Redraw();

    /// <summary>Zooms by a factor about the centre of the viewport.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    public void ZoomBy(double factor) => _controller?.ZoomBy(factor);

    /// <summary>Scrolls the least distance that brings an area of a page into view (a found match, an annotation), or centres it.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="area">The area in the page's default user space.</param>
    public void Reveal(int pageIndex, PdfRectangle area) => _controller?.Reveal(pageIndex, area);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_controller is not null)
        {
            await _controller.DisposeAsync().ConfigureAwait(true);
            _controller = null;
        }

        _ownedClient?.Dispose();
        _ownedClient = null;
    }

    /// <inheritdoc/>
    protected override void OnInitialized() => _controller = new ViewerController(this);

    /// <inheritdoc/>
    protected override async Task OnParametersSetAsync()
    {
        if (_controller is null)
        {
            return;
        }

        _controller.AllowAnnotationEditing = AllowAnnotationEditing;
        _controller.TileObserver = TileObserver;
        _controller.SetShowAnnotations(ShowAnnotations);
        _controller.SetLayoutOptions(LayoutOptions);
        if (_attached)
        {
            await OpenIfChangedAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _controller is not null)
        {
            await _controller.AttachAsync().ConfigureAwait(true);
            _attached = true;
            await OpenIfChangedAsync().ConfigureAwait(true);
        }
    }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (_controller is null)
        {
            return;
        }

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", _controller.ElementId);
        builder.AddAttribute(2, "class", Class is null ? "bs-viewer" : "bs-viewer " + Class);
        builder.AddAttribute(3, "tabindex", "0");
        builder.AddAttribute(4, "role", "region");
        builder.AddAttribute(5, "aria-label", Label ?? "PDF viewer");
        builder.AddAttribute(6, "aria-busy", _controller.State.IsLoading ? "true" : "false");

        builder.OpenElement(7, "canvas");
        builder.AddAttribute(8, "aria-hidden", "true");
        builder.CloseElement();

        string? status = _error ?? (_controller.State.IsLoading ? "Loading…" : null);
        if (status is not null)
        {
            builder.OpenElement(9, "div");
            builder.AddAttribute(10, "class", "bs-status");
            builder.AddAttribute(11, "role", "status");
            builder.AddContent(12, status);
            builder.CloseElement();
        }

        builder.OpenElement(13, "div");
        builder.AddAttribute(14, "class", "bs-a11y");
        builder.AddAttribute(15, "role", "document");
        if (_controller.Bridge is { } bridge)
        {
            builder.AddAttribute(16, "aria-label", bridge.Root.Name ?? Label ?? "Document");
            AccessibleText.Render(builder, _controller, bridge);
        }

        builder.CloseElement();
        builder.CloseElement();
    }

    /// <summary>Re-renders the text layer (pages loaded or changed).</summary>
    internal void RequestRender() => _ = InvokeAsync(StateHasChanged);

    internal void RaiseStateChanged(PdfViewerState state)
    {
        if (state.IsLoading != _wasLoading)
        {
            _wasLoading = state.IsLoading;
            RequestRender();
        }

        if (OnStateChanged.HasDelegate)
        {
            _ = InvokeAsync(() => OnStateChanged.InvokeAsync(state));
        }
    }

    internal void RaiseAction(ViewerActionEventArgs action)
    {
        if (OnAction.HasDelegate)
        {
            _ = InvokeAsync(() => OnAction.InvokeAsync(action));
        }
    }

    internal void ReportError(Exception exception)
    {
        if (OnError.HasDelegate)
        {
            _ = InvokeAsync(() => OnError.InvokeAsync(exception));
        }
    }

    private async Task OpenIfChangedAsync()
    {
        if (ReferenceEquals(Session, _openedSession) && Equals(Source, _openedSource))
        {
            return;
        }

        _openedSession = Session;
        _openedSource = Source;
        _error = null;
        try
        {
            if (Session is { } session)
            {
                await _controller!.OpenAsync(_ => Task.FromResult(session), ownsSession: false, LayoutOptions).ConfigureAwait(true);
            }
            else if (Source is { } source)
            {
                Uri address = source.IsAbsoluteUri ? source : new Uri(new Uri(Navigation.BaseUri), source);
                HttpClient client = HttpClient ?? (_ownedClient ??= new HttpClient());
                ViewerOptions? options = Options;
                await _controller!.OpenAsync(token => DocumentSession.OpenAsync(client, address, options, token), ownsSession: true, LayoutOptions)
                    .ConfigureAwait(true);
            }
        }
#pragma warning disable CA1031 // shown in the viewer and reported to the host
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _error = string.Create(CultureInfo.InvariantCulture, $"The document could not be opened: {exception.Message}");
            ReportError(exception);
        }

        StateHasChanged();
    }
}
