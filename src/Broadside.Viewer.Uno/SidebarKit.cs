using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using Windows.UI.Text;

namespace Broadside.Viewer.Uno;

/// <summary>The side panels' controls: Uno.s own, with automation names and IDs (the UI tests find them by those).</summary>
internal static class SidebarKit
{
    public static FrameworkElement Text(string text, SidebarTextStyle style = SidebarTextStyle.Normal, double indent = 0, string? automationId = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(indent, style == SidebarTextStyle.Heading ? 6 : 0, 0, 0),
            FontWeight = style == SidebarTextStyle.Heading ? FontWeights.SemiBold : FontWeights.Normal,
            FontStyle = style == SidebarTextStyle.Label ? FontStyle.Italic : FontStyle.Normal,
        };
        if (style == SidebarTextStyle.Hint)
        {
            block.Opacity = 0.7;
            block.FontSize = 12;
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
            Padding = small ? new Thickness(4, 2, 4, 2) : new Thickness(8, 4, 8, 4),
            MinWidth = small ? 28 : 0,
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
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(indent, 0, 0, 0) };
        foreach (FrameworkElement child in children)
        {
            row.Children.Add(child);
        }

        return row;
    }

    public static FrameworkElement Stack(params FrameworkElement[] children)
    {
        var stack = new StackPanel { Spacing = 4 };
        foreach (FrameworkElement child in children)
        {
            stack.Children.Add(child);
        }

        return stack;
    }

    /// <summary>A fixed top and a part that fills the rest.</summary>
    public static FrameworkElement Docked(FrameworkElement top, FrameworkElement fill)
    {
        var grid = new Grid { RowSpacing = 4 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(fill, 1);
        grid.Children.Add(top);
        grid.Children.Add(fill);
        return grid;
    }

    public static SidebarThumbnailSlot Thumbnail(string label, Action click) => new(label, click);

    public static SidebarQueryBox QueryBox(string automationId, string placeholder, Action<string> submit) => new(automationId, placeholder, submit);

    public static SidebarTabs Tabs(IReadOnlyList<(string Name, string AutomationId, FrameworkElement Content)> tabs) => new(tabs);

    /// <summary>A thumbnail as an image the size of its pixels (premultiplied BGRA, the rasterizer's own layout). On the UI thread.</summary>
    public static WriteableBitmap Image(SidebarThumbnail thumbnail)
    {
        var bitmap = new WriteableBitmap(thumbnail.Width, thumbnail.Height);
        using (Stream pixels = bitmap.PixelBuffer.AsStream())
        {
            pixels.Write(thumbnail.Pixels.Span);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}

/// <summary>A scrolling list of rows.</summary>
internal sealed class SidebarList
{
    private readonly StackPanel _rows = new() { Spacing = 2 };

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
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(_image);
        stack.Children.Add(_label);
        var button = new Button { Content = stack, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
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
        _box = new TextBox { PlaceholderText = placeholder };
        AutomationProperties.SetAutomationId(_box, automationId);
        AutomationProperties.SetName(_box, placeholder);
        _box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                submit(Text);
                e.Handled = true;
            }
        };
        FrameworkElement find = SidebarKit.Button("Find", automationId + "-go", () => submit(Text), small: true);
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(find, 1);
        grid.Children.Add(_box);
        grid.Children.Add(find);
        Element = grid;
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
    private readonly ContentControl _host = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private int _selected = -1;

    public SidebarTabs(IReadOnlyList<(string Name, string AutomationId, FrameworkElement Content)> tabs)
    {
        _tabs = tabs;
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var button = new ToggleButton { Content = tabs[i].Name };
            AutomationProperties.SetAutomationId(button, tabs[i].AutomationId);
            AutomationProperties.SetName(button, tabs[i].Name);
            button.Click += (_, _) => Selected = index;
            _buttons.Add(button);
            strip.Children.Add(button);
        }

        var scroller = new ScrollViewer
        {
            Content = strip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        Element = SidebarKit.Docked(scroller, _host);
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
