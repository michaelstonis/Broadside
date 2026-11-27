using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Broadside.Viewer.Wpf;

/// <summary>
/// A toolbar for a <see cref="PdfView"/> (or any <see cref="IDocumentViewer"/>): the shared <see cref="ViewerToolbar"/> model laid out
/// in a WPF <see cref="ToolBar"/> with its own buttons, toggle buttons, combo boxes and text boxes; overflow commands go to the
/// tool bar's overflow panel.
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
/// <see cref="ICommand"/>); the others keep the default controls. The <see cref="ToolBar"/> itself is the content, so implicit styles
/// apply to it.
/// </para>
/// </remarks>
public sealed class PdfToolbar : ContentControl, IDisposable
{
    /// <summary>Identifies the <see cref="Viewer"/> property.</summary>
    public static readonly DependencyProperty ViewerProperty = DependencyProperty.Register(
        nameof(Viewer), typeof(IDocumentViewer), typeof(PdfToolbar), new PropertyMetadata(null, (d, e) => ((PdfToolbar)d).Bind((IDocumentViewer?)e.NewValue)));

    /// <summary>Identifies the <see cref="Options"/> property.</summary>
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(ViewerToolbarOptions), typeof(PdfToolbar), new PropertyMetadata(null, (d, _) => ((PdfToolbar)d).Bind(((PdfToolbar)d).Viewer)));

    /// <summary>Identifies the <see cref="ItemTemplateSelector"/> property.</summary>
    public static readonly DependencyProperty ItemTemplateSelectorProperty = DependencyProperty.Register(
        nameof(ItemTemplateSelector), typeof(DataTemplateSelector), typeof(PdfToolbar), new PropertyMetadata(null, (d, _) => ((PdfToolbar)d).Rebuild()));

    private readonly ToolBar _bar = new();
    private readonly List<(ViewerCommand Command, EventHandler Handler)> _subscriptions = [];
    private string _layout = string.Empty;

    /// <summary>Creates the toolbar; set <see cref="Viewer"/> to bind it to a view.</summary>
    public PdfToolbar()
    {
        AutomationProperties.SetName(_bar, "Document toolbar");
        Content = _bar;
        Focusable = false;
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

    /// <summary>Gets the tool bar the items are laid out in (tests and restyling).</summary>
    internal ToolBar Bar => _bar;

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
        _bar.Items.Clear();
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

            FrameworkElement control = ItemTemplateSelector?.SelectTemplate(command, this) is { } template
                ? new ContentPresenter { Content = command, ContentTemplate = template }
                : CreateDefault(command);
            AutomationProperties.SetAutomationId(control, command.Id);
            ToolBar.SetOverflowMode(control, command.Placement == ViewerCommandPlacement.Overflow ? OverflowMode.Always : OverflowMode.AsNeeded);
            _bar.Items.Add(control);
        }
    }

    private FrameworkElement CreateDefault(ViewerCommand command)
    {
        FrameworkElement control = command switch
        {
            ViewerButtonCommand button => CreateButton(button),
            ViewerToggleCommand toggle => CreateToggle(toggle),
            ViewerChoiceCommand choice => CreateChoice(choice),
            ViewerPageCommand page => CreatePageBox(page),
            ViewerSearchCommand search => CreateFindBox(search),
            _ => new TextBlock { Text = command.Label },
        };
        AutomationProperties.SetName(control, command.Label);
        if (command.ToolTip is { } tip)
        {
            control.ToolTip = command.Shortcut is { } shortcut ? $"{tip} ({shortcut})" : tip;
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
        var button = new Button { Content = command.Glyph ?? command.Label, MinWidth = 28 };
        button.Click += (_, _) => command.Execute();
        Follow(command, () => button.IsEnabled = command.IsEnabled);
        return button;
    }

    private ToggleButton CreateToggle(ViewerToggleCommand command)
    {
        var toggle = new ToggleButton { Content = command.Glyph ?? command.Label, MinWidth = 28 };
        bool updating = false;
        void Changed(object sender, RoutedEventArgs e)
        {
            if (!updating)
            {
                command.IsChecked = toggle.IsChecked == true;
            }
        }

        toggle.Checked += Changed;
        toggle.Unchecked += Changed;
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
        // Editable but read-only, so the box shows the current value (the zoom as a percentage) when no choice matches it.
        var box = new ComboBox { ItemsSource = command.Choices, MinWidth = 96, IsEditable = true, IsReadOnly = true };
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
            box.Text = command.Text;
            updating = false;
        });
        return box;
    }

    private StackPanel CreatePageBox(ViewerPageCommand command)
    {
        var box = new TextBox { MinWidth = 44, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        AutomationProperties.SetName(box, command.Label);
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                if (!command.TrySubmit(box.Text))
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
        box.LostKeyboardFocus += (_, _) => box.Text = command.Text;
        Follow(command, () =>
        {
            box.IsEnabled = command.IsEnabled;
            if (!box.IsKeyboardFocusWithin)
            {
                box.Text = command.Text;
            }

            count.Text = command.PageCount > 0 ? $"/ {command.PageCount}" : string.Empty;
            box.ToolTip = command.Error ?? command.ToolTip;
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { box, count } };
    }

    private StackPanel CreateFindBox(ViewerSearchCommand command)
    {
        var box = new TextBox { MinWidth = 140, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, command.Label);
        var previous = new Button { Content = "‹", MinWidth = 24 };
        var next = new Button { Content = "›", MinWidth = 24 };
        AutomationProperties.SetName(previous, "Previous match");
        AutomationProperties.SetName(next, "Next match");
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), Foreground = Brushes.Gray };
        box.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Return))
            {
                return;
            }

            e.Handled = true;
            if (box.Text.Length > 0 && string.Equals(box.Text, command.Query, StringComparison.Ordinal))
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
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
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { box, previous, next, status } };
    }
}
