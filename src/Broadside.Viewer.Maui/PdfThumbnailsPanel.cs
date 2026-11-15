using System.ComponentModel;
using System.Globalization;
using Broadside.Rendering;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui;

/// <summary>
/// Page thumbnails of a <see cref="PdfView"/>'s document in a grid, each with its page label; choosing one shows that page. The
/// thumbnails come from the viewer core's <see cref="PageThumbnails"/>: each page is replayed from the display list in the session's
/// shared cache (never interpreted twice) after the view's own tiles, starting at the page in view. They are drawn only while the panel
/// is on screen, with the view's color filter, and drawn again when it changes.
/// </summary>
/// <remarks>ISO 32000-2 §14.11.2 (the page's displayed region), §12.4.2 (page labels). Spec #33 viewer capability 10.</remarks>
#pragma warning disable CA1001 // the token source is cancelled and disposed when the panel leaves the screen or the document changes
public sealed class PdfThumbnailsPanel : ContentView
#pragma warning restore CA1001
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfThumbnailsPanel), null, propertyChanged: (b, o, n) => ((PdfThumbnailsPanel)b).Attach((PdfView?)o, (PdfView?)n));

    private const int MaxPages = 500;
    private const int ThumbnailPixels = 220;
    private readonly ViewerFollower _follower;
    private readonly Label _status = PanelKit.Hint(string.Empty, "thumbnail-status");
    private readonly GridItemsLayout _grid = new(2, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 };
    private readonly CollectionView _pages;
    private List<ThumbnailItem> _items = [];
    private CancellationTokenSource? _work;
    private bool _onScreen;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view it navigates.</summary>
    public PdfThumbnailsPanel()
    {
        _follower = new ViewerFollower(() => _ = ReloadAsync(), MarkCurrent);
        _pages = new CollectionView { ItemsLayout = _grid, SelectionMode = SelectionMode.Single, AutomationId = "thumbnails" };
        _pages.ItemTemplate = new DataTemplate(() =>
        {
            var image = new Image { HeightRequest = 150, Aspect = Aspect.AspectFit, BackgroundColor = Color.FromRgb(0xE8, 0xE8, 0xEC) };
            image.SetBinding(Image.SourceProperty, static (ThumbnailItem t) => t.Image);
            var label = new Label { FontSize = 12, HorizontalTextAlignment = TextAlignment.Center };
            label.SetBinding(Label.TextProperty, static (ThumbnailItem t) => t.Label);
            label.SetBinding(Label.FontAttributesProperty, static (ThumbnailItem t) => t.LabelAttributes);
            var cell = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(2), AutomationId = "thumbnail", Children = { image, label } };
            cell.SetBinding(SemanticProperties.DescriptionProperty, static (ThumbnailItem t) => t.Description);
            return cell;
        });
        _pages.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is ThumbnailItem item)
            {
                _pages.SelectedItem = null;
                _follower.View?.GoToPage(item.PageIndex);
            }
        };
        Loaded += (_, _) =>
        {
            _onScreen = true;
            _ = RenderAsync();
        };
        Unloaded += (_, _) =>
        {
            _onScreen = false;
            Cancel();
        };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 4 };
        root.Add(_status);
        root.Add(_pages, 0, 1);
        Content = root;
    }

    /// <summary>Gets or sets the view this panel navigates.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets how many thumbnails have been drawn for the open document.</summary>
    public int RenderedCount => _items.Count(item => item.Image is not null);

    /// <summary>Gets how many pages the panel lists.</summary>
    public int PageCount => _items.Count;

    /// <summary>Shows a page, as a tap on its thumbnail does.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    public void Activate(int pageIndex) => _follower.View?.GoToPage(pageIndex);

    /// <inheritdoc/>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        int span = Math.Max(2, (int)(width / 130));
        if (width > 0 && span != _grid.Span)
        {
            _grid.Span = span;
        }
    }

    private void Attach(PdfView? old, PdfView? view)
    {
        old?.PropertyChanged -= OnViewPropertyChanged;
        view?.PropertyChanged += OnViewPropertyChanged;
        _follower.Attach(view);
    }

    // The thumbnails are drawn with the session's color filter: a change draws them again.
    private void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == PdfView.ColorFilterProperty.PropertyName)
        {
            foreach (ThumbnailItem item in _items)
            {
                item.Image = null;
            }

            // After the view applied the filter to the session (its own property callback runs after this event).
            if (Dispatcher is { } dispatcher)
            {
                dispatcher.Dispatch(() => _ = RenderAsync());
            }
            else
            {
                _ = RenderAsync();
            }
        }
    }

    private async Task ReloadAsync()
    {
        Cancel();
        _items = [];
        _pages.ItemsSource = null;
        if (_follower.View?.CurrentSession is not { } session)
        {
            _status.Text = "No document.";
            return;
        }

        int count = Math.Min(session.PageCount, MaxPages);
        _items = [.. Enumerable.Range(0, count).Select(page => new ThumbnailItem(page, (page + 1).ToString(CultureInfo.InvariantCulture)))];
        _status.Text = session.PageCount > MaxPages
            ? string.Create(CultureInfo.InvariantCulture, $"The first {MaxPages} of {session.PageCount} pages.")
            : string.Create(CultureInfo.InvariantCulture, $"{session.PageCount} page(s)");
        MarkCurrent();
        _pages.ItemsSource = _items;
        List<ThumbnailItem> items = _items;
        try
        {
            PageLabelTable labels = await PageLabelTable.LoadAsync(session).ConfigureAwait(true);
            if (ReferenceEquals(items, _items) && labels.HasLabels)
            {
                foreach (ThumbnailItem item in items)
                {
                    item.Label = item.PageIndex < labels.Count ? labels.GetLabel(item.PageIndex) : item.Label;
                }
            }
        }
#pragma warning disable CA1031 // page numbers stand in for labels that cannot be read
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        await RenderAsync().ConfigureAwait(true);
    }

    private void MarkCurrent()
    {
        int current = _follower.CurrentPage;
        foreach (ThumbnailItem item in _items)
        {
            item.IsCurrent = item.PageIndex == current;
        }
    }

    // Draws the missing thumbnails, nearest the page in view first, while the panel is on screen.
    private async Task RenderAsync()
    {
        Cancel();
        if (!_onScreen || _follower.View?.CurrentSession is not { } session || _items.Count == 0)
        {
            return;
        }

        var work = new CancellationTokenSource();
        _work = work;
        List<ThumbnailItem> items = _items;
        int first = Math.Min(_follower.CurrentPage, items.Count - 1);
        IEnumerable<int> order = Enumerable.Range(0, items.Count)
            .Select(n => (first + n) % items.Count)
            .Where(page => items[page].Image is null);
        var surface = new ThumbnailSurface(this, items);
        var thumbnails = new PageThumbnails(session, surface, new ThumbnailOptions().WithSize(ThumbnailPixels));
        try
        {
            await thumbnails.RenderAsync([.. order], work.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
#pragma warning disable CA1031 // a page that cannot be drawn keeps its placeholder
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            if (ReferenceEquals(_work, work))
            {
                _work = null;
            }

            work.Dispose();
        }
    }

    private void Cancel()
    {
        if (_work is { } work)
        {
            _work = null;
            work.Cancel();
        }
    }

    /// <summary>Receives finished thumbnails (on a worker in a background session) and hands PNG copies to the UI thread.</summary>
    private sealed class ThumbnailSurface(PdfThumbnailsPanel panel, List<ThumbnailItem> items) : IRenderSurface
    {
        public void Present(Tile tile, Bitmap pixels)
        {
            using var stream = new MemoryStream();
            pixels.SavePng(stream);
            byte[] png = stream.ToArray();
            void Apply()
            {
                if ((uint)tile.PageIndex < (uint)items.Count)
                {
                    items[tile.PageIndex].Image = ImageSource.FromStream(() => new MemoryStream(png));
                }
            }

            if (panel.Dispatcher is { } dispatcher && dispatcher.IsDispatchRequired)
            {
                dispatcher.Dispatch(Apply);
            }
            else
            {
                Apply();
            }
        }
    }

    /// <summary>One page of the grid.</summary>
    internal sealed class ThumbnailItem(int pageIndex, string label) : INotifyPropertyChanged
    {
        private ImageSource? _image;
        private string _label = label;
        private bool _isCurrent;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int PageIndex { get; } = pageIndex;

        public ImageSource? Image
        {
            get => _image;
            set
            {
                _image = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
            }
        }

        public string Label
        {
            get => _label;
            set
            {
                _label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
            }
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent != value)
                {
                    _isCurrent = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelAttributes)));
                }
            }
        }

        public FontAttributes LabelAttributes => _isCurrent ? FontAttributes.Bold : FontAttributes.None;

        public string Description => "Page " + _label;
    }
}
