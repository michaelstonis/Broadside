using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Broadside.Viewer.Wpf;

/// <summary>The side panels' controls: WPF's own, with automation names and IDs (the UI tests find them by those).</summary>
internal static class SidebarKit
{
    private static readonly Brush HintBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x73)));

    public static FrameworkElement Text(string text, SidebarTextStyle style = SidebarTextStyle.Normal, double indent = 0, string? automationId = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indent, style == SidebarTextStyle.Heading ? 6 : 0, 0, 0),
            FontWeight = style == SidebarTextStyle.Heading ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = style == SidebarTextStyle.Label ? FontStyles.Italic : FontStyles.Normal,
        };
        if (style == SidebarTextStyle.Hint)
        {
            block.Foreground = HintBrush;
            block.FontSize = 11;
        }

        if (automationId is not null)
        {
            AutomationProperties.SetAutomationId(block, automationId);
        }

        return block;
    }

    public static void SetText(FrameworkElement element, string text) => ((TextBlock)element).Text = text;

    public static FrameworkElement Button(string text, string automationId, Action click, string? name = null, bool selected = false, bool small = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalAlignment = small ? HorizontalAlignment.Left : HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
            Padding = small ? new Thickness(4, 1, 4, 1) : new Thickness(6, 3, 6, 3),
            MinWidth = small ? 24 : 0,
        };
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, name ?? text);
        button.Click += (_, _) => click();
        return button;
    }

    public static FrameworkElement Check(string text, string automationId, bool isChecked, bool isEnabled, Action<bool> changed, double indent)
    {
        var box = new CheckBox { Content = text, IsChecked = isChecked, IsEnabled = isEnabled, Margin = new Thickness(indent, 0, 0, 0) };
        AutomationProperties.SetAutomationId(box, automationId);
        AutomationProperties.SetName(box, text);
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    public static FrameworkElement Row(double indent, params FrameworkElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent, 0, 0, 0) };
        foreach (FrameworkElement child in children)
        {
            child.Margin = new Thickness(child.Margin.Left, child.Margin.Top, 4, child.Margin.Bottom);
            row.Children.Add(child);
        }

        return row;
    }

    public static FrameworkElement Stack(params FrameworkElement[] children)
    {
        var stack = new StackPanel();
        foreach (FrameworkElement child in children)
        {
            stack.Children.Add(child);
        }

        return stack;
    }

    /// <summary>A fixed top and a part that fills the rest.</summary>
    public static FrameworkElement Docked(FrameworkElement top, FrameworkElement fill)
    {
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(fill);
        return dock;
    }

    public static SidebarThumbnailSlot Thumbnail(string label, Action click) => new(label, click);

    public static SidebarQueryBox QueryBox(string automationId, string placeholder, Action<string> submit) => new(automationId, placeholder, submit);

    public static SidebarTabs Tabs(IReadOnlyList<(string Name, string AutomationId, FrameworkElement Content)> tabs) => new(tabs);

    /// <summary>A thumbnail as a frozen image the size of its pixels (premultiplied BGRA, the rasterizer's own layout).</summary>
    public static BitmapSource Image(SidebarThumbnail thumbnail)
    {
        BitmapSource image = BitmapSource.Create(thumbnail.Width, thumbnail.Height, 96, 96, PixelFormats.Pbgra32, null, thumbnail.Pixels.ToArray(), thumbnail.Width * 4);
        image.Freeze();
        return image;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>A scrolling list of rows.</summary>
internal sealed class SidebarList
{
    private readonly StackPanel _rows = new();

    public SidebarList() => Root = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public FrameworkElement Root { get; }

    public int Count => _rows.Children.Count;

    public void Clear() => _rows.Children.Clear();

    public void Add(FrameworkElement row) => _rows.Children.Add(row);
}

/// <summary>One page of the thumbnails panel: its image once drawn, and its label; the page in view is bold.</summary>
internal sealed class SidebarThumbnailSlot
{
    private readonly Image _image = new() { Height = 120, Stretch = Stretch.Uniform };
    private readonly TextBlock _label;

    public SidebarThumbnailSlot(string label, Action click)
    {
        _label = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { Children = { _image, _label } };
        var button = new Button { Content = stack, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetAutomationId(button, "thumbnail");
        AutomationProperties.SetName(button, "Page " + label);
        button.Click += (_, _) => click();
        Element = button;
    }

    public FrameworkElement Element { get; }

    public bool HasImage => _image.Source is not null;

    public void SetImage(SidebarThumbnail thumbnail) => _image.Source = SidebarKit.Image(thumbnail);

    public void SetCurrent(bool current) => _label.FontWeight = current ? FontWeights.Bold : FontWeights.Normal;
}

/// <summary>A text box that submits on Enter and with a Find button.</summary>
internal sealed class SidebarQueryBox
{
    private readonly TextBox _box;

    public SidebarQueryBox(string automationId, string placeholder, Action<string> submit)
    {
        _box = new TextBox { ToolTip = placeholder };
        AutomationProperties.SetAutomationId(_box, automationId);
        AutomationProperties.SetName(_box, placeholder);
        _box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                submit(Text);
                e.Handled = true;
            }
        };
        FrameworkElement find = SidebarKit.Button("Find", automationId + "-go", () => submit(Text), small: true);
        var dock = new DockPanel();
        DockPanel.SetDock(find, Dock.Right);
        dock.Children.Add(find);
        dock.Children.Add(_box);
        Element = dock;
    }

    public FrameworkElement Element { get; }

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
    private readonly IReadOnlyList<(string Name, string AutomationId, FrameworkElement Content)> _tabs;
    private readonly ContentControl _host = new();
    private int _selected = -1;

    public SidebarTabs(IReadOnlyList<(string Name, string AutomationId, FrameworkElement Content)> tabs)
    {
        _tabs = tabs;
        var strip = new WrapPanel();
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var button = new ToggleButton { Content = tabs[i].Name, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 2, 6, 2) };
            AutomationProperties.SetAutomationId(button, tabs[i].AutomationId);
            AutomationProperties.SetName(button, tabs[i].Name);
            button.Click += (_, _) => Selected = index;
            _buttons.Add(button);
            strip.Children.Add(button);
        }

        Element = SidebarKit.Docked(strip, _host);
        Selected = 0;
    }

    public FrameworkElement Element { get; }

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
