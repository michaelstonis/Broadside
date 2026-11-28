using Broadside.Annotations;

namespace Broadside.Viewer;

/// <summary>
/// Every annotation and form field of a document as a comments panel lists them: page by page, in each page's keyboard focus order,
/// with the author, text, modification date and reply count of markup annotations. A view over the <see cref="AnnotationLayer"/>:
/// items hold <see cref="AnnotationTarget"/>s, never copies, and the list follows the layer's edits.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.5 (annotations), §12.5.1 (<c>Tabs</c>, the focus order) and §12.5.6.2 (markup annotations, Table 172). Popups,
/// hidden annotations and annotations whose optional content is hidden (<see cref="DocumentSession.OptionalContentState"/>) do not
/// take part, as in the layer.
/// </para>
/// <para>
/// <see cref="RefreshAsync"/> loads the pages into the layer one at a time (<see cref="AnnotationLayer.ReadPageAsync"/>), raising
/// <see cref="Changed"/> as pages arrive, so a very large document's list fills in progressively. Single-threaded like the layer: use
/// it on the shell's UI thread.
/// </para>
/// </remarks>
public sealed class AnnotationList
{
    private readonly AnnotationLayer _layer;
    private readonly int _pageCount;
    private IReadOnlyList<AnnotationListItem> _items = [];
    private long _edits;
    private PdfOptionalContentState? _state;
    private int _loaded;

    /// <summary>Initializes a new instance of the <see cref="AnnotationList"/> class over a layer; call <see cref="RefreshAsync"/> to read it.</summary>
    /// <param name="layer">The annotation layer of the open document.</param>
    public AnnotationList(AnnotationLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layer = layer;
        _pageCount = layer.Session.PageCount;
        _edits = layer.Session.EditCount;
        _state = layer.Session.OptionalContentState;
        layer.Changed += OnLayerChanged;
    }

    /// <summary>Raised when <see cref="Items"/> changed: pages arrived, or the layer's annotations were edited or its layers toggled.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the layer.</summary>
    public AnnotationLayer Layer => _layer;

    /// <summary>Gets the items of every page read so far, in page order, then focus order.</summary>
    public IReadOnlyList<AnnotationListItem> Items => _items;

    /// <summary>Gets a value indicating whether every page has been read.</summary>
    public bool IsComplete => _loaded == _pageCount;

    /// <summary>Reads every page's annotations into the list, page by page.</summary>
    /// <param name="cancellationToken">Stops reading; the pages read so far stay listed.</param>
    /// <returns>A task that completes once every page is listed.</returns>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        for (int page = 0; page < _pageCount; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _layer.ReadPageAsync(page, cancellationToken).ConfigureAwait(true);
            Rebuild();
        }
    }

    /// <summary>Finds the item of an annotation.</summary>
    /// <param name="target">The annotation.</param>
    /// <returns>The item, or <see langword="null"/> when it is not listed.</returns>
    public AnnotationListItem? FindItem(AnnotationTarget target)
    {
        foreach (AnnotationListItem item in _items)
        {
            if (item.Target == target)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Selects an annotation in the layer and shows it in a viewer: what a click on an item does.</summary>
    /// <param name="item">The item.</param>
    /// <param name="viewer">The viewer, or <see langword="null"/> to only select it.</param>
    public void Reveal(AnnotationListItem item, IDocumentViewer? viewer)
    {
        ArgumentNullException.ThrowIfNull(item);
        _layer.Select(item.Target);
        viewer?.Reveal(item.Target.PageIndex, item.Rectangle);
    }

    /// <summary>The item a layer lists for one of its annotations.</summary>
    internal static AnnotationListItem CreateItem(AnnotationLayer layer, AnnotationTarget target)
    {
        PdfAnnotation annotation = layer.GetAnnotation(target);
        AnnotationRole role = layer.GetRole(target);
        string kind = role == AnnotationRole.Field ? "Form field" : annotation.Subtype?.Value ?? "Annotation";
        string? author = null;
        int replies = 0;
        if (annotation is PdfMarkupAnnotation)
        {
            AnnotationNote note = layer.GetNote(target);
            author = note.Title;
            replies = note.Replies.Count;
        }

        return new AnnotationListItem(target, role, kind, author, annotation.Contents ?? layer.GetAccessibleName(target), annotation.ModifiedText, replies, layer.GetRectangle(target));
    }

    private void OnLayerChanged(object? sender, EventArgs e)
    {
        // Hover, selection and focus changes raise the layer's event too: rebuild only after an edit or a change of layers.
        if (_layer.Session.EditCount != _edits || !ReferenceEquals(_layer.Session.OptionalContentState, _state) || Loaded() != _loaded)
        {
            Rebuild();
        }
    }

    private int Loaded()
    {
        int loaded = 0;
        for (int page = 0; page < _pageCount; page++)
        {
            if (_layer.IsLoaded(page))
            {
                loaded++;
            }
        }

        return loaded;
    }

    private void Rebuild()
    {
        _edits = _layer.Session.EditCount;
        _state = _layer.Session.OptionalContentState;
        var items = new List<AnnotationListItem>();
        AnnotationTarget[] order = [];
        int loaded = 0;
        for (int page = 0; page < _pageCount; page++)
        {
            if (!_layer.IsLoaded(page))
            {
                continue;
            }

            loaded++;
            int count = _layer.GetFocusOrder(page, order);
            if (count > order.Length)
            {
                order = new AnnotationTarget[count];
                count = _layer.GetFocusOrder(page, order);
            }

            for (int i = 0; i < count; i++)
            {
                items.Add(CreateItem(_layer, order[i]));
            }
        }

        _loaded = loaded;
        _items = items;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>One annotation or form field as a comments panel lists it.</summary>
/// <param name="Target">Where it is in the <see cref="AnnotationLayer"/>.</param>
/// <param name="Role">Whether it is a link, a form field or another annotation.</param>
/// <param name="Kind">What to call it: <c>Form field</c> for a widget, else the annotation's subtype (<c>Text</c>, <c>Highlight</c>, …).</param>
/// <param name="Author">The author (<c>T</c>) of a markup annotation.</param>
/// <param name="Text">The text (<c>Contents</c>), else the layer's accessible name.</param>
/// <param name="Modified">The modification date (<c>M</c>) as stored.</param>
/// <param name="ReplyCount">How many markup annotations of the page reply to it (<c>IRT</c>).</param>
/// <param name="Rectangle">Its rectangle in default user space: what <see cref="IDocumentViewer.Reveal"/> scrolls to.</param>
/// <remarks>ISO 32000-2 §12.5.2 (Table 166) and §12.5.6.2 (Table 172).</remarks>
public sealed record AnnotationListItem(
    AnnotationTarget Target,
    AnnotationRole Role,
    string Kind,
    string? Author,
    string? Text,
    string? Modified,
    int ReplyCount,
    PdfRectangle Rectangle)
{
    /// <summary>Gets a value indicating whether the layer can remove it (not a form field, not locked).</summary>
    public bool IsRemovable => Role != AnnotationRole.Field;
}
