using System.Globalization;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// The models of a viewer's side panels, bound to one <see cref="IDocumentViewer"/>: page thumbnails and labels, the outline, search
/// results, attachments, layers and annotations of the open document, with the calls that navigate the viewer from them. Every shell's
/// panels (<c>PdfSidebar</c>, <c>PdfThumbnailsPanel</c>, …) lay this model out with their own controls, so the same logic serves every
/// shell and a headless test.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.3 (outline), §12.4.2 (page labels), §7.11.4 and §12.5.6.15 (attachments), §8.11.4 (layers) and §12.5.6.2
/// (comments). The models are views over the live document (ADR 0004): <see cref="Outline"/>, <see cref="SearchResults"/>,
/// <see cref="Attachments"/>, <see cref="Layers"/> and <see cref="Annotations"/>. Each is read again when the viewer opens another
/// document; a model that cannot be read stays <see langword="null"/> and <see cref="GetError"/> says why (the sidebar never throws).
/// </para>
/// <para>
/// Use it on the UI thread: it is created there, captures that thread's synchronization context and raises <see cref="Changed"/> and
/// <see cref="AttachmentOpening"/> there, including for the search's and the thumbnails' progress, which arrive on workers.
/// </para>
/// </remarks>
public sealed class ViewerSidebar : IDisposable
{
    private readonly SynchronizationContext? _context;
    private readonly Lock _gate = new();
    private readonly ThumbnailOptions _thumbnailOptions;
    private readonly HashSet<OutlineEntry> _open = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SidebarPanels, string> _errors = [];
    private DocumentSession? _session;
    private SidebarThumbnail?[] _thumbnails = [];
    private CancellationTokenSource? _work;
    private PageLabelTable? _labels;
    private int _generation;
    private int _page = -1;
    private bool _searchPosted;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ViewerSidebar"/> class over a viewer.</summary>
    /// <param name="viewer">The viewer (a shell's <c>PdfView</c>).</param>
    /// <param name="panels">The panels whose models to read; others stay <see langword="null"/>.</param>
    /// <param name="thumbnailOptions">The thumbnails' size; 160 pixels on the long side by default.</param>
    public ViewerSidebar(IDocumentViewer viewer, SidebarPanels panels = SidebarPanels.All, ThumbnailOptions? thumbnailOptions = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        Viewer = viewer;
        Panels = panels;
        _thumbnailOptions = thumbnailOptions ?? new ThumbnailOptions().WithSize(160);
        _context = SynchronizationContext.Current;
        viewer.ViewChanged += OnViewChanged;
        Attach();
    }

    /// <summary>Raised on the UI thread when a model, the current page or a thumbnail changed.</summary>
    public event EventHandler<SidebarChangedEventArgs>? Changed;

    /// <summary>
    /// Raised on the UI thread with an attachment's bytes when the user asks to open or save it (<see cref="OpenAttachmentAsync"/>).
    /// The viewer never writes or launches a file: the application decides.
    /// </summary>
    public event EventHandler<AttachmentOpeningEventArgs>? AttachmentOpening;

    /// <summary>Gets the viewer.</summary>
    public IDocumentViewer Viewer { get; }

    /// <summary>Gets the panels whose models the sidebar reads.</summary>
    public SidebarPanels Panels { get; }

    /// <summary>Gets the session the models were read from, or <see langword="null"/> while nothing is open.</summary>
    public DocumentSession? Session => _session;

    /// <summary>Gets the page at the viewer's center (zero-based).</summary>
    public int PageIndex => Viewer.PageIndex;

    /// <summary>Gets the document's page count (0 while nothing is open).</summary>
    public int PageCount => _session?.PageCount ?? 0;

    /// <summary>Gets the outline, or <see langword="null"/> before it is read or without <see cref="SidebarPanels.Outline"/>.</summary>
    public DocumentOutline? Outline { get; private set; }

    /// <summary>Gets the search results of the viewer's search, or <see langword="null"/>.</summary>
    public SearchResults? SearchResults { get; private set; }

    /// <summary>Gets the attachments, or <see langword="null"/> before they are read.</summary>
    public DocumentAttachments? Attachments { get; private set; }

    /// <summary>Gets the layers, or <see langword="null"/> before they are read.</summary>
    public DocumentLayers? Layers { get; private set; }

    /// <summary>Gets the annotation list of the viewer's annotation layer, or <see langword="null"/>.</summary>
    public AnnotationList? Annotations { get; private set; }

