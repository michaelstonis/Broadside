namespace Broadside.Viewer.WinUI;

/// <summary>What a <c>PdfView</c> shows: the current page, the page count, the zoom and whether it is still loading or drawing.</summary>
/// <param name="PageIndex">The current page (zero-based): the page under the view's centre.</param>
/// <param name="PageCount">The document's page count (0 while nothing is open).</param>
/// <param name="Zoom">The zoom relative to the actual size (1 = 100%).</param>
/// <param name="IsLoading">Whether the document is still opening.</param>
/// <param name="IsRendering">Whether tiles of the visible pages are still being drawn.</param>
/// <remarks>ISO 32000-2 §14.11.2 (the page's crop box is what is shown).</remarks>
public readonly record struct PdfViewState(int PageIndex, int PageCount, double Zoom, bool IsLoading, bool IsRendering);

/// <summary>Arguments of <c>PdfView.StateChanged</c>.</summary>
/// <param name="state">The new state.</param>
public sealed class PdfViewStateChangedEventArgs(PdfViewState state) : EventArgs
{
    /// <summary>Gets the new state.</summary>
    public PdfViewState State { get; } = state;
}

/// <summary>Arguments of <c>PdfView.Error</c>.</summary>
/// <param name="exception">What failed.</param>
public sealed class PdfViewErrorEventArgs(Exception exception) : EventArgs
{
    /// <summary>Gets what failed.</summary>
    public Exception Exception { get; } = exception;
}
