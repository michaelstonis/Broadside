using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// Page thumbnails of the viewer's document with their page labels, as images in buttons; a click shows the page and the page in view
/// is marked (<c>aria-current</c>). The thumbnails replay each page's cached display list (<see cref="ViewerSidebar.RenderThumbnailsAsync"/>).
/// </summary>
/// <remarks>ISO 32000-2 §14.11.2 and §12.4.2. Bind with <see cref="Viewer"/> (a <see cref="PdfViewer"/>), or lay out another control's <see cref="Model"/>.</remarks>
public sealed class PdfThumbnailsPanel : ComponentBase, IDisposable
{
    private readonly PanelBinding _binding;

    /// <summary>Creates the panel.</summary>
    public PdfThumbnailsPanel() => _binding = new PanelBinding(SidebarPanels.Thumbnails, () => InvokeAsync(StateHasChanged));

    /// <summary>Gets or sets the viewer the panel follows with a model of its own.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets a model another control owns (a <see cref="PdfSidebar"/>'s), laid out instead of <see cref="Viewer"/>'s.</summary>
    [Parameter]
    public ViewerSidebar? Model { get; set; }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/>.</summary>
    public ViewerSidebar? Sidebar => _binding.Sidebar;

    /// <summary>Unbinds the panel.</summary>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnParametersSet() => _binding.Set(Viewer, Model);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => SidebarMarkup.Thumbnails(builder, this, _binding);
}

/// <summary>The outline (bookmarks) as a tree that expands and collapses; a click shows the entry's destination.</summary>
/// <remarks>ISO 32000-2 §12.3.3 and §12.3.2. Bind with <see cref="Viewer"/>, or lay out another control's <see cref="Model"/>.</remarks>
public sealed class PdfOutlinePanel : ComponentBase, IDisposable
{
    private readonly PanelBinding _binding;

    /// <summary>Creates the panel.</summary>
    public PdfOutlinePanel() => _binding = new PanelBinding(SidebarPanels.Outline, () => InvokeAsync(StateHasChanged));

    /// <summary>Gets or sets the viewer the panel follows with a model of its own.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets a model another control owns, laid out instead of <see cref="Viewer"/>'s.</summary>
    [Parameter]
    public ViewerSidebar? Model { get; set; }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/>.</summary>
    public ViewerSidebar? Sidebar => _binding.Sidebar;

    /// <summary>Unbinds the panel.</summary>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnParametersSet() => _binding.Set(Viewer, Model);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => SidebarMarkup.Outline(builder, this, _binding);
}

/// <summary>A search form, the search's progress and its matches grouped by page with the text around them; a click shows a match.</summary>
/// <remarks>ISO 32000-2 §9.10. Bind with <see cref="Viewer"/>, or lay out another control's <see cref="Model"/>.</remarks>
public sealed class PdfSearchPanel : ComponentBase, IDisposable
{
    private readonly PanelBinding _binding;

    /// <summary>Creates the panel.</summary>
    public PdfSearchPanel() => _binding = new PanelBinding(SidebarPanels.Search, () => InvokeAsync(StateHasChanged));

    /// <summary>Gets or sets the viewer the panel follows with a model of its own.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets a model another control owns, laid out instead of <see cref="Viewer"/>'s.</summary>
    [Parameter]
    public ViewerSidebar? Model { get; set; }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/>.</summary>
    public ViewerSidebar? Sidebar => _binding.Sidebar;

    /// <summary>Unbinds the panel.</summary>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnParametersSet() => _binding.Set(Viewer, Model);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => SidebarMarkup.Search(builder, this, _binding);
}

/// <summary>
/// The files the document carries with their size, type and checksum status. Open and Save raise <see cref="Opening"/> with the decoded
/// bytes: the app decides what to do with them (a download, for example); the viewer never launches a file.
/// </summary>
/// <remarks>ISO 32000-2 §7.11.4 and §12.5.6.15. Bind with <see cref="Viewer"/>, or lay out another control's <see cref="Model"/>.</remarks>
public sealed class PdfAttachmentsPanel : ComponentBase, IDisposable
{
    private readonly PanelBinding _binding;

    /// <summary>Creates the panel.</summary>
    public PdfAttachmentsPanel() => _binding = new PanelBinding(SidebarPanels.Attachments, () => InvokeAsync(StateHasChanged), e => Opening.InvokeAsync(e));