    /// <summary>Gets a task that completes when the open document's models have been read (or failed to be).</summary>
    public Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Gets why a panel's model could not be read, or <see langword="null"/>.</summary>
    /// <param name="panel">One panel.</param>
    /// <returns>The message.</returns>
    public string? GetError(SidebarPanels panel) => _errors.GetValueOrDefault(panel);

    /// <summary>Gets a page's label, or its one-based number when the document has none.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The label.</returns>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    public string GetPageLabel(int pageIndex) =>
        _labels is { HasLabels: true } labels && pageIndex < labels.Count ? labels.GetLabel(pageIndex) : (pageIndex + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Gets a page's thumbnail once <see cref="RenderThumbnailsAsync"/> drew it, or <see langword="null"/>.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <returns>The thumbnail.</returns>
    public SidebarThumbnail? GetThumbnail(int pageIndex) => (uint)pageIndex < (uint)_thumbnails.Length ? _thumbnails[pageIndex] : null;

    /// <summary>
    /// Draws the missing thumbnails of a range of pages, the page in view first, each raising <see cref="Changed"/>
    /// (<see cref="SidebarChange.Thumbnail"/>) as it arrives. Thumbnails are drawn again after a change of layers.
    /// </summary>
    /// <param name="firstPage">The first page of the range.</param>
    /// <param name="count">How many pages.</param>
    /// <param name="cancellationToken">Stops drawing; what was drawn is kept.</param>
    /// <returns>A task that completes when the range is drawn.</returns>
    /// <remarks>Through <see cref="PageThumbnails"/>: each page replays its cached display list.</remarks>
    public async Task RenderThumbnailsAsync(int firstPage, int count, CancellationToken cancellationToken = default)
    {
        if (_session is not { } session || _thumbnails.Length == 0)
        {
            return;
        }

        int first = Math.Clamp(firstPage, 0, _thumbnails.Length - 1);
        int end = Math.Min(_thumbnails.Length, first + Math.Max(0, count));
        int current = Math.Clamp(PageIndex, first, Math.Max(first, end - 1));
        SidebarThumbnail?[] thumbnails = _thumbnails;
        List<int> pages = [.. Enumerable.Range(first, end - first).OrderBy(p => Math.Abs(p - current)).Where(p => thumbnails[p] is null)];
        if (pages.Count == 0)
        {
            return;
        }

        int generation = _generation;
        var surface = new ThumbnailSurface(this, generation);
        var renderer = new PageThumbnails(session, surface, _thumbnailOptions);
        try
        {
            await renderer.RenderAsync(pages, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
#pragma warning disable CA1031 // a page that cannot be drawn keeps its placeholder
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Shows a page.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    public void GoToPage(int pageIndex)
    {
        if ((uint)pageIndex < (uint)PageCount)
        {
            Viewer.GoToPage(pageIndex);
        }
    }

    /// <summary>Shows an outline entry's destination.</summary>
    /// <param name="entry">An entry of <see cref="Outline"/>.</param>
    /// <returns>Whether the entry has a destination in this document (a URI entry has none; the shell may offer it to the user).</returns>
    /// <remarks>ISO 32000-2 §12.3.3 and §12.3.2.</remarks>
    public bool GoTo(OutlineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Target is not { } target)
        {
            return false;
        }

        Viewer.GoTo(target);
        return true;
    }

    /// <summary>Gets whether an outline entry shows its children: as the user toggled it, else as the file says (<c>Count</c> &gt; 0).</summary>
    /// <param name="entry">An entry of <see cref="Outline"/>.</param>
    /// <returns>Whether it is open.</returns>
    public bool IsOpen(OutlineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _open.Contains(entry) != entry.IsOpen;
    }

    /// <summary>Opens a closed outline entry or closes an open one.</summary>
    /// <param name="entry">An entry of <see cref="Outline"/>.</param>
    public void Toggle(OutlineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_open.Remove(entry))
        {
            _open.Add(entry);
        }

        Raise(SidebarPanels.Outline, SidebarChange.Content);
    }

    /// <summary>Gets the outline entries a tree shows: the roots and the children of every open entry, depth first.</summary>
    /// <returns>The rows; empty without an outline.</returns>
    public IReadOnlyList<OutlineEntry> GetVisibleOutline() => Outline?.GetVisible(IsOpen) ?? [];

    /// <summary>Gets the outline entry of the page in view, or <see langword="null"/>.</summary>
    public OutlineEntry? CurrentOutlineEntry => Outline?.FindEntryFor(PageIndex);

    /// <summary>Searches the document from the page in view; results arrive in <see cref="SearchResults"/>.</summary>
    /// <param name="query">The text; empty clears the search.</param>
    /// <param name="options">Case, whole words and diacritics, or <see langword="null"/> for the defaults.</param>
    public void Search(string query, TextSearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Viewer.Search is not { } search)
        {
            return;
        }

        if (query.Length == 0)
        {
            search.Clear();
        }
        else
        {
            search.Search(query, options, Math.Max(0, PageIndex));
        }
    }

    /// <summary>Makes a result the current match and shows it.</summary>
    /// <param name="item">A result of <see cref="SearchResults"/>.</param>
    public void Show(SearchResultItem item) => SearchResults?.Show(item, Viewer);

    /// <summary>Selects an annotation and shows it.</summary>
    /// <param name="item">An item of <see cref="Annotations"/>.</param>
    public void Reveal(AnnotationListItem item) => Annotations?.Reveal(item, Viewer);

    /// <summary>Shows a file attachment annotation's page and rectangle; nothing for an entry of <c>EmbeddedFiles</c>.</summary>
    /// <param name="item">An item of <see cref="Attachments"/>.</param>
    /// <returns>Whether the viewer moved.</returns>
    public bool Reveal(AttachmentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Annotation is { } target && Viewer.Annotations is { } layer)
        {
            layer.Select(target);
            Viewer.Reveal(target.PageIndex, layer.GetRectangle(target));
            return true;
        }

        if (item.PageIndex is int page)
        {
            GoToPage(page);
            return true;
        }

        return false;
    }

