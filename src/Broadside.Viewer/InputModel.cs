namespace Broadside.Viewer;

/// <summary>
/// Turns a shell's pointer, touch, wheel and keyboard events into scrolls, zooms and page turns of a <see cref="ProgressiveZoom"/>: a
/// drag scrolls and flings with inertia, two fingers pinch about their centre, a double-tap toggles between fit-width and a zoom anchored
/// at the tap, paged modes snap to spreads, Ctrl or Cmd with the wheel zooms about the pointer, and the keys page, jump and zoom.
/// </summary>
/// <remarks>
/// <para>
/// Spec #33 viewer capability 14. Framework-neutral: a shell translates its platform's events (the DOM's pointer, wheel and keyboard
/// events in the Blazor shell) into these calls one for one, with coordinates in device-independent pixels relative to the viewport's
/// top-left corner, and calls <see cref="RunFrame"/> once per display frame, then draws <see cref="ProgressiveZoom.GetFrame"/>.
/// </para>
/// <para>
/// Single-threaded and deterministic: no timers, tasks or threads (so it runs on the browser's one thread); every time it needs comes
/// from <see cref="InputModelOptions.TimeProvider"/>, so a manual clock replays a gesture exactly. Events and frames allocate nothing.
/// </para>
/// </remarks>
public sealed class InputModel
{
    /// <summary>The device-independent pixels an arrow key or one wheel line scrolls.</summary>
    public const double LineScroll = 40;

    private const double DipsPerPoint = 96.0 / 72;
    private const double TapSlop = 10;
    private const double DoubleTapSlop = 40;
    private const double TapSeconds = 0.3;
    private const double DoubleTapSeconds = 0.35;
    private const double VelocityWindowSeconds = 0.1;
    private const double MinFlingSpeed = 50;
    private const double StopSpeed = 10;
    private const double TurnSpeed = 300;
    private const double TurnFraction = 0.25;
    private const double ZoomSnapTolerance = 0.05;
    private const double FitTolerance = 0.01;
    private const double WheelZoomRate = 0.002;
    private const double KeyZoomStep = 1.25;
    private const double PageFraction = 0.9;
    private const double WheelBurstGapSeconds = 0.3;
    private const double EdgeTolerance = 0.5;
    private const int Samples = 8;

    private readonly ProgressiveZoom _zoom;
    private readonly TimeProvider _time;
    private readonly double _frequency;
    private readonly double _animationSeconds;
    private readonly double _flingSeconds;
    private readonly double _doubleTapZoom;
    private readonly bool _snapToPages;
    private readonly long[] _sampleTimes = new long[Samples];
    private readonly double[] _sampleX = new double[Samples];
    private readonly double[] _sampleY = new double[Samples];
    private double _pixelRatio;

    private int _pointers;
    private long _id0;
    private long _id1;
    private double _x0;
    private double _y0;
    private double _x1;
    private double _y1;
    private PointerKind _kind;
    private bool _panning;
    private bool _pinching;
    private bool _tap;
    private long _downTime;
    private double _downX;
    private double _downY;
    private double _panX;
    private double _panY;
    private int _dragSpread;
    private double _pinchDistance;
    private double _pinchX;
    private double _pinchY;
    private int _sampleCount;
    private int _sampleNext;
    private bool _hasTap;
    private long _tapTime;
    private double _tapX;
    private double _tapY;
    private long _wheelTime = long.MinValue / 2;
    private long _wheelBurst = long.MinValue / 2;
    private long _wheelTurn = long.MinValue / 2;

    private Motion _motion;
    private long _motionStart;
    private double _fromX;
    private double _fromY;
    private double _toX;
    private double _toY;
    private double _velocityX;
    private double _velocityY;
    private double _minAlong;
    private double _maxAlong;
    private double _fromZoom;
    private double _toZoom;
    private int _thenPage = -1;

