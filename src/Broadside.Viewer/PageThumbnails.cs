using System.Diagnostics;
using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Page thumbnails for a document session: each page rendered whole at a low resolution from the page's display list in the document's
/// shared cache, one after another and behind the main view. A list the main view already built is replayed, not built again, and a
/// list a thumbnail builds is there for the main view; a thumbnail never costs a second interpretation of the page.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.11.2 (the page's displayed region) and §8.2 (display lists replay the page). Spec #33 viewer capability 10.
/// </para>
/// <para>
/// Priority: before each thumbnail the renderer waits while the session has other work pending (page loads, list builds, tile renders), up
/// to <see cref="ThumbnailOptions.MaxDeferral"/>, so tiles the reader is waiting for come first and thumbnails fill in when the
/// viewer is quiet; a viewer that is never quiet (search indexing, a long scroll) still gets one thumbnail per deferral. The
/// thumbnails are drawn with the session's color filter (<see cref="DocumentSession.ColorFilter"/>): a dark viewer shows dark
/// thumbnails, and the shell draws them again after it changes the filter.
/// </para>
/// <para>
/// Rendering is sequential on the session's tile path (one session slot at a time, so thumbnails cannot take all of them from the
/// scheduler), and each finished thumbnail goes to the surface as a <see cref="Tile"/> of its page (<see cref="Tile.Scale"/> is the
/// thumbnail's scale; the bitmap is lent, copy what you keep). Cancel the token to stop, for example when the strip scrolls to other pages.
/// </para>
/// </remarks>
public sealed class PageThumbnails
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

    private readonly DocumentSession _session;
    private readonly IRenderSurface _surface;
    private readonly ThumbnailOptions _options;

    /// <summary>Creates a thumbnail renderer.</summary>
    /// <param name="session">The session whose cached display lists the thumbnails replay.</param>
    /// <param name="surface">Where each finished thumbnail goes (see <see cref="IRenderSurface"/>; a background session presents from a worker thread).</param>
    /// <param name="options">The thumbnails' size and priority, or <see langword="null"/> for the defaults.</param>
    public PageThumbnails(DocumentSession session, IRenderSurface surface, ThumbnailOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(surface);
        _session = session;
        _surface = surface;
        _options = options ?? new ThumbnailOptions();
    }

    /// <summary>Raised when a page's thumbnail could not be drawn (the page cannot be read or drawn); the pages after it are still drawn.</summary>
    public event EventHandler<ThumbnailFailedEventArgs>? Failed;

    /// <summary>Gets the scale a page's thumbnail is drawn at: pixels per point, so the page's longer side is <see cref="ThumbnailOptions.Size"/> pixels.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="cancellationToken">Stops loading the page.</param>
    /// <returns>The scale.</returns>
    public async ValueTask<double> GetScaleAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        PageMetrics metrics = await _session.GetPageMetricsAsync(pageIndex, cancellationToken).ConfigureAwait(true);
        double longer = Math.Max(metrics.Width, metrics.Height);
        return longer > 0 ? _options.Size / longer : 1;
    }

    /// <summary>Draws the thumbnails of some pages, in the order given, each after the session's other work.</summary>
    /// <param name="pages">The zero-based pages, nearest the reader first (a page twice is drawn twice).</param>
    /// <param name="cancellationToken">Stops drawing; the thumbnail in progress never reaches the surface.</param>
    /// <returns>How many thumbnails reached the surface.</returns>
    /// <exception cref="OperationCanceledException">The token was canceled or the session closed.</exception>
    public async Task<int> RenderAsync(IEnumerable<int> pages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        int drawn = 0;
        foreach (int page in pages)
        {
            await WaitForQuietAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                double scale = await GetScaleAsync(page, cancellationToken).ConfigureAwait(true);
                await _session.RenderPageAsync(page, scale, _surface, cancellationToken).ConfigureAwait(true);
                drawn++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
            {
                Failed?.Invoke(this, new ThumbnailFailedEventArgs(page, exception));
            }
        }

        return drawn;
    }

    private async ValueTask WaitForQuietAsync(CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        while (_session.Statistics.PendingOperations > 0 && Stopwatch.GetElapsedTime(start) < _options.MaxDeferral)
        {
            await Task.Delay(Poll, cancellationToken).ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