    /// <summary>Decodes an attachment and raises <see cref="AttachmentOpening"/> with its bytes.</summary>
    /// <param name="item">An item of <see cref="Attachments"/>.</param>
    /// <param name="action">Whether the user asked to open or to save it.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>Whether the event was raised: <see langword="false"/> when the item has no data or it could not be decoded.</returns>
    public async Task<bool> OpenAttachmentAsync(AttachmentItem item, AttachmentAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Attachments is not { } attachments || !item.IsEmbedded)
        {
            return false;
        }

        ReadOnlyMemory<byte> data;
        try
        {
            data = await attachments.ExtractAsync(item, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _errors[SidebarPanels.Attachments] = exception.Message;
            Raise(SidebarPanels.Attachments, SidebarChange.Content);
            return false;
        }

        AttachmentOpening?.Invoke(this, new AttachmentOpeningEventArgs(item, data, action));
        return true;
    }

    /// <summary>Shows or hides a layer (see <see cref="DocumentLayers.SetVisible"/>).</summary>
    /// <param name="item">A layer of <see cref="Layers"/>.</param>
    /// <param name="visible">Whether it shows.</param>
    /// <returns>Whether the state changed.</returns>
    public bool SetLayerVisible(LayerItem item, bool visible) => Layers?.SetVisible(item, visible) ?? false;

    /// <summary>Returns the layers to the default configuration.</summary>
    public void ResetLayers() => Layers?.Reset();

