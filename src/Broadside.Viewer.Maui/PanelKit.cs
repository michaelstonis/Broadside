using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui;

/// <summary>
/// Follows a <see cref="PdfView"/> for a panel: calls back when the view shows another document and when its page changes, always on
/// the UI thread, and stops when the panel is given another view.
/// </summary>
internal sealed class ViewerFollower
{
    private readonly Action _documentChanged;
    private readonly Action? _pageChanged;
    private PdfView? _view;
    private int _page = -1;

    public ViewerFollower(Action documentChanged, Action? pageChanged = null)
    {
        _documentChanged = documentChanged;
        _pageChanged = pageChanged;
    }

    public PdfView? View => _view;

    /// <summary>The page the view shows (zero-based, 0 without a document).</summary>
    public int CurrentPage => _view?.State.PageCount > 0 ? Math.Clamp(_view.State.PageIndex, 0, _view.State.PageCount - 1) : 0;

    public void Attach(PdfView? view)
    {
        if (ReferenceEquals(view, _view))
        {
            return;
        }

        if (_view is not null)
        {
            _view.DocumentChanged -= OnDocumentChanged;
            _view.StateChanged -= OnStateChanged;
        }

        _view = view;
        _page = -1;
        if (view is not null)
        {
            view.DocumentChanged += OnDocumentChanged;
            view.StateChanged += OnStateChanged;
        }

        _documentChanged();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _page = -1;
        _documentChanged();
    }

    private void OnStateChanged(object? sender, PdfViewStateChangedEventArgs e)
    {
        if (e.State.PageIndex != _page && e.State.PageCount > 0)
        {
            _page = e.State.PageIndex;
            _pageChanged?.Invoke();
        }
    }
}

/// <summary>The panels' shared look: plain MAUI controls with automation IDs (the UI tests find them by those).</summary>
internal static class PanelKit
{
    public static readonly Color HintColor = Color.FromRgb(0x6B, 0x6B, 0x73);

    public static Label Heading(string text) => new() { Text = text, FontAttributes = FontAttributes.Bold, FontSize = 15, Margin = new Thickness(0, 8, 0, 2) };

    public static Label Hint(string text, string? automationId = null) =>
        new() { Text = text, FontSize = 12, TextColor = HintColor, LineBreakMode = LineBreakMode.WordWrap, AutomationId = automationId };

    public static Button Button(string text, string automationId, Action action, string? description = null)
    {
        var button = new Button { Text = text, AutomationId = automationId, Padding = new Thickness(10, 4), MinimumHeightRequest = 40, MinimumWidthRequest = 44 };
        if (description is not null)
        {
            SemanticProperties.SetDescription(button, description);
        }

        button.Clicked += (_, _) => action();
        return button;
    }

    /// <summary>A labelled switch in one row; <paramref name="changed"/> runs when the user flips it.</summary>
    public static (View Row, Switch Switch) Toggle(string text, string automationId, bool value, Action<bool> changed)
    {
        var toggle = new Switch { IsToggled = value, AutomationId = automationId, VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center };
        SemanticProperties.SetDescription(toggle, text);
        toggle.Toggled += (_, e) => changed(e.Value);
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        grid.Add(new Label { Text = text, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap });
        grid.Add(toggle, 1, 0);
        return (grid, toggle);
    }

    /// <summary>Buttons that wrap onto as many lines as the panel's width needs (never past its edge on a phone).</summary>
    public static FlexLayout Wrap(params View[] children)
    {
        var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        foreach (View child in children)
        {
            child.Margin = new Thickness(0, 0, 6, 6);
            flex.Children.Add(child);
        }

        return flex;
    }

    /// <summary>Marks the selected button of a segmented group.</summary>
    public static void Select(IEnumerable<Button> buttons, Button? selected)
    {
        foreach (Button button in buttons)
        {
            bool on = ReferenceEquals(button, selected);
            button.FontAttributes = on ? FontAttributes.Bold : FontAttributes.None;
            button.BorderWidth = on ? 2 : 0;
            button.BorderColor = on ? Color.FromRgb(0x33, 0x7A, 0xFF) : Colors.Transparent;
        }
    }

    /// <summary>A two-column list of names and values.</summary>
    public static Grid Facts(IEnumerable<DocumentFact> facts, string? automationId = null)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12, RowSpacing = 4, AutomationId = automationId };
        int row = 0;
        foreach (DocumentFact fact in facts)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(new Label { Text = fact.Name, TextColor = HintColor, FontSize = 13 }, 0, row);
            grid.Add(new Label { Text = fact.Value, FontSize = 13, LineBreakMode = LineBreakMode.CharacterWrap }, 1, row);
            row++;
        }

        return grid;
    }
}