    /// <summary>Gets or sets the viewer the panel follows with a model of its own.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets a model another control owns, laid out instead of <see cref="Viewer"/>'s.</summary>
    [Parameter]
    public ViewerSidebar? Model { get; set; }

    /// <summary>Gets or sets what runs with an attachment's decoded bytes when the user opens or saves it.</summary>
    [Parameter]
    public EventCallback<AttachmentOpeningEventArgs> Opening { get; set; }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/>.</summary>
    public ViewerSidebar? Sidebar => _binding.Sidebar;

    /// <summary>Unbinds the panel.</summary>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnParametersSet() => _binding.Set(Viewer, Model);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => SidebarMarkup.Attachments(builder, this, _binding);
}

/// <summary>The layers as the default configuration's tree with a check box each; locked layers cannot be switched. Reset returns to the file's states.</summary>
/// <remarks>ISO 32000-2 §8.11.2 and §8.11.4. Bind with <see cref="Viewer"/>, or lay out another control's <see cref="Model"/>.</remarks>
public sealed class PdfLayersPanel : ComponentBase, IDisposable
{
    private readonly PanelBinding _binding;

    /// <summary>Creates the panel.</summary>
    public PdfLayersPanel() => _binding = new PanelBinding(SidebarPanels.Layers, () => InvokeAsync(StateHasChanged));

    /// <summary>Gets or sets the viewer the panel follows with a model of its own.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets a model another control owns, laid out instead of <see cref="Viewer"/>'s.</summary>
    [Parameter]
    public ViewerSidebar? Model { get; set; }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/>.</summary>
    public ViewerSidebar? Sidebar => _binding.Sidebar;

    /// <summary>Unbinds the panel.</summary>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnParametersSet() => _binding.Set(Viewer, Model);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => SidebarMarkup.Layers(builder, this, _binding);
}

/// <summary>Every annotation and comment, page by page, with author, text and replies; a click selects it and shows it.</summary>
/// <remarks>ISO 32000-2 §12.5 and §12.5.6.2. Bind with <see cref="Viewer"/>, or lay out another control's <see cref="Model"/>.</remarks>
public sealed class PdfAnnotationsPanel : ComponentBase, IDisposable
{
    private readonly PanelBinding _binding;

    /// <summary>Creates the panel.</summary>
    public PdfAnnotationsPanel() => _binding = new PanelBinding(SidebarPanels.Annotations, () => InvokeAsync(StateHasChanged));

    /// <summary>Gets or sets the viewer the panel follows with a model of its own.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets a model another control owns, laid out instead of <see cref="Viewer"/>'s.</summary>
    [Parameter]
    public ViewerSidebar? Model { get; set; }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/>.</summary>
    public ViewerSidebar? Sidebar => _binding.Sidebar;

    /// <summary>Unbinds the panel.</summary>
    public void Dispose() => _binding.Dispose();

    /// <inheritdoc/>
    protected override void OnParametersSet() => _binding.Set(Viewer, Model);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder) => SidebarMarkup.Annotations(builder, this, _binding);
}

/// <summary>
/// The side panels of a viewer behind a <c>role="tablist"</c> row: thumbnails, outline, search, attachments, layers and comments,
/// sharing one <see cref="ViewerSidebar"/>. <see cref="AttachmentOpening"/> hands the app an attachment's bytes.
/// </summary>
/// <remarks>
/// Not a spec feature; each panel cites its clauses. Bind it with <c>&lt;PdfViewer @ref="viewer" /&gt; &lt;PdfSidebar Viewer="viewer" /&gt;</c>.
/// The layout rules are in <c>:where(.bs-sidebar…)</c> styles of no specificity, so <see cref="Class"/> wins.
/// </remarks>
public sealed class PdfSidebar : ComponentBase, IDisposable
{
    private const string Style =
        ":where(.bs-sidebar){display:flex;flex-direction:column;gap:6px;min-height:0}"
        + ":where(.bs-sidebar-tabs){display:flex;flex-wrap:wrap;gap:4px}"
        + ":where(.bs-sidebar-tabs button[aria-selected=true]){font-weight:600}"
        + ":where(.bs-panel){display:flex;flex-direction:column;gap:2px;overflow:auto;min-height:0}"
        + ":where(.bs-panel button){text-align:start}"
        + ":where(.bs-panel-hint){opacity:.7;font-size:.85em}"
        + ":where(.bs-thumbnail img){max-width:100%;height:auto;display:block;margin:auto}";