    /// <summary>Stops following the viewer.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Viewer.ViewChanged -= OnViewChanged;
        Detach();
        _work?.Dispose(); // Detach disposed it already
    }

    private void OnViewChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (!ReferenceEquals(Viewer.Session, _session))
        {
            Attach();
        }
        else if (Viewer.PageIndex != _page)
        {
            _page = Viewer.PageIndex;
            Raise(SidebarPanels.None, SidebarChange.CurrentPage);
        }
    }

    private void Attach()
    {
        Detach();
        _session = Viewer.Session;
        _page = Viewer.PageIndex;
        int generation = ++_generation;
        if (_session is not { } session)
        {
            Raise(SidebarPanels.All, SidebarChange.Document);
            return;
        }

        _thumbnails = new SidebarThumbnail?[session.PageCount];
        _labels = Viewer.PageLabels;
        if (Has(SidebarPanels.Search) && Viewer.Search is { } search)
        {
            SearchResults = new SearchResults(search);
            SearchResults.Changed += OnSearchChanged;
        }

        if (Has(SidebarPanels.Annotations) && Viewer.Annotations is { } layer)
        {
            Annotations = new AnnotationList(layer);
            Annotations.Changed += OnAnnotationsChanged;
        }

        var work = new CancellationTokenSource();
        _work = work;
        Raise(SidebarPanels.All, SidebarChange.Document);
        Loading = LoadAsync(session, generation, work.Token);
    }

    private void Detach()
    {
        if (_work is { } work)
        {
            _work = null;
            work.Cancel();
            work.Dispose();
        }

        SearchResults?.Changed -= OnSearchChanged;
        SearchResults?.Dispose();
        Annotations?.Changed -= OnAnnotationsChanged;
        Layers?.Changed -= OnLayersChanged;
        (SearchResults, Annotations, Layers, Outline, Attachments, _labels) = (null, null, null, null, null, null);
        _thumbnails = [];
        _open.Clear();
        _errors.Clear();
        _session = null;
    }

    private async Task LoadAsync(DocumentSession session, int generation, CancellationToken token)
    {
        if (_labels is null && Has(SidebarPanels.Thumbnails | SidebarPanels.Outline))
        {
            PageLabelTable? labels = await ReadAsync(SidebarPanels.Thumbnails, () => PageLabelTable.ReadAsync(session, token)).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            _labels = labels;
            Raise(SidebarPanels.Thumbnails, SidebarChange.Content);
        }

        if (Has(SidebarPanels.Outline))
        {
            DocumentOutline? outline = await ReadAsync(SidebarPanels.Outline, () => DocumentOutline.ReadAsync(session, token)).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Outline = outline;
            Raise(SidebarPanels.Outline, SidebarChange.Content);
        }

        if (Has(SidebarPanels.Layers))
        {
            DocumentLayers? layers = await ReadAsync(SidebarPanels.Layers, () => DocumentLayers.ReadAsync(session, token)).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Layers = layers;
            Layers?.Changed += OnLayersChanged;
            Raise(SidebarPanels.Layers, SidebarChange.Content);
        }

        if (Has(SidebarPanels.Attachments))
        {
            AnnotationLayer? layer = Viewer.Annotations;
            DocumentAttachments? attachments = await ReadAsync(
                SidebarPanels.Attachments,
                () => layer is not null && ReferenceEquals(layer.Session, session) ? DocumentAttachments.ReadAsync(layer, token) : DocumentAttachments.ReadAsync(session, token)).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Attachments = attachments;
            Raise(SidebarPanels.Attachments, SidebarChange.Content);
        }

        if (Annotations is { } list)
        {
            await RunAsync(SidebarPanels.Annotations, async () =>
            {
                await list.RefreshAsync(token).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
    }

    // Reads one model; a failure is kept for GetError and the model stays null.
    private async Task<T?> ReadAsync<T>(SidebarPanels panel, Func<ValueTask<T>> read)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
#pragma warning disable CA1031 // shown in the panel: the sidebar never throws
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _errors[panel] = exception.Message;
            return null;
        }
    }

    private async Task RunAsync(SidebarPanels panel, Func<Task> read)
    {
        try
        {
            await read().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
#pragma warning disable CA1031 // shown in the panel: the sidebar never throws
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _errors[panel] = exception.Message;
            Raise(panel, SidebarChange.Content);
        }
    }

    private bool Has(SidebarPanels panels) => (Panels & panels) != 0;

    private void OnAnnotationsChanged(object? sender, EventArgs e) => Raise(SidebarPanels.Annotations, SidebarChange.Content);

    private void OnLayersChanged(object? sender, EventArgs e)
    {
        // The pages look different under the new layers: their thumbnails are drawn again when asked.
        Array.Clear(_thumbnails);
        Raise(SidebarPanels.Layers | SidebarPanels.Thumbnails, SidebarChange.Content);
    }

    // The search raises its event on the worker that indexed a page: one update per burst is posted to the UI thread.
    private void OnSearchChanged(object? sender, EventArgs e)
    {
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
        {
            Raise(SidebarPanels.Search, SidebarChange.Content);
            return;
        }

        lock (_gate)
        {
            if (_searchPosted)
            {
                return;
            }

            _searchPosted = true;
        }

        _context.Post(
            _ =>
            {
                lock (_gate)
                {
                    _searchPosted = false;
                }

                if (!_disposed && ReferenceEquals(sender, SearchResults))
                {
                    Raise(SidebarPanels.Search, SidebarChange.Content);
                }
            },
            null);
    }

    private void Raise(SidebarPanels panels, SidebarChange change, int pageIndex = -1) =>
        Changed?.Invoke(this, new SidebarChangedEventArgs(panels, change, pageIndex));

    private void OnUiThread(Action action)
    {
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
        {
            action();
        }
        else
        {
            _context.Post(static state => ((Action)state!)(), action);
        }
    }

    /// <summary>Receives finished thumbnails (on a worker in a background session) and keeps a copy of their pixels on the UI thread.</summary>
    private sealed class ThumbnailSurface(ViewerSidebar sidebar, int generation) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels)
        {
            int rowBytes = pixels.Width * 4;
            byte[] copy = new byte[rowBytes * pixels.Height];
            for (int y = 0; y < pixels.Height; y++)
            {
                pixels.GetRow(y)[..rowBytes].CopyTo(copy.AsSpan(y * rowBytes));
            }

            var thumbnail = new SidebarThumbnail(tile.PageIndex, pixels.Width, pixels.Height, copy);
            sidebar.OnUiThread(() =>
            {
                if (generation == sidebar._generation && (uint)tile.PageIndex < (uint)sidebar._thumbnails.Length)
                {
                    sidebar._thumbnails[tile.PageIndex] = thumbnail;
                    sidebar.Raise(SidebarPanels.Thumbnails, SidebarChange.Thumbnail, tile.PageIndex);
                }
            });
        }

        public void Evict(Tile tile)
        {
        }
    }
}

