using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// A toolbar for a <see cref="PdfView"/> (or any <see cref="IDocumentViewer"/>): the shared <see cref="ViewerToolbar"/> model laid out
/// in a WinUI <see cref="CommandBar"/>: buttons and toggles as app bar buttons, the pickers, the page box and find in element
/// containers; overflow commands go to the bar's secondary ("more") menu.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature, except the page box (ISO 32000-2 §12.4.2, page labels) and the default visibility (§12.2, Table 147,
/// <c>HideToolbar</c>). The view itself stays toolbar-free: an app that wants none pays nothing.
/// </para>
/// <para>
/// Set <see cref="Viewer"/> to bind it; <see cref="Toolbar"/> is the model, where an app adds, removes, reorders or hides commands.
/// <see cref="ItemTemplateSelector"/> restyles items: a template it picks for a command is shown with the command as content (a
/// command raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>, and a button command is an
/// <see cref="System.Windows.Input.ICommand"/>); the others keep the default controls.
/// </para>
/// </remarks>
public sealed partial class PdfToolbar : UserControl, IDisposable
{
    /// <summary>Identifies the <see cref="Viewer"/> property.</summary>
    public static readonly DependencyProperty ViewerProperty =
        DependencyProperty.Register(nameof(Viewer), typeof(object), typeof(PdfToolbar), new PropertyMetadata(null, (d, e) => ((PdfToolbar)d).Bind((IDocumentViewer?)e.NewValue)));

    /// <summary>Identifies the <see cref="Options"/> property.</summary>
    public static readonly DependencyProperty OptionsProperty =
        DependencyProperty.Register(nameof(Options), typeof(object), typeof(PdfToolbar), new PropertyMetadata(null, (d, _) => ((PdfToolbar)d).Bind(((PdfToolbar)d).Viewer)));

