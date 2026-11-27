using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Broadside.Viewer.Avalonia;

/// <summary>The side panels' controls: Avalonia's own, with automation names and IDs (the UI tests find them by those).</summary>
internal static class SidebarKit
{
    private static readonly IBrush HintBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x73));

    public static Control Text(string text, SidebarTextStyle style = SidebarTextStyle.Normal, double indent = 0, string? automationId = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indent, style == SidebarTextStyle.Heading ? 6 : 0, 0, 0),
            FontWeight = style == SidebarTextStyle.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            FontStyle = style == SidebarTextStyle.Label ? FontStyle.Italic : FontStyle.Normal,
        };
        if (style == SidebarTextStyle.Hint)
        {
            block.Foreground = HintBrush;
            block.FontSize = 12;
        }

        if (automationId is not null)
        {
            AutomationProperties.SetAutomationId(block, automationId);
        }

        return block;
    }

    public static void SetText(Control element, string text) => ((TextBlock)element).Text = text;

    public static Control Button(string text, string automationId, Action click, string? name = null, bool selected = false, bool small = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalAlignment = small ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal,
            Padding = small ? new Thickness(4, 2) : new Thickness(8, 4),
            MinWidth = small ? 28 : 0,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name ?? text);
        button.Click += (_, _) => click();
        return button;
    }

    public static Control Check(string text, string automationId, bool isChecked, bool isEnabled, Action<bool> changed, double indent)
    {
        var box = new CheckBox { Content = text, IsChecked = isChecked, IsEnabled = isEnabled, Margin = new Thickness(indent, 0, 0, 0) };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, text);
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    public static Control Row(double indent, params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(indent, 0, 0, 0) };
        row.Children.AddRange(children);
        return row;
    }

    public static Control Stack(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.AddRange(children);
        return stack;
    }

    /// <summary>A fixed top and a part that fills the rest.</summary>
    public static Control Docked(Control top, Control fill)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(fill);
        return dock;
    }

    public static SidebarThumbnailSlot Thumbnail(string label, Action click) => new(label, click);

    public static SidebarQueryBox QueryBox(string automationId, string placeholder, Action<string> submit) => new(automationId, placeholder, submit);

    public static SidebarTabs Tabs(IReadOnlyList<(string Name, string AutomationId, Control Content)> tabs) => new(tabs);

    /// <summary>A thumbnail as an image the size of its pixels (premultiplied BGRA, the rasterizer's own layout).</summary>
    public static unsafe WriteableBitmap Image(SidebarThumbnail thumbnail)
    {
        var image = new WriteableBitmap(new PixelSize(thumbnail.Width, thumbnail.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer target = image.Lock();
        int rowBytes = thumbnail.Width * 4;
        ReadOnlySpan<byte> pixels = thumbnail.Pixels.Span;
        for (int y = 0; y < thumbnail.Height; y++)
        {
            pixels.Slice(y * rowBytes, rowBytes).CopyTo(new Span<byte>((byte*)target.Address + ((long)y * target.RowBytes), rowBytes));
        }

        return image;
    }
}

/// <summary>A scrolling list of rows.</summary>
internal sealed class SidebarList
{
    private readonly StackPanel _rows = new() { Spacing = 2 };

    public SidebarList() => Root = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    public Control Root { get; }

    public int Count => _rows.Children.Count;

    public void Clear() => _rows.Children.Clear();

    public void Add(Control row) => _rows.Children.Add(row);
}

/// <summary>One page of the thumbnails panel: its image once drawn, and its label; the page in view is bold.</summary>
internal sealed class SidebarThumbnailSlot
{
    private readonly Image _image = new() { Height = 120, Stretch = Stretch.Uniform };
    private readonly TextBlock _label;

    public SidebarThumbnailSlot(string label, Action click)
    {
        _label = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { Spacing = 2, Children = { _image, _label } };
        var button = new Button { Content = stack, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(button, "thumbnail");
        AutomationProperties.SetName(button, "Page " + label);
        button.Click += (_, _) => click();
        Element = button;
    }

    public Control Element { get; }

    public bool HasImage => _image.Source is not null;

    public void SetImage(SidebarThumbnail thumbnail)
    {
        (_image.Source as IDisposable)?.Dispose();
        _image.Source = SidebarKit.Image(thumbnail);
    }

    public void SetCurrent(bool current) => _label.FontWeight = current ? FontWeight.Bold : FontWeight.Normal;
}

/// <summary>A text box that submits on Enter and with a Find button.</summary>
internal sealed class SidebarQueryBox
{
    private readonly TextBox _box;

    public SidebarQueryBox(string automationId, string placeholder, Action<string> submit)
    {
        _box = new TextBox { PlaceholderText = placeholder };
        AutomationProperties.SetAutomationId(_box, automationId);
        AutomationProperties.SetName(_box, placeholder);
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                submit(Text);
                e.Handled = true;
            }
        };
        var find = (Button)SidebarKit.Button("Find", automationId + "-go", () => submit(Text), small: true);
        var dock = new DockPanel();
        DockPanel.SetDock(find, Dock.Right);
        dock.Children.Add(find);
        dock.Children.Add(_box);
        Element = dock;
    }

    public Control Element { get; }

    public string Text
    {
        get => _box.Text ?? string.Empty;
        set => _box.Text = value;
    }
}

/// <summary>A row of tab buttons over the content of the selected one.</summary>
internal sealed class SidebarTabs
{
    private readonly List<ToggleButton> _buttons = [];
    private readonly IReadOnlyList<(string Name, string AutomationId, Control Content)> _tabs;
    private readonly ContentControl _host = new();
    private int _selected = -1;

    public SidebarTabs(IReadOnlyList<(string Name, string AutomationId, Control Content)> tabs)
    {
        _tabs = tabs;
        var strip = new WrapPanel();
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var button = new ToggleButton { Content = tabs[i].Name, Margin = new Thickness(0, 0, 4, 4) };
            AutomationProperties.SetAutomationId(button, tabs[i].AutomationId);
            AutomationProperties.SetName(button, tabs[i].Name);
            button.Click += (_, _) => Selected = index;
            _buttons.Add(button);
            strip.Children.Add(button);
        }

        Element = SidebarKit.Docked(strip, _host);
        Selected = 0;
    }

    public Control Element { get; }

    public int Selected
    {
        get => _selected;
        set
        {
            _selected = Math.Clamp(value, 0, _tabs.Count - 1);
            _host.Content = _tabs[_selected].Content;
            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].IsChecked = i == _selected;
            }
        }
    }
}
