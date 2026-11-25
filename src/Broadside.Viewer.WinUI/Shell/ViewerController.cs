using System.Diagnostics;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.WinUI;

/// <summary>
/// The WinUI shell's logic, free of any WinUI type: it maps pointers (mouse, touch, pen, wheel) and key names onto the viewer core
/// (<see cref="InputModel"/>, <see cref="TextSelection"/>, <see cref="AnnotationLayer"/>, <see cref="AccessibilityBridge"/>,
/// <see cref="DocumentSearch"/>) and composes frames from the <see cref="TileScheduler"/>'s tiles for the host.
/// </summary>
/// <remarks>
/// A port of the MAUI shell's controller (#226) with the Blazor shell's search and dark mode (#215, #219): the input model owns the
/// gestures (#213); this class only routes pointers to the annotation layer, the text selection or the input model. Coordinates
/// arrive in device-independent units; frames are composed in device pixels. All members run on the UI thread; tiles render on
/// workers in a background session and reach the UI thread through <see cref="TileStore.Drain"/>, so tiles never render on the UI thread.
/// </remarks>
internal sealed class ViewerController : IDisposable
{
    internal static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(8);
    internal static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan IdleFrame = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan RelayoutInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClickInterval = TimeSpan.FromMilliseconds(350);
    private const double ClickSlop = 6;
    private const double TouchSlop = 10;
    private const double HandleReach = 28;
    private const double HandleRadius = 7;
    private const DisplayLayers OverlayLayers = DisplayLayers.Annotations | DisplayLayers.FormFields;
    private const DisplayLayers AllLayers = DisplayLayers.Content | OverlayLayers;

    private readonly IViewerHost _host;
    private readonly SynchronizationContext? _ui;
    private int _wakePosted;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _closing = new();
    private readonly HashSet<int> _requested = [];
    private readonly List<int> _accessiblePages = [];
    private FramePart[] _parts = new FramePart[64];
    private VisiblePage[] _visible = new VisiblePage[8];
    private Quad[] _quads = new Quad[64];
    private double _width;
    private double _height;
    private double _ratio = 1;
    private DocumentSession? _session;
    private bool _ownsSession;
    private int _generation;
    private PageMetrics[] _metrics = [];
    private bool _metricsDirty;
    private long _lastRelayout;
    private PageLayoutOptions? _layoutOptions;
    private TileScheduler? _scheduler;
    private ProgressiveZoom? _zoom;
    private InputModel? _input;
    private TextSelection? _selection;
    private AnnotationLayer? _annotations;
    private FormFilling? _filling;
    private AccessibilityBridge? _bridge;
    private DocumentSearch? _search;
    private ColorFilter? _colorFilter;
    private bool _showAnnotations = true;
    private bool _interacted;
    private bool _frameRequested;
    private PointerMode _mode;
    private int _panPointers;
    private long _selectPointer = -1;
    private FieldEditor? _editor;
    private LayoutRectangle _editorBounds;
    private PdfViewState _state;
    private int _pendingLoads;
    private bool _keyboardFocus;
    private bool _mouseHover;
    private bool _disposed;

    // Touch: a press that stays put on text turns into a word selection after LongPress.
    private long _pressId = -1;
    private long _pressTime;
    private double _pressX;
    private double _pressY;
    private bool _pressOnText;

    // Mouse: consecutive clicks (word, line, paragraph granularity).
    private long _lastClick;
    private double _lastClickX;
    private double _lastClickY;
    private int _clicks;

    // Selection handles (touch), in DIPs; valid after a frame drew them.
    private bool _handlesShown;
    private PointerKind _lastPointerKind = PointerKind.Touch;
    private PathPoint _startHandle;
    private PathPoint _endHandle;

    public ViewerController(IViewerHost host, ITileImageFactory images, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(images);
        _host = host;
        _time = time ?? TimeProvider.System;
        Surface = new TileStore(images, _time);

        // A background session presents on workers (#357): the frame request goes back to the UI thread.
        _ui = SynchronizationContext.Current;
        Surface.Presented += (_, _) => WakeFromAnyThread();
    }

    private enum PointerMode
    {
        None,
        Pan,
        Select,
        Handle,
        Annotation,
    }

    /// <summary>Gets the newest composed frame (the host shows it; tests read it).</summary>
    public ViewerFrame LatestFrame { get; } = new();

    internal TileStore Surface { get; }

    public bool AllowAnnotationEditing { get; set; }

    /// <summary>Receives the tile scheduler's trace (diagnostics and tests); set before opening.</summary>
    public Action<TileSchedulerEvent>? SchedulerObserver { get; set; }

    public DocumentSession? Session => _session;

    public TextSelection? Selection => _selection;

    public AnnotationLayer? Annotations => _annotations;

    /// <summary>The form filling of the open document (#218): Tab order, one commit per edit through the form API.</summary>
    public FormFilling? Forms => _filling;

    public AccessibilityBridge? Bridge => _bridge;

    public DocumentSearch? Search => _search;

    /// <summary>Gets the color filter the open document is drawn with (the session's own until <see cref="SetColorFilter"/>).</summary>
    public ColorFilter ColorFilter => _session?.ColorFilter ?? _colorFilter ?? ColorFilter.None;

    public InputModel? Input => _input;

    public ProgressiveZoom? Zoom => _zoom;

    public TileScheduler? Scheduler => _scheduler;

    public FieldEditor? Editor => _editor;

    public IReadOnlyList<int> AccessiblePages => _accessiblePages;

    public PdfViewState State => _state;

    /// <summary>Whether visible pages' text, links and accessibility nodes are still loading.</summary>
    internal bool IsLoadingPages => _pendingLoads > 0;

    public double PixelRatio => _ratio;

    // ---- Documents ----

