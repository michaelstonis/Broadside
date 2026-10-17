namespace Broadside.Viewer.Maui;

/// <summary>What a <see cref="PdfView"/> shows: the current page, the page count, the zoom and whether it is still working.</summary>
/// <param name="PageIndex">The page at the viewport's centre (zero-based).</param>
/// <param name="PageCount">The document's page count (0 while nothing is open).</param>
/// <param name="Zoom">The zoom relative to the page's actual size (1 = 100 %).</param>
/// <param name="IsLoading">Whether a document is being opened.</param>
/// <param name="IsRendering">Whether tiles the viewport wants are still being drawn.</param>
/// <remarks>ISO 32000-2 §14.11.2 (page boundaries: the zoom's actual size is one point per 1/72 inch).</remarks>
public readonly record struct PdfViewState(int PageIndex, int PageCount, double Zoom, bool IsLoading, bool IsRendering);

/// <summary>Carries a <see cref="PdfView"/>'s new <see cref="PdfViewState"/>.</summary>
/// <param name="state">The new state.</param>
public sealed class PdfViewStateChangedEventArgs(PdfViewState state) : EventArgs
{
    /// <summary>Gets the new state.</summary>
    public PdfViewState State { get; } = state;
}

/// <summary>Carries an exception a <see cref="PdfView"/> caught while opening, drawing or loading a page.</summary>
/// <param name="exception">The exception.</param>
public sealed class PdfViewErrorEventArgs(Exception exception) : EventArgs
{
    /// <summary>Gets the exception.</summary>
    public Exception Exception { get; } = exception;
}
