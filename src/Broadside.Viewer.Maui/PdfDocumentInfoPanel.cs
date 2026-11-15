using System.Globalization;
using Broadside.Diagnostics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Broadside.Viewer.Maui;

/// <summary>
/// What a <see cref="PdfView"/>'s document is, in three tabs: the document's facts (version, encryption, linearization, the document
/// information dictionary and XMP metadata, PDF/A and PDF/UA identification), the diagnostics the reader recorded while repairing or
/// reading it (Broadside never repairs silently), and its tag tree (the logical structure, as assistive technology reads it).
/// </summary>
/// <remarks>ISO 32000-2 §14.3 (metadata), §14.7 (logical structure), §14.8 (tagged PDF), §7.5.8 (linearization).</remarks>
public sealed class PdfDocumentInfoPanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfDocumentInfoPanel), null, propertyChanged: (b, _, n) => ((PdfDocumentInfoPanel)b)._follower.Attach((PdfView?)n));

    /// <summary>The <see cref="FileName"/> property.</summary>
    public static readonly BindableProperty FileNameProperty =
        BindableProperty.Create(nameof(FileName), typeof(string), typeof(PdfDocumentInfoPanel), null, propertyChanged: (b, _, _) => ((PdfDocumentInfoPanel)b).Refresh());

    private const int MaxDiagnostics = 300;
    private readonly ViewerFollower _follower;
    private readonly Button[] _tabs;
    private readonly ContentView _body = new();
    private readonly CollectionView _diagnostics = new() { AutomationId = "info-diagnostics" };
    private readonly CollectionView _tags = new() { SelectionMode = SelectionMode.Single, AutomationId = "info-tags" };
    private TagTree? _tree;
    private int _tab;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view whose document it describes.</summary>
    public PdfDocumentInfoPanel()
    {
        _follower = new ViewerFollower(Refresh);
        _tabs =
        [
            PanelKit.Button("Document", "info-tab-document", () => ShowTab(0)),
            PanelKit.Button("Diagnostics", "info-tab-diagnostics", () => ShowTab(1)),
            PanelKit.Button("Tags", "info-tab-tags", () => ShowTab(2)),
        ];
        _diagnostics.ItemTemplate = new DataTemplate(() =>
        {
            var code = new Label { FontAttributes = FontAttributes.Bold, FontSize = 13 };
            code.SetBinding(Label.TextProperty, static (DiagnosticItem d) => d.Code);
            var severity = new Label { FontSize = 11, TextColor = PanelKit.HintColor };
            severity.SetBinding(Label.TextProperty, static (DiagnosticItem d) => d.Detail);
            var message = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
            message.SetBinding(Label.TextProperty, static (DiagnosticItem d) => d.Message);
            return new VerticalStackLayout { Padding = new Thickness(2, 6), Children = { code, severity, message } };
        });
        _tags.ItemTemplate = new DataTemplate(() =>
        {
            var text = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation };
            text.SetBinding(Label.TextProperty, static (TagItem t) => t.Text);
            text.SetBinding(MarginProperty, static (TagItem t) => t.Indent);
            return new ContentView { Padding = new Thickness(2, 6), Content = text };
        });
        _tags.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.Count > 0 && e.CurrentSelection[0] is TagItem item && _tree is not null && item.Row.HasChildren)
            {
                _tree.Toggle(item.Row.Element);
                ShowTags();
            }
        };

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 6 };
        root.Add(PanelKit.Wrap(_tabs));
        root.Add(_body, 0, 1);
        Content = root;
        ShowTab(0);
    }

    /// <summary>Gets or sets the view whose document this panel describes.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets or sets the file name the Document tab lists (the view does not know where its document came from).</summary>
    public string? FileName
    {
        get => (string?)GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    /// <summary>Reads the document again: diagnostics grow as pages are read, and an edit can change the facts.</summary>
    public void Refresh()
    {
        _tree = _follower?.View?.CurrentSession?.Document is { } document ? new TagTree(document) : null;
        ShowTab(_tab);
    }

    private void ShowTab(int tab)
    {
        _tab = tab;
        PanelKit.Select(_tabs, _tabs[tab]);
        PdfDocument? document = _follower?.View?.CurrentSession?.Document;
        if (document is null)
        {
            _body.Content = PanelKit.Hint("No document.");
            return;
        }

        switch (tab)
        {
            case 0:
                IReadOnlyList<DocumentFact> facts = DocumentFacts.Read(document, FileName, out bool incomplete);
                var stack = new VerticalStackLayout { Spacing = 8, Children = { PanelKit.Facts(facts, "info-facts") } };
                if (incomplete)
                {
                    stack.Children.Add(PanelKit.Hint("Some facts need parts of the file that are not loaded yet."));
                }

                _body.Content = new ScrollView { Content = stack };
                break;
            case 1:
                IReadOnlyList<Diagnostic> diagnostics = document.Diagnostics;
                _diagnostics.ItemsSource = diagnostics.Take(MaxDiagnostics).Select(DiagnosticItem.From).ToList();
                var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 4 };
                grid.Add(PanelKit.Hint(
                    diagnostics.Count == 0
                        ? "No diagnostics: the file read cleanly so far."
                        : string.Create(CultureInfo.InvariantCulture, $"{diagnostics.Count} diagnostic(s): what the reader repaired or noticed while opening and reading this file."),
                    "info-diagnostic-count"));
                grid.Add(_diagnostics, 0, 1);
                _body.Content = grid;
                break;
            default:
                ShowTags();
                break;
        }
    }

    private void ShowTags()
    {
        if (_tree is null || _tree.IsEmpty)
        {
            _body.Content = PanelKit.Hint("This document is not tagged (no structure tree).", "info-tag-count");
            return;
        }

        _tags.SelectedItem = null;
        _tags.ItemsSource = _tree.Rows().Select(row => new TagItem(row)).ToList();
        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 4 };
        grid.Add(PanelKit.Hint("The logical structure, as assistive technology reads it. Tap an element to expand it.", "info-tag-count"));
        grid.Add(_tags, 0, 1);
        _body.Content = grid;
    }

    /// <summary>One listed diagnostic.</summary>
    internal sealed record DiagnosticItem(string Code, string Detail, string Message)
    {
        public static DiagnosticItem From(Diagnostic diagnostic) => new(
            diagnostic.Code,
            diagnostic.Severity + (diagnostic.Offset is long offset ? string.Create(CultureInfo.InvariantCulture, $" at byte {offset}") : string.Empty),
            diagnostic.Message);
    }

    /// <summary>One visible tag row.</summary>
    internal sealed record TagItem(TagRow Row)
    {
        public string Text => (Row.HasChildren ? (Row.IsExpanded ? "▾ " : "▸ ") : "   ") + Row.Text;

        public Thickness Indent => new(Row.Depth * 14, 0, 0, 0);
    }
}