    /// <summary>Opens the session <paramref name="open"/> returns and shows the top of page 1 at fit-width once the view has a size.</summary>
    public async Task OpenAsync(Func<CancellationToken, Task<DocumentSession>> open, bool ownsSession, PageLayoutOptions? layoutOptions)
    {
        ArgumentNullException.ThrowIfNull(open);
        Close();
        int generation = ++_generation;
        _layoutOptions = CopyLayout(layoutOptions);
        SetState(_state with { IsLoading = true, PageCount = 0, PageIndex = 0 });
        DocumentSession session = await open(_closing.Token).ConfigureAwait(true);
        if (generation != _generation || _closing.IsCancellationRequested)
        {
            if (ownsSession)
            {
                await session.DisposeAsync().ConfigureAwait(true);
            }

            return;
        }

        _session = session;
        _ownsSession = ownsSession;
        if (_colorFilter is { } filter)
        {
            session.ColorFilter = filter;
        }

        if (session.PageCount == 0)
        {
            SetState(_state with { IsLoading = false });
            return;
        }

        // Page 1's size stands in for every page until its own arrives (#211's seam), so a remote file draws page 1 at once.
        PageMetrics first = await session.GetPageMetricsAsync(0, _closing.Token).ConfigureAwait(true);
        if (generation != _generation)
        {
            return;
        }

        _metrics = new PageMetrics[session.PageCount];
        for (int i = 0; i < _metrics.Length; i++)
        {
            _metrics[i] = session.TryGetPageMetrics(i, out PageMetrics known) ? known : first with { Index = i };
        }

        PageLayout layout = PageLayout.Create(_metrics, _layoutOptions);

        // Overlay tiles of their own: a form or annotation edit redraws only the transparent overlay tiles it touches, never the page.
        var schedulerOptions = new TileSchedulerOptions().WithSeparateOverlays();
        if (SchedulerObserver is { } observer)
        {
            schedulerOptions.UseObserver(observer);
        }

        _scheduler = new TileScheduler(session, layout, Surface.ForCurrentSession(), schedulerOptions);
        if (!_showAnnotations)
        {
            _scheduler.SetLayers(DisplayLayers.Content);
        }

        _interacted = false;
        _zoom = new ProgressiveZoom(_scheduler, InitialViewport(layout), new ProgressiveZoomOptions().UseTimeProvider(_time));
        _input = new InputModel(_zoom, new InputModelOptions().WithPixelRatio(_ratio).UseTimeProvider(_time));
        _input.Resize(Math.Max(_width, 1), Math.Max(_height, 1));
        _selection = new TextSelection(session);
        _selection.Changed += (_, _) => Wake();
        _search = new DocumentSearch(session);
        _search.Changed += OnSearchChanged;
        _annotations = new AnnotationLayer(session, _scheduler);
        _annotations.ActionRequested += OnActionRequested;
        _annotations.Changed += OnAnnotationsChanged;
        _filling = new FormFilling(_annotations);
        _filling.Changed += OnFillingChanged;
        _bridge = new AccessibilityBridge(session, _annotations) { Viewer = _zoom, Observer = OnAccessibilityChange };
        _annotations.Accessibility = _bridge;
        SetState(_state with { IsLoading = false, PageCount = session.PageCount, IsRendering = true });
        _ = SweepMetricsAsync(session, generation);
        Wake();
    }

    public void SetShowAnnotations(bool show)
    {
        _showAnnotations = show;
        if (!show)
        {
            Surface.ExpireStale(all: true);
        }

        _scheduler?.SetLayers(show ? AllLayers : DisplayLayers.Content);
        Wake();
    }

    /// <summary>
    /// Sets the color filter of the open document and of those opened later; null keeps each session's own. A change draws every
    /// tile again from the cached display lists (spec #33 viewer capability 9).
    /// </summary>
    public void SetColorFilter(ColorFilter? filter)
    {
        _colorFilter = filter;
        if (filter is { } value && _session is { } session && session.ColorFilter != value)
        {
            session.ColorFilter = value;
            Redraw();
        }
    }

    /// <summary>Invalidates every tile of every page so that all are drawn again.</summary>
    public void Redraw()
    {
        if (_scheduler is null || _session is null)
        {
            return;
        }

        for (int page = 0; page < _session.PageCount; page++)
        {
            _scheduler.Invalidate(page, AllLayers);
        }

        Wake();
    }

