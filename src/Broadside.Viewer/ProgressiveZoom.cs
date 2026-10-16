namespace Broadside.Viewer;

/// <summary>
/// Zooms a viewer progressively: each zoom step shows at once, drawn from the tiles already on the surface (stretched or shrunk, over
/// each page's paper, so nothing goes blank), keeps the anchor (the pointer or the pinch centre) on the same page point, and once the
/// zoom settles has the <see cref="TileScheduler"/> render sharp tiles at the settled scale in time-sliced frames, evicting the tiles of
/// the old scale as the new ones cover the pages.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33 viewer capability 2. Framework-neutral and single-threaded (no timers, tasks or threads, so it runs on the browser's one
/// thread): the shell, or the input model of spec viewer capability 14, calls <see cref="SetViewport"/> for scrolls and resizes,
/// <see cref="ZoomBy"/>/<see cref="ZoomTo"/> for zoom steps (bracketed by <see cref="BeginGesture"/> and <see cref="EndGesture"/> for a
/// pinch or an animation), and once per frame <see cref="RunFrame"/> then <see cref="GetFrame"/>, drawing the parts in order.
/// </para>
/// <para>
/// While a zoom is in progress no tile renders: intermediate scales would be thrown away. Steps outside an explicit gesture (mouse
/// wheel, keyboard) settle once they pause for <see cref="ProgressiveZoomOptions.SettleDelay"/>; a gesture settles when it ends. The
/// zoom is held within <see cref="ProgressiveZoomOptions.MinZoom"/> and <see cref="ProgressiveZoomOptions.MaxZoom"/>. Steps, frames and
/// <see cref="GetFrame"/> allocate nothing once the scheduler's buffers have grown to the viewport.
/// </para>
/// </remarks>
public sealed class ProgressiveZoom
{
    private readonly TileScheduler _scheduler;
    private readonly TimeProvider _time;
    private readonly double _minZoom;
    private readonly double _maxZoom;
    private readonly long _settleTicks;
    private Viewport _viewport;
    private bool _zooming;
    private bool _gesture;
    private long _lastStep;

    /// <summary>Initializes a new instance of the <see cref="ProgressiveZoom"/> class and shows <paramref name="viewport"/>.</summary>
    /// <param name="scheduler">The scheduler that renders the tiles; the zoom drives its <see cref="TileScheduler.Update"/>.</param>
    /// <param name="viewport">The viewport to start from (its zoom is kept as given).</param>
    /// <param name="options">The zoom range and settle delay, or <see langword="null"/> for defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException">The viewport is empty or its zoom is not positive and finite.</exception>
    /// <remarks>Spec #33 viewer capability 2.</remarks>
    public ProgressiveZoom(TileScheduler scheduler, in Viewport viewport, ProgressiveZoomOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        options ??= new ProgressiveZoomOptions();
        _scheduler = scheduler;
        _time = options.TimeProvider;
        _minZoom = options.MinZoom;
        _maxZoom = options.MaxZoom;
        _settleTicks = (long)(options.SettleDelay.Ticks * (_time.TimestampFrequency / (double)TimeSpan.TicksPerSecond));
        scheduler.Update(viewport);
        _viewport = viewport;
    }

    /// <summary>Gets the viewport the shell shows now (mid-zoom, the zoom of the last step).</summary>
    public Viewport Viewport => _viewport;

    /// <summary>Gets the layout the zoom moves through: the scheduler's current <see cref="TileScheduler.Layout"/>.</summary>
    public PageLayout Layout => _scheduler.Layout;

    /// <summary>Gets a value indicating whether a zoom is in progress: steps taken that have not settled, so no tile renders yet.</summary>
    public bool IsZooming => _zooming;

    /// <summary>Gets the smallest zoom a step goes to.</summary>
    public double MinZoom => _minZoom;

    /// <summary>Gets the largest zoom a step goes to.</summary>
    public double MaxZoom => _maxZoom;

    /// <summary>Shows a viewport after a scroll or a resize; mid-zoom (a pinch that also pans) it waits for the zoom to settle.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <exception cref="ArgumentOutOfRangeException">The viewport is empty or its zoom is not positive and finite.</exception>
    public void SetViewport(in Viewport viewport)
    {
        if (!_zooming)
        {
            _scheduler.Update(viewport);
        }
        else if (!(viewport.Width > 0) || !(viewport.Height > 0) || !(viewport.Zoom > 0) || !double.IsFinite(viewport.Zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(viewport), viewport, "A viewport needs a positive size and a positive, finite zoom.");
        }

        _viewport = viewport;
    }