    private static readonly (SidebarPanels Panel, string Name, string Id)[] Tabs =
    [
        (SidebarPanels.Thumbnails, "Pages", "thumbnails"),
        (SidebarPanels.Outline, "Outline", "outline"),
        (SidebarPanels.Search, "Search", "search"),
        (SidebarPanels.Attachments, "Attachments", "attachments"),
        (SidebarPanels.Layers, "Layers", "layers"),
        (SidebarPanels.Annotations, "Comments", "annotations"),
    ];

    private IDocumentViewer? _bound;
    private ViewerSidebar? _sidebar;

    /// <summary>Gets or sets the viewer the panels follow (a <see cref="PdfViewer"/>).</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets the tab shown (one panel of <see cref="SidebarPanels"/>).</summary>
    [Parameter]
    public SidebarPanels SelectedPanel { get; set; } = SidebarPanels.Thumbnails;

    /// <summary>Gets or sets what runs when the user picks another tab.</summary>
    [Parameter]
    public EventCallback<SidebarPanels> SelectedPanelChanged { get; set; }

    /// <summary>Gets or sets what runs with an attachment's decoded bytes when the user opens or saves it.</summary>
    [Parameter]
    public EventCallback<AttachmentOpeningEventArgs> AttachmentOpening { get; set; }

    /// <summary>Gets or sets CSS classes added to the sidebar element (<c>.bs-sidebar</c>).</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Gets the model the panels lay out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _sidebar;

    /// <summary>Unbinds the sidebar.</summary>
    public void Dispose() => Bind(null);

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Viewer, _bound))
        {
            Bind(Viewer);
        }
    }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "style");
        builder.AddContent(1, Style);
        builder.CloseElement();
        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", Class is null ? "bs-sidebar" : "bs-sidebar " + Class);
        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "class", "bs-sidebar-tabs");
        builder.AddAttribute(6, "role", "tablist");
        foreach ((SidebarPanels panel, string name, string id) in Tabs)
        {
            builder.OpenElement(7, "button");
            builder.SetKey(id);
            builder.AddAttribute(8, "type", "button");
            builder.AddAttribute(9, "role", "tab");
            builder.AddAttribute(10, "aria-selected", panel == SelectedPanel ? "true" : "false");
            builder.AddAttribute(11, "data-tab", id);
            builder.AddAttribute(12, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => SelectAsync(panel)));
            builder.AddContent(13, name);
            builder.CloseElement();
        }

        builder.CloseElement();
        builder.OpenElement(14, "div");
        builder.AddAttribute(15, "role", "tabpanel");
        builder.OpenRegion(16);
        switch (SelectedPanel)
        {
            case SidebarPanels.Outline:
                Child<PdfOutlinePanel>(builder);
                break;
            case SidebarPanels.Search:
                Child<PdfSearchPanel>(builder);
                break;
            case SidebarPanels.Attachments:
                builder.OpenComponent<PdfAttachmentsPanel>(0);
                builder.AddComponentParameter(1, nameof(PdfAttachmentsPanel.Model), _sidebar);
                builder.AddComponentParameter(2, nameof(PdfAttachmentsPanel.Opening), EventCallback.Factory.Create<AttachmentOpeningEventArgs>(this, e => AttachmentOpening.InvokeAsync(e)));
                builder.CloseComponent();
                break;
            case SidebarPanels.Layers:
                Child<PdfLayersPanel>(builder);
                break;
            case SidebarPanels.Annotations:
                Child<PdfAnnotationsPanel>(builder);
                break;
            default:
                Child<PdfThumbnailsPanel>(builder);
                break;
        }

        builder.CloseRegion();
        builder.CloseElement();
        builder.CloseElement();
    }

    private void Child<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(RenderTreeBuilder builder)
        where T : IComponent
    {
        builder.OpenComponent<T>(0);
        builder.AddComponentParameter(1, "Model", _sidebar);
        builder.CloseComponent();
    }

    private async Task SelectAsync(SidebarPanels panel)
    {
        SelectedPanel = panel;
        await SelectedPanelChanged.InvokeAsync(panel).ConfigureAwait(true);
    }

    private void Bind(IDocumentViewer? viewer)
    {
        _bound = viewer;
        ViewerSidebar? old = _sidebar;
        _sidebar = viewer is null ? null : new ViewerSidebar(viewer);
        old?.Dispose();
    }
}

