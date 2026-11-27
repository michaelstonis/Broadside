using System.Globalization;
using Microsoft.Maui.Controls;

namespace Broadside.Viewer.Maui;

/// <summary>
/// Annotation editing for a <see cref="PdfView"/>: show or hide annotations and form fields, let presses move markup annotations, add a
/// note, box, typed text or stamp to the page in view, highlight the selected text, and list (reveal, remove) every annotation of the
/// document, page by page, with its author and replies (the core's <see cref="AnnotationList"/>). Every change goes through the view's <see cref="PdfView.Annotations"/> layer: appearances are generated and only the
/// overlay tiles an edit touches are drawn again.
/// </summary>
/// <remarks>ISO 32000-2 §12.5 (annotations), §12.5.6.4 (text), §12.5.6.6 (free text), §12.5.6.8 (square), §12.5.6.10 (highlight), §12.5.6.12 (stamp).</remarks>
public sealed class PdfAnnotationsPanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfAnnotationsPanel), null, propertyChanged: (b, _, n) => ((PdfAnnotationsPanel)b)._follower.Attach((PdfView?)n));

    private readonly ViewerFollower _follower;
    private readonly Switch _show;
    private readonly Switch _move;
    private readonly Label _addHeading = PanelKit.Heading("Add to the page in view");
    private readonly Button _highlight;
    private readonly Label _count = PanelKit.Hint(string.Empty, "annotation-count");
    private readonly VerticalStackLayout _list = new() { Spacing = 2, AutomationId = "annotation-list" };
    private AnnotationList? _model;
    private int _generation;
    private int _added;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view it edits.</summary>
    public PdfAnnotationsPanel()
    {
        _follower = new ViewerFollower(OnDocumentChanged, () => _ = ReloadAsync());
        (View showRow, _show) = PanelKit.Toggle("Show annotations and form fields", "annotations-show", true, value =>
        {
            if (_follower.View is { } view)
            {
                view.ShowAnnotations = value;
            }
        });
        (View moveRow, _move) = PanelKit.Toggle("Drag to move annotations", "annotations-move", false, value =>
        {
            if (_follower.View is { } view)
            {
                view.AllowAnnotationEditing = value;
            }
        });
        _highlight = PanelKit.Button("Highlight selection", "annotate-highlight", HighlightSelection);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    showRow,
                    moveRow,
                    _addHeading,
                    PanelKit.Wrap(
                        PanelKit.Button("Note", "annotate-note", () => Add(AnnotationTool.Note)),
                        PanelKit.Button("Box", "annotate-box", () => Add(AnnotationTool.Box)),
                        PanelKit.Button("Text", "annotate-text", () => Add(AnnotationTool.Text)),
                        PanelKit.Button("Stamp", "annotate-stamp", () => Add(AnnotationTool.Stamp)),
                        _highlight),
                    PanelKit.Hint("Added through the viewer's annotation layer: the appearance is generated and only the tiles it touches redraw."),
                    PanelKit.Heading("In the document"),
                    _count,
                    _list,
                },
            },
        };
    }

    /// <summary>Raised after the panel added or removed an annotation (the document now differs from its file).</summary>
    public event EventHandler? Edited;

    /// <summary>Gets or sets the view whose annotations this panel edits.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Reads the annotations of the page in view again (after edits made elsewhere).</summary>
    /// <param name="cancellationToken">Stops waiting for the list; the panel still shows it when it is read.</param>
    /// <returns>A task that completes when the list shows them.</returns>
    public Task RefreshAsync(CancellationToken cancellationToken = default) => ReloadAsync().WaitAsync(cancellationToken);

    private void OnDocumentChanged()
    {
        if (_follower.View is { } view)
        {
            _show.IsToggled = view.ShowAnnotations;
            _move.IsToggled = view.AllowAnnotationEditing;
        }

        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        int generation = ++_generation;
        int page = _follower.CurrentPage;
        _addHeading.Text = string.Create(CultureInfo.InvariantCulture, $"Add to page {page + 1}");
        _highlight.IsEnabled = true;
        if (_follower.View?.Annotations is not { } layer)
        {
            Follow(null);
            _list.Children.Clear();
            _count.Text = "No document.";
            return;
        }

        if (!ReferenceEquals(_model?.Layer, layer))
        {
            Follow(new AnnotationList(layer));
            _list.Children.Clear();
            _count.Text = "Reading…";
            try
            {
                await _model!.RefreshAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
#pragma warning disable CA1031 // shown in the panel
            catch (Exception exception)
#pragma warning restore CA1031
            {
                _count.Text = "The annotations could not be read: " + exception.Message;
                return;
            }
        }

        if (generation == _generation)
        {
            Render();
        }
    }

    private void Follow(AnnotationList? model)
    {
        _model?.Changed -= OnModelChanged;
        _model = model;
        _model?.Changed += OnModelChanged;
    }

    private void OnModelChanged(object? sender, EventArgs e) => Render();

    // Every page's annotations, grouped by page, in focus order (the core's AnnotationList).
    private void Render()
    {
        _list.Children.Clear();
        if (_model is not { } model)
        {
            return;
        }

        IReadOnlyList<AnnotationListItem> items = model.Items;
        _count.Text = items.Count == 0
            ? (model.IsComplete ? "No annotations." : "Reading…")
            : string.Create(CultureInfo.InvariantCulture, $"{items.Count} in the document{(model.IsComplete ? string.Empty : " so far")}");
        int page = -1;
        foreach (AnnotationListItem item in items)
        {
            if (item.Target.PageIndex != page)
            {
                page = item.Target.PageIndex;
                _list.Children.Add(PanelKit.Hint(string.Create(CultureInfo.InvariantCulture, $"Page {page + 1}")));
            }

            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 6 };
            string text = string.IsNullOrEmpty(item.Text) ? item.Kind : item.Kind + ": " + item.Text;
            if (item.Author is { } author)
            {
                text = author + " · " + text;
            }

            if (item.ReplyCount > 0)
            {
                text += string.Create(CultureInfo.InvariantCulture, $" ({item.ReplyCount} repl{(item.ReplyCount == 1 ? "y" : "ies")})");
            }

            var show = new Button
            {
                Text = text,
                LineBreakMode = LineBreakMode.TailTruncation,
                HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill,
                AutomationId = "annotation-item",
            };
            show.Clicked += (_, _) => model.Reveal(item, _follower.View);
            row.Add(show);
            if (item.IsRemovable)
            {
                Button remove = PanelKit.Button("×", "annotation-remove", () => Remove(item), "Remove " + item.Kind);
                row.Add(remove, 1, 0);
            }

            _list.Children.Add(row);
        }
    }

    private void Add(AnnotationTool tool)
    {
        if (_follower.View is not { Annotations: { } layer } view)
        {
            return;
        }

        int page = _follower.CurrentPage;
        if (AnnotationEditing.Add(layer, page, tool, _added++) is { } target)
        {
            view.Reveal(page, layer.GetRectangle(target));
            Changed();
        }
    }

    private void HighlightSelection()
    {
        if (_follower.View is { Annotations: { } layer, Selection: { } selection } && AnnotationEditing.HighlightSelection(layer, selection) is not null)
        {
            Changed();
        }
    }

    private void Remove(AnnotationListItem item)
    {
        if (_follower.View?.Annotations is { } layer && layer.Remove(item.Target))
        {
            Changed();
        }
    }

    // The list follows the layer's edits itself (AnnotationList.Changed).
    private void Changed() => Edited?.Invoke(this, EventArgs.Empty);
}
