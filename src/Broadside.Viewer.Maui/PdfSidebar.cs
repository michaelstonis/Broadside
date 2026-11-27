using System.Globalization;
using Microsoft.Maui.Controls;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The files a <see cref="PdfView"/>'s document carries (embedded files and file attachment annotations), each with its size, type
/// and checksum status. Open and Save raise <see cref="Opening"/> with the decoded bytes: the app decides what to do with them, the
/// viewer never launches a file. An attachment annotation's row also shows it on its page.
/// </summary>
/// <remarks>ISO 32000-2 §7.11.4 (embedded file streams) and §12.5.6.15 (file attachment annotations), through <see cref="ViewerSidebar"/>.</remarks>
#pragma warning disable CA1001 // the sidebar model is disposed when the panel is given another view (or none)
public sealed class PdfAttachmentsPanel : ContentView
#pragma warning restore CA1001
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfAttachmentsPanel), null, propertyChanged: (b, _, n) => ((PdfAttachmentsPanel)b).Bind((PdfView?)n));

    private readonly Label _status = PanelKit.Hint(string.Empty, "attachments-status");
    private readonly VerticalStackLayout _list = new() { Spacing = 4, AutomationId = "attachments" };
    private ViewerSidebar? _sidebar;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view whose document it lists.</summary>
    public PdfAttachmentsPanel()
    {
        Content = new ScrollView { Content = new VerticalStackLayout { Spacing = 6, Children = { _status, _list } } };
        Update();
    }

    /// <summary>Raised with an attachment's bytes when the user opens or saves it.</summary>
    public event EventHandler<AttachmentOpeningEventArgs>? Opening;

    /// <summary>Gets or sets the view whose document this panel lists.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a view.</summary>
    public ViewerSidebar? Sidebar => _sidebar;

    /// <summary>Gets the number of attachments listed.</summary>
    public int Count => _sidebar?.Attachments?.Items.Count ?? 0;

    private void Bind(PdfView? view)
    {
        if (_sidebar is not null)
        {
            _sidebar.Changed -= OnChanged;
            _sidebar.AttachmentOpening -= OnOpening;
            _sidebar.Dispose();
            _sidebar = null;
        }

        if (view is not null)
        {
            _sidebar = new ViewerSidebar(view, SidebarPanels.Attachments);
            _sidebar.Changed += OnChanged;
            _sidebar.AttachmentOpening += OnOpening;
        }

        Update();
    }

    private void OnOpening(object? sender, AttachmentOpeningEventArgs e) => Opening?.Invoke(this, e);

    private void OnChanged(object? sender, SidebarChangedEventArgs e)
    {
        if ((e.Panels & SidebarPanels.Attachments) != 0)
        {
            Update();
        }
    }

    private void Update()
    {
        _list.Children.Clear();
        if (_sidebar is not { Session: not null } sidebar)
        {
            _status.Text = "No document.";
            return;
        }

        if (sidebar.Attachments is not { } attachments)
        {
            _status.Text = sidebar.GetError(SidebarPanels.Attachments) is { } error ? "The attachments could not be read: " + error : "Reading…";
            return;
        }

        _status.Text = attachments.IsEmpty ? "No attachments." : string.Create(CultureInfo.InvariantCulture, $"{attachments.Items.Count} attachment(s){(attachments.IsComplete ? string.Empty : ", more may follow")}");
        foreach (AttachmentItem item in attachments.Items)
        {
            var row = new VerticalStackLayout { Spacing = 2, AutomationId = "attachment" };
            row.Children.Add(new Label { Text = item.FileName, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation });
            row.Children.Add(PanelKit.Hint(SidebarText.Describe(item)));
            var buttons = new List<View>();
            if (item.IsEmbedded)
            {
                buttons.Add(PanelKit.Button("Open", "attachment-open", () => _ = sidebar.OpenAttachmentAsync(item, AttachmentAction.Open), "Open " + item.FileName));
                buttons.Add(PanelKit.Button("Save", "attachment-save", () => _ = sidebar.OpenAttachmentAsync(item, AttachmentAction.Save), "Save " + item.FileName));
            }

            if (item.PageIndex is not null)
            {
                buttons.Add(PanelKit.Button("Show", "attachment-show", () => sidebar.Reveal(item), "Show " + item.FileName + " on its page"));
            }

            row.Children.Add(PanelKit.Wrap([.. buttons]));
            _list.Children.Add(row);
        }
    }
}

