using Avalonia;
using Avalonia.Controls;

namespace Broadside.Viewer.Avalonia;

/// <summary>
/// Page thumbnails of the viewer's document with their page labels; a click shows the page and the page in view is marked. The thumbnails replay each page's cached display list (<see cref="ViewerSidebar.RenderThumbnailsAsync"/>).
/// </summary>
/// <remarks>ISO 32000-2 §14.11.2 (the displayed region) and §12.4.2 (page labels). The logic is the viewer core's <see cref="ViewerSidebar"/>; <see cref="PdfSidebar"/> shows this panel as one of its tabs.</remarks>
public sealed class PdfThumbnailsPanel : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfThumbnailsPanel, IDocumentViewer?>(nameof(Viewer));

    private readonly ThumbnailsCore _core = new();

    /// <summary>Creates the panel; set <see cref="Viewer"/> to bind it.</summary>
    public PdfThumbnailsPanel()
    {
        Content = _core.Root;
    }

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Gets the number of pages listed (at most 500).</summary>
    public int PageCount => _core.Count;

    /// <summary>Gets the number of thumbnails drawn so far.</summary>
    public int RenderedCount => _core.RenderedCount;

    /// <summary>Unbinds the panel from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}

/// <summary>
/// The outline (bookmarks) of the viewer's document as a tree that expands and collapses; a click shows the entry's destination and the entry of the page in view is marked.
/// </summary>
/// <remarks>ISO 32000-2 §12.3.3 (document outline) and §12.3.2 (destinations). The logic is the viewer core's <see cref="ViewerSidebar"/>; <see cref="PdfSidebar"/> shows this panel as one of its tabs.</remarks>
public sealed class PdfOutlinePanel : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfOutlinePanel, IDocumentViewer?>(nameof(Viewer));

    private readonly OutlineCore _core = new();

    /// <summary>Creates the panel; set <see cref="Viewer"/> to bind it.</summary>
    public PdfOutlinePanel()
    {
        Content = _core.Root;
    }

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Gets the number of outline rows shown.</summary>
    public int VisibleCount => _core.VisibleCount;

    /// <summary>Unbinds the panel from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}

/// <summary>
/// Find in the viewer's document: a query box, the search's progress and its matches grouped by page with the text around them (<see cref="SearchResults"/>); a click shows a match.
/// </summary>
/// <remarks>ISO 32000-2 §9.10 (text extraction). The logic is the viewer core's <see cref="ViewerSidebar"/>; <see cref="PdfSidebar"/> shows this panel as one of its tabs.</remarks>
public sealed class PdfSearchPanel : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfSearchPanel, IDocumentViewer?>(nameof(Viewer));

    private readonly SearchCore _core = new();

    /// <summary>Creates the panel; set <see cref="Viewer"/> to bind it.</summary>
    public PdfSearchPanel()
    {
        Content = _core.Root;
    }

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Gets the number of results listed.</summary>
    public int ResultCount => _core.ResultCount;

    /// <summary>Searches the document from the page in view, as Enter in the query box does.</summary>
    /// <param name="query">The text; empty clears the search.</param>
    public void Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _core.Run(query);
    }

    /// <summary>Unbinds the panel from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}

/// <summary>
/// The files the viewer's document carries (embedded files and file attachment annotations) with their size, type and checksum status. Open and Save raise <see cref="Opening"/> with the decoded bytes: the app decides what to do with them, the viewer never launches a file.
/// </summary>
/// <remarks>ISO 32000-2 §7.11.4 (embedded file streams) and §12.5.6.15 (file attachment annotations). The logic is the viewer core's <see cref="ViewerSidebar"/>; <see cref="PdfSidebar"/> shows this panel as one of its tabs.</remarks>
public sealed class PdfAttachmentsPanel : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfAttachmentsPanel, IDocumentViewer?>(nameof(Viewer));

    private readonly AttachmentsCore _core = new();

    /// <summary>Creates the panel; set <see cref="Viewer"/> to bind it.</summary>
    public PdfAttachmentsPanel()
    {
        Content = _core.Root;
        _core.Opening = e => Opening?.Invoke(this, e);
    }

    /// <summary>Raised with an attachment's decoded bytes when the user opens or saves it.</summary>
    public event EventHandler<AttachmentOpeningEventArgs>? Opening;

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Gets the number of attachments listed.</summary>
    public int Count => _core.Count;

    /// <summary>Unbinds the panel from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}

