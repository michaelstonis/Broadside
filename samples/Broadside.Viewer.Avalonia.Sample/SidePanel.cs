using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Broadside.Rendering;
using AvaloniaPixelSize = Avalonia.PixelSize;
using TileBitmap = Broadside.Rendering.Bitmap;

namespace Broadside.Viewer.Avalonia.Sample;

/// <summary>
/// The sample's side panel: the document outline (bookmarks) and page thumbnails labeled with the page labels, both from the viewer
/// core (<see cref="DocumentOutline"/>, <see cref="PageThumbnails"/>, <see cref="PageLabelTable"/>). Choosing an entry shows it in the viewer.
/// </summary>
internal sealed class SidePanel : TabControl, IDisposable
{
    private readonly PdfView _viewer;
    private readonly TreeView _outline = new();
    private readonly ListBox _pages = new();
    private DocumentSession? _session;
    private CancellationTokenSource? _loading;
    private Thumbnail[] _thumbnails = [];
    private bool _selecting;

    public SidePanel(PdfView viewer)
    {
        _viewer = viewer;
        Width = 220;
        _outline.ItemTemplate = new FuncTreeDataTemplate<OutlineEntry>(
            (entry, _) => new TextBlock { Text = entry.Title, FontWeight = entry.IsBold ? FontWeight.Bold : FontWeight.Normal, FontStyle = entry.IsItalic ? FontStyle.Italic : FontStyle.Normal },
            entry => entry.Children);
        _outline.SelectionChanged += (_, _) =>
        {
            if (_outline.SelectedItem is OutlineEntry { Target: { } target })
            {
                _viewer.GoTo(target);
            }
        };
        _pages.ItemTemplate = new FuncDataTemplate<Thumbnail>((thumbnail, _) => new StackPanel
        {
            Margin = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new Image { [!Image.SourceProperty] = new Binding(nameof(Thumbnail.Image)), Width = ThumbnailOptions.DefaultSize, Height = ThumbnailOptions.DefaultSize },
                new TextBlock { Text = thumbnail?.Label, HorizontalAlignment = HorizontalAlignment.Center },
            },
        });
        _pages.SelectionChanged += (_, _) =>
        {
            if (!_selecting && _pages.SelectedIndex >= 0)
            {
                _viewer.GoToPage(_pages.SelectedIndex);
            }
        };
        Items.Add(new TabItem { Header = "Outline", Content = _outline });
        Items.Add(new TabItem { Header = "Pages", Content = _pages });
    }

    public void Dispose()
    {
        _loading?.Cancel();
        _loading?.Dispose();
        _loading = null;
    }

    /// <summary>Follows the viewer: a new document loads its outline, labels and thumbnails; a page change selects its thumbnail.</summary>
    public void Update(PdfViewState state)
    {
        if (!ReferenceEquals(_viewer.CurrentSession, _session))
        {
            _session = _viewer.CurrentSession;
            _ = LoadAsync(_session);
        }

        if (state.PageIndex < _thumbnails.Length && _pages.SelectedIndex != state.PageIndex)
        {
            _selecting = true;
            _pages.SelectedIndex = state.PageIndex;
            _selecting = false;
        }
    }

    private async Task LoadAsync(DocumentSession? session)
    {
        if (_loading is not null)
        {
            await _loading.CancelAsync();
            _loading.Dispose();
        }

        _loading = new CancellationTokenSource();
        CancellationToken token = _loading.Token;
        _outline.ItemsSource = null;
        _pages.ItemsSource = null;
        _thumbnails = [];
        if (session is null || session.PageCount == 0)
        {
            return;
        }

        try
        {
            DocumentOutline outline = await DocumentOutline.LoadAsync(session, token);
            PageLabelTable labels = await PageLabelTable.LoadAsync(session, token);
            _outline.ItemsSource = outline.Roots;
            _thumbnails = [.. Enumerable.Range(0, session.PageCount).Select(page => new Thumbnail(labels.GetLabel(page)))];
            _pages.ItemsSource = _thumbnails;
            var thumbnails = new PageThumbnails(session, new ThumbnailSurface(this, _thumbnails));
            await thumbnails.RenderAsync(Enumerable.Range(0, session.PageCount), token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>One page in the list: its label and, once drawn, its picture.</summary>
    private sealed class Thumbnail(string label) : INotifyPropertyChanged
    {
        private WriteableBitmap? _image;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label { get; } = label;

        public WriteableBitmap? Image
        {
            get => _image;
            set
            {
                _image = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
            }
        }
    }

    /// <summary>Receives thumbnails (on workers in a background session) and hands each to its list item on the UI thread.</summary>
    private sealed class ThumbnailSurface(SidePanel panel, Thumbnail[] items) : IRenderSurface
    {
        public unsafe void Present(Tile tile, TileBitmap pixels)
        {
#pragma warning disable CA2000 // ownership passes to the list item on the UI thread (disposed there when the list moved on)
            var image = new WriteableBitmap(new AvaloniaPixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
#pragma warning restore CA2000
            using (ILockedFramebuffer target = image.Lock())
            {
                for (int y = 0; y < pixels.Height; y++)
                {
                    pixels.GetRow(y)[..(pixels.Width * 4)].CopyTo(new Span<byte>((byte*)target.Address + ((long)y * target.RowBytes), pixels.Width * 4));
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(panel._thumbnails, items) && tile.PageIndex < items.Length)
                {
                    items[tile.PageIndex].Image = image;
                }
                else
                {
                    image.Dispose();
                }
            });
        }
    }
}