/// <summary>The side panels a <see cref="ViewerSidebar"/> reads models for, and a shell's <c>PdfSidebar</c> shows.</summary>
[Flags]
public enum SidebarPanels
{
    /// <summary>No panel.</summary>
    None = 0,

    /// <summary>Page thumbnails and labels.</summary>
    Thumbnails = 1,

    /// <summary>The outline (bookmarks).</summary>
    Outline = 2,

    /// <summary>Search results.</summary>
    Search = 4,

    /// <summary>Embedded files and file attachment annotations.</summary>
    Attachments = 8,

    /// <summary>Optional content groups.</summary>
    Layers = 16,

    /// <summary>Annotations and comments.</summary>
    Annotations = 32,

    /// <summary>Every panel.</summary>
    All = Thumbnails | Outline | Search | Attachments | Layers | Annotations,
}

/// <summary>What changed in a <see cref="ViewerSidebar"/>.</summary>
public enum SidebarChange
{
    /// <summary>The viewer opened another document (or closed it): every model is read again.</summary>
    Document,

    /// <summary>A model arrived or changed (the panels of <see cref="SidebarChangedEventArgs.Panels"/>).</summary>
    Content,

    /// <summary>The page in view changed (the current thumbnail and outline entry).</summary>
    CurrentPage,

    /// <summary>A page's thumbnail was drawn (<see cref="SidebarChangedEventArgs.PageIndex"/>).</summary>
    Thumbnail,
}

/// <summary>Data of <see cref="ViewerSidebar.Changed"/>.</summary>
public sealed class SidebarChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="SidebarChangedEventArgs"/> class.</summary>
    /// <param name="panels">The panels whose content changed.</param>
    /// <param name="change">What changed.</param>
    /// <param name="pageIndex">The page of a <see cref="SidebarChange.Thumbnail"/>, else -1.</param>
    public SidebarChangedEventArgs(SidebarPanels panels, SidebarChange change, int pageIndex)
    {
        Panels = panels;
        Change = change;
        PageIndex = pageIndex;
    }

    /// <summary>Gets the panels whose content changed.</summary>
    public SidebarPanels Panels { get; }

    /// <summary>Gets what changed.</summary>
    public SidebarChange Change { get; }

    /// <summary>Gets the page of a <see cref="SidebarChange.Thumbnail"/>, else -1.</summary>
    public int PageIndex { get; }
}

/// <summary>A page thumbnail: premultiplied BGRA pixels, rows packed (stride = 4 × <see cref="Width"/>), as the rasterizer draws them.</summary>
public sealed class SidebarThumbnail
{
    private readonly byte[] _pixels;

    internal SidebarThumbnail(int pageIndex, int width, int height, byte[] pixels)
    {
        PageIndex = pageIndex;
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    /// <summary>Gets the zero-based page.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the pixels: premultiplied BGRA, row after row.</summary>
    public ReadOnlyMemory<byte> Pixels => _pixels;

    /// <summary>Encodes the thumbnail as a PNG file (for an HTML image, for example).</summary>
    /// <returns>The PNG bytes.</returns>
    public byte[] EncodePng()
    {
        using var bitmap = new Bitmap(_pixels.AsMemory(), Width, Height, Width * 4);
        using var stream = new MemoryStream();
        bitmap.SavePng(stream);
        return stream.ToArray();
    }
}
