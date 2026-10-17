namespace Broadside.Viewer.Blazor;

/// <summary>What a toolbar shows about a <see cref="PdfViewer"/>: the current page, the page count, the zoom and whether a document is loading.</summary>
/// <param name="PageIndex">The zero-based page at the centre of the viewport (<see cref="InputModel.CurrentPage"/>).</param>
/// <param name="PageCount">The number of pages, 0 while nothing is open.</param>
/// <param name="Zoom">The zoom as a multiple of actual size (1 = one point of the page is one point on the screen).</param>
/// <param name="IsLoading">Whether a document is being opened.</param>
/// <remarks>Spec #33 track 6 shell (issue #221).</remarks>
public readonly record struct PdfViewerState(int PageIndex, int PageCount, double Zoom, bool IsLoading);