    /// <summary>Initializes a new instance of the <see cref="InputModel"/> class over a progressive zoom.</summary>
    /// <param name="zoom">The zoom the model drives: every scroll and zoom goes through it, and its layout decides the snapping.</param>
    /// <param name="options">The clock, pixel ratio, animation and snapping settings, or <see langword="null"/> for defaults.</param>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public InputModel(ProgressiveZoom zoom, InputModelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(zoom);
        options ??= new InputModelOptions();
        _zoom = zoom;
        _time = options.TimeProvider;
        _frequency = _time.TimestampFrequency;
        _pixelRatio = options.PixelRatio;
        _animationSeconds = options.AnimationDuration.TotalSeconds;
        _flingSeconds = options.FlingTimeConstant.TotalSeconds;
        _doubleTapZoom = options.DoubleTapZoom;
        _snapToPages = options.SnapToPages;
    }

    private enum Motion
    {
        None,
        Scroll,
        Zoom,
        Fling,
    }

    /// <summary>Gets the progressive zoom the model drives.</summary>
    public ProgressiveZoom Zoom => _zoom;

    /// <summary>Gets the viewport the shell shows now (<see cref="ProgressiveZoom.Viewport"/>), in device pixels.</summary>
    public Viewport Viewport => _zoom.Viewport;

    /// <summary>Gets the page a viewer reports as current (<see cref="PageLayout.GetCurrentPage"/>), or -1 without pages.</summary>
    public int CurrentPage => _zoom.Layout.GetCurrentPage(_zoom.Viewport);

    /// <summary>Gets a value indicating whether a fling, a page turn, a snap or an animated zoom is running: keep calling <see cref="RunFrame"/>.</summary>
    public bool IsAnimating => _motion != Motion.None;

    /// <summary>Gets the zoom at which one point of a page is one point on the screen: <see cref="PixelRatio"/> times 96 / 72.</summary>
    public double ActualSize => _pixelRatio * DipsPerPoint;

    /// <summary>
    /// Gets or sets the device pixels per device-independent pixel (the DOM's <c>devicePixelRatio</c>): event coordinates are multiplied
    /// by it to reach the viewport's device pixels. When it changes, the shell also calls <see cref="Resize"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double PixelRatio
    {
        get => _pixelRatio;
        set => _pixelRatio = CheckPixelRatio(value);
    }

    private PageLayout Layout => _zoom.Layout;

    private bool Horizontal => _zoom.Layout.Mode == PageLayoutMode.Horizontal;

    private bool Paged => _zoom.Layout.Mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp;

    /// <summary>A pointer went down: the first starts a drag or a tap, a second finger or pen starts a pinch; a third is ignored.</summary>
    /// <param name="pointerId">The pointer's identifier (the DOM's <c>pointerId</c>).</param>
    /// <param name="kind">What the pointer is.</param>
    /// <param name="x">The x in device-independent pixels from the viewport's left edge.</param>
    /// <param name="y">The y in device-independent pixels from the viewport's top edge.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public void PointerDown(long pointerId, PointerKind kind, double x, double y)
    {
        CheckPoint(x, y);
        x *= _pixelRatio;
        y *= _pixelRatio;
        long now = _time.GetTimestamp();
        if (_pointers == 0)
        {
            Stop();
            (_pointers, _id0, _x0, _y0, _kind) = (1, pointerId, x, y, kind);
            (_panning, _pinching, _tap, _downTime, _downX, _downY) = (false, false, true, now, x, y);
            _dragSpread = SpreadAt(Viewport);
            ResetSamples(now, x, y);
        }
        else if (_pointers == 1 && pointerId != _id0 && kind != PointerKind.Mouse && _kind != PointerKind.Mouse)
        {
            (_pointers, _id1, _x1, _y1) = (2, pointerId, x, y);
            (_pinching, _panning, _tap, _hasTap) = (true, false, false, false);
            _pinchDistance = Distance();
            (_pinchX, _pinchY) = ((_x0 + _x1) / 2, (_y0 + _y1) / 2);
            _zoom.BeginGesture();
        }
    }

    /// <summary>A pointer that is down moved: drags scroll with it, a pinch zooms by the change of the fingers' distance about their centre and pans with the centre.</summary>
    /// <param name="pointerId">The pointer's identifier; pointers that are not down (a hovering mouse) are ignored.</param>
    /// <param name="x">The x in device-independent pixels.</param>
    /// <param name="y">The y in device-independent pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public void PointerMove(long pointerId, double x, double y)
    {
        CheckPoint(x, y);
        Move(pointerId, x * _pixelRatio, y * _pixelRatio, _time.GetTimestamp());
    }

    /// <summary>
    /// A pointer went up: a drag flings on (a finger or pen) or snaps (paged modes), a quick touch is a tap and two taps close together
    /// toggle the zoom; ending a pinch snaps to fit-width or fit-page when the zoom is within 5 % of it, and the other finger drags on.
    /// </summary>
    /// <param name="pointerId">The pointer's identifier.</param>
    /// <param name="x">The x in device-independent pixels.</param>
    /// <param name="y">The y in device-independent pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is not finite.</exception>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public void PointerUp(long pointerId, double x, double y)
    {
        CheckPoint(x, y);
        x *= _pixelRatio;
        y *= _pixelRatio;
        long now = _time.GetTimestamp();
        if (!IsDown(pointerId))
        {
            return;
        }

        Move(pointerId, x, y, now);
        if (_pinching)
        {
            if (pointerId == _id0)
            {
                (_id0, _x0, _y0) = (_id1, _x1, _y1);
            }

            (_pointers, _pinching, _panning, _panX, _panY) = (1, false, true, _x0, _y0);
            EndPinch();
            _dragSpread = SpreadAt(Viewport);
            ResetSamples(now, _x0, _y0);
            return;
        }

        _pointers = 0;
        if (_tap)
        {
            if (Seconds(_downTime, now) <= TapSeconds)
            {
                Tap(now, x, y);
            }
        }
        else if (_panning)
        {
            (double vx, double vy) = _kind == PointerKind.Mouse ? (0, 0) : Velocity(now);
            Release(-vx, -vy);
        }
    }

    /// <summary>The platform cancelled a pointer (it left the window or the system took the gesture): a pinch ends where it is, a drag snaps without inertia.</summary>
    /// <param name="pointerId">The pointer's identifier.</param>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public void PointerCancel(long pointerId)
    {
        if (!IsDown(pointerId))
        {
            return;
        }

        _pointers = 0;
        if (_pinching)
        {
            _pinching = false;
            _zoom.EndGesture();
        }
        else if (_panning)
        {
            Release(0, 0);
        }
    }

    /// <summary>
    /// A wheel turned: it scrolls (Shift turns a vertical wheel sideways; in a paged mode at a spread's edge it turns the spread, once per
    /// burst of wheel events), and with Ctrl or Cmd held it zooms about the pointer, as trackpad pinches arrive in browsers.
    /// </summary>
    /// <param name="deltaX">The horizontal delta (positive scrolls right).</param>
    /// <param name="deltaY">The vertical delta (positive scrolls down, or zooms out).</param>
    /// <param name="mode">The deltas' unit.</param>
    /// <param name="modifiers">The modifier keys held.</param>
    /// <param name="x">The pointer's x in device-independent pixels.</param>
    /// <param name="y">The pointer's y in device-independent pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is not finite.</exception>
    /// <remarks>Spec #33 viewer capability 14. Zoom steps settle after <see cref="ProgressiveZoomOptions.SettleDelay"/>.</remarks>
    public void Wheel(double deltaX, double deltaY, WheelDeltaMode mode, InputModifiers modifiers, double x, double y)
    {
        CheckPoint(deltaX, deltaY);
        CheckPoint(x, y);
        long now = _time.GetTimestamp();
        if (Seconds(_wheelTime, now) > WheelBurstGapSeconds)
        {
            _wheelBurst = now;
        }

        _wheelTime = now;
        Viewport v = Viewport;
        double unit = mode switch
        {
            WheelDeltaMode.Line => LineScroll * _pixelRatio,
            WheelDeltaMode.Page => PageFraction * Extent(v),
            _ => _pixelRatio,
        };

        if ((modifiers & (InputModifiers.Control | InputModifiers.Meta)) != 0)
        {
            Stop();
            _zoom.ZoomBy(Math.Exp(-deltaY * unit / _pixelRatio * WheelZoomRate), x * _pixelRatio, y * _pixelRatio);
            return;
        }

        if (_motion == Motion.Scroll && Paged)
        {
            return;
        }

        Stop();
        double dx = deltaX * unit;
        double dy = deltaY * unit;
        if ((modifiers & InputModifiers.Shift) != 0 && dx == 0)
        {
            (dx, dy) = (dy, 0);
        }

        ScrollBy(dx, dy, now, wheel: true);
    }

    /// <summary>A key went down (or repeated): arrows scroll a line, Page Up/Down and Space page, Home/End jump, plus, minus and zero zoom.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held (Shift with Space pages back; Ctrl or Cmd is accepted with the zoom keys).</param>
    /// <returns><see langword="true"/> when the key was handled, so the shell stops the platform's default action.</returns>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public bool KeyDown(InputKey key, InputModifiers modifiers)
    {
        long now = _time.GetTimestamp();
        double line = LineScroll * _pixelRatio;
        switch (key)
        {
            case InputKey.ArrowUp:
            case InputKey.ArrowDown:
                Stop();
                ScrollBy(0, key == InputKey.ArrowUp ? -line : line, now, wheel: false);
                return true;
            case InputKey.ArrowLeft:
            case InputKey.ArrowRight:
                Stop();
                double dx = key == InputKey.ArrowLeft ? -line : line;
                Viewport v = Viewport;
                if (Paged && Layout.Clamp(v with { X = v.X + dx }).X == v.X)
                {
                    Turn(dx > 0 ? 1 : -1);
                }
                else
                {
                    ScrollBy(dx, 0, now, wheel: false);
                }

                return true;
            case InputKey.PageUp:
            case InputKey.PageDown:
            case InputKey.Space:
                Page(key == InputKey.PageUp || (key == InputKey.Space && (modifiers & InputModifiers.Shift) != 0) ? -1 : 1);
                return true;
            case InputKey.Home:
            case InputKey.End:
                if (Layout.PageCount > 0)
                {
                    GoToPage(key == InputKey.Home ? 0 : Layout.PageCount - 1);
                }

                return true;
            case InputKey.Plus:
            case InputKey.Minus:
                Stop();
                _zoom.ZoomBy(key == InputKey.Plus ? KeyZoomStep : 1 / KeyZoomStep, Viewport.Width / 2, Viewport.Height / 2);
                return true;
            case InputKey.Zero:
                ZoomTo(ZoomMode.FitWidth);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The viewport's size changed: keeps the scroll position (kept inside the content) at the new size.</summary>
    /// <param name="width">The width in device-independent pixels.</param>
    /// <param name="height">The height in device-independent pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive and finite.</exception>
    public void Resize(double width, double height)
    {
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A viewport has a positive, finite size.");
        }

        Stop();
        Viewport v = Viewport;
        _zoom.SetViewport(Layout.Clamp(v with { Width = width * _pixelRatio, Height = height * _pixelRatio }));
    }

    /// <summary>Goes to a page (animated): in a paged mode its spread centred, otherwise its spread's leading edge at the viewport's (<see cref="PageLayout.ScrollTo"/>).</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageIndex"/> is outside the layout.</exception>
    /// <remarks>Spec #33 viewer capability 14.</remarks>
    public void GoToPage(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, Layout.PageCount);
        Stop();
        AnimateScroll(PageTarget(Viewport, pageIndex));
    }

    /// <summary>Goes to the first page of the next spread (from where a running page animation is heading).</summary>
    public void NextPage() => Turn(1);

    /// <summary>Goes to the first page of the previous spread (from where a running page animation is heading).</summary>
    public void PreviousPage() => Turn(-1);

    /// <summary>Zooms (animated, about the viewport's centre) to actual size or to fit the current page's spread; fit-page also brings the page into view.</summary>
    /// <param name="mode">The zoom.</param>
    /// <remarks>Spec #33 viewer capability 14 (<see cref="PageLayout.GetZoom"/>, at <see cref="ActualSize"/>).</remarks>
    public void ZoomTo(ZoomMode mode)
    {
        Stop();
        Viewport v = Viewport;
        int page = Layout.GetCurrentPage(v);
        if (page < 0)
        {
            return;
        }

        _thenPage = mode == ZoomMode.FitPage ? page : -1;
        StartZoom(Layout.GetZoom(mode, v.Width, v.Height, page, ActualSize), v.Width / 2, v.Height / 2);
    }

    /// <summary>Stops a running fling or animation where it is (an animated zoom settles at its current zoom).</summary>
    public void Stop()
    {
        if (_motion == Motion.Zoom)
        {
            _zoom.EndGesture();
        }

        _motion = Motion.None;
        _thenPage = -1;
    }

    /// <summary>
    /// Does one display frame's work: advances a fling or an animation to the clock's time, then the zoom's frame
    /// (<see cref="ProgressiveZoom.RunFrame"/>), which settles zoom steps and draws tiles within <paramref name="budget"/>.
    /// </summary>
    /// <param name="budget">The time the frame may spend drawing tiles.</param>
    /// <returns><see langword="true"/> when the shell should run another frame: something animates, or tiles remain to draw.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="budget"/> is negative and not infinite.</exception>
    /// <remarks>Spec #33 viewer capabilities 2 and 14. Allocates nothing beyond the tiles the scheduler starts.</remarks>
    public bool RunFrame(TimeSpan budget)
    {
        if (budget < TimeSpan.Zero && budget != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "A frame budget must not be negative.");
        }

        long now = _time.GetTimestamp();
        switch (_motion)
        {
            case Motion.Scroll:
                AdvanceScroll(now);
                break;
            case Motion.Zoom:
                AdvanceZoom(now);
                break;
            case Motion.Fling:
                AdvanceFling(now);
                break;
        }

        bool more = _zoom.RunFrame(budget);
        return more || _motion != Motion.None;
    }

    internal static double CheckPixelRatio(double pixelRatio)
    {
        if (!double.IsFinite(pixelRatio) || pixelRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRatio), pixelRatio, "A pixel ratio is positive and finite.");
        }

        return pixelRatio;
    }

    private static void CheckPoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Event coordinates and deltas are finite.");
        }
    }

    private static double Ease(double progress) => 1 - Math.Pow(1 - progress, 3);

    private double Seconds(long from, long to) => (to - from) / _frequency;

    private bool IsDown(long pointerId) => _pointers > 0 && (pointerId == _id0 || (_pointers == 2 && pointerId == _id1));

    private double Distance() => Math.Sqrt(((_x1 - _x0) * (_x1 - _x0)) + ((_y1 - _y0) * (_y1 - _y0)));

    private void Move(long pointerId, double x, double y, long now)
    {
        if (!IsDown(pointerId))
        {
            return;
        }

        if (pointerId == _id0)
        {
            (_x0, _y0) = (x, y);
        }
        else
        {
            (_x1, _y1) = (x, y);
        }

        if (_pinching)
        {
            double distance = Distance();
            double cx = (_x0 + _x1) / 2;
            double cy = (_y0 + _y1) / 2;
            Viewport v = Viewport;
            _zoom.SetViewport(Layout.Clamp(v with { X = v.X - (cx - _pinchX), Y = v.Y - (cy - _pinchY) }));
            if (_pinchDistance > 0 && distance > 0)
            {
                _zoom.ZoomBy(distance / _pinchDistance, cx, cy);
            }

            (_pinchDistance, _pinchX, _pinchY) = (distance, cx, cy);
            return;
        }

        if (!_panning)
        {
            double sx = x - _downX;
            double sy = y - _downY;
            if (Math.Sqrt((sx * sx) + (sy * sy)) <= TapSlop * _pixelRatio)
            {
                return;
            }

            (_panning, _tap, _hasTap, _panX, _panY) = (true, false, false, _downX, _downY);
        }

        Viewport current = Viewport;
        _zoom.SetViewport(Layout.Clamp(current with { X = current.X - (x - _panX), Y = current.Y - (y - _panY) }));
        (_panX, _panY) = (x, y);
        AddSample(now, x, y);
    }

    private void ResetSamples(long now, double x, double y)
    {
        _sampleCount = 0;
        _sampleNext = 0;
        AddSample(now, x, y);
    }

    private void AddSample(long now, double x, double y)
    {
        (_sampleTimes[_sampleNext], _sampleX[_sampleNext], _sampleY[_sampleNext]) = (now, x, y);
        _sampleNext = (_sampleNext + 1) % Samples;
        _sampleCount = Math.Min(_sampleCount + 1, Samples);
    }

    /// <summary>The pointer's velocity in pixels per second over the last tenth of a second, zero when it has rested since.</summary>
    private (double X, double Y) Velocity(long now)
    {
        int newest = (_sampleNext + Samples - 1) % Samples;
        if (_sampleCount < 2 || Seconds(_sampleTimes[newest], now) > VelocityWindowSeconds)
        {
            return (0, 0);
        }

        int oldest = newest;
        for (int i = 1; i < _sampleCount; i++)
        {
            int k = (newest + Samples - i) % Samples;
            if (Seconds(_sampleTimes[k], _sampleTimes[newest]) > VelocityWindowSeconds)
            {
                break;
            }

            oldest = k;
        }

        double dt = Seconds(_sampleTimes[oldest], _sampleTimes[newest]);
        return dt > 0 ? ((_sampleX[newest] - _sampleX[oldest]) / dt, (_sampleY[newest] - _sampleY[oldest]) / dt) : (0, 0);
    }

    private void Tap(long now, double x, double y)
    {
        double dx = x - _tapX;
        double dy = y - _tapY;
        if (_hasTap && Seconds(_tapTime, now) <= DoubleTapSeconds && Math.Sqrt((dx * dx) + (dy * dy)) <= DoubleTapSlop * _pixelRatio)
        {
            _hasTap = false;
            DoubleTap(x, y);
        }
        else
        {
            (_hasTap, _tapTime, _tapX, _tapY) = (true, now, x, y);
        }
    }

    private void DoubleTap(double x, double y)
    {
        Viewport v = Viewport;
        int page = Layout.TryGetPageAt(v, x, y, out PagePoint point) ? point.PageIndex : Layout.GetCurrentPage(v);
        if (page < 0)
        {
            return;
        }

        double fitWidth = Layout.GetZoom(ZoomMode.FitWidth, v.Width, v.Height, page, ActualSize);
        StartZoom(Math.Abs((v.Zoom / fitWidth) - 1) <= FitTolerance ? fitWidth * _doubleTapZoom : fitWidth, x, y);
    }

    private void EndPinch()
    {
        Viewport v = Viewport;
        int page = Layout.GetCurrentPage(v);
        if (page >= 0)
        {
            double fitWidth = Layout.GetZoom(ZoomMode.FitWidth, v.Width, v.Height, page, ActualSize);
            double fitPage = Layout.GetZoom(ZoomMode.FitPage, v.Width, v.Height, page, ActualSize);
            double target = Math.Abs((v.Zoom / fitWidth) - 1) <= ZoomSnapTolerance ? fitWidth
                : Math.Abs((v.Zoom / fitPage) - 1) <= ZoomSnapTolerance ? fitPage : 0;
            if (target > 0)
            {
                StartZoom(target, _pinchX, _pinchY);
                return;
            }
        }

        _zoom.EndGesture();
    }

    private void StartZoom(double target, double anchorX, double anchorY)
    {
        target = Math.Clamp(target, _zoom.MinZoom, _zoom.MaxZoom);
        _motion = Motion.None;
        _fromZoom = Viewport.Zoom;
        (_toZoom, _fromX, _fromY) = (target, anchorX, anchorY);
        if (_animationSeconds <= 0 || target == _fromZoom)
        {
            FinishZoom();
            return;
        }

        _zoom.BeginGesture();
        (_motion, _motionStart) = (Motion.Zoom, _time.GetTimestamp());
    }

    private void AdvanceZoom(long now)
    {
        double progress = Seconds(_motionStart, now) / _animationSeconds;
        if (progress >= 1)
        {
            _motion = Motion.None;
            FinishZoom();
        }
        else
        {
            _zoom.ZoomTo(_fromZoom * Math.Pow(_toZoom / _fromZoom, Ease(progress)), _fromX, _fromY);
        }
    }

    private void FinishZoom()
    {
        _zoom.ZoomTo(_toZoom, _fromX, _fromY);
        _zoom.EndGesture();
        if (_thenPage >= 0)
        {
            int page = _thenPage;
            _thenPage = -1;
            _zoom.SetViewport(PageTarget(Viewport, page));
        }
    }

    private void Release(double vx, double vy)
    {
        if (_motion == Motion.Zoom)
        {
            return;
        }

        Viewport v = Viewport;
        if ((Paged || _snapToPages) && _dragSpread >= 0)
        {
            (double lo, double hi) = SpreadRange(_dragSpread, v);
            double along = Along(v);
            double velocity = Horizontal ? vx : vy;
            double turnDistance = TurnFraction * Extent(v);
            double turnSpeed = TurnSpeed * _pixelRatio;
            if (_dragSpread + 1 < Layout.SpreadCount && (along > hi + turnDistance || (along > hi + EdgeTolerance && velocity > turnSpeed)))
            {
                AnimateScroll(SpreadTarget(v, _dragSpread + 1, atStart: true));
            }
            else if (_dragSpread > 0 && (along < lo - turnDistance || (along < lo - EdgeTolerance && velocity < -turnSpeed)))
            {
                AnimateScroll(SpreadTarget(v, _dragSpread - 1, atStart: true));
            }
            else if (along < lo || along > hi)
            {
                AnimateScroll(Layout.Clamp(WithAlong(v, Math.Clamp(along, lo, hi))));
            }
            else
            {
                StartFling(v, vx, vy, lo, hi);
            }

            return;
        }

        StartFling(v, vx, vy, 0, MaxAlong(v));
    }

    private void StartFling(in Viewport v, double vx, double vy, double minAlong, double maxAlong)
    {
        if (_flingSeconds <= 0 || Math.Sqrt((vx * vx) + (vy * vy)) < MinFlingSpeed * _pixelRatio)
        {
            return;
        }

        (_motion, _motionStart, _fromX, _fromY) = (Motion.Fling, _time.GetTimestamp(), v.X, v.Y);
        (_velocityX, _velocityY, _minAlong, _maxAlong) = (vx, vy, minAlong, maxAlong);
    }

    private void AdvanceFling(long now)
    {
        double t = Seconds(_motionStart, now);
        double decay = Math.Exp(-t / _flingSeconds);
        double travel = _flingSeconds * (1 - decay);
        Viewport v = Viewport;
        Viewport next = Layout.Clamp(v with { X = _fromX + (_velocityX * travel), Y = _fromY + (_velocityY * travel) });
        next = WithAlong(next, Math.Clamp(Along(next), _minAlong, _maxAlong));
        double speed = Math.Sqrt((_velocityX * _velocityX) + (_velocityY * _velocityY)) * decay;
        if (speed < StopSpeed * _pixelRatio || (t > 0 && next == v))
        {
            _motion = Motion.None;
        }

        _zoom.SetViewport(next);
    }

    private void AnimateScroll(in Viewport target)
    {
        Viewport v = Viewport;
        if (_animationSeconds <= 0 || (target.X == v.X && target.Y == v.Y))
        {
            _motion = Motion.None;
            _zoom.SetViewport(target);
            return;
        }

        (_motion, _motionStart, _fromX, _fromY, _toX, _toY) = (Motion.Scroll, _time.GetTimestamp(), v.X, v.Y, target.X, target.Y);
    }

    private void AdvanceScroll(long now)
    {
        double progress = Seconds(_motionStart, now) / _animationSeconds;
        Viewport v = Viewport;
        if (progress >= 1)
        {
            _motion = Motion.None;
            _zoom.SetViewport(v with { X = _toX, Y = _toY });
            return;
        }

        double eased = Ease(progress);
        _zoom.SetViewport(v with { X = _fromX + ((_toX - _fromX) * eased), Y = _fromY + ((_toY - _fromY) * eased) });
    }

    /// <summary>Scrolls by a delta; in a paged mode stays within the current spread, turning it at the edge the delta pushes against.</summary>
    private void ScrollBy(double dx, double dy, long now, bool wheel)
    {
        Viewport v = Viewport;
        Viewport target = Layout.Clamp(v with { X = v.X + dx, Y = v.Y + dy });
        int spread = Paged ? SpreadAt(v) : -1;
        if (spread >= 0)
        {
            (double lo, double hi) = SpreadRange(spread, v);
            double delta = Horizontal ? dx : dy;
            double along = Along(v);
            if ((delta > 0 && along >= hi - EdgeTolerance) || (delta < 0 && along <= lo + EdgeTolerance))
            {
                int next = spread + (delta > 0 ? 1 : -1);
                if (next >= 0 && next < Layout.SpreadCount && (!wheel || _wheelTurn < _wheelBurst))
                {
                    _wheelTurn = wheel ? now : _wheelTurn;
                    AnimateScroll(SpreadTarget(v, next, atStart: delta > 0));
                }

                return;
            }

            target = WithAlong(target, Math.Clamp(Along(target), lo, hi));
        }

        _zoom.SetViewport(target);
    }

    private void Page(int direction)
    {
        if (Paged)
        {
            Turn(direction);
            return;
        }

        Viewport destination = Destination();
        Stop();
        AnimateScroll(Layout.Clamp(WithAlong(destination, Along(destination) + (direction * PageFraction * Extent(destination)))));
    }

    private void Turn(int direction)
    {
        Viewport destination = Destination();
        int page = Layout.GetCurrentPage(destination);
        if (page < 0)
        {
            return;
        }

        int spread = Layout.GetSpread(page) + direction;
        if (spread >= 0 && spread < Layout.SpreadCount)
        {
            GoToPage(Layout.GetSpreadPages(spread).FirstPage);
        }
    }

    /// <summary>Where a running scroll animation is heading, or the viewport.</summary>
    private Viewport Destination() => _motion == Motion.Scroll ? Viewport with { X = _toX, Y = _toY } : Viewport;

    private Viewport PageTarget(in Viewport v, int pageIndex)
    {
        Viewport target = Layout.ScrollTo(v, pageIndex);
        return Paged ? SpreadTarget(target, Layout.GetSpread(pageIndex), atStart: true) : target;
    }

    private Viewport SpreadTarget(in Viewport v, int spread, bool atStart)
    {
        (double lo, double hi) = SpreadRange(spread, v);
        return Layout.Clamp(WithAlong(v, atStart ? lo : hi));
    }

    private int SpreadAt(in Viewport v)
    {
        int page = Layout.GetCurrentPage(v);
        return page < 0 ? -1 : Layout.GetSpread(page);
    }

    /// <summary>
    /// The scroll offsets along the strip at which a spread fills the viewport: from its leading edge to its trailing edge (with the
    /// padding) when it is longer than the viewport, otherwise the one offset that centres it; kept inside the content.
    /// </summary>
    private (double Low, double High) SpreadRange(int spread, in Viewport v)
    {
        LayoutRectangle bounds = Layout.GetSpreadBounds(spread);
        double zoom = v.Zoom;
        double start = ((Horizontal ? bounds.X : bounds.Y) - Layout.Padding) * zoom;
        double end = ((Horizontal ? bounds.Right : bounds.Bottom) + Layout.Padding) * zoom;
        double extent = Extent(v);
        double max = MaxAlong(v);
        if (end - start <= extent)
        {
            double centred = Math.Clamp((start + end - extent) / 2, 0, max);
            return (centred, centred);
        }

        double low = Math.Clamp(start, 0, max);
        return (low, Math.Clamp(end - extent, low, max));
    }

    private double MaxAlong(in Viewport v) =>
        Math.Max(0, Horizontal ? (Layout.ContentWidth * v.Zoom) - v.Width : (Layout.ContentHeight * v.Zoom) - v.Height);

    private double Along(in Viewport v) => Horizontal ? v.X : v.Y;

    private double Extent(in Viewport v) => Horizontal ? v.Width : v.Height;

    private Viewport WithAlong(in Viewport v, double along) => Horizontal ? v with { X = along } : v with { Y = along };
}
