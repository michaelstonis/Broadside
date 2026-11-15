using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Rendering;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using Matrix = Avalonia.Matrix;
using PixelSize = Avalonia.PixelSize;
using RenderOptions = Avalonia.Media.RenderOptions;

namespace Broadside.Viewer.Avalonia;

/// <summary>
/// The Avalonia shell's logic: it maps pointer (mouse, touch, pen), wheel and key events onto the viewer core (<see cref="InputModel"/>,
/// <see cref="TextSelection"/>, <see cref="AnnotationLayer"/>, <see cref="AccessibilityBridge"/>, <see cref="DocumentSearch"/>) and
/// draws the <see cref="TileScheduler"/>'s tiles, the search highlights, the selection and the focus chrome into the control's
/// drawing context.
/// </summary>
/// <remarks>
/// A port of the MAUI and Blazor shells' controllers: the input model owns the gestures (pan, fling, pinch, double-tap, keys), this
/// class only routes pointers to the annotation layer, the text selection or the input model. Coordinates arrive in device-independent
/// units from the control's top-left; the viewport is in device pixels (units times the render scaling). Every member runs on the
/// UI thread; tiles are rendered and copied into bitmaps on workers (background sessions) and only applied here.
/// </remarks>
internal sealed class ViewerController : IDisposable
{
    internal static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(8);
    internal static readonly TimeSpan LongPress = TimeSpan.FromMilliseconds(500);
    internal static readonly Color LightBackground = Color.FromRgb(0xE8, 0xE8, 0xEC);
    internal static readonly Color DarkBackground = Color.FromRgb(0x2B, 0x2B, 0x2B);
    private static readonly TimeSpan IdleFrame = TimeSpan.FromMilliseconds(4);
    private static readonly TimeSpan RelayoutInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClickInterval = TimeSpan.FromMilliseconds(350);
    private static readonly IBrush LightBackgroundBrush = new ImmutableSolidColorBrush(LightBackground);
    private static readonly IBrush DarkBackgroundBrush = new ImmutableSolidColorBrush(DarkBackground);
    private static readonly IBrush ShadowBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x28, 0, 0, 0));
    private static readonly IBrush SelectionBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0x33, 0x7A, 0xFF));
    private static readonly IBrush MatchBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD0, 0x00));
    private static readonly IBrush CurrentMatchBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0xFF, 0x8C, 0x00));
    private static readonly ImmutableSolidColorBrush HandleBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x33, 0x7A, 0xFF));
    private static readonly ImmutableSolidColorBrush ChromeBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0x33, 0x7A, 0xFF));
    private const double ClickSlop = 6;
    private const double TouchSlop = 10;
    private const double HandleReach = 28;
    private const double HandleRadius = 7;
    private const DisplayLayers OverlayLayers = DisplayLayers.Annotations | DisplayLayers.FormFields;
    private const DisplayLayers AllLayers = DisplayLayers.Content | OverlayLayers;

    private readonly IViewerHost _host;
    private readonly SynchronizationContext? _ui;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _closing = new();
    private readonly HashSet<int> _requested = [];
    private readonly List<int> _accessiblePages = [];
    private int _wakePosted;
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
    private AccessibilityBridge? _bridge;
    private DocumentSearch? _search;
    private SearchMatch? _revealed;
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

    // Selection handles (touch), in device-independent units; valid after a frame drew them.
    private bool _handlesShown;
    private Point _startHandle;
    private Point _endHandle;

    public ViewerController(IViewerHost host, TimeProvider? time = null)
    {
        _host = host;
        _time = time ?? TimeProvider.System;
        Surface = new BitmapTileSurface(_time);

        // A background session presents on workers: the frame request goes back to the UI thread.
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

    internal BitmapTileSurface Surface { get; }

    public bool AllowAnnotationEditing { get; set; }

    /// <summary>Receives the tile scheduler's trace (diagnostics and tests); set before opening.</summary>
    public Action<TileSchedulerEvent>? SchedulerObserver { get; set; }

    public DocumentSession? Session => _session;

    public TextSelection? Selection => _selection;

    public AnnotationLayer? Annotations => _annotations;

    public AccessibilityBridge? Bridge => _bridge;

    public DocumentSearch? Search => _search;

    public InputModel? Input => _input;

    public ProgressiveZoom? Zoom => _zoom;

    public TileScheduler? Scheduler => _scheduler;

    public FieldEditor? Editor => _editor;

    public IReadOnlyList<int> AccessiblePages => _accessiblePages;

    public PdfViewState State => _state;

    /// <summary>Whether visible pages' text, links and accessibility nodes are still loading.</summary>
    internal bool IsLoadingPages => _pendingLoads > 0;

    public double PixelRatio => _ratio;

    /// <summary>Gets whether the open document draws with the dark-mode color filter (dark paper and surroundings).</summary>
    public bool IsDark => _session?.ColorFilter == ColorFilter.DarkMode;

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

        // Page 1's size stands in for every page until its own arrives, so a remote file draws page 1 at once.
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
        _annotations = new AnnotationLayer(session, _scheduler);
        _annotations.ActionRequested += OnActionRequested;
        _annotations.Changed += OnAnnotationsChanged;
        _bridge = new AccessibilityBridge(session, _annotations) { Viewer = _zoom, Observer = OnAccessibilityChange };
        _annotations.Accessibility = _bridge;
        _search = new DocumentSearch(session);
        _search.Changed += (_, _) => WakeFromAnyThread();
        _revealed = null;
        SetState(_state with { IsLoading = false, PageCount = session.PageCount, IsRendering = true });
        _host.AccessibilityChanged(new AccessibilityChange(AccessibilityChangeKind.ChildrenChanged, null));
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
    /// Sets the color filter of the open document and of those opened later; null keeps each session's own. A change draws every tile
    /// again from the cached display lists (spec #33 viewer capability 9); the tiles on screen stay until their replacements arrive.
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

        _host.RequestPaint();
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

    /// <summary>Scrolls the least distance that shows an area of a page (in default user space) with a margin around it.</summary>
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

    // ---- Search ----

    /// <summary>Searches the document from the current page; the first match found becomes current and is scrolled into view.</summary>
    public void Find(string query, TextSearchOptions? options)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_search is null || _input is null)
        {
            return;
        }

        _revealed = null;
        _search.Search(query, options, _input.CurrentPage);
        Wake();
    }

    /// <summary>Makes the next (or previous) match current and scrolls it into view.</summary>
    public SearchMatch? FindNext(bool backward)
    {
        SearchMatch? match = backward ? _search?.Previous() : _search?.Next();
        RevealCurrentMatch();
        return match;
    }

    public void ClearSearch()
    {
        _search?.Clear();
        _revealed = null;
        Wake();
    }

    private void RevealCurrentMatch()
    {
        if (_search?.Current is { } current && !ReferenceEquals(current, _revealed))
        {
            _revealed = current;
            Reveal(current.PageIndex, current.Bounds);
        }
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
            // Keep fit-width across a resize: the old tiles stay on screen, scaled, until sharp ones arrive.
            Viewport viewport = _zoom.Viewport;
            double zoom = _zoom.Layout.GetZoom(ZoomMode.FitWidth, viewport.Width, viewport.Height, _input.CurrentPage, _input.ActualSize);
            _zoom.ZoomTo(zoom, 0, 0);
        }

        Wake();
    }

    /// <summary>Counts consecutive mouse clicks at a point (double: word, triple: line, quadruple: paragraph).</summary>
    public int CountClick(double x, double y)
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

    public void PointerDown(long pointerId, PointerKind kind, double x, double y, int clicks, InputModifiers modifiers)
    {
        if (_input is null)
        {
            return;
        }

        _interacted = true;
        _keyboardFocus = false;
        _mouseHover = kind == PointerKind.Mouse;
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
            case PointerMode.None:
                Hover(x, y);
                return;
        }
    }

    public void PointerUp(long pointerId, double x, double y)
    {
        if (_input is null)
        {
            return;
        }

        switch (_mode)
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

    /// <summary>A trackpad pinch step: zooms by <paramref name="factor"/> around a point, as Ctrl+wheel does.</summary>
    public void Magnify(double factor, double x, double y)
    {
        if (factor > 0 && double.IsFinite(factor))
        {
            // InputModel's Ctrl+wheel zoom is exp(-dy * 0.002) for dy in device-independent units.
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

    /// <summary>The selected text (null when nothing is selected); pages the selection spans load first.</summary>
    public async Task<string?> GetSelectedTextAsync()
    {
        if (_selection is null || _selection.IsEmpty)
        {
            return null;
        }

        return await _selection.GetTextAsync(_closing.Token).ConfigureAwait(true);
    }

    // ---- Form fields ----

    /// <summary>Commits the editor's text to the focused text field (or an editable combo box's value).</summary>
    public bool CommitFieldText(string? text)
    {
        if (_annotations is null || _editor is null)
        {
            return false;
        }

        bool done = _editor.Kind == FieldEditorKind.Text ? _annotations.SetFieldText(text) : _annotations.SetFieldChoice(text);
        Wake();
        return done;
    }

    /// <summary>Selects option <paramref name="index"/> of the focused choice field.</summary>
    public bool CommitFieldChoice(int index)
    {
        if (_annotations is null || _editor is not { Kind: FieldEditorKind.Choice })
        {
            return false;
        }

        bool done = _annotations.SetFieldSelection([index]);
        Wake();
        return done;
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

    /// <summary>Runs one frame: input animation, tile work, page loads, then asks the control to render. Returns whether another frame is needed.</summary>
    public bool Frame()
    {
        _frameRequested = false;
        if (_disposed)
        {
            return false;
        }

        if (_input is null || _zoom is null)
        {
            _host.RequestPaint();
            return false;
        }

        long start = Stopwatch.GetTimestamp();
        if (_metricsDirty && Stopwatch.GetElapsedTime(_lastRelayout) >= RelayoutInterval)
        {
            Relayout();
        }

        RevealCurrentMatch();
        bool longPress = CheckLongPress();
        bool more = _input.RunFrame(FrameBudget);
        Viewport viewport = _input.Viewport;
        _bridge!.Update();
        bool unloaded = RequestPageLoad(viewport, !more || Stopwatch.GetElapsedTime(start) < IdleFrame);
        Surface.Drain();
        bool stale = Surface.ExpireStale();
        PlaceEditor(viewport);
        _host.RequestPaint();
        SetState(new PdfViewState(_input.CurrentPage, _metrics.Length, viewport.Zoom / _input.ActualSize, false, !_scheduler!.IsIdle));
        return more || unloaded || stale || longPress || _metricsDirty || _input.IsAnimating;
    }

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
            _ = LoadPageAsync(page.PageIndex, _generation);
            start = false;
        }

        return false;
    }

    /// <summary>Loads a page's links, text and accessibility nodes (screen readers ask for pages the viewport does not show).</summary>
    public void RequestAccessibilityPage(int pageIndex)
    {
        if (_bridge is not null && (uint)pageIndex < (uint)_metrics.Length && _requested.Add(pageIndex))
        {
            _ = LoadPageAsync(pageIndex, _generation);
        }
    }

    /// <summary>Scrolls the least distance that shows an accessible node (an assistive technology's "bring into view").</summary>
    public void BringIntoView(AccessibleNode node)
    {
        if (_bridge is not null && _zoom is not null && _bridge.TryGetViewportShowing(node, out Viewport viewport))
        {
            _interacted = true;
            _zoom.SetViewport(viewport);
            Wake();
        }
    }

    private async Task LoadPageAsync(int pageIndex, int generation)
    {
        // The open document's layers, held here: a document opened while this page loads replaces the fields, not these.
        AnnotationLayer annotations = _annotations!;
        TextSelection selection = _selection!;
        AccessibilityBridge bridge = _bridge!;
        _pendingLoads++;
        try
        {
            CancellationToken token = _closing.Token;
            await annotations.LoadPageAsync(pageIndex, token).ConfigureAwait(true);
            Wake();
            await selection.LoadPageAsync(pageIndex, token).ConfigureAwait(true);
            await bridge.LoadPageAsync(pageIndex, token).ConfigureAwait(true);
            if (generation == _generation)
            {
                RefreshAccessiblePages(pageIndex);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
        }
#pragma warning disable CA1031 // the page still draws; the host hears why its text or links are missing
        catch (Exception exception)
#pragma warning restore CA1031
        {
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

    private void RefreshAccessiblePages(int loaded)
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

            _host.AccessibilityChanged(new AccessibilityChange(AccessibilityChangeKind.ChildrenChanged, _bridge.GetPage(loaded)));
        }
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

    private void OnAnnotationsChanged(object? sender, EventArgs e)
    {
        UpdateEditor();
        Wake();
    }

    private void UpdateEditor()
    {
        FieldEditor? editor = null;
        if (_annotations?.Focused is { } target)
        {
            editor = _annotations.FocusedField switch
            {
                PdfTextField { IsReadOnly: false } text => new FieldEditor(
                    target, FieldEditorKind.Text, text.Value ?? string.Empty, text.IsMultiline, text.IsPassword, text.MaxLength, [], -1),
                PdfChoiceField { IsReadOnly: false } choice => new FieldEditor(
                    target,
                    FieldEditorKind.Choice,
                    choice.Values.Count > 0 ? choice.Values[0] : string.Empty,
                    false,
                    false,
                    null,
                    [.. choice.Options.Select(option => option.DisplayText)],
                    choice.SelectedIndices.Count > 0 ? choice.SelectedIndices[0] : -1),
                _ => null,
            };
        }

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

    // ---- Drawing ----

    /// <summary>
    /// Draws the current frame: the surroundings, each visible page's paper, the content tiles (farthest scale first, so sharp tiles
    /// end on top), the transparent overlay tiles, search highlights, the selection and the focus chrome. Runs in the control's render
    /// pass; tiles are only drawn here, never rendered.
    /// </summary>
    public void Draw(DrawingContext context, Size size)
    {
        bool dark = IsDark;
        context.FillRectangle(dark ? DarkBackgroundBrush : LightBackgroundBrush, new Rect(size));
        if (_zoom is null || _input is null)
        {
            return;
        }

        PageLayout layout = _zoom.Layout;
        Viewport viewport = _input.Viewport;
        int count = _zoom.GetFrame(_parts);
        if (count > _parts.Length)
        {
            _parts = new FramePart[count * 2];
            count = _zoom.GetFrame(_parts);
        }

        ReadOnlySpan<FramePart> parts = _parts.AsSpan(0, count);
        IBrush paper = dark ? Brushes.Black : Brushes.White;

        // Device pixels from here on: tiles are drawn one bitmap pixel per device pixel when sharp, scaled when not.
        using (context.PushTransform(Matrix.CreateScale(1 / _ratio, 1 / _ratio)))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.MediumQuality, EdgeMode = EdgeMode.Aliased }))
        {
            foreach (FramePart part in parts)
            {
                if (part.Kind == FramePartKind.Paper)
                {
                    Rect r = ToRect(part.Destination);
                    context.FillRectangle(ShadowBrush, r.Inflate(_ratio));
                    context.FillRectangle(paper, r);
                }
                else if ((part.Tile.Layers & DisplayLayers.Content) != 0 && Surface.TryGetImage(part.Tile, out AvaloniaBitmap? image))
                {
                    DrawTile(context, image!, part.Destination);
                }
            }

            if (_showAnnotations && Surface.Stale.Count > 0)
            {
                DrawStaleOverlays(context, parts, viewport, layout.Rotation);
            }

            foreach (FramePart part in parts)
            {
                if (part.Kind != FramePartKind.Paper && (part.Tile.Layers & DisplayLayers.Content) == 0 && Surface.TryGetImage(part.Tile, out AvaloniaBitmap? image))
                {
                    DrawTile(context, image!, part.Destination);
                }
            }

            DrawSearch(context, layout, viewport);
            DrawSelection(context, layout, viewport);

            // Chrome: a pressed link or field, the mouse's hovered one, and the keyboard focus (never a ring after a tap or click).
            if (_annotations!.Pressed is { } pressed)
            {
                DrawChrome(context, pressed, layout, viewport, 2);
            }
            else if (_mouseHover && _mode == PointerMode.None && _annotations.Hovered is { } hovered
                && (AllowAnnotationEditing || _annotations.GetRole(hovered) != AnnotationRole.Annotation))
            {
                DrawChrome(context, hovered, layout, viewport, 1);
            }

            if (_keyboardFocus && _annotations.Focused is { } focused && _editor is null)
            {
                DrawChrome(context, focused, layout, viewport, 2);
            }
        }
    }

    private static void DrawTile(DrawingContext context, AvaloniaBitmap image, in LayoutRectangle destination)
    {
        PixelSize size = image.PixelSize;
        context.DrawImage(image, new Rect(0, 0, size.Width, size.Height), ToRect(destination));
    }

    private void DrawStaleOverlays(DrawingContext context, ReadOnlySpan<FramePart> parts, in Viewport viewport, int rotation)
    {
        foreach ((Tile tile, (WriteableBitmap image, long _)) in Surface.Stale)
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
                    DrawTile(context, image, new LayoutRectangle(d.X + tile.Region.X, d.Y + tile.Region.Y, tile.Region.Width, tile.Region.Height));
                    break;
                }
            }
        }
    }

    private void DrawSearch(DrawingContext context, PageLayout layout, in Viewport viewport)
    {
        // Search matches are an overlay over the tiles: highlighting them draws no page again.
        if (_search is null || _search.MatchCount == 0)
        {
            return;
        }

        int visible = VisiblePages(layout, viewport);
        foreach (VisiblePage page in _visible.AsSpan(0, visible))
        {
            int quads = _search.GetQuads(page.PageIndex, _quads);
            if (quads > _quads.Length)
            {
                _quads = new Quad[quads * 2];
                quads = _search.GetQuads(page.PageIndex, _quads);
            }

            for (int i = 0; i < quads; i++)
            {
                FillQuad(context, MatchBrush, layout, viewport, page.PageIndex, _quads[i]);
            }
        }

        if (_search.Current is { } current)
        {
            foreach (Quad quad in current.Quads)
            {
                FillQuad(context, CurrentMatchBrush, layout, viewport, current.PageIndex, quad);
            }
        }
    }

    private void DrawSelection(DrawingContext context, PageLayout layout, in Viewport viewport)
    {
        _handlesShown = false;
        if (_selection is null || _selection.IsEmpty)
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
                Quad quad = _quads[i];
                (Point a, Point b, Point c, Point d) = FillQuad(context, SelectionBrush, layout, viewport, page.PageIndex, quad);
                if (page.PageIndex == start.PageIndex && i == 0)
                {
                    DrawHandle(context, d, a);
                    _startHandle = new Point(a.X / _ratio, a.Y / _ratio);
                    startShown = true;
                }

                if (page.PageIndex == end.PageIndex && i == quads - 1)
                {
                    DrawHandle(context, c, b);
                    _endHandle = new Point(b.X / _ratio, b.Y / _ratio);
                    endShown = true;
                }
            }
        }

        _handlesShown = startShown || endShown;
        if (!startShown)
        {
            _startHandle = new Point(double.NaN, double.NaN);
        }

        if (!endShown)
        {
            _endHandle = new Point(double.NaN, double.NaN);
        }
    }

    private static (Point A, Point B, Point C, Point D) FillQuad(DrawingContext context, IBrush brush, PageLayout layout, in Viewport viewport, int page, in Quad quad)
    {
        Point a = ToPoint(layout.PageToViewport(viewport, page, quad.BottomLeft.X, quad.BottomLeft.Y));
        Point b = ToPoint(layout.PageToViewport(viewport, page, quad.BottomRight.X, quad.BottomRight.Y));
        Point c = ToPoint(layout.PageToViewport(viewport, page, quad.TopRight.X, quad.TopRight.Y));
        Point d = ToPoint(layout.PageToViewport(viewport, page, quad.TopLeft.X, quad.TopLeft.Y));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(a, isFilled: true);
            g.LineTo(b);
            g.LineTo(c);
            g.LineTo(d);
            g.EndFigure(isClosed: true);
        }

        context.DrawGeometry(brush, null, geometry);
        return (a, b, c, d);
    }

    private void DrawHandle(DrawingContext context, Point top, Point bottom)
    {
        // A stem along the selection's edge with a knob below it (touch only: a mouse drags the text itself).
        double radius = HandleRadius * _ratio;
        context.DrawLine(new ImmutablePen(HandleBrush, 2 * _ratio), top, bottom);
        context.DrawEllipse(HandleBrush, null, new Point(bottom.X, bottom.Y + radius), radius, radius);
    }

    private void DrawChrome(DrawingContext context, AnnotationTarget target, PageLayout layout, in Viewport viewport, double width)
    {
        LayoutRectangle box = _annotations!.GetScreenRectangle(target, layout, viewport);
        context.DrawRectangle(null, new ImmutablePen(ChromeBrush, width * _ratio), ToRect(box).Inflate(_ratio));
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

        static double Distance(Point handle, double x, double y) =>
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
        _mouseHover = true;
        if (_annotations is not null && TryGetPagePoint(x, y, out PagePoint point))
        {
            AnnotationTarget? before = _annotations.Hovered;
            if (_annotations.PointerMove(point) != before)
            {
                Wake();
            }
        }
    }

    /// <summary>What the pointer is over: a link or field (hand), selectable text (I-beam) or nothing.</summary>
    public HoverTarget GetHoverTarget(double x, double y)
    {
        if (_annotations is null || !TryGetPagePoint(x, y, out PagePoint point))
        {
            return HoverTarget.None;
        }

        if (_annotations.HitTest(point) is { } target && (AllowAnnotationEditing || _annotations.GetRole(target) != AnnotationRole.Annotation))
        {
            return HoverTarget.Link;
        }

        return IsOverText(point) ? HoverTarget.Text : HoverTarget.None;
    }

    private bool HandleKey(string key, bool shift, bool command, InputModifiers modifiers)
    {
        switch (key)
        {
            case "Tab":
                _keyboardFocus = true;
                return shift ? _annotations!.FocusPrevious() : _annotations!.FocusNext();
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
            case "F3":
                return FindNext(shift) is not null;
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

    private static Rect ToRect(in LayoutRectangle r) => new(r.X, r.Y, Math.Max(r.Width, 0), Math.Max(r.Height, 0));

    private static Point ToPoint(PathPoint p) => new(p.X, p.Y);

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

        _annotations = null;
        _bridge = null;
        _search?.Dispose();
        _search = null;
        _revealed = null;
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
        _closing.Dispose();
    }
}

/// <summary>What the pointer hovers over, for the cursor.</summary>
internal enum HoverTarget
{
    None,
    Text,
    Link,
}