    /// <summary>
    /// Starts an explicit gesture (a pinch, or an animated zoom such as a double-tap's): its steps show at once and nothing renders
    /// until <see cref="EndGesture"/>, however long it pauses.
    /// </summary>
    public void BeginGesture()
    {
        _zooming = true;
        _gesture = true;
        _lastStep = _time.GetTimestamp();
    }

    /// <summary>Multiplies the zoom by <paramref name="factor"/>, keeping the page point under the anchor where it is.</summary>
    /// <param name="factor">The zoom factor (above 1 zooms in).</param>
    /// <param name="anchorX">The anchor's viewport x (the pointer or the pinch centre).</param>
    /// <param name="anchorY">The anchor's viewport y.</param>
    /// <returns>The new viewport (also <see cref="Viewport"/>).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is not positive and finite.</exception>
    /// <remarks>Spec #33 viewer capability 2.</remarks>
    public Viewport ZoomBy(double factor, double anchorX, double anchorY)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "A zoom factor is positive and finite.");
        }

        return ZoomTo(_viewport.Zoom * factor, anchorX, anchorY);
    }

    /// <summary>
    /// Sets the zoom (held within <see cref="MinZoom"/> and <see cref="MaxZoom"/>), keeping the page point under the anchor where it is
    /// unless the scroll range stops it (<see cref="PageLayout.ZoomAt"/>). The step shows at once from the tiles on the surface; outside a
    /// gesture it starts (or extends) a zoom that settles after <see cref="ProgressiveZoomOptions.SettleDelay"/>.
    /// </summary>
    /// <param name="zoom">The zoom in pixels per point.</param>
    /// <param name="anchorX">The anchor's viewport x.</param>
    /// <param name="anchorY">The anchor's viewport y.</param>
    /// <returns>The new viewport (also <see cref="Viewport"/>).</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is not finite, or <paramref name="zoom"/> is not positive.</exception>
    /// <remarks>Spec #33 viewer capability 2.</remarks>
    public Viewport ZoomTo(double zoom, double anchorX, double anchorY)
    {
        if (!double.IsFinite(zoom) || zoom <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "A zoom is a positive, finite number of pixels per point.");
        }

        if (!double.IsFinite(anchorX) || !double.IsFinite(anchorY))
        {
            throw new ArgumentOutOfRangeException(nameof(anchorX), "An anchor is a finite viewport point.");
        }

        _viewport = _scheduler.Layout.ZoomAt(_viewport, Math.Clamp(zoom, _minZoom, _maxZoom), anchorX, anchorY);
        _zooming = true;
        _lastStep = _time.GetTimestamp();
        return _viewport;
    }

    /// <summary>Ends the gesture (or the pending steps) and settles: sharp tiles render at the current zoom from the next frame.</summary>
    /// <returns>The settled viewport.</returns>
    public Viewport EndGesture()
    {
        if (_zooming)
        {
            Settle();
        }

        _gesture = false;
        return _viewport;
    }

    /// <summary>
    /// Does one frame's work: settles a zoom whose steps have paused for the settle delay, and outside a zoom runs the scheduler's
    /// frame (<see cref="TileScheduler.RunFrame"/>), which draws or starts tiles within <paramref name="budget"/>.
    /// </summary>
    /// <param name="budget">The time the frame may take; <see cref="Timeout.InfiniteTimeSpan"/> works until nothing is left.</param>
    /// <returns>
    /// <see langword="true"/> when the shell should run another frame: tiles remain to draw, or pending steps wait to settle.
    /// <see langword="false"/> mid-gesture, until <see cref="EndGesture"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not infinite.</exception>
    /// <remarks>Spec #33 viewer capabilities 1 and 2. Allocates nothing per frame beyond the tiles it starts.</remarks>
    public bool RunFrame(TimeSpan budget)
    {
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "A frame budget must not be negative.");
        }

        if (_zooming)
        {
            if (_gesture)
            {
                return false;
            }

            if (_time.GetTimestamp() - _lastStep < _settleTicks)
            {
                return true;
            }

            Settle();
        }

        return _scheduler.RunFrame(budget);
    }

    /// <summary>Lists what the shell draws for the current <see cref="Viewport"/>, in order (<see cref="TileScheduler.GetFrame"/>).</summary>
    /// <param name="destination">Receives the parts.</param>
    /// <returns>How many parts the frame has; when more than fit, call again with a larger span (nothing is sorted until they fit).</returns>
    /// <remarks>Spec #33 viewer capability 2. Allocates nothing.</remarks>
    public int GetFrame(Span<FramePart> destination) => _scheduler.GetFrame(_viewport, destination);

    private void Settle()
    {
        _zooming = false;
        _scheduler.Update(_viewport);
    }
}
