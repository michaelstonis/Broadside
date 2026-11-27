// The side panels' logic over the core's ViewerSidebar (#636), written against SidebarKit so that every XAML shell shares it: the
// Avalonia, WPF, WinUI and Uno copies differ only in this header (the element type and the namespace) and in SidebarKit.
using System.Globalization;
using UiElement = System.Windows.FrameworkElement;

namespace Broadside.Viewer.Wpf;

/// <summary>One panel's logic: binds to a viewer (its own <see cref="ViewerSidebar"/>) or to a sidebar's shared one, and lays it out.</summary>
internal abstract class SidebarPanelCore
{
    private ViewerSidebar? _owned;

    protected SidebarPanelCore()
    {
        List = new SidebarList();
    }

    public ViewerSidebar? Sidebar { get; private set; }

    public abstract SidebarPanels Panel { get; }

    public virtual UiElement Root => List.Root;

    protected SidebarList List { get; }

    /// <summary>Follows a viewer with a sidebar model of this panel's own (none for <see langword="null"/>).</summary>
    public void Bind(IDocumentViewer? viewer)
    {
        ViewerSidebar? old = _owned;
        _owned = viewer is null ? null : new ViewerSidebar(viewer, Panel);
        Use(_owned);
        old?.Dispose();
    }

    /// <summary>Lays out a model another control owns (the sidebar's shared one).</summary>
    public void Use(ViewerSidebar? sidebar)
    {
        if (Sidebar is not null)
        {
            Sidebar.Changed -= OnChanged;
            Unbound(Sidebar);
        }

        Sidebar = sidebar;
        if (sidebar is not null)
        {
            sidebar.Changed += OnChanged;
            Bound(sidebar);
        }

        Render();
    }

    protected virtual void Bound(ViewerSidebar sidebar)
    {
    }

    protected virtual void Unbound(ViewerSidebar sidebar)
    {
    }

    protected virtual bool Concerns(SidebarChangedEventArgs e) => e.Change == SidebarChange.Document || (e.Panels & Panel) != 0;

    protected abstract void Render();