/// <summary>
/// The layers (optional content groups) of the viewer's document as the default configuration's tree, each with a check box that shows or hides it; locked layers cannot be switched and radio-button sets keep one layer on. Reset returns to the file's own states.
/// </summary>
/// <remarks>ISO 32000-2 §8.11.2 and §8.11.4, through <see cref="DocumentSession.OptionalContentState"/>. The logic is the viewer core's <see cref="ViewerSidebar"/>; <see cref="PdfSidebar"/> shows this panel as one of its tabs.</remarks>
public sealed class PdfLayersPanel : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfLayersPanel, IDocumentViewer?>(nameof(Viewer));

    private readonly LayersCore _core = new();

    /// <summary>Creates the panel; set <see cref="Viewer"/> to bind it.</summary>
    public PdfLayersPanel()
    {
        Content = _core.Root;
    }

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Unbinds the panel from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}

/// <summary>
/// Every annotation and comment of the viewer's document, page by page, with its author, text and reply count (<see cref="AnnotationList"/>); a click selects it and shows it.
/// </summary>
/// <remarks>ISO 32000-2 §12.5 (annotations) and §12.5.6.2 (markup annotations). The logic is the viewer core's <see cref="ViewerSidebar"/>; <see cref="PdfSidebar"/> shows this panel as one of its tabs.</remarks>
public sealed class PdfAnnotationsPanel : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfAnnotationsPanel, IDocumentViewer?>(nameof(Viewer));

    private readonly AnnotationsCore _core = new();

    /// <summary>Creates the panel; set <see cref="Viewer"/> to bind it.</summary>
    public PdfAnnotationsPanel()
    {
        Content = _core.Root;
    }

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panel lays out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Gets the number of annotations listed.</summary>
    public int Count => _core.Count;

    /// <summary>Unbinds the panel from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}

/// <summary>
/// The side panels of a viewer behind one row of tabs: thumbnails, outline, search, attachments, layers and comments, sharing one
/// <see cref="ViewerSidebar"/> model. <see cref="AttachmentOpening"/> hands the app an attachment's bytes.
/// </summary>
/// <remarks>Not a spec feature; each panel cites its clauses (see <see cref="PdfThumbnailsPanel"/> and the others).</remarks>
public sealed class PdfSidebar : ContentControl, IDisposable
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly StyledProperty<IDocumentViewer?> ViewerProperty = AvaloniaProperty.Register<PdfSidebar, IDocumentViewer?>(nameof(Viewer));

    private readonly SidebarCore _core;

    /// <summary>Creates the sidebar; set <see cref="Viewer"/> to bind it.</summary>
    public PdfSidebar()
    {
        _core = new SidebarCore(e => AttachmentOpening?.Invoke(this, e));
        Content = _core.Root;
    }

    /// <summary>Raised with an attachment's decoded bytes when the user opens or saves it in the attachments tab.</summary>
    public event EventHandler<AttachmentOpeningEventArgs>? AttachmentOpening;

    /// <summary>Gets or sets the viewer the panel follows (a <see cref="PdfView"/>, or any <see cref="IDocumentViewer"/>).</summary>
    public IDocumentViewer? Viewer
    {
        get => GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the model the panels lay out, or <see langword="null"/> without a viewer.</summary>
    public ViewerSidebar? Sidebar => _core.Sidebar;

    /// <summary>Gets or sets the tab shown (one panel of <see cref="SidebarPanels"/>).</summary>
    public SidebarPanels SelectedPanel
    {
        get => _core.Selected;
        set => _core.Selected = value;
    }

    /// <summary>Unbinds the sidebar from its viewer.</summary>
    public void Dispose() => _core.Bind(null);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == ViewerProperty)
        {
            _core.Bind(Viewer);
        }
    }
}