    public void SetLayoutOptions(PageLayoutOptions? options)
    {
        if (Same(options, _layoutOptions))
        {
            return;
        }

        _layoutOptions = CopyLayout(options);
        if (_scheduler is null || _zoom is null || _input is null)
        {
            return;
        }

        int page = _input.CurrentPage;
        PageLayout layout = PageLayout.Create(_metrics, _layoutOptions);
        _scheduler.SetLayout(layout);
        Viewport viewport = _zoom.Viewport;
        double zoom = layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, page, _input.ActualSize);
        _zoom.SetViewport(layout.Clamp(layout.ScrollTo(viewport with { Zoom = zoom }, page)));
        Wake();
    }

    // ---- Navigation ----

    public void GoToPage(int pageIndex)
    {
        _interacted = true;
        _input?.GoToPage(pageIndex);
        Wake();
    }

    public void NextPage()
    {
        _interacted = true;
        _input?.NextPage();
        Wake();
    }

    public void PreviousPage()
    {
        _interacted = true;
        _input?.PreviousPage();
        Wake();
    }

    public void ZoomTo(ZoomMode mode)
    {
        _interacted = true;
        _input?.ZoomTo(mode);
        Wake();
    }

    public void ZoomBy(double factor)
    {
        if (_zoom is not null)
        {
            _interacted = true;
            _zoom.ZoomBy(factor, _width * _ratio / 2, _height * _ratio / 2);
            Wake();
        }
    }

    /// <summary>Scrolls the least distance that shows an area of a page (a search match), with a margin; a page off screen is centred.</summary>
    public void Reveal(int pageIndex, PdfRectangle area)
    {
        if (_zoom is null || _input is null || (uint)pageIndex >= (uint)_metrics.Length)
        {
            return;
        }

        _interacted = true;
        PageLayout layout = _zoom.Layout;
        Viewport viewport = _input.Viewport;
        if (layout.Mode is PageLayoutMode.SinglePage or PageLayoutMode.TwoUp && layout.GetSpread(pageIndex) != layout.GetSpread(_input.CurrentPage))
        {
            viewport = layout.ScrollTo(viewport, pageIndex);
        }

        LayoutRectangle box = layout.PageToViewport(viewport, pageIndex, area);
        double margin = 24 * _ratio;
        double dx = box.X < margin ? box.X - margin : box.X + box.Width > viewport.Width - margin ? box.X + box.Width - viewport.Width + margin : 0;
        double dy = box.Y < margin ? box.Y - margin : box.Y + box.Height > viewport.Height - margin ? box.Y + box.Height - viewport.Height + margin : 0;
        if (box.Y + box.Height < 0 || box.Y > viewport.Height)
        {
            dy = box.Y + (box.Height / 2) - (viewport.Height / 2);
        }

        _zoom.SetViewport(layout.Clamp(viewport with { X = viewport.X + dx, Y = viewport.Y + dy }));
        Wake();
    }

    // ---- Input ----

    /// <summary>Sets the view's size in device-independent units and its pixels per unit. The first real size shows the top of page 1.</summary>
    public void Resize(double width, double height, double pixelRatio)
    {
        if (width <= 0 || height <= 0 || pixelRatio <= 0 || (width == _width && height == _height && pixelRatio == _ratio))
        {
            return;
        }

        bool fitWidth = _input is not null && _zoom is not null && IsAtFitWidth();
        _width = width;
        _height = height;
        _ratio = pixelRatio;
        if (_input is null || _zoom is null)
        {
            Wake();
            return;
        }

        _input.PixelRatio = pixelRatio;
        _input.Resize(width, height);
        if (!_interacted)
        {
            _zoom.SetViewport(InitialViewport(_zoom.Layout));
        }
        else if (fitWidth)
        {
            // Keep fit-width across a resize (a rotation, a split view): the old tiles stay on screen, scaled, until sharp ones arrive.
            Viewport viewport = _zoom.Viewport;
            double zoom = _zoom.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, _input.CurrentPage, _input.ActualSize);
            _zoom.ZoomTo(zoom, 0, 0);
        }

        Wake();
    }

    /// <summary>A pointer went down at a point in device-independent units; a mouse press counts consecutive clicks (word, line, paragraph).</summary>
    /// <returns>Whether the viewer took the pointer (the host captures it).</returns>
    public bool PointerPressed(long pointerId, PointerKind kind, double x, double y, InputModifiers modifiers, bool isPrimaryButton = true)
    {
        if (_input is null || (kind == PointerKind.Mouse && !isPrimaryButton))
        {
            return false;
        }

        PointerDown(pointerId, kind, x, y, kind == PointerKind.Mouse ? CountClick(x, y) : 1, modifiers);
        return true;
    }

    /// <summary>A pointer moved; a mouse without a pressed button hovers (links and fields show their ring).</summary>
    public void PointerMoved(long pointerId, PointerKind kind, double x, double y, bool inContact)
    {
        if (kind == PointerKind.Mouse && !inContact && _mode == PointerMode.None)
        {
            Hover(x, y);
        }
        else
        {
            PointerMove(pointerId, x, y);
        }
    }

    /// <summary>A mouse wheel turned by <paramref name="delta"/> (WinUI's 120 per notch; positive away from the user, or right when horizontal).</summary>
    public void MouseWheel(int delta, bool horizontal, InputModifiers modifiers, double x, double y)
    {
        // A notch is three lines; vertical wheel deltas are positive when the wheel turns away from the user (scroll up).
        double lines = delta / 40.0;
        if (horizontal)
        {
            Wheel(lines, 0, WheelDeltaMode.Line, modifiers, x, y);
        }
        else
        {
            Wheel(0, -lines, WheelDeltaMode.Line, modifiers, x, y);
        }
    }

    public void PointerDown(long pointerId, PointerKind kind, double x, double y, int clicks, InputModifiers modifiers)
    {
        if (_input is null)
        {
            return;
        }

        _interacted = true;
        _keyboardFocus = false;
        _mouseHover = kind == PointerKind.Mouse;
        _lastPointerKind = kind;
        if (_mode == PointerMode.Pan)
        {
            // A further finger joins the pan: the input model turns two into a pinch.
            _panPointers++;
            _pressId = -1;
            _input.PointerDown(pointerId, kind, x, y);
            Wake();
            return;
        }

        if (_mode != PointerMode.None)
        {
            return;
        }

        if (kind != PointerKind.Mouse && _handlesShown && !_selection!.IsEmpty && TryBeginHandleDrag(x, y))
        {
            _selectPointer = pointerId;
            _mode = PointerMode.Handle;
            Wake();
            return;
        }

        if (TryGetPagePoint(x, y, out PagePoint point))
        {
            if (_annotations!.HitTest(point) is { } target
                && (AllowAnnotationEditing || _annotations.GetRole(target) != AnnotationRole.Annotation)
                && _annotations.PointerDown(point))
            {
                _mode = PointerMode.Annotation;
                Wake();
                return;
            }

            if (kind == PointerKind.Mouse && IsOverText(point))
            {
                if ((modifiers & InputModifiers.Shift) != 0 && !_selection!.IsEmpty)
                {
                    _selection.Extend(point);
                }
                else
                {
                    _selection!.Begin(point, clicks switch
                    {
                        2 => SelectionGranularity.Word,
                        3 => SelectionGranularity.Line,
                        >= 4 => SelectionGranularity.Paragraph,
                        _ => SelectionGranularity.Character,
                    });
                }

                _selectPointer = pointerId;
                _mode = PointerMode.Select;
                Wake();
                return;
            }

            _pressOnText = kind != PointerKind.Mouse && IsOverText(point);
        }
        else
        {
            _pressOnText = false;
        }

        if (!_selection!.IsEmpty)
        {
            _selection.Clear();
        }

        if (_annotations!.Focused is not null)
        {
            _annotations.Focus(null);
        }

        _pressId = kind == PointerKind.Mouse ? -1 : pointerId;
        _pressTime = _time.GetTimestamp();
        _pressX = x;
        _pressY = y;
        _mode = PointerMode.Pan;
        _panPointers = 1;
        _input.PointerDown(pointerId, kind, x, y);
        Wake();
    }

    public void PointerMove(long pointerId, double x, double y)
    {
        if (_input is null)
        {
            return;
        }

        switch (_mode)
        {
            case PointerMode.Pan:
                if (pointerId == _pressId && Math.Abs(x - _pressX) + Math.Abs(y - _pressY) > TouchSlop)
                {
                    _pressId = -1;
                }

                _input.PointerMove(pointerId, x, y);
                Wake();
                return;
            case PointerMode.Select or PointerMode.Handle when pointerId == _selectPointer:
                if (TryGetPagePoint(x, y, out PagePoint selecting))
                {
                    _selection!.Extend(selecting);
                }

                return;
            case PointerMode.Annotation:
                if (TryGetPagePoint(x, y, out PagePoint dragging))
                {
                    _annotations!.PointerMove(dragging);
                }

                return;
        }
    }

    public void PointerUp(long pointerId, double x, double y)
    {
        if (_input is null)
        {
            return;
        }

        PointerMode mode = _mode;
        switch (mode)
        {
            case PointerMode.Annotation:
                _mode = PointerMode.None;
                if (TryGetPagePoint(x, y, out PagePoint point))
                {
                    _annotations!.PointerUp(point);
                }
                else
                {
                    _annotations!.PointerCancel();
                }

                break;
            case PointerMode.Pan:
                _input.PointerUp(pointerId, x, y);
                if (--_panPointers <= 0)
                {
                    _mode = PointerMode.None;
                }

                if (pointerId == _pressId)
                {
                    _pressId = -1;
                }

                break;
            case PointerMode.Select or PointerMode.Handle when pointerId == _selectPointer:
                // The release point ends the drag too: a last move the platform folded into the release, or moves it never
                // delivered (synthesized input that only repositions the cursor), still select up to where the pointer let go.
                if (TryGetPagePoint(x, y, out PagePoint released))
                {
                    _selection!.Extend(released);
                }

                _mode = PointerMode.None;
                _selectPointer = -1;
                break;
        }

        Wake();
    }

    public void PointerCancel(long pointerId)
    {
        switch (_mode)
        {
            case PointerMode.Annotation:
                _annotations?.PointerCancel();
                _mode = PointerMode.None;
                break;
            case PointerMode.Pan:
                _input?.PointerCancel(pointerId);
                _pressId = pointerId == _pressId ? -1 : _pressId;
                if (--_panPointers <= 0)
                {
                    _mode = PointerMode.None;
                }

                break;
            case PointerMode.Select or PointerMode.Handle when pointerId == _selectPointer:
                _mode = PointerMode.None;
                _selectPointer = -1;
                break;
        }

        Wake();
    }

    public void Wheel(double deltaX, double deltaY, WheelDeltaMode mode, InputModifiers modifiers, double x, double y)
    {
        if (_input is null)
        {
            return;
        }

        _interacted = true;
        _input.Wheel(deltaX, deltaY, mode, modifiers, x, y);
        Wake();
    }

    /// <summary>A trackpad or platform pinch step (Mac Catalyst): zooms by <paramref name="factor"/> around a point, as Ctrl+wheel does.</summary>
    public void Magnify(double factor, double x, double y)
    {
        if (factor > 0 && double.IsFinite(factor))
        {
            // InputModel's Ctrl+wheel zoom is exp(-dy * 0.002) for dy in DIPs.
            Wheel(0, -Math.Log(factor) / 0.002, WheelDeltaMode.Pixel, InputModifiers.Control, x, y);
        }
    }

    /// <summary>A key press, named as DOM <c>KeyboardEvent.key</c> names it ("ArrowDown", "PageUp", " ", "a"); returns whether the viewer used it.</summary>
    public bool KeyDown(string key, InputModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_input is null)
        {
            return false;
        }

        bool shift = (modifiers & InputModifiers.Shift) != 0;
        bool command = (modifiers & (InputModifiers.Control | InputModifiers.Meta)) != 0;
        bool handled = HandleKey(key, shift, command, modifiers);
        if (handled)
        {
            _interacted = true;
            Wake();
        }

        return handled;
    }

    /// <summary>
    /// The selected text (null when nothing is selected); pages the selection spans load first. A copy the document's closing
    /// overtakes while those pages load (another document opened, the view disposed) has nothing left to copy: it returns null rather
    /// than throwing into the host's click handler.
    /// </summary>
    public async Task<string?> GetSelectedTextAsync()
    {
        if (_selection is not { IsEmpty: false } selection)
        {
            return null;
        }

        try
        {
            return await selection.GetTextAsync(_closing.Token).ConfigureAwait(true);
        }
        catch (Exception exception) when ((exception is OperationCanceledException or ObjectDisposedException)
            && (!ReferenceEquals(selection, _selection) || _closing.IsCancellationRequested))
        {
            // The document closed under the copy and its session stopped the page loads.
            return null;
        }
    }

    // ---- Form fields ----

    // The native editor over the widget is only the input surface: the form filling (#218) holds what is typed, cuts it at MaxLen
    // and to one line, picks options, and writes the field once through the form API when the edit ends (#543), so filling a field
    // here saves the same file as filling it through the API or in any other shell.

    /// <summary>The native editor's text changed (typing, an input method, a paste): the form filling holds it until the edit ends.</summary>
    public bool EditorInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _filling is { } filling && _editor is { } editor && filling.Target == editor.Target && filling.SetText(text);
    }

    /// <summary>
    /// Ends the edit of the editor's field (focus loss, Enter): the form filling writes it once through the form API when it differs
    /// from the field's value, which regenerates the field's appearance and redraws only its overlay tiles.
    /// </summary>
    public bool CommitFieldEdit()
    {
        bool done = _filling?.Commit() ?? false;
        Wake();
        return done;
    }

    /// <summary>Abandons the editor's edit (Escape): the field keeps its value.</summary>
    public bool CancelFieldEdit()
    {
        bool done = _filling?.Cancel() ?? false;
        Wake();
        return done;
    }

    /// <summary>Sets the editor's text and ends the edit (the text a native editor holds when it closes).</summary>
    public bool CommitFieldText(string? text)
    {
        if (_filling is null || _editor is null || _filling.Target != _editor.Target)
        {
            return false;
        }

        _filling.SetText(text ?? string.Empty);
        return CommitFieldEdit();
    }

    /// <summary>
    /// Sets the options picked in the editor's choice field (all of a multi-select list box's picks, as its native list holds them
    /// after each click), held by the form filling until the edit ends.
    /// </summary>
    public bool EditorSelectedOptions(IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        return _filling is { } filling && _editor is { Kind: FieldEditorKind.Choice } editor && filling.Target == editor.Target
            && filling.SetSelectedOptions(indices);
    }

    /// <summary>Picks option <paramref name="index"/> of the editor's choice field and ends the edit.</summary>
    public bool CommitFieldChoice(int index)
    {
        if (_filling is null || _editor is not { Kind: FieldEditorKind.Choice } editor || _filling.Target != editor.Target)
        {
            return false;
        }

        _filling.SetSelectedOptions([index]);
        return CommitFieldEdit();
    }

    /// <summary>Closes the editor (focus leaves the field).</summary>
    public void EndFieldEdit()
    {
        if (_annotations?.Focused is not null)
        {
            _annotations.Focus(null);
        }

        UpdateEditor();
        Wake();
    }

    // ---- Frames ----

    /// <summary>Runs one frame: input animation, tile work, page loads, then composes and publishes what the canvas shows. Returns whether another frame is needed.</summary>
    public bool Frame()
    {
        _frameRequested = false;
        if (_disposed)
        {
            return false;
        }

        if (_input is null || _zoom is null)
        {
            ViewerFrame blank = LatestFrame.Reset();
            blank.Width = _width * _ratio;
            blank.Height = _height * _ratio;
            blank.IsDark = ColorFilter == ColorFilter.DarkMode;
            _host.Present(blank);
            return false;
        }

        long start = Stopwatch.GetTimestamp();
        if (_metricsDirty && Stopwatch.GetElapsedTime(_lastRelayout) >= RelayoutInterval)
        {
            Relayout();
        }

        bool longPress = CheckLongPress();
        bool more = _input.RunFrame(FrameBudget);
        Viewport viewport = _input.Viewport;
        _bridge!.Update();
        bool unloaded = RequestPageLoad(viewport, !more || Stopwatch.GetElapsedTime(start) < IdleFrame);
        Surface.Drain();
        bool stale = Surface.ExpireStale();
        Compose(viewport);
        PlaceEditor(viewport);
        SetState(new PdfViewState(_input.CurrentPage, _metrics.Length, viewport.Zoom / _input.ActualSize, false, !_scheduler!.IsIdle));
        return more || unloaded || stale || longPress || _metricsDirty || _input.IsAnimating;
    }

    // Tiles presented and search progress arrive on workers in a background session: the frame request goes back to the UI thread.
    private void WakeFromAnyThread()
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            Wake();
        }
        else if (Interlocked.Exchange(ref _wakePosted, 1) == 0)
        {
            _ui.Post(static state => ((ViewerController)state!).WakeFromWorker(), this);
        }
    }

    private void WakeFromWorker()
    {
        Volatile.Write(ref _wakePosted, 0);
        Wake();
    }

    private void OnSearchChanged(object? sender, EventArgs e) => WakeFromAnyThread();

    private void Wake()
    {
        if (!_frameRequested && !_disposed)
        {
            _frameRequested = true;
            _host.RequestFrame();
        }
    }

    private bool CheckLongPress()
    {
        if (_pressId < 0 || !_pressOnText || _mode != PointerMode.Pan || _panPointers != 1)
        {
            return false;
        }

        if (_time.GetElapsedTime(_pressTime) < LongPress)
        {
            return true;
        }

        long pointer = _pressId;
        _pressId = -1;
        if (!TryGetPagePoint(_pressX, _pressY, out PagePoint point))
        {
            return false;
        }

        // The press becomes a selection: the input model forgets the pointer (it never panned: it stayed inside the slop).
        _input!.PointerCancel(pointer);
        _panPointers = 0;
        _selection!.Begin(point, SelectionGranularity.Word);
        _selectPointer = pointer;
        _mode = PointerMode.Select;
        return false;
    }

    private void Relayout()
    {
        _metricsDirty = false;
        _lastRelayout = Stopwatch.GetTimestamp();
        PageLayout previous = _zoom!.Layout;
        PageLayout layout = PageLayout.Create(_metrics, _layoutOptions);
        _scheduler!.SetLayout(layout);
        _zoom.SetViewport(_interacted ? layout.Anchor(previous, _zoom.Viewport) : InitialViewport(layout));
    }

    private async Task SweepMetricsAsync(DocumentSession session, int generation)
    {
        for (int i = 1; i < _metrics.Length; i++)
        {
            PageMetrics metrics;
            try
            {
                metrics = await session.GetPageMetricsAsync(i, _closing.Token).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
#pragma warning disable CA1031 // a page that cannot be read keeps its placeholder size; its tiles report the failure
            catch (Exception)
#pragma warning restore CA1031
            {
                continue;
            }

            if (generation != _generation)
            {
                return;
            }

            if (metrics != _metrics[i])
            {
                _metrics[i] = metrics;
                _metricsDirty = true;
                Wake();
            }
        }
    }

    private bool RequestPageLoad(in Viewport viewport, bool start)
    {
        PageLayout layout = _zoom!.Layout;
        int count = VisiblePages(layout, viewport);
        foreach (VisiblePage page in _visible.AsSpan(0, count))
        {
            if (_requested.Contains(page.PageIndex))
            {
                continue;
            }

            if (!start)
            {
                return true;
            }

            _requested.Add(page.PageIndex);
            _ = ReadPageAsync(page.PageIndex, _generation);
            start = false;
        }

        return false;
    }

    private async Task ReadPageAsync(int pageIndex, int generation)
    {
        // The open document's layers, held here: a document opened while this page loads replaces the fields, and the page index
        // belongs to this document, never to the next one (#358: a late page of a novel reached an 8-page textbook's layers).
        AnnotationLayer annotations = _annotations!;
        TextSelection selection = _selection!;
        AccessibilityBridge bridge = _bridge!;
        _pendingLoads++;
        try
        {
            CancellationToken token = _closing.Token;
            await annotations.ReadPageAsync(pageIndex, token).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            Wake();
            await selection.ReadPageAsync(pageIndex, token).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            await bridge.ReadPageAsync(pageIndex, token).ConfigureAwait(true);
            if (generation == _generation)
            {
                RefreshAccessiblePages();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
#pragma warning disable CA1031 // the page still draws; the host hears why its text or links are missing
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // A closed document's failure is no longer the host's concern.
            if (generation == _generation)
            {
                _host.Error(exception);
            }
        }
        finally
        {
            _pendingLoads--;
        }

        Wake();
    }

    private void RefreshAccessiblePages()
    {
        _accessiblePages.Clear();
        if (_bridge is not null)
        {
            for (int page = 0; page < _metrics.Length; page++)
            {
                if (_bridge.IsPageLoaded(page))
                {
                    _accessiblePages.Add(page);
                }
            }
        }

        _host.AccessibilityChanged(new AccessibilityChange(AccessibilityChangeKind.ChildrenChanged, null));
    }

    private void OnAccessibilityChange(AccessibilityChange change)
    {
        if (change.Kind != AccessibilityChangeKind.BoundsChanged)
        {
            _host.AccessibilityChanged(change);
        }
    }

    private void OnActionRequested(object? sender, ViewerActionEventArgs e)
    {
        // Destinations inside the document are followed; URIs, launches and scripts go to the host and are never performed here.
        if (e.Destination is { } destination && _zoom is not null && _input is not null)
        {
            _zoom.SetViewport(AnnotationLayer.GetDestinationViewport(destination, _zoom.Layout, _input.Viewport));
        }
        else if (e.PageIndex is int page && e.Uri is null)
        {
            _input?.GoToPage(page);
        }

        _host.ActionRequested(e);
        Wake();
    }

    private void OnAnnotationsChanged(object? sender, EventArgs e) => Wake();

    // The form filling follows the layer's focus and raises Changed after it: the editor opens, changes field or closes from its state.
    private void OnFillingChanged(object? sender, EventArgs e)
    {
        UpdateEditor();
        Wake();
    }

    private void UpdateEditor()
    {
        // What the form filling edits decides the editor: read-only fields, buttons and signatures take focus but open none.
        FieldEditor? editor = _filling switch
        {
            { Target: { } target, IsReadOnly: false, Kind: FieldEditKind.Text, Field: PdfTextField text } filling => new FieldEditor(
                target, FieldEditorKind.Text, filling.Text, text.IsMultiline, text.IsPassword, filling.MaxLength, [], -1),
            { Target: { } target, IsReadOnly: false, Kind: FieldEditKind.Choice, Field: PdfChoiceField choice } filling => new FieldEditor(
                target,
                FieldEditorKind.Choice,
                filling.Text,
                false,
                false,
                null,
                [.. choice.Options.Select(option => option.DisplayText)],
                filling.SelectedOptions.Count > 0 ? filling.SelectedOptions[0] : -1,
                IsEditable: choice is PdfComboBoxField { IsEditable: true },
                IsMultiSelect: choice is PdfListBoxField { IsMultiSelect: true })
            { SelectedIndices = [.. filling.SelectedOptions] },
            _ => null,
        };

        // The same field keeps its editor (a committed value does not reopen it).
        if (editor?.Target == _editor?.Target && (editor is null) == (_editor is null))
        {
            return;
        }

        _editor = editor;
        _editorBounds = default;
        _host.EditorChanged(editor);
    }

    private void PlaceEditor(in Viewport viewport)
    {
        if (_editor is null || _annotations is null)
        {
            return;
        }

        LayoutRectangle box = _annotations.GetScreenRectangle(_editor.Target, _zoom!.Layout, viewport);
        if (box != _editorBounds)
        {
            _editorBounds = box;
            _host.EditorMoved(box.X / _ratio, box.Y / _ratio, box.Width / _ratio, box.Height / _ratio);
        }
    }

    private void Compose(in Viewport viewport)
    {
        PageLayout layout = _zoom!.Layout;
        ViewerFrame frame = LatestFrame.Reset();
        frame.Width = viewport.Width;
        frame.Height = viewport.Height;
        frame.IsDark = ColorFilter == ColorFilter.DarkMode;
        int count = _zoom.GetFrame(_parts);
        if (count > _parts.Length)
        {
            _parts = new FramePart[count * 2];
            count = _zoom.GetFrame(_parts);
        }

        ReadOnlySpan<FramePart> parts = _parts.AsSpan(0, count);

        // Paper and content tiles first (farthest scale first, so sharp tiles end on top), then the transparent overlay tiles over them.
        foreach (FramePart part in parts)
        {
            if (part.Kind == FramePartKind.Paper)
            {
                frame.Add(new ViewerFrame.Item(ViewerFrame.ItemKind.Paper, ViewerFrame.Snap(part.Destination)));
            }
            else if ((part.Tile.Layers & DisplayLayers.Content) != 0 && Surface.TryGetImage(part.Tile, out ITileImage? image))
            {
                frame.Add(new ViewerFrame.Item(ViewerFrame.ItemKind.Tile, ViewerFrame.Snap(part.Destination), image));
            }
        }

        if (_showAnnotations && Surface.Stale.Count > 0)
        {
            AddStaleOverlays(frame, parts, viewport, layout.Rotation);
        }

        foreach (FramePart part in parts)
        {
            if (part.Kind != FramePartKind.Paper && (part.Tile.Layers & DisplayLayers.Content) == 0 && Surface.TryGetImage(part.Tile, out ITileImage? image))
            {
                frame.Add(new ViewerFrame.Item(ViewerFrame.ItemKind.Tile, ViewerFrame.Snap(part.Destination), image));
            }
        }

        AddSearchMatches(frame, layout, viewport);
        AddSelection(frame, layout, viewport);

        // Chrome: a pressed link or field, the mouse's hovered one, and the keyboard focus (never a ring after a tap or click).
        if (_annotations!.Pressed is { } pressed)
        {
            AddChrome(frame, pressed, layout, viewport, 2);
        }
        else if (_mouseHover && _mode == PointerMode.None && _annotations.Hovered is { } hovered
            && (AllowAnnotationEditing || _annotations.GetRole(hovered) != AnnotationRole.Annotation))
        {
            AddChrome(frame, hovered, layout, viewport, 1);
        }

        if (_keyboardFocus && _annotations.Focused is { } focused && _editor is null)
        {
            AddChrome(frame, focused, layout, viewport, 2);
        }

        _host.Present(frame);
    }

    private void AddChrome(ViewerFrame frame, AnnotationTarget target, PageLayout layout, in Viewport viewport, double width)
    {
        LayoutRectangle box = _annotations!.GetScreenRectangle(target, layout, viewport);
        double inset = _ratio;
        frame.Add(new ViewerFrame.Item(
            ViewerFrame.ItemKind.Chrome,
            new LayoutRectangle(box.X - inset, box.Y - inset, box.Width + (2 * inset), box.Height + (2 * inset)),
            Width: width * _ratio));
    }

    private void AddStaleOverlays(ViewerFrame frame, ReadOnlySpan<FramePart> parts, in Viewport viewport, int rotation)
    {
        foreach ((Tile tile, (ITileImage image, long _)) in Surface.Stale)
        {
            if (tile.Scale != viewport.Zoom || tile.Rotation != rotation)
            {
                continue;
            }

            foreach (FramePart part in parts)
            {
                if (part.Kind == FramePartKind.Paper && part.Tile.PageIndex == tile.PageIndex)
                {
                    LayoutRectangle d = part.Destination;
                    frame.Add(new ViewerFrame.Item(
                        ViewerFrame.ItemKind.Tile,
                        ViewerFrame.Snap(new LayoutRectangle(d.X + tile.Region.X, d.Y + tile.Region.Y, tile.Region.Width, tile.Region.Height)),
                        image));
                    break;
                }
            }
        }
    }

    // Search matches are an overlay over the tiles (#215): highlighting them draws no page again.
    private void AddSearchMatches(ViewerFrame frame, PageLayout layout, in Viewport viewport)
    {
        if (_search is not { MatchCount: > 0 } search)
        {
            return;
        }

        SearchMatch? current = search.Current;
        int visible = VisiblePages(layout, viewport);
        foreach (VisiblePage page in _visible.AsSpan(0, visible))
        {
            int quads = search.GetQuads(page.PageIndex, _quads);
            if (quads > _quads.Length)
            {
                _quads = new Quad[quads * 2];
                quads = search.GetQuads(page.PageIndex, _quads);
            }

            for (int i = 0; i < quads; i++)
            {
                AddQuad(frame, ViewerFrame.ItemKind.SearchMatch, layout, viewport, page.PageIndex, _quads[i]);
            }
        }

        if (current is not null)
        {
            IReadOnlyList<Quad> quads = current.Quads;
            for (int i = 0; i < quads.Count; i++)
            {
                AddQuad(frame, ViewerFrame.ItemKind.CurrentMatch, layout, viewport, current.PageIndex, quads[i]);
            }
        }
    }

    private static ViewerFrame.Item AddQuad(ViewerFrame frame, ViewerFrame.ItemKind kind, PageLayout layout, in Viewport viewport, int pageIndex, in Quad quad)
    {
        var item = new ViewerFrame.Item(kind, default)
        {
            A = layout.PageToViewport(viewport, pageIndex, quad.BottomLeft.X, quad.BottomLeft.Y),
            B = layout.PageToViewport(viewport, pageIndex, quad.BottomRight.X, quad.BottomRight.Y),
            C = layout.PageToViewport(viewport, pageIndex, quad.TopRight.X, quad.TopRight.Y),
            D = layout.PageToViewport(viewport, pageIndex, quad.TopLeft.X, quad.TopLeft.Y),
        };
        frame.Add(item);
        return item;
    }

    private void AddSelection(ViewerFrame frame, PageLayout layout, in Viewport viewport)
    {
        _handlesShown = false;
        if (_selection!.IsEmpty)
        {
            return;
        }

        TextPosition start = _selection.Start;
        TextPosition end = _selection.End;
        bool startShown = false;
        bool endShown = false;
        int visible = VisiblePages(layout, viewport);
        foreach (VisiblePage page in _visible.AsSpan(0, visible))
        {
            int quads = _selection.GetQuads(page.PageIndex, _quads);
            if (quads > _quads.Length)
            {
                _quads = new Quad[quads * 2];
                quads = _selection.GetQuads(page.PageIndex, _quads);
            }

            for (int i = 0; i < quads; i++)
            {
                ViewerFrame.Item item = AddQuad(frame, ViewerFrame.ItemKind.Selection, layout, viewport, page.PageIndex, _quads[i]);
                if (page.PageIndex == start.PageIndex && i == 0)
                {
                    AddHandle(frame, item.D, item.A);
                    _startHandle = new PathPoint(item.A.X / _ratio, item.A.Y / _ratio);
                    startShown = true;
                }

                if (page.PageIndex == end.PageIndex && i == quads - 1)
                {
                    AddHandle(frame, item.C, item.B);
                    _endHandle = new PathPoint(item.B.X / _ratio, item.B.Y / _ratio);
                    endShown = true;
                }
            }
        }

        // Handles exist only where they are drawn: a mouse selection has none to grab.
        _handlesShown = (startShown || endShown) && _lastPointerKind != PointerKind.Mouse;
        if (!startShown)
        {
            _startHandle = new PathPoint(double.NaN, double.NaN);
        }

        if (!endShown)
        {
            _endHandle = new PathPoint(double.NaN, double.NaN);
        }
    }

    // Handles are drawn for touch and pen selections (a mouse extends a selection by dragging or Shift+click).
    private void AddHandle(ViewerFrame frame, PathPoint top, PathPoint bottom)
    {
        if (_lastPointerKind != PointerKind.Mouse)
        {
            frame.Add(new ViewerFrame.Item(
                ViewerFrame.ItemKind.Handle,
                new LayoutRectangle(bottom.X, top.Y, 0, bottom.Y - top.Y),
                Width: HandleRadius * _ratio));
        }
    }

    private bool TryBeginHandleDrag(double x, double y)
    {
        double startDistance = Distance(_startHandle, x, y);
        double endDistance = Distance(_endHandle, x, y);
        if (!(Math.Min(startDistance, endDistance) <= HandleReach))
        {
            return false;
        }

        // Dragging the start handle moves the selection's start: the end becomes the anchor and the finger moves the focus.
        TextPosition start = _selection!.Start;
        TextPosition end = _selection.End;
        if (startDistance < endDistance)
        {
            _selection.Select(end, start);
        }
        else
        {
            _selection.Select(start, end);
        }

        return true;

        static double Distance(PathPoint handle, double x, double y) =>
            double.IsNaN(handle.X) ? double.PositiveInfinity : Math.Sqrt(((handle.X - x) * (handle.X - x)) + ((handle.Y - y) * (handle.Y - y)));
    }

    private int VisiblePages(PageLayout layout, in Viewport viewport)
    {
        int count = layout.GetVisiblePages(viewport, _visible);
        if (count > _visible.Length)
        {
            _visible = new VisiblePage[count * 2];
            count = layout.GetVisiblePages(viewport, _visible);
        }

        return Math.Min(count, _visible.Length);
    }

    private void Hover(double x, double y)
    {
        if (_annotations is not null && TryGetPagePoint(x, y, out PagePoint point))
        {
            AnnotationTarget? before = _annotations.Hovered;
            if (_annotations.PointerMove(point) != before)
            {
                Wake();
            }
        }
    }

    private int CountClick(double x, double y)
    {
        long now = _time.GetTimestamp();
        bool again = _clicks > 0 && _time.GetElapsedTime(_lastClick, now) <= ClickInterval
            && Math.Abs(x - _lastClickX) <= ClickSlop && Math.Abs(y - _lastClickY) <= ClickSlop;
        _clicks = again ? _clicks + 1 : 1;
        _lastClick = now;
        _lastClickX = x;
        _lastClickY = y;
        return _clicks;
    }

    private static InputKey FieldKey(string key) => key switch
    {
        " " => InputKey.Space,
        "Enter" => InputKey.Enter,
        "ArrowUp" => InputKey.ArrowUp,
        "ArrowDown" => InputKey.ArrowDown,
        "Home" => InputKey.Home,
        "End" => InputKey.End,
        _ => InputKey.None,
    };

    private bool HandleKey(string key, bool shift, bool command, InputModifiers modifiers)
    {
        switch (key)
        {
            case "Tab":
                // The form filling commits the field's edit and moves on in each page's Tabs order (#218).
                _keyboardFocus = true;
                return _filling!.KeyDown(InputKey.Tab, modifiers);
            case " " or "Enter" or "ArrowUp" or "ArrowDown" or "Home" or "End" when !command && _filling!.Kind is FieldEditKind.Button or FieldEditKind.Choice
                && _filling.KeyDown(FieldKey(key), modifiers):
                return true;
            case "Enter" when _annotations!.Focused is { } focused:
                _annotations.Activate(focused);
                return true;
            case " " when _annotations!.Focused is not null && _annotations.ToggleField():
                return true;
            case "Escape":
                _selection!.Clear();
                _annotations!.Focus(null);
                _bridge!.SetFocus(null);
                return true;
            case "a" or "A" when command:
                _selection!.SelectAll();
                return true;
            case "c" or "C" when command:
                if (_selection!.IsEmpty)
                {
                    return false;
                }

                _ = CopyAsync();
                return true;
        }

        if (shift && !command && !_selection!.IsEmpty && key.StartsWith("Arrow", StringComparison.Ordinal))
        {
            return _selection.Move(key switch
            {
                "ArrowLeft" => SelectionMove.CharacterBackward,
                "ArrowRight" => SelectionMove.CharacterForward,
                "ArrowUp" => SelectionMove.LineUp,
                _ => SelectionMove.LineDown,
            });
        }

        // Single letters move the accessible focus by kind, as screen readers' quick navigation does; Shift goes backwards.
        if (!command && (modifiers & InputModifiers.Alt) == 0 && key.Length == 1 && NavigationFor(char.ToLowerInvariant(key[0])) is { } navigation)
        {
            _bridge!.MoveFocus(navigation, shift);
            return true;
        }

        InputKey input = key switch
        {
            "ArrowLeft" => InputKey.ArrowLeft,
            "ArrowRight" => InputKey.ArrowRight,
            "ArrowUp" => InputKey.ArrowUp,
            "ArrowDown" => InputKey.ArrowDown,
            "PageUp" => InputKey.PageUp,
            "PageDown" => InputKey.PageDown,
            " " => InputKey.Space,
            "Home" => InputKey.Home,
            "End" => InputKey.End,
            "+" or "=" => InputKey.Plus,
            "-" => InputKey.Minus,
            "0" => InputKey.Zero,
            _ => InputKey.None,
        };
        return input != InputKey.None && _input!.KeyDown(input, modifiers);
    }

    private async Task CopyAsync()
    {
        try
        {
            string? text = await GetSelectedTextAsync().ConfigureAwait(true);
            if (!string.IsNullOrEmpty(text))
            {
                _host.CopyText(text);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static AccessibleNavigation? NavigationFor(char letter) => letter switch
    {
        'n' => AccessibleNavigation.Element,
        'h' => AccessibleNavigation.Heading,
        'd' => AccessibleNavigation.Landmark,
        't' => AccessibleNavigation.Table,
        'l' => AccessibleNavigation.List,
        'g' => AccessibleNavigation.Figure,
        'k' => AccessibleNavigation.Link,
        'f' => AccessibleNavigation.Control,
        'p' => AccessibleNavigation.Page,
        _ => null,
    };

    // ---- Helpers ----

    /// <summary>Fit-width at the top of page 1 (the content's top edge, padding included).</summary>
    private Viewport InitialViewport(PageLayout layout)
    {
        double width = Math.Max(_width, 1) * _ratio;
        double height = Math.Max(_height, 1) * _ratio;
        double zoom = layout.GetZoom(ZoomMode.FitWidth, width, height, 0, _ratio * 96 / 72);
        return layout.Clamp(new Viewport(0, 0, width, height, zoom));
    }

    private bool IsAtFitWidth()
    {
        Viewport viewport = _zoom!.Viewport;
        double fit = _zoom.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, _input!.CurrentPage, _input.ActualSize);
        return Math.Abs(viewport.Zoom - fit) <= fit * 0.01;
    }

    public bool TryGetPagePoint(double x, double y, out PagePoint point)
    {
        if (_zoom is null || _input is null)
        {
            point = default;
            return false;
        }

        return _zoom.Layout.TryGetPageAt(_input.Viewport, x * _ratio, y * _ratio, out point);
    }

    /// <summary>A node's bounds in device-independent units from the view's top-left.</summary>
    public bool TryGetNodeBounds(AccessibleNode node, out LayoutRectangle bounds)
    {
        if (_bridge is not null && _bridge.TryGetBounds(node, out LayoutRectangle pixels))
        {
            bounds = new LayoutRectangle(pixels.X / _ratio, pixels.Y / _ratio, pixels.Width / _ratio, pixels.Height / _ratio);
            return true;
        }

        bounds = default;
        return false;
    }

    private bool IsOverText(in PagePoint point) =>
        _selection!.TryGetPage(point.PageIndex, out SelectableText? text) && text.GetGlyphAt(new PathPoint(point.X, point.Y)) >= 0;

    private static PageLayoutOptions? CopyLayout(PageLayoutOptions? options) =>
        options is null ? null : new PageLayoutOptions().WithMode(options.Mode).WithCover(options.ShowCover).WithGap(options.Gap).WithPadding(options.Padding).WithRotation(options.Rotation);

    private static bool Same(PageLayoutOptions? a, PageLayoutOptions? b)
    {
        a ??= new PageLayoutOptions();
        b ??= new PageLayoutOptions();
        return a.Mode == b.Mode && a.ShowCover == b.ShowCover && a.Gap == b.Gap && a.Padding == b.Padding && a.Rotation == b.Rotation;
    }

    private void SetState(PdfViewState state)
    {
        if (state != _state)
        {
            _state = state;
            _host.StateChanged(state);
        }
    }

    // ---- Lifetime ----

    /// <summary>Closes the open document (the view shows nothing).</summary>
    public void CloseDocument() => Close();

    private void Close()
    {
        _generation++;
        _scheduler?.Dispose();
        _scheduler = null;
        _zoom = null;
        _input = null;
        _selection = null;
        if (_annotations is not null)
        {
            _annotations.ActionRequested -= OnActionRequested;
            _annotations.Changed -= OnAnnotationsChanged;
        }

        _filling?.Changed -= OnFillingChanged;
        _filling = null;
        _annotations = null;
        _bridge = null;
        if (_search is not null)
        {
            _search.Changed -= OnSearchChanged;
            _search.Dispose();
            _search = null;
        }

        Surface.Clear();
        _requested.Clear();
        _accessiblePages.Clear();
        _metrics = [];
        _metricsDirty = false;
        _mode = PointerMode.None;
        _pressId = -1;
        _handlesShown = false;
        if (_editor is not null)
        {
            _editor = null;
            _host.EditorChanged(null);
        }

        if (_session is not null && _ownsSession)
        {
            _session.Dispose();
        }

        _session = null;
        _host.AccessibilityChanged(new AccessibilityChange(AccessibilityChangeKind.ChildrenChanged, null));
        Wake();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _closing.Cancel();
        Close();
        _disposed = true;
        _host.Present(LatestFrame.Reset());
        _closing.Dispose();
    }
}