    /// <summary>The status line for a panel whose model is missing: no document, being read, or why it could not be.</summary>
    protected bool Missing(object? model, string what)
    {
        if (Sidebar is not { Session: not null } sidebar)
        {
            List.Add(SidebarKit.Text("No document.", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return true;
        }

        if (model is null)
        {
            List.Add(SidebarKit.Text(sidebar.GetError(Panel) is { } error ? $"The {what} could not be read: {error}" : "Reading…", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return true;
        }

        return false;
    }

    private void OnChanged(object? sender, SidebarChangedEventArgs e)
    {
        if (Concerns(e))
        {
            Changed(e);
        }
    }

    protected virtual void Changed(SidebarChangedEventArgs e) => Render();
}

/// <summary>Page thumbnails with their labels; a click shows the page, the page in view is marked.</summary>
internal sealed class ThumbnailsCore : SidebarPanelCore
{
    private const int MaxPages = 500;
    private readonly List<SidebarThumbnailSlot> _slots = [];
    private CancellationTokenSource? _work;

    public override SidebarPanels Panel => SidebarPanels.Thumbnails;

    public int Count => _slots.Count;

    public int RenderedCount => _slots.Count(slot => slot.HasImage);

    protected override bool Concerns(SidebarChangedEventArgs e) => base.Concerns(e) || e.Change == SidebarChange.CurrentPage;

    protected override void Changed(SidebarChangedEventArgs e)
    {
        switch (e.Change)
        {
            case SidebarChange.Thumbnail when (uint)e.PageIndex < (uint)_slots.Count && Sidebar?.GetThumbnail(e.PageIndex) is { } thumbnail:
                _slots[e.PageIndex].SetImage(thumbnail);
                break;
            case SidebarChange.CurrentPage:
                MarkCurrent();
                break;
            default:
                Render();
                break;
        }
    }

    protected override void Unbound(ViewerSidebar sidebar) => Cancel();

    protected override void Render()
    {
        Cancel();
        List.Clear();
        _slots.Clear();
        if (Missing(Sidebar?.Session, "pages") || Sidebar is not { } sidebar)
        {
            return;
        }

        int count = Math.Min(sidebar.PageCount, MaxPages);
        for (int page = 0; page < count; page++)
        {
            int index = page;
            SidebarThumbnailSlot slot = SidebarKit.Thumbnail(sidebar.GetPageLabel(page), () => sidebar.GoToPage(index));
            if (sidebar.GetThumbnail(page) is { } thumbnail)
            {
                slot.SetImage(thumbnail);
            }

            _slots.Add(slot);
            List.Add(slot.Element);
        }

        MarkCurrent();
        var work = new CancellationTokenSource();
        _work = work;
        _ = sidebar.RenderThumbnailsAsync(0, count, work.Token);
    }

    private void MarkCurrent()
    {
        int current = Sidebar?.PageIndex ?? -1;
        for (int i = 0; i < _slots.Count; i++)
        {
            _slots[i].SetCurrent(i == current);
        }
    }

    private void Cancel()
    {
        if (_work is { } work)
        {
            _work = null;
            work.Cancel();
            work.Dispose();
        }
    }
}

/// <summary>The outline as a tree that expands and collapses; a click shows the entry's destination.</summary>
internal sealed class OutlineCore : SidebarPanelCore
{
    public override SidebarPanels Panel => SidebarPanels.Outline;

    public int VisibleCount { get; private set; }

    protected override bool Concerns(SidebarChangedEventArgs e) => base.Concerns(e) || e.Change == SidebarChange.CurrentPage;

    protected override void Render()
    {
        List.Clear();
        VisibleCount = 0;
        if (Missing(Sidebar?.Outline, "outline") || Sidebar is not { Outline: { } outline } sidebar)
        {
            return;
        }

        if (outline.IsEmpty)
        {
            List.Add(SidebarKit.Text("No outline.", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return;
        }

        OutlineEntry? current = sidebar.CurrentOutlineEntry;
        foreach (OutlineEntry entry in sidebar.GetVisibleOutline())
        {
            string chevron = entry.Children.Count == 0 ? "  " : sidebar.IsOpen(entry) ? "▾" : "▸";
            UiElement toggle = SidebarKit.Button(chevron, "outline-toggle", () => sidebar.Toggle(entry), (sidebar.IsOpen(entry) ? "Collapse " : "Expand ") + entry.Title, small: true);
            string page = entry.PageLabel is { } label ? "  p. " + label : string.Empty;
            UiElement title = SidebarKit.Button(entry.Title + page, "outline-entry", () => sidebar.GoTo(entry), selected: ReferenceEquals(entry, current));
            List.Add(SidebarKit.Row(entry.Level * 14, toggle, title));
            VisibleCount++;
        }
    }
}

/// <summary>A query box, the search's progress, and its matches by page with their context; a click shows a match.</summary>
internal sealed class SearchCore : SidebarPanelCore
{
    private const int MaxResults = 500;
    private readonly SidebarList _results = new();
    private readonly UiElement _root;
    private readonly SidebarQueryBox _query;
    private readonly UiElement _status;

    public SearchCore()
    {
        _query = SidebarKit.QueryBox("search-query", "Find in document", text => Sidebar?.Search(text));
        _status = SidebarKit.Text(string.Empty, SidebarTextStyle.Hint, automationId: "search-status");
        _root = SidebarKit.Docked(SidebarKit.Stack(_query.Element, _status), _results.Root);
    }

    public override SidebarPanels Panel => SidebarPanels.Search;

    public override UiElement Root => _root;

    public int ResultCount { get; private set; }

    public void Run(string query)
    {
        _query.Text = query;
        Sidebar?.Search(query);
    }

    protected override void Render()
    {
        _results.Clear();
        ResultCount = 0;
        if (Sidebar is not { SearchResults: { } results } sidebar)
        {
            SidebarKit.SetText(_status, Sidebar?.Session is null ? "No document." : "This view has no search.");
            return;
        }

        DocumentSearch search = results.Search;
        SidebarKit.SetText(_status, search.Query.Length == 0
            ? string.Empty
            : results.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{search.CurrentIndex + 1} of {results.Count}{(search.IsSearchComplete ? string.Empty : "+")}")
                : search.IsSearchComplete ? "No matches." : string.Create(CultureInfo.InvariantCulture, $"Searching… {search.SearchedPageCount} of {sidebar.PageCount} pages"));
        foreach (SearchResultGroup group in results.Pages)
        {
            if (ResultCount >= MaxResults)
            {
                break;
            }

            _results.Add(SidebarKit.Text("Page " + sidebar.GetPageLabel(group.PageIndex), SidebarTextStyle.Heading));
            foreach (SearchResultItem item in group.Items)
            {
                if (ResultCount++ >= MaxResults)
                {
                    break;
                }

                _results.Add(SidebarKit.Button(item.Before + item.Text + item.After, "search-result", () => sidebar.Show(item), selected: item.MatchIndex == search.CurrentIndex));
            }
        }
    }
}

/// <summary>Embedded files and file attachment annotations; Open and Save hand the bytes to the app.</summary>
internal sealed class AttachmentsCore : SidebarPanelCore
{
    public override SidebarPanels Panel => SidebarPanels.Attachments;

    public int Count => Sidebar?.Attachments?.Items.Count ?? 0;

    /// <summary>What the standalone panel does with an attachment's bytes (raises its own event); the sidebar handles its shared model's.</summary>
    public Action<AttachmentOpeningEventArgs>? Opening { get; set; }

    protected override void Bound(ViewerSidebar sidebar) => sidebar.AttachmentOpening += OnOpening;

    protected override void Unbound(ViewerSidebar sidebar) => sidebar.AttachmentOpening -= OnOpening;

    private void OnOpening(object? sender, AttachmentOpeningEventArgs e) => Opening?.Invoke(e);

    protected override void Render()
    {
        List.Clear();
        if (Missing(Sidebar?.Attachments, "attachments") || Sidebar is not { Attachments: { } attachments } sidebar)
        {
            return;
        }

        if (attachments.IsEmpty)
        {
            List.Add(SidebarKit.Text("No attachments.", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return;
        }

        foreach (AttachmentItem item in attachments.Items)
        {
            List.Add(SidebarKit.Text(item.FileName, SidebarTextStyle.Heading, automationId: "attachment"));
            List.Add(SidebarKit.Text(SidebarText.Describe(item), SidebarTextStyle.Hint));
            var buttons = new List<UiElement>();
            if (item.IsEmbedded)
            {
                buttons.Add(SidebarKit.Button("Open", "attachment-open", () => _ = sidebar.OpenAttachmentAsync(item, AttachmentAction.Open), "Open " + item.FileName));
                buttons.Add(SidebarKit.Button("Save", "attachment-save", () => _ = sidebar.OpenAttachmentAsync(item, AttachmentAction.Save), "Save " + item.FileName));
            }

            if (item.PageIndex is not null)
            {
                buttons.Add(SidebarKit.Button("Show", "attachment-show", () => sidebar.Reveal(item), "Show " + item.FileName + " on its page"));
            }

            List.Add(SidebarKit.Row(0, [.. buttons]));
        }

        if (!attachments.IsComplete)
        {
            List.Add(SidebarKit.Text("Some parts of the document could not be read yet.", SidebarTextStyle.Hint));
        }
    }
}

/// <summary>The layer tree with a check box per layer; locked layers and labels cannot be switched. Reset returns to the file's states.</summary>
internal sealed class LayersCore : SidebarPanelCore
{
    public override SidebarPanels Panel => SidebarPanels.Layers;

    protected override void Render()
    {
        List.Clear();
        if (Missing(Sidebar?.Layers, "layers") || Sidebar is not { Layers: { } layers } sidebar)
        {
            return;
        }

        if (layers.IsEmpty)
        {
            List.Add(SidebarKit.Text("No layers.", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return;
        }

        foreach ((LayerItem item, int depth) in SidebarText.Flatten(layers.Roots))
        {
            List.Add(item.Group is null
                ? SidebarKit.Text(item.Title, SidebarTextStyle.Label, depth * 16)
                : SidebarKit.Check(item.Title + (item.IsLocked ? " (locked)" : string.Empty), "layer", item.IsVisible, !item.IsLocked, visible =>
                {
                    if (!sidebar.SetLayerVisible(item, visible))
                    {
                        Render(); // refused: show the state again
                    }
                }, depth * 16));
        }

        List.Add(SidebarKit.Button("Reset", "layers-reset", sidebar.ResetLayers, "Reset the layers to the document's own states"));
    }
}

/// <summary>Every annotation and comment by page, with author and replies; a click selects it and shows it.</summary>
internal sealed class AnnotationsCore : SidebarPanelCore
{
    public override SidebarPanels Panel => SidebarPanels.Annotations;

    public int Count => Sidebar?.Annotations?.Items.Count ?? 0;

    protected override void Render()
    {
        List.Clear();
        if (Sidebar is { Session: not null, Annotations: null })
        {
            List.Add(SidebarKit.Text("This view has no annotation layer.", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return;
        }

        if (Missing(Sidebar?.Annotations, "annotations") || Sidebar is not { Annotations: { } annotations } sidebar)
        {
            return;
        }

        if (annotations.Items.Count == 0)
        {
            List.Add(SidebarKit.Text(annotations.IsComplete ? "No annotations." : "Reading…", SidebarTextStyle.Hint, automationId: "sidebar-status"));
            return;
        }

        int page = -1;
        foreach (AnnotationListItem item in annotations.Items)
        {
            if (item.Target.PageIndex != page)
            {
                page = item.Target.PageIndex;
                List.Add(SidebarKit.Text("Page " + sidebar.GetPageLabel(page), SidebarTextStyle.Heading));
            }

            List.Add(SidebarKit.Button(SidebarText.Describe(item), "annotation-item", () => sidebar.Reveal(item)));
        }
    }
}

/// <summary>The six panels behind a row of tabs, sharing one <see cref="ViewerSidebar"/>.</summary>
internal sealed class SidebarCore
{
    private static readonly (SidebarPanels Panel, string Name, string Id)[] Tabs =
    [
        (SidebarPanels.Thumbnails, "Pages", "thumbnails"),
        (SidebarPanels.Outline, "Outline", "outline"),
        (SidebarPanels.Search, "Search", "search"),
        (SidebarPanels.Attachments, "Attachments", "attachments"),
        (SidebarPanels.Layers, "Layers", "layers"),
        (SidebarPanels.Annotations, "Comments", "annotations"),
    ];

    private readonly SidebarTabs _tabs;
    private ViewerSidebar? _sidebar;

    public SidebarCore(Action<AttachmentOpeningEventArgs> opening)
    {
        Opening = opening;
        Panels = [new ThumbnailsCore(), new OutlineCore(), new SearchCore(), new AttachmentsCore(), new LayersCore(), new AnnotationsCore()];
        _tabs = SidebarKit.Tabs([.. Tabs.Select((tab, i) => (tab.Name, "sidebar-tab-" + tab.Id, Panels[i].Root))]);
    }

    public UiElement Root => _tabs.Element;

    public SidebarPanelCore[] Panels { get; }

    public ViewerSidebar? Sidebar => _sidebar;

    public SidebarPanels Selected
    {
        get => Tabs[_tabs.Selected].Panel;
        set
        {
            int index = Array.FindIndex(Tabs, tab => tab.Panel == value);
            if (index >= 0)
            {
                _tabs.Selected = index;
            }
        }
    }

    private Action<AttachmentOpeningEventArgs> Opening { get; }

    public void Bind(IDocumentViewer? viewer)
    {
        ViewerSidebar? old = _sidebar;
        old?.AttachmentOpening -= OnOpening;
        _sidebar = viewer is null ? null : new ViewerSidebar(viewer);
        _sidebar?.AttachmentOpening += OnOpening;
        foreach (SidebarPanelCore panel in Panels)
        {
            panel.Use(_sidebar);
        }

        old?.Dispose();
    }

    private void OnOpening(object? sender, AttachmentOpeningEventArgs e) => Opening(e);
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

/// <summary>How a panel's text reads.</summary>
internal enum SidebarTextStyle
{
    Normal,
    Heading,
    Hint,
    Label,
}