/// <summary>
/// The layers (optional content groups) of a <see cref="PdfView"/>'s document as the default configuration's tree, each with a switch
/// that shows or hides it in the view; locked layers cannot be switched and radio-button sets keep one layer on. Reset returns to the
/// file's own states.
/// </summary>
/// <remarks>ISO 32000-2 §8.11.2 and §8.11.4, through <see cref="ViewerSidebar"/> and <see cref="DocumentSession.OptionalContentState"/>.</remarks>
#pragma warning disable CA1001 // the sidebar model is disposed when the panel is given another view (or none)
public sealed class PdfLayersPanel : ContentView
#pragma warning restore CA1001
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfLayersPanel), null, propertyChanged: (b, _, n) => ((PdfLayersPanel)b).Bind((PdfView?)n));

    private readonly Label _status = PanelKit.Hint(string.Empty, "layers-status");
    private readonly VerticalStackLayout _list = new() { Spacing = 2, AutomationId = "layers" };
    private ViewerSidebar? _sidebar;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view whose layers it switches.</summary>
    public PdfLayersPanel()
    {
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { _status, _list, PanelKit.Button("Reset", "layers-reset", () => _sidebar?.ResetLayers(), "Reset the layers to the document's own states") },
            },
        };
        Update();
    }

    /// <summary>Gets or sets the view whose layers this panel switches.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a view.</summary>
    public ViewerSidebar? Sidebar => _sidebar;

    private void Bind(PdfView? view)
    {
        if (_sidebar is not null)
        {
            _sidebar.Changed -= OnChanged;
            _sidebar.Dispose();
            _sidebar = null;
        }

        if (view is not null)
        {
            _sidebar = new ViewerSidebar(view, SidebarPanels.Layers);
            _sidebar.Changed += OnChanged;
        }

        Update();
    }

    private void OnChanged(object? sender, SidebarChangedEventArgs e)
    {
        if ((e.Panels & SidebarPanels.Layers) != 0)
        {
            Update();
        }
    }

    private void Update()
    {
        _list.Children.Clear();
        if (_sidebar is not { Session: not null } sidebar)
        {
            _status.Text = "No document.";
            return;
        }

        if (sidebar.Layers is not { } layers)
        {
            _status.Text = sidebar.GetError(SidebarPanels.Layers) is { } error ? "The layers could not be read: " + error : "Reading…";
            return;
        }

        _status.Text = layers.IsEmpty ? "No layers." : "Switch a layer to show or hide it.";
        foreach ((LayerItem item, int depth) in SidebarText.Flatten(layers.Roots))
        {
            if (item.Group is null)
            {
                _list.Children.Add(new Label { Text = item.Title, FontAttributes = FontAttributes.Italic, Margin = new Thickness(depth * 16, 6, 0, 0) });
                continue;
            }

            (View row, Switch toggle) = PanelKit.Toggle(item.Title + (item.IsLocked ? " (locked)" : string.Empty), "layer", item.IsVisible, visible =>
            {
                if (!sidebar.SetLayerVisible(item, visible))
                {
                    Update(); // refused (locked): show the state again
                }
            });
            toggle.IsEnabled = !item.IsLocked;
            row.Margin = new Thickness(depth * 16, 0, 0, 0);
            _list.Children.Add(row);
        }
    }
}

/// <summary>
/// The side panels of a <see cref="PdfView"/> behind one row of tabs: thumbnails, outline, search, attachments, layers and annotations.
/// Each tab is the shell's own panel bound to the same view.
/// </summary>
/// <remarks>Not a spec feature; the panels cite theirs.</remarks>
public sealed class PdfSidebar : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfSidebar), null, propertyChanged: (b, _, n) => ((PdfSidebar)b).Bind((PdfView?)n));

    private readonly ContentView _host = new();
    private readonly List<Button> _tabs = [];
    private readonly View[] _panels;
    private int _selected = -1;

    /// <summary>Creates the sidebar; set <see cref="Viewer"/> to the view its panels follow.</summary>
    public PdfSidebar()
    {
        Thumbnails = new PdfThumbnailsPanel();
        Outline = new PdfOutlinePanel();
        Search = new PdfSearchPanel();
        Attachments = new PdfAttachmentsPanel();
        Layers = new PdfLayersPanel();
        Annotations = new PdfAnnotationsPanel();
        _panels = [Thumbnails, Outline, Search, Attachments, Layers, Annotations];
        Attachments.Opening += (_, e) => AttachmentOpening?.Invoke(this, e);
        string[] names = ["Pages", "Outline", "Search", "Attachments", "Layers", "Comments"];
        string[] ids = ["thumbnails", "outline", "search", "attachments", "layers", "annotations"];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            _tabs.Add(PanelKit.Button(names[i], "sidebar-tab-" + ids[i], () => SelectedIndex = index, "Show the " + names[i].ToLowerInvariant() + " panel"));
        }

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 4 };
        root.Add(PanelKit.Wrap([.. _tabs]));
        root.Add(_host, 0, 1);
        Content = root;
        SelectedIndex = 0;
    }

    /// <summary>Raised with an attachment's decoded bytes when the user opens or saves it in the attachments tab.</summary>
    public event EventHandler<AttachmentOpeningEventArgs>? AttachmentOpening;

    /// <summary>Gets or sets the view the panels follow.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets or sets the tab shown: 0 thumbnails, 1 outline, 2 search, 3 attachments, 4 layers, 5 annotations.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int index = Math.Clamp(value, 0, _panels.Length - 1);
            if (index == _selected)
            {
                return;
            }

            _selected = index;
            _host.Content = _panels[index];
            PanelKit.Select(_tabs, _tabs[index]);
        }
    }

    /// <summary>Gets the thumbnails panel.</summary>
    public PdfThumbnailsPanel Thumbnails { get; }

    /// <summary>Gets the outline panel.</summary>
    public PdfOutlinePanel Outline { get; }

    /// <summary>Gets the search panel.</summary>
    public PdfSearchPanel Search { get; }

    /// <summary>Gets the attachments panel; its <see cref="PdfAttachmentsPanel.Opening"/> hands the app an attachment's bytes.</summary>
    public PdfAttachmentsPanel Attachments { get; }

    /// <summary>Gets the layers panel.</summary>
    public PdfLayersPanel Layers { get; }

    /// <summary>Gets the annotations panel.</summary>
    public PdfAnnotationsPanel Annotations { get; }

    private void Bind(PdfView? view)
    {
        Thumbnails.Viewer = view;
        Outline.Viewer = view;
        Search.Viewer = view;
        Attachments.Viewer = view;
        Layers.Viewer = view;
        Annotations.Viewer = view;
    }
}

/// <summary>The panels' texts, shared by the attachments and layers panels.</summary>
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
