using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The outline (bookmarks) of a <see cref="PdfView"/>'s document as a tree: entries open as the file says, expand and collapse, show
/// the file's color and style and the page label they lead to, and choosing one shows its destination (page, view and zoom) through
/// <see cref="PdfView.GoTo"/>. The entry the page in view is under is marked.
/// </summary>
/// <remarks>ISO 32000-2 §12.3.3 (document outline), §12.3.2 (destinations), §12.4.2 (page labels). Spec #33 viewer capability 10.</remarks>
public sealed class PdfOutlinePanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfOutlinePanel), null, propertyChanged: (b, _, n) => ((PdfOutlinePanel)b)._follower.Attach((PdfView?)n));

    private readonly ViewerFollower _follower;
    private readonly Label _status = PanelKit.Hint(string.Empty, "outline-status");
    private readonly CollectionView _rows = new() { AutomationId = "outline" };
    private readonly HashSet<int> _open = [];
    private DocumentOutline? _outline;
    private int _generation;
    private int _current = -1;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view it navigates.</summary>
    public PdfOutlinePanel()
    {
        _follower = new ViewerFollower(() => _ = LoadAsync(), MarkCurrent);
        _rows.ItemTemplate = new DataTemplate(() =>
        {
            var toggle = new Button { Padding = new Thickness(0), WidthRequest = 36, MinimumHeightRequest = 36, BackgroundColor = Colors.Transparent, AutomationId = "outline-toggle" };
            toggle.SetBinding(Button.TextProperty, static (OutlineRow r) => r.Chevron);
            toggle.SetBinding(IsVisibleProperty, static (OutlineRow r) => r.HasChildren);
            toggle.SetBinding(SemanticProperties.DescriptionProperty, static (OutlineRow r) => r.ToggleDescription);
            toggle.Clicked += (s, _) =>
            {
                if (((BindableObject)s!).BindingContext is OutlineRow row)
                {
                    Toggle(row.Entry);
                }
            };
            var title = new Label { FontSize = 14, LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center, AutomationId = "outline-entry" };
            title.SetBinding(Label.TextProperty, static (OutlineRow r) => r.Entry.Title);
            title.SetBinding(Label.FontAttributesProperty, static (OutlineRow r) => r.Attributes);
            title.SetBinding(Label.TextColorProperty, static (OutlineRow r) => r.Color);
            title.SetBinding(BackgroundColorProperty, static (OutlineRow r) => r.Highlight);
            var page = new Label { FontSize = 11, TextColor = PanelKit.HintColor, VerticalTextAlignment = TextAlignment.Center };
            page.SetBinding(Label.TextProperty, static (OutlineRow r) => r.PageText);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, _) =>
            {
                if (((BindableObject)s!).BindingContext is OutlineRow row)
                {
                    Go(row.Entry);
                }
            };
            var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(36), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 4, MinimumHeightRequest = 40 };
            grid.SetBinding(PaddingProperty, static (OutlineRow r) => r.Indent);
            grid.Add(toggle);
            grid.Add(title, 1, 0);
            grid.Add(page, 2, 0);
            grid.GestureRecognizers.Add(tap);
            return grid;
        });
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 4 };
        root.Add(_status);
        root.Add(_rows, 0, 1);
        Content = root;
    }

    /// <summary>Gets or sets the view this panel navigates.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the number of outline rows shown (entries whose parents are all expanded).</summary>
    public int VisibleCount => (_rows.ItemsSource as IReadOnlyList<OutlineRow>)?.Count ?? 0;

    /// <summary>Shows the destination of the visible row at an index, as a tap on it does (keyboard and test access).</summary>
    /// <param name="index">The zero-based visible row.</param>
    /// <returns>Whether the entry had a destination in the document.</returns>
    public bool Activate(int index) =>
        _rows.ItemsSource is IReadOnlyList<OutlineRow> rows && (uint)index < (uint)rows.Count && Go(rows[index].Entry);

    private async Task LoadAsync()
    {
        int generation = ++_generation;
        _open.Clear();
        _outline = null;
        _current = -1;
        _rows.ItemsSource = null;
        if (_follower.View?.CurrentSession is not { } session)
        {
            _status.Text = "No document.";
            return;
        }

        _status.Text = "Reading the outline…";
        DocumentOutline outline;
        try
        {
            outline = await DocumentOutline.ReadAsync(session).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
#pragma warning disable CA1031 // shown in the panel
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (generation == _generation)
            {
                _status.Text = "The outline could not be read: " + exception.Message;
            }

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _outline = outline;
        _open.UnionWith(outline.Entries.Where(entry => entry.IsOpen).Select(entry => entry.Id));
        _status.Text = outline.IsEmpty ? "This document has no outline (bookmarks)." : "Tap an entry to go to it; ▸ expands it.";
        MarkCurrent();
    }

    private void MarkCurrent()
    {
        _current = _outline?.FindEntryFor(_follower.CurrentPage)?.Id ?? -1;
        Show();
    }

    private void Show()
    {
        if (_outline is null)
        {
            return;
        }

        _rows.ItemsSource = _outline.GetVisible(entry => _open.Contains(entry.Id)).Select(entry => new OutlineRow(entry, _open.Contains(entry.Id), entry.Id == _current)).ToList();
    }

    private void Toggle(OutlineEntry entry)
    {
        if (!_open.Remove(entry.Id))
        {
            _open.Add(entry.Id);
        }

        Show();
    }

    // The view goes where the destination says (its page, view and zoom); an address is the host's business, never opened here.
    private bool Go(OutlineEntry entry)
    {
        if (entry.Target is { } target && _follower.View is { } view)
        {
            view.GoTo(target);
            return true;
        }

        return false;
    }

    /// <summary>One visible outline row.</summary>
    internal sealed class OutlineRow(OutlineEntry entry, bool isOpen, bool isCurrent)
    {
        public OutlineEntry Entry { get; } = entry;

        public bool HasChildren => Entry.Children.Count > 0;

        public string Chevron => isOpen ? "▾" : "▸";

        public string ToggleDescription => (isOpen ? "Collapse " : "Expand ") + Entry.Title;

        public string PageText => Entry.PageLabel is { } label ? "p. " + label : string.Empty;

        public Thickness Indent => new(Entry.Level * 14, 0, 0, 0);

        public FontAttributes Attributes => (Entry.IsBold ? FontAttributes.Bold : FontAttributes.None) | (Entry.IsItalic ? FontAttributes.Italic : FontAttributes.None);

        // The file's color for the item (Table 151); black, the default, keeps the theme's own text color.
        public Color? Color => Entry.Color.Red + Entry.Color.Green + Entry.Color.Blue > 0
            ? new Color((float)Entry.Color.Red, (float)Entry.Color.Green, (float)Entry.Color.Blue)
            : null;

        public Color Highlight => isCurrent ? new Color(0.2f, 0.48f, 1f, 0.15f) : Colors.Transparent;
    }
}