    /// <summary>Identifies the <see cref="ItemTemplateSelector"/> property.</summary>
    public static readonly DependencyProperty ItemTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ItemTemplateSelector), typeof(DataTemplateSelector), typeof(PdfToolbar), new PropertyMetadata(null, (d, _) => ((PdfToolbar)d).Rebuild()));

    private readonly CommandBar _bar = new() { DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed, IsDynamicOverflowEnabled = true };
    private readonly List<(ViewerCommand Command, EventHandler Handler)> _subscriptions = [];
    private string _layout = string.Empty;

    /// <summary>Creates the toolbar; set <see cref="Viewer"/> to bind it to a view.</summary>
    public PdfToolbar()
    {
        AutomationProperties.SetName(_bar, "Document toolbar");
        Content = _bar;
    }

    /// <summary>Gets or sets the viewer the toolbar drives (a <see cref="PdfView"/>); a new model is created for each.</summary>
    public IDocumentViewer? Viewer
    {
        get => (IDocumentViewer?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets or sets the options of the model (the commands, their order, the zoom presets, compact); a change creates the model again.</summary>
    public ViewerToolbarOptions? Options
    {
        get => (ViewerToolbarOptions?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Gets or sets a selector of item templates: a template it returns for a <see cref="ViewerCommand"/> replaces the default control.</summary>
    public DataTemplateSelector? ItemTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ItemTemplateSelectorProperty);
        set => SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>Gets the model bound to <see cref="Viewer"/>, or <see langword="null"/> without a viewer.</summary>
    public ViewerToolbar? Toolbar { get; private set; }

    /// <summary>Unbinds the toolbar from its viewer.</summary>
    public void Dispose() => Viewer = null;

    private void Bind(IDocumentViewer? viewer)
    {
        if (Toolbar is { } old)
        {
            old.Changed -= OnToolbarChanged;
            old.Dispose();
        }

        Toolbar = viewer is null ? null : new ViewerToolbar(viewer, Options);
        Toolbar?.Changed += OnToolbarChanged;
        Rebuild();
    }

    private void OnToolbarChanged(object? sender, EventArgs e)
    {
        if (LayoutKey() != _layout)
        {
            Rebuild();
        }
    }

    // What decides the controls: which commands, where, and whether the bar shows. Item state alone updates the controls in place.
    private string LayoutKey() =>
        Toolbar is not { } toolbar ? string.Empty : string.Join('|', toolbar.Items.Select(item => item.Id + ":" + (int)item.Placement)) + "|" + toolbar.IsVisible;

    private void Rebuild()
    {
        foreach ((ViewerCommand command, EventHandler handler) in _subscriptions)
        {
            command.Changed -= handler;
        }

        _subscriptions.Clear();
        _bar.PrimaryCommands.Clear();
        _bar.SecondaryCommands.Clear();
        _layout = LayoutKey();
        if (Toolbar is not { } toolbar)
        {
            return;
        }

        _bar.Visibility = toolbar.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        foreach (ViewerCommand command in toolbar.Items)
        {
            if (command.Placement == ViewerCommandPlacement.Hidden)
            {
                continue;
            }

            ICommandBarElement element = ItemTemplateSelector?.SelectTemplate(command) is { } template
                ? new AppBarElementContainer { Content = new ContentPresenter { Content = command, ContentTemplate = template } }
                : CreateDefault(command);
            var control = (FrameworkElement)element;
            AutomationProperties.SetAutomationId(control, command.Id);
            AutomationProperties.SetName(control, command.Label);
            if (command.ToolTip is { } tip)
            {
                ToolTipService.SetToolTip(control, command.Shortcut is { } shortcut ? $"{tip} ({shortcut})" : tip);
            }

            (command.Placement == ViewerCommandPlacement.Overflow ? _bar.SecondaryCommands : _bar.PrimaryCommands).Add(element);
        }
    }

    private ICommandBarElement CreateDefault(ViewerCommand command) => command switch
    {
        ViewerButtonCommand button => CreateButton(button),
        ViewerToggleCommand toggle => CreateToggle(toggle),
        ViewerChoiceCommand choice => new AppBarElementContainer { Content = CreateChoice(choice), VerticalContentAlignment = VerticalAlignment.Center },
        ViewerPageCommand page => new AppBarElementContainer { Content = CreatePageBox(page), VerticalContentAlignment = VerticalAlignment.Center },
        ViewerSearchCommand search => new AppBarElementContainer { Content = CreateFindBox(search), VerticalContentAlignment = VerticalAlignment.Center },
        _ => new AppBarElementContainer { Content = new TextBlock { Text = command.Label } },
    };

    private void Follow(ViewerCommand command, Action update)
    {
        EventHandler handler = (_, _) => update();
        command.Changed += handler;
        _subscriptions.Add((command, handler));
        update();
    }

    private static FontIcon Icon(ViewerCommand command) =>
        new() { Glyph = command.Glyph ?? command.Label[..1], FontFamily = new FontFamily("Segoe UI") };

    private AppBarButton CreateButton(ViewerButtonCommand command)
    {
        var button = new AppBarButton { Label = command.Label, Icon = Icon(command) };
        button.Click += (_, _) => command.Execute();
        Follow(command, () => button.IsEnabled = command.IsEnabled);
        return button;
    }

    private AppBarToggleButton CreateToggle(ViewerToggleCommand command)
    {
        var toggle = new AppBarToggleButton { Label = command.Label, Icon = Icon(command) };
        bool updating = false;
        toggle.Click += (_, _) =>
        {
            if (!updating)
            {
                command.IsChecked = toggle.IsChecked == true;
            }
        };
        Follow(command, () =>
        {
            updating = true;
            toggle.IsEnabled = command.IsEnabled;
            toggle.IsChecked = command.IsChecked;
            updating = false;
        });
        return toggle;
    }

    private ComboBox CreateChoice(ViewerChoiceCommand command)
    {
        var box = new ComboBox { ItemsSource = command.Choices, MinWidth = 120 };
        AutomationProperties.SetName(box, command.Label);
        bool updating = false;
        box.SelectionChanged += (_, _) =>
        {
            if (!updating && box.SelectedIndex >= 0)
            {
                command.Selected = box.SelectedIndex;
            }
        };
        Follow(command, () =>
        {
            updating = true;
            box.IsEnabled = command.IsEnabled;
            box.SelectedIndex = command.Selected;
            box.PlaceholderText = command.Text.Length > 0 ? command.Text : command.Label;
            updating = false;
        });
        return box;
    }

    private StackPanel CreatePageBox(ViewerPageCommand command)
    {
        var box = new TextBox { MinWidth = 56, TextAlignment = TextAlignment.Center };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, command.Label);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                if (!command.TrySubmit(box.Text))
                {
                    box.Text = command.Text;
                    box.SelectAll();
                }
            }
            else if (e.Key == VirtualKey.Escape)
            {
                box.Text = command.Text;
            }
        };
        box.LostFocus += (_, _) => box.Text = command.Text;
        Follow(command, () =>
        {
            box.IsEnabled = command.IsEnabled;
            if (box.FocusState == FocusState.Unfocused)
            {
                box.Text = command.Text;
            }

            count.Text = command.PageCount > 0 ? $"/ {command.PageCount}" : string.Empty;
            ToolTipService.SetToolTip(box, command.Error ?? command.ToolTip);
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { box, count } };
    }

    private StackPanel CreateFindBox(ViewerSearchCommand command)
    {
        var box = new TextBox { MinWidth = 160, PlaceholderText = command.Label };
        AutomationProperties.SetName(box, command.Label);
        var previous = new Button { Content = "‹" };
        var next = new Button { Content = "›" };
        AutomationProperties.SetName(previous, "Previous match");
        AutomationProperties.SetName(next, "Next match");
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            if (box.Text.Length > 0 && string.Equals(box.Text, command.Query, StringComparison.Ordinal))
            {
                command.Next();
            }
            else
            {
                command.Submit(box.Text);
            }
        };
        previous.Click += (_, _) => command.Previous();
        next.Click += (_, _) => command.Next();
        Follow(command, () =>
        {
            box.IsEnabled = previous.IsEnabled = next.IsEnabled = command.IsEnabled;
            status.Text = command.Status;
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { box, previous, next, status } };
    }
}
