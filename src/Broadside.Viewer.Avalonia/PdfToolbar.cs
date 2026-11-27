using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Broadside.Viewer.Avalonia;

/// <summary>
/// A toolbar for a <see cref="PdfView"/> (or any <see cref="IDocumentViewer"/>): the shared <see cref="ViewerToolbar"/> model laid out
/// with Avalonia's own buttons, toggle buttons, combo boxes and text boxes, in a row with an overflow ("⋯") flyout.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature, except the page box (ISO 32000-2 §12.4.2, page labels) and the default visibility (§12.2, Table 147,
/// <c>HideToolbar</c>). The view itself stays toolbar-free: an app that wants none pays nothing.
/// </para>
/// <para>
/// Set <see cref="Viewer"/> to bind it; <see cref="Toolbar"/> is the model, where an app adds, removes, reorders or hides commands.
/// <see cref="ItemTemplate"/> restyles items: a template that matches a command builds its control (its data context is the command,
/// which raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>); the others keep the default controls.
/// </para>
/// </remarks>
public sealed class PdfToolbar : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfToolbar, IDocumentViewer?>(nameof(Viewer));

    /// <summary>The <see cref="Options"/> property.</summary>
    public static readonly StyledProperty<ViewerToolbarOptions?> OptionsProperty = AvaloniaProperty.Register<PdfToolbar, ViewerToolbarOptions?>(nameof(Options));

    /// <summary>The <see cref="ItemTemplate"/> property.</summary>
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<PdfToolbar, IDataTemplate?>(nameof(ItemTemplate));

    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _overflowPanel = new() { Orientation = Orientation.Vertical, Spacing = 4 };
    private readonly Button _more;
    private readonly List<(ViewerCommand Command, EventHandler Handler)> _subscriptions = [];
    private string _layout = string.Empty;

    /// <summary>Creates the toolbar; set <see cref="Viewer"/> to bind it to a view.</summary>
    public PdfToolbar()
    {
        _more = new Button { Content = "⋯", Flyout = new Flyout { Content = _overflowPanel } };
        ToolTip.SetTip(_more, "More");
        AutomationProperties.SetName(_more, "More");
        AutomationProperties.SetAutomationId(_more, "toolbar.more");
        AutomationProperties.SetControlTypeOverride(this, global::Avalonia.Automation.Peers.AutomationControlType.ToolBar);
        Content = _bar;
    }

    /// <summary>Gets or sets the viewer the toolbar drives (a <see cref="PdfView"/>); a new model is created for each.</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets or sets the options of the model (the commands, their order, the zoom presets, compact); a change creates the model again.</summary>
    public ViewerToolbarOptions? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Gets or sets a template for items: one that matches a <see cref="ViewerCommand"/> builds its control instead of the default.</summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Gets the model bound to <see cref="Viewer"/>, or <see langword="null"/> without a viewer.</summary>
    public ViewerToolbar? Toolbar { get; private set; }

    /// <summary>Unbinds the toolbar from its viewer.</summary>
    public void Dispose() => Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty || change.Property == OptionsProperty)
        {
            Bind(Viewer);
        }
        else if (change.Property == ItemTemplateProperty)
        {
            Rebuild();
        }
    }

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
        _bar.Children.Clear();
        _overflowPanel.Children.Clear();
        _layout = LayoutKey();
        if (Toolbar is not { } toolbar)
        {
            return;
        }

        _bar.IsVisible = toolbar.IsVisible;
        foreach (ViewerCommand command in toolbar.Items)
        {
            if (command.Placement == ViewerCommandPlacement.Hidden)
            {
                continue;
            }

            Control control = ItemTemplate is { } template && template.Match(command) && template.Build(command) is { } built
                ? WithContext(built, command)
                : CreateDefault(command);
            (command.Placement == ViewerCommandPlacement.Overflow ? _overflowPanel : _bar).Children.Add(control);
        }

        if (_overflowPanel.Children.Count > 0)
        {
            _bar.Children.Add(_more);
        }
    }

    private static Control WithContext(Control control, ViewerCommand command)
    {
        control.DataContext = command;
        return control;
    }

    private Control CreateDefault(ViewerCommand command)
    {
        Control control = command switch
        {
            ViewerButtonCommand button => CreateButton(button),
            ViewerToggleCommand toggle => CreateToggle(toggle),
            ViewerChoiceCommand choice => CreateChoice(choice),
            ViewerPageCommand page => CreatePageBox(page),
            ViewerSearchCommand search => CreateFindBox(search),
            _ => new TextBlock { Text = command.Label },
        };
        AutomationProperties.SetAutomationId(control, command.Id);
        AutomationProperties.SetName(control, command.Label);
        if (command.ToolTip is { } tip)
        {
            ToolTip.SetTip(control, command.Shortcut is { } shortcut ? $"{tip} ({shortcut})" : tip);
        }

        return control;
    }

    private void Follow(ViewerCommand command, Action update)
    {
        EventHandler handler = (_, _) => update();
        command.Changed += handler;
        _subscriptions.Add((command, handler));
        update();
    }

    private Button CreateButton(ViewerButtonCommand command)
    {
        var button = new Button { Content = command.Glyph ?? command.Label, MinWidth = 32 };
        button.Click += (_, _) => command.Execute();
        Follow(command, () => button.IsEnabled = command.IsEnabled);
        return button;
    }

    private ToggleButton CreateToggle(ViewerToggleCommand command)
    {
        var toggle = new ToggleButton { Content = command.Glyph ?? command.Label, MinWidth = 32 };
        bool updating = false;
        toggle.IsCheckedChanged += (_, _) =>
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
        var box = new ComboBox { ItemsSource = command.Choices, MinWidth = 96 };
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
        var box = new TextBox { MinWidth = 48, HorizontalContentAlignment = HorizontalAlignment.Center };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, command.Label);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (!command.TrySubmit(box.Text ?? string.Empty))
                {
                    box.Text = command.Text;
                    box.SelectAll();
                }
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = command.Text;
            }
        };
        box.LostFocus += (_, _) => box.Text = command.Text;
        Follow(command, () =>
        {
            box.IsEnabled = command.IsEnabled;
            if (!box.IsFocused)
            {
                box.Text = command.Text;
            }

            count.Text = command.PageCount > 0 ? $"/ {command.PageCount}" : string.Empty;
            ToolTip.SetTip(box, command.Error ?? command.ToolTip);
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { box, count } };
    }

    private StackPanel CreateFindBox(ViewerSearchCommand command)
    {
        var box = new TextBox { MinWidth = 140, PlaceholderText = command.Label };
        AutomationProperties.SetName(box, command.Label);
        var previous = new Button { Content = "‹" };
        var next = new Button { Content = "›" };
        AutomationProperties.SetName(previous, "Previous match");
        AutomationProperties.SetName(next, "Next match");
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            string text = box.Text ?? string.Empty;
            if (text.Length > 0 && string.Equals(text, command.Query, StringComparison.Ordinal))
            {
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    command.Previous();
                }
                else
                {
                    command.Next();
                }
            }
            else
            {
                command.Submit(text);
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