/// <summary>A panel's model: its own <see cref="ViewerSidebar"/> over a viewer, or another control's, and a redraw when it changes.</summary>
internal sealed class PanelBinding(SidebarPanels panel, Func<Task> redraw, Func<AttachmentOpeningEventArgs, Task>? opening = null) : IDisposable
{
    private readonly Dictionary<int, string> _images = [];
    private IDocumentViewer? _viewer;
    private ViewerSidebar? _owned;
    private ViewerSidebar? _shared;
    private CancellationTokenSource? _thumbnails;

    public ViewerSidebar? Sidebar { get; private set; }

    public SidebarPanels Panel => panel;

    public string Query { get; set; } = string.Empty;

    public void Set(IDocumentViewer? viewer, ViewerSidebar? shared)
    {
        if (ReferenceEquals(viewer, _viewer) && ReferenceEquals(shared, _shared))
        {
            return;
        }

        _viewer = viewer;
        _shared = shared;
        ViewerSidebar? old = _owned;
        _owned = shared is null && viewer is not null ? new ViewerSidebar(viewer, panel) : null;
        Use(shared ?? _owned);
        old?.Dispose();
    }

    /// <summary>The thumbnail of a page as a data URL, once drawn; drawing starts the first time a page is asked for.</summary>
    public string? Image(int pageIndex)
    {
        if (Sidebar is not { } sidebar)
        {
            return null;
        }

        if (_thumbnails is null)
        {
            _thumbnails = new CancellationTokenSource();
            _ = sidebar.RenderThumbnailsAsync(0, Math.Min(sidebar.PageCount, SidebarMarkup.MaxRows), _thumbnails.Token);
        }

        if (_images.TryGetValue(pageIndex, out string? url))
        {
            return url;
        }

        if (sidebar.GetThumbnail(pageIndex) is not { } thumbnail)
        {
            return null;
        }

        url = "data:image/png;base64," + Convert.ToBase64String(thumbnail.EncodePng());
        _images[pageIndex] = url;
        return url;
    }

    public void Dispose()
    {
        Set(null, null);
        _thumbnails?.Dispose(); // Set stopped and disposed it already
    }

    private void Use(ViewerSidebar? sidebar)
    {
        if (Sidebar is not null)
        {
            Sidebar.Changed -= OnChanged;
            Sidebar.AttachmentOpening -= OnOpening;
        }

        StopThumbnails();
        _images.Clear();
        Sidebar = sidebar;
        Sidebar?.Changed += OnChanged;
        Sidebar?.AttachmentOpening += OnOpening;
    }

    private void OnOpening(object? sender, AttachmentOpeningEventArgs e) => _ = opening?.Invoke(e);

    private void OnChanged(object? sender, SidebarChangedEventArgs e)
    {
        if (e.Change == SidebarChange.Document)
        {
            StopThumbnails();
            _images.Clear();
        }
        else if (e.Change == SidebarChange.Content && (e.Panels & SidebarPanels.Thumbnails) != 0 && (e.Panels & SidebarPanels.Layers) != 0)
        {
            StopThumbnails(); // drawn again under the new layers
            _images.Clear();
        }

        if (e.Change is SidebarChange.Document or SidebarChange.CurrentPage || (e.Panels & panel) != 0)
        {
            _ = redraw();
        }
    }

    private void StopThumbnails()
    {
        if (_thumbnails is { } work)
        {
            _thumbnails = null;
            work.Cancel();
            work.Dispose();
        }
    }
}

/// <summary>The panels' markup: plain HTML with ARIA roles and <c>data-*</c> hooks (the browser tests find elements by those).</summary>
internal static class SidebarMarkup
{
    public const int MaxRows = 500;

