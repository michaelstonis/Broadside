using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// What a toolbar, a panel or a tool drives in a viewer control: the open document session, where the view is, and the navigation, zoom
/// and layout calls every shell offers. Each shell's view (<c>PdfView</c>, Blazor's <c>PdfViewer</c>) implements it explicitly, so its own
/// members stay as they shipped.
/// </summary>
/// <remarks>
/// Not a spec feature. Every member runs on the UI thread, as the shells' own members do; <see cref="ViewChanged"/> is raised there.
/// A <see cref="ViewerToolbar"/> binds to one through this interface only, so the same toolbar model serves every shell and a headless
/// test.
/// </remarks>
public interface IDocumentViewer
{
    /// <summary>Gets the open document's session, or <see langword="null"/> while nothing is open.</summary>
    DocumentSession? Session { get; }

    /// <summary>Gets the page at the viewport's center (zero-based; 0 while nothing is open).</summary>
    int PageIndex { get; }

    /// <summary>Gets the document's page count (0 while nothing is open, or while a remote document is still opening).</summary>
    int PageCount { get; }

    /// <summary>Gets the zoom relative to the page's actual size (1 = 100 %).</summary>
    /// <remarks>ISO 32000-2 §14.11.2 (the actual size is one point per 1/72 inch).</remarks>
    double Zoom { get; }

    /// <summary>Gets the text selection of the open document, or <see langword="null"/>.</summary>
    TextSelection? Selection { get; }

    /// <summary>Gets the find-in-document search of the open document, or <see langword="null"/>.</summary>
    DocumentSearch? Search { get; }

    /// <summary>Gets the annotation and link layer of the open document, or <see langword="null"/>.</summary>
    AnnotationLayer? Annotations { get; }

    /// <summary>
    /// Gets the page labels of the open document, or <see langword="null"/> while they have not been read (or the shell does not read them;
    /// a <see cref="ViewerToolbar"/> then reads them itself).
    /// </summary>
    /// <remarks>ISO 32000-2 §12.4.2.</remarks>
    PageLabelTable? PageLabels { get; }

    /// <summary>Gets the page layout the view uses, or <see langword="null"/> for the default (continuous).</summary>
    PageLayoutOptions? LayoutOptions { get; }

    /// <summary>
    /// Gets or sets the color filter the pages are drawn with (<see cref="ColorFilter.DarkMode"/> for smart dark mode); <see langword="null"/>
    /// keeps the session's own. A change draws every tile again from the cached display lists.
    /// </summary>
    ColorFilter? ColorFilter { get; set; }

    /// <summary>Raised on the UI thread when the page, the page count, the zoom, the session, the layout or the color filter changed.</summary>
    event EventHandler? ViewChanged;

    /// <summary>Shows a page.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    void GoToPage(int pageIndex);

    /// <summary>Shows the next page or spread.</summary>
    void NextPage();

    /// <summary>Shows the previous page or spread.</summary>
    void PreviousPage();

    /// <summary>Shows a resolved destination: its page, zoom and position.</summary>
    /// <param name="target">The destination.</param>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
#pragma warning disable CA1716 // the name every shell's view shipped (PdfView.GoTo), which this member normalizes
    void GoTo(ViewTarget target);
#pragma warning restore CA1716

    /// <summary>Zooms to a fit mode or the actual size.</summary>
    /// <param name="mode">The zoom mode.</param>
    void ZoomTo(ZoomMode mode);

    /// <summary>Zooms by a factor around the view's center.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    void ZoomBy(double factor);

    /// <summary>Changes the page layout, keeping the current page.</summary>
    /// <param name="options">The layout; the view copies it.</param>
    void SetLayout(PageLayoutOptions options);

    /// <summary>Scrolls the least distance that shows an area of a page, such as a search match's bounds.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="area">The area, in the page's default user space.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3 (user space).</remarks>
    void Reveal(int pageIndex, PdfRectangle area);
}
