using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;

namespace Broadside.Viewer.Maui;

/// <summary>
/// A toolbar for a <see cref="PdfView"/> (or any <see cref="IDocumentViewer"/>): the shared <see cref="ViewerToolbar"/> model laid out
/// with MAUI's own buttons, pickers and entries in a row that scrolls sideways on a narrow screen; overflow commands go to a second
/// row the "⋯" button shows and hides.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature, except the page box (ISO 32000-2 §12.4.2, page labels) and the default visibility (§12.2, Table 147,
/// <c>HideToolbar</c>). The view itself stays toolbar-free: an app that wants none pays nothing.
/// </para>
/// <para>
/// Set <see cref="Viewer"/> to bind it; <see cref="Toolbar"/> is the model, where an app adds, removes, reorders or hides commands.
/// <see cref="ItemTemplate"/> restyles items: a template (or a <see cref="DataTemplateSelector"/>) builds a command's view with the
/// command as its binding context (a command raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>, and a
/// button command is an <see cref="System.Windows.Input.ICommand"/>); a selector that returns no template keeps the default control.
/// Every default control carries the command's identifier as its <see cref="Element.AutomationId"/>.
/// </para>
/// </remarks>
public sealed class PdfToolbar : ContentView, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(IDocumentViewer), typeof(PdfToolbar), null, propertyChanged: (b, _, n) => ((PdfToolbar)b).Bind((IDocumentViewer?)n));

    /// <summary>The <see cref="Options"/> property.</summary>
    public static readonly BindableProperty OptionsProperty =
        BindableProperty.Create(nameof(Options), typeof(ViewerToolbarOptions), typeof(PdfToolbar), null, propertyChanged: (b, _, _) => ((PdfToolbar)b).Bind(((PdfToolbar)b).Viewer));

    /// <summary>The <see cref="ItemTemplate"/> property.</summary>
    public static readonly BindableProperty ItemTemplateProperty =
        BindableProperty.Create(nameof(ItemTemplate), typeof(DataTemplate), typeof(PdfToolbar), null, propertyChanged: (b, _, _) => ((PdfToolbar)b).Rebuild());

    private readonly HorizontalStackLayout _bar = new() { Spacing = 4 };
    private readonly FlexLayout _overflow = new() { Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Center, IsVisible = false };
    private readonly Button _more;
    private readonly VerticalStackLayout _root;
    private readonly List<(ViewerCommand Command, EventHandler Handler)> _subscriptions = [];
    private string _layout = string.Empty;

    /// <summary>Creates the toolbar; set <see cref="Viewer"/> to bind it to a view.</summary>
    public PdfToolbar()
    {
        _more = PanelKit.Button("⋯", "toolbar.more", () => _overflow.IsVisible = !_overflow.IsVisible, "More");
        _root = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _bar }, _overflow },
        };
        Content = _root;
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

    /// <summary>Gets or sets a template (or a selector) for items: the view it builds for a <see cref="ViewerCommand"/> replaces the default control.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>Gets the model bound to <see cref="Viewer"/>, or <see langword="null"/> without a viewer.</summary>
    public ViewerToolbar? Toolbar { get; private set; }

    /// <summary>Gets the controls in the bar (tests).</summary>
    internal IReadOnlyList<IView> BarItems => [.. _bar.Children];

    /// <summary>Gets the controls in the overflow row (tests).</summary>
    internal IReadOnlyList<IView> OverflowItems => [.. _overflow.Children];

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
        _bar.Children.Clear();
        _overflow.Children.Clear();
        _layout = LayoutKey();
        if (Toolbar is not { } toolbar)
        {
            return;
        }

        _root.IsVisible = toolbar.IsVisible;
        foreach (ViewerCommand command in toolbar.Items)
        {
            if (command.Placement == ViewerCommandPlacement.Hidden)
            {
                continue;
            }

            View view = Templated(command) ?? CreateDefault(command);
            if (command.Placement == ViewerCommandPlacement.Overflow)
            {
                view.Margin = new Thickness(0, 0, 6, 6);
                _overflow.Children.Add(view);
            }
            else
            {
                _bar.Children.Add(view);
            }
        }

        if (_overflow.Children.Count > 0)
        {
            _bar.Children.Add(_more);
        }
        else
        {
            _overflow.IsVisible = false;
        }
    }

    private View? Templated(ViewerCommand command)
    {
        DataTemplate? template = ItemTemplate is DataTemplateSelector selector ? selector.SelectTemplate(command, this) : ItemTemplate;
        if (template?.CreateContent() is not View view)
        {
            return null;
        }

        view.BindingContext = command;
        view.AutomationId ??= command.Id;
        return view;
    }

    private View CreateDefault(ViewerCommand command)
    {
        View view = command switch
        {
            ViewerButtonCommand button => CreateButton(button),
            ViewerToggleCommand toggle => CreateToggle(toggle),
            ViewerChoiceCommand choice => CreateChoice(choice),
            ViewerPageCommand page => CreatePageBox(page),
            ViewerSearchCommand search => CreateFindBox(search),
            _ => new Label { Text = command.Label },
        };
        view.AutomationId ??= command.Id;
        SemanticProperties.SetDescription(view, command.Label);
        if (command.ToolTip is { } tip)
        {
            ToolTipProperties.SetText(view, command.Shortcut is { } shortcut ? $"{tip} ({shortcut})" : tip);
        }

        return view;
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
        Button button = PanelKit.Button(command.Glyph ?? command.Label, command.Id, command.Execute, command.Label);
        Follow(command, () => button.IsEnabled = command.IsEnabled);
        return button;
    }

    private Button CreateToggle(ViewerToggleCommand command)
    {
        Button button = PanelKit.Button(command.Glyph ?? command.Label, command.Id, () => command.IsChecked = !command.IsChecked, command.Label);
        Follow(command, () =>
        {
            button.IsEnabled = command.IsEnabled;
            PanelKit.Select([button], command.IsChecked ? button : null);
        });
        return button;
    }

    private Picker CreateChoice(ViewerChoiceCommand command)
    {
        var picker = new Picker { ItemsSource = command.Choices.ToList(), MinimumWidthRequest = 110, Title = command.Label };
        bool updating = false;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!updating && picker.SelectedIndex >= 0)
            {
                command.Selected = picker.SelectedIndex;
            }
        };
        Follow(command, () =>
        {
            updating = true;
            picker.IsEnabled = command.IsEnabled;
            picker.SelectedIndex = command.Selected;
            picker.Title = command.Text.Length > 0 ? command.Text : command.Label;
            updating = false;
        });
        return picker;
    }

    private HorizontalStackLayout CreatePageBox(ViewerPageCommand command)
    {
        var entry = new Entry { MinimumWidthRequest = 52, HorizontalTextAlignment = TextAlignment.Center, Keyboard = Keyboard.Text, ReturnType = ReturnType.Go };
        var count = new Label { VerticalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetDescription(entry, command.Label);
        entry.Completed += (_, _) =>
        {
            if (!command.TrySubmit(entry.Text ?? string.Empty))
            {
                entry.Text = command.Text;
            }
        };
        entry.Unfocused += (_, _) => entry.Text = command.Text;
        Follow(command, () =>
        {
            entry.IsEnabled = command.IsEnabled;
            if (!entry.IsFocused)
            {
                entry.Text = command.Text;
            }

            count.Text = command.PageCount > 0 ? $"/ {command.PageCount}" : string.Empty;
            ToolTipProperties.SetText(entry, command.Error ?? command.ToolTip ?? command.Label);
        });
        return new HorizontalStackLayout { Spacing = 4, Children = { entry, count } };
    }

    private HorizontalStackLayout CreateFindBox(ViewerSearchCommand command)
    {
        var entry = new Entry { MinimumWidthRequest = 140, Placeholder = command.Label, ReturnType = ReturnType.Search };
        SemanticProperties.SetDescription(entry, command.Label);
        Button previous = PanelKit.Button("‹", "search.previous", command.Previous, "Previous match");
        Button next = PanelKit.Button("›", "search.next", command.Next, "Next match");
        var status = new Label { VerticalTextAlignment = TextAlignment.Center, TextColor = PanelKit.HintColor, FontSize = 12 };
        entry.Completed += (_, _) =>
        {
            string text = entry.Text ?? string.Empty;
            if (text.Length > 0 && string.Equals(text, command.Query, StringComparison.Ordinal))
            {
                command.Next();
            }
            else
            {
                command.Submit(text);
            }
        };
        Follow(command, () =>
        {
            entry.IsEnabled = previous.IsEnabled = next.IsEnabled = command.IsEnabled;
            status.Text = command.Status;
        });
        return new HorizontalStackLayout { Spacing = 4, Children = { entry, previous, next, status } };
    }
}