    public static void Thumbnails(RenderTreeBuilder builder, object receiver, PanelBinding binding)
    {
        Open(builder, "thumbnails");
        if (Status(builder, binding, binding.Sidebar?.Session, "pages") is { } sidebar)
        {
            int current = sidebar.PageIndex;
            for (int page = 0; page < Math.Min(sidebar.PageCount, MaxRows); page++)
            {
                int index = page;
                string label = sidebar.GetPageLabel(page);
                builder.OpenElement(10, "button");
                builder.SetKey(page);
                builder.AddAttribute(11, "type", "button");
                builder.AddAttribute(12, "class", "bs-thumbnail");
                builder.AddAttribute(13, "data-thumbnail", index);
                builder.AddAttribute(14, "aria-label", "Page " + label);
                builder.AddAttribute(15, "aria-current", page == current ? "page" : null);
                builder.AddAttribute(16, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, () => sidebar.GoToPage(index)));
                if (binding.Image(page) is { } url)
                {
                    builder.OpenElement(17, "img");
                    builder.AddAttribute(18, "src", url);
                    builder.AddAttribute(19, "alt", string.Empty);
                    builder.CloseElement();
                }

                builder.OpenElement(20, "span");
                builder.AddContent(21, label);
                builder.CloseElement();
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    public static void Outline(RenderTreeBuilder builder, object receiver, PanelBinding binding)
    {
        Open(builder, "outline");
        if (Status(builder, binding, binding.Sidebar?.Outline, "outline") is { Outline: { } outline } sidebar)
        {
            if (outline.IsEmpty)
            {
                Hint(builder, "No outline.");
            }

            OutlineEntry? current = sidebar.CurrentOutlineEntry;
            foreach (OutlineEntry entry in sidebar.GetVisibleOutline())
            {
                builder.OpenElement(10, "div");
                builder.SetKey(entry.Id);
                builder.AddAttribute(11, "role", "treeitem");
                builder.AddAttribute(12, "aria-level", entry.Level + 1);
                builder.AddAttribute(13, "style", string.Create(CultureInfo.InvariantCulture, $"padding-inline-start:{entry.Level}em"));
                if (entry.Children.Count > 0)
                {
                    bool open = sidebar.IsOpen(entry);
                    builder.AddAttribute(14, "aria-expanded", open ? "true" : "false");
                    builder.OpenElement(15, "button");
                    builder.AddAttribute(16, "type", "button");
                    builder.AddAttribute(17, "data-outline-toggle", entry.Id);
                    builder.AddAttribute(18, "aria-label", (open ? "Collapse " : "Expand ") + entry.Title);
                    builder.AddAttribute(19, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, () => sidebar.Toggle(entry)));
                    builder.AddContent(20, open ? "▾" : "▸");
                    builder.CloseElement();
                }

                builder.OpenElement(21, "button");
                builder.AddAttribute(22, "type", "button");
                builder.AddAttribute(23, "data-outline-entry", entry.Id);
                builder.AddAttribute(24, "aria-current", ReferenceEquals(entry, current) ? "true" : null);
                builder.AddAttribute(25, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, () => sidebar.GoTo(entry)));
                builder.AddContent(26, entry.PageLabel is { } label ? entry.Title + "  p. " + label : entry.Title);
                builder.CloseElement();
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    public static void Search(RenderTreeBuilder builder, object receiver, PanelBinding binding)
    {
        Open(builder, "search");
        builder.OpenElement(10, "form");
        builder.AddAttribute(11, "role", "search");
        builder.AddAttribute(12, "onsubmit", EventCallback.Factory.Create(receiver, () => binding.Sidebar?.Search(binding.Query)));
        builder.AddEventPreventDefaultAttribute(13, "onsubmit", true);
        builder.OpenElement(14, "input");
        builder.AddAttribute(15, "type", "search");
        builder.AddAttribute(16, "aria-label", "Find in document");
        builder.AddAttribute(17, "placeholder", "Find in document");
        builder.AddAttribute(18, "value", binding.Query);
        builder.AddAttribute(19, "data-search-query", true);
        builder.AddAttribute(20, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(receiver, e => binding.Query = e.Value as string ?? string.Empty));
        builder.CloseElement();
        builder.OpenElement(21, "button");
        builder.AddAttribute(22, "type", "submit");
        builder.AddContent(23, "Find");
        builder.CloseElement();
        builder.CloseElement();
        if (binding.Sidebar is not { SearchResults: { } results } sidebar)
        {
            Hint(builder, binding.Sidebar?.Session is null ? "No document." : "This view has no search.");
            builder.CloseElement();
            return;
        }

        DocumentSearch search = results.Search;
        if (search.Query.Length > 0)
        {
            builder.OpenElement(30, "div");
            builder.AddAttribute(31, "class", "bs-panel-hint");
            builder.AddAttribute(32, "role", "status");
            builder.AddContent(33, results.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{search.CurrentIndex + 1} of {results.Count}{(search.IsSearchComplete ? string.Empty : "+")}")
                : search.IsSearchComplete ? "No matches." : string.Create(CultureInfo.InvariantCulture, $"Searching… {search.SearchedPageCount} of {sidebar.PageCount} pages"));
            builder.CloseElement();
        }

        int shown = 0;
        foreach (SearchResultGroup group in results.Pages)
        {
            if (shown >= MaxRows)
            {
                break;
            }

            builder.OpenElement(40, "h4");
            builder.SetKey(group.PageIndex);
            builder.AddContent(41, "Page " + sidebar.GetPageLabel(group.PageIndex));
            builder.CloseElement();
            foreach (SearchResultItem item in group.Items)
            {
                if (shown++ >= MaxRows)
                {
                    break;
                }

                builder.OpenElement(42, "button");
                builder.AddAttribute(43, "type", "button");
                builder.AddAttribute(44, "data-search-result", item.MatchIndex);
                builder.AddAttribute(45, "aria-current", item.MatchIndex == search.CurrentIndex ? "true" : null);
                builder.AddAttribute(46, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, () => sidebar.Show(item)));
                builder.AddContent(47, item.Before);
                builder.OpenElement(48, "mark");
                builder.AddContent(49, item.Text);
                builder.CloseElement();
                builder.AddContent(50, item.After);
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    public static void Attachments(RenderTreeBuilder builder, object receiver, PanelBinding binding)
    {
        Open(builder, "attachments");
        if (Status(builder, binding, binding.Sidebar?.Attachments, "attachments") is { Attachments: { } attachments } sidebar)
        {
            if (attachments.IsEmpty)
            {
                Hint(builder, "No attachments.");
            }

            int key = 0;
            foreach (AttachmentItem item in attachments.Items)
            {
                builder.OpenElement(10, "div");
                builder.SetKey(key++);
                builder.AddAttribute(11, "data-attachment", item.FileName);
                builder.OpenElement(12, "strong");
                builder.AddContent(13, item.FileName);
                builder.CloseElement();
                builder.OpenElement(14, "div");
                builder.AddAttribute(15, "class", "bs-panel-hint");
                builder.AddContent(16, SidebarText.Describe(item));
                builder.CloseElement();
                if (item.IsEmbedded)
                {
                    Button(builder, 17, receiver, "Open", "open", "Open " + item.FileName, () => sidebar.OpenAttachmentAsync(item, AttachmentAction.Open));
                    Button(builder, 18, receiver, "Save", "save", "Save " + item.FileName, () => sidebar.OpenAttachmentAsync(item, AttachmentAction.Save));
                }

                if (item.PageIndex is not null)
                {
                    Button(builder, 19, receiver, "Show", "show", "Show " + item.FileName + " on its page", () =>
                    {
                        sidebar.Reveal(item);
                        return Task.CompletedTask;
                    });
                }

                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    public static void Layers(RenderTreeBuilder builder, object receiver, PanelBinding binding)
    {
        Open(builder, "layers");
        if (Status(builder, binding, binding.Sidebar?.Layers, "layers") is { Layers: { } layers } sidebar)
        {
            if (layers.IsEmpty)
            {
                Hint(builder, "No layers.");
            }

            int key = 0;
            foreach ((LayerItem item, int depth) in SidebarText.Flatten(layers.Roots))
            {
                builder.OpenElement(10, "label");
                builder.SetKey(key++);
                builder.AddAttribute(11, "style", string.Create(CultureInfo.InvariantCulture, $"padding-inline-start:{depth}em"));
                if (item.Group is null)
                {
                    builder.OpenElement(12, "em");
                    builder.AddContent(13, item.Title);
                    builder.CloseElement();
                }
                else
                {
                    builder.OpenElement(14, "input");
                    builder.AddAttribute(15, "type", "checkbox");
                    builder.AddAttribute(16, "checked", item.IsVisible);
                    builder.AddAttribute(17, "disabled", item.IsLocked);
                    builder.AddAttribute(18, "data-layer", item.Title);
                    builder.AddAttribute(19, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(receiver, e => sidebar.SetLayerVisible(item, e.Value is true)));
                    builder.CloseElement();
                    builder.AddContent(20, item.IsLocked ? item.Title + " (locked)" : item.Title);
                }

                builder.CloseElement();
            }

            Button(builder, 30, receiver, "Reset", "reset", "Reset the layers to the document's own states", () =>
            {
                sidebar.ResetLayers();
                return Task.CompletedTask;
            });
        }

        builder.CloseElement();
    }

    public static void Annotations(RenderTreeBuilder builder, object receiver, PanelBinding binding)
    {
        Open(builder, "annotations");
        if (binding.Sidebar is { Session: not null, Annotations: null })
        {
            Hint(builder, "This view has no annotation layer.");
        }
        else if (Status(builder, binding, binding.Sidebar?.Annotations, "annotations") is { Annotations: { } annotations } sidebar)
        {
            if (annotations.Items.Count == 0)
            {
                Hint(builder, annotations.IsComplete ? "No annotations." : "Reading…");
            }

            int page = -1;
            foreach (AnnotationListItem item in annotations.Items)
            {
                if (item.Target.PageIndex != page)
                {
                    page = item.Target.PageIndex;
                    builder.OpenElement(10, "h4");
                    builder.SetKey(-1 - page);
                    builder.AddContent(11, "Page " + sidebar.GetPageLabel(page));
                    builder.CloseElement();
                }

                builder.OpenElement(12, "button");
                builder.SetKey(item.Target);
                builder.AddAttribute(13, "type", "button");
                builder.AddAttribute(14, "data-annotation", string.Create(CultureInfo.InvariantCulture, $"{item.Target.PageIndex}:{item.Target.Index}"));
                builder.AddAttribute(15, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, () => sidebar.Reveal(item)));
                builder.AddContent(16, SidebarText.Describe(item));
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    private static void Open(RenderTreeBuilder builder, string panel)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "bs-panel");
        builder.AddAttribute(2, "data-panel", panel);
    }

    // The status of a panel whose model is missing (no document, being read, or why not); the sidebar when the model is there.
    private static ViewerSidebar? Status(RenderTreeBuilder builder, PanelBinding binding, object? model, string what)
    {
        if (binding.Sidebar is not { Session: not null } sidebar)
        {
            Hint(builder, "No document.");
            return null;
        }

        if (model is null)
        {
            Hint(builder, sidebar.GetError(binding.Panel) is { } error ? $"The {what} could not be read: {error}" : "Reading…");
            return null;
        }

        return sidebar;
    }

    private static void Hint(RenderTreeBuilder builder, string text)
    {
        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "class", "bs-panel-hint");
        builder.AddAttribute(5, "role", "status");
        builder.AddContent(6, text);
        builder.CloseElement();
    }

    private static void Button(RenderTreeBuilder builder, int sequence, object receiver, string text, string action, string label, Func<Task> click)
    {
        builder.OpenRegion(sequence);
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "data-action", action);
        builder.AddAttribute(3, "aria-label", label);
        builder.AddAttribute(4, "onclick", EventCallback.Factory.Create<MouseEventArgs>(receiver, click));
        builder.AddContent(5, text);
        builder.CloseElement();
        builder.CloseRegion();
    }
}

/// <summary>The panels' texts.</summary>
internal static class SidebarText
{
    public static string Describe(AttachmentItem item)
    {
        var parts = new List<string>();
        if (item.Description is { Length: > 0 } description)
        {
            parts.Add(description);
        }

        if (item.MediaType is { } type)
        {
            parts.Add(type);
        }

        if (item.Size is long size)
        {
            parts.Add(size < 1024 ? string.Create(CultureInfo.InvariantCulture, $"{size} bytes") : string.Create(CultureInfo.InvariantCulture, $"{size / 1024.0:0.#} KB"));
        }

        if (item.PageIndex is int page)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"on page {page + 1}"));
        }

        parts.Add(!item.IsEmbedded ? "not embedded" : item.CheckSum switch
        {
            PdfCheckSumStatus.Matches => "checksum verified",
            PdfCheckSumStatus.DoesNotMatch => "checksum does not match",
            _ => "no checksum",
        });
        return string.Join(" · ", parts);
    }

    public static string Describe(AnnotationListItem item)
    {
        string text = string.IsNullOrEmpty(item.Text) ? item.Kind : item.Kind + ": " + item.Text;
        if (item.Author is { } author)
        {
            text = author + " · " + text;
        }

        if (item.ReplyCount > 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" ({item.ReplyCount} repl{(item.ReplyCount == 1 ? "y" : "ies")})");
        }

        return text;
    }

    public static IEnumerable<(LayerItem Item, int Depth)> Flatten(IReadOnlyList<LayerItem> items, int depth = 0)
    {
        foreach (LayerItem item in items)
        {
            yield return (item, depth);
            foreach ((LayerItem, int) child in Flatten(item.Children, depth + 1))
            {
                yield return child;
            }
        }
    }
}
