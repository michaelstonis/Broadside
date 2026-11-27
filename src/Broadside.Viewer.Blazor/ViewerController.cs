using System.Diagnostics;
using System.Runtime.Versioning;
using Broadside.Graphics;
using Broadside.Rendering;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// Everything behind one <see cref="PdfViewer"/>: the viewer core objects of the open document, the frame loop paced by
/// <c>requestAnimationFrame</c>, and the translation of DOM events into the core's input model, text selection, annotation layer and
/// accessibility bridge. Runs on the browser's single thread; nothing here blocks it except the work the core does in a frame.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class ViewerController : IAsyncDisposable
{
    /// <summary>The time a frame gives the tile scheduler; the rest of a 60 Hz frame is left to composing and the browser.</summary>
    private static readonly TimeSpan FrameBudget = TimeSpan.FromMilliseconds(8);

    /// <summary>A frame with less work than this starts loading one visible page's text, links and accessible tree.</summary>
    private static readonly TimeSpan IdleFrame = TimeSpan.FromMilliseconds(4);

    // With a render worker, a page's text, links and accessibility nodes load once the view has been still this long: their display
    // lists are built on this thread, which a scroll must not wait for (#357).
    private static readonly TimeSpan SettleBeforeLoads = TimeSpan.FromMilliseconds(300);

    /// <summary>Real page sizes replace placeholders at most this often, so a remote document does not relayout per page.</summary>
    private static readonly TimeSpan RelayoutInterval = TimeSpan.FromMilliseconds(250);

    private const int Stride = 9;
    private const DisplayLayers AllLayers = DisplayLayers.Content | DisplayLayers.Annotations | DisplayLayers.FormFields;

    private readonly PdfViewer _owner;
    private readonly CancellationTokenSource _closing = new();
    private readonly CanvasSurface _surface;
    private readonly HashSet<int> _requested = [];
    private readonly List<int> _accessiblePages = [];
    private FramePart[] _parts = new FramePart[64];
    private VisiblePage[] _visible = new VisiblePage[8];
    private Quad[] _quads = new Quad[64];
    private double[] _commands = new double[Stride * 256];

    private double _width = 1;
    private double _height = 1;
    private double _ratio = 1;

    private DocumentSession? _session;
    private bool _remoteRendering;
    private Viewport _lastViewport;
    private long _lastMove;
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
    private DocumentSearch? _search;
    private AnnotationLayer? _annotations;
    private AccessibilityBridge? _bridge;
    private FormFilling? _filling;
    private AnnotationTarget? _editorTarget;
    private string _editorShown = string.Empty;
    private bool _fromEditor;
    private bool _showAnnotations = true;
    private ColorFilter? _colorFilter;

    private TaskCompletionSource? _wake;
    private bool _needsFrame = true;
    private Task? _loop;
    private PointerMode _mode;
    private int _panPointers;
    private AccessibleNode? _pendingFocus;
    private bool _pendingFollow;
    private bool _touched;
    private bool _focusHost;
    private PdfViewerState _state;

    public ViewerController(PdfViewer owner)
    {
        _owner = owner;
        Id = ViewerInterop.Register(this);
        _surface = new CanvasSurface(Id);
    }

    private enum PointerMode
    {
        None,
        Pan,
        Select,
        Annotation,
    }

    /// <summary>Gets the id the JavaScript side knows this viewer by.</summary>
    public int Id { get; }

    /// <summary>Gets the DOM id of the viewer's host element.</summary>
    public string ElementId => $"bs-viewer-{Id}";

    /// <summary>Gets or sets whether presses on markup annotations select and drag them (otherwise they pan or select text).</summary>
    public bool AllowAnnotationEditing { get; set; }

    /// <summary>Gets the accessibility bridge of the open document.</summary>
    public AccessibilityBridge? Bridge => _bridge;

    /// <summary>Gets the pages whose accessible tree is loaded, in page order: the component renders their text layer.</summary>
    public IReadOnlyList<int> AccessiblePages => _accessiblePages;

    /// <summary>Gets the DOM ids of the rendered accessible nodes (the component fills it while rendering).</summary>
    public Dictionary<AccessibleNode, string> NodeIds { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets the accessible nodes by DOM id.</summary>
    public Dictionary<string, AccessibleNode> NodesById { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the current state.</summary>
    public PdfViewerState State => _state;

    /// <summary>Attaches to the DOM and starts the frame loop.</summary>
    public async Task AttachAsync()
    {
        await ViewerInterop.ImportAsync().ConfigureAwait(true);
        await ViewerInterop.Attach(Id, ElementId).ConfigureAwait(true);
        _loop = RunAsync();
    }

    /// <summary>Opens a document: page 1 is laid out and drawn as soon as its own objects are loaded.</summary>
    public async Task OpenAsync(Func<CancellationToken, Task<DocumentSession>> open, bool ownsSession, PageLayoutOptions? layoutOptions)
    {
        Close();
        int generation = ++_generation;
        _layoutOptions = layoutOptions is null ? null : LayoutKey.Of(layoutOptions).ToOptions();
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

        // Page 1's size stands in for every page until its own arrives (#211's seam): the layout exists without loading any other
        // page object, so a remote linearized file draws page 1 from its first-page section alone.
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
        // With a render worker (#357) the content tiles come from its copy of the document; annotations and form fields get tiles of
        // their own, drawn here, so an edit never waits for the worker and the worker's copy never needs it.
        _remoteRendering = session.RemoteRenderer is not null;
        var schedulerOptions = new TileSchedulerOptions();
        if (_remoteRendering)
        {
            schedulerOptions.WithSeparateOverlays();
        }

        if (TileObserver is { } observer)
        {
            schedulerOptions.UseObserver(observer);
        }

        ViewerInterop.MarkWorker(Id, _remoteRendering);
        _scheduler = new TileScheduler(session, layout, _surface, schedulerOptions);
        if (!_showAnnotations)
        {
            _scheduler.SetLayers(DisplayLayers.Content);
        }

        double width = _width * _ratio;
        double height = _height * _ratio;
        double actualSize = _ratio * 96 / 72;
        double zoom = layout.GetZoom(ZoomMode.FitWidth, width, height, 0, actualSize);
        _zoom = new ProgressiveZoom(_scheduler, new Viewport(0, 0, width, height, zoom));
        _input = new InputModel(_zoom, new InputModelOptions().WithPixelRatio(_ratio));
        _input.Resize(_width, _height);

        _selection = new TextSelection(session);
        _selection.Changed += (_, _) => Wake();
        _search = new DocumentSearch(session);
        _search.Changed += (_, _) => Wake();
        _annotations = new AnnotationLayer(session, _scheduler);
        _annotations.ActionRequested += OnActionRequested;
        _annotations.Changed += (_, _) => Wake();
        _bridge = new AccessibilityBridge(session, _annotations) { Viewer = _zoom, Observer = OnAccessibilityChange };
        _annotations.Accessibility = _bridge;
        _annotations.XfaFormDetected += (_, e) => _owner.RaiseXfaForm(e);
        _filling = new FormFilling(_annotations);
        _filling.Changed += OnFillingChanged;

        SetState(_state with { IsLoading = false, PageCount = session.PageCount });
        _ = SweepMetricsAsync(session, generation);
        Wake();
    }

    /// <summary>Gets or sets the observer given to the tile scheduler of the next document opened.</summary>
    public Action<TileSchedulerEvent>? TileObserver { get; set; }

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

    /// <summary>
    /// Sets the color filter of the open document and of those opened later; null keeps each session's own. A change redraws every
    /// tile from the cached display lists (spec #33 viewer capability 9).
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

    /// <summary>Shows or hides the annotation and form-field layers.</summary>
    public void SetShowAnnotations(bool show)
    {
        _showAnnotations = show;
        _scheduler?.SetLayers(show ? AllLayers : DisplayLayers.Content);
        Wake();
    }

    public void GoToPage(int pageIndex)
    {
        _input?.GoToPage(pageIndex);
        Wake();
    }

    public void GoTo(ViewTarget target)
    {
        if (_zoom is not null && _input is not null)
        {
            _zoom.SetViewport(target.GetViewport(_zoom.Layout, _input.Viewport, _input.ActualSize, _zoom.MinZoom, _zoom.MaxZoom));
            Wake();
        }
    }

    public void NextPage()
    {
        _input?.NextPage();
        Wake();
    }

    public void PreviousPage()
    {
        _input?.PreviousPage();
        Wake();
    }

    public void ZoomTo(ZoomMode mode)
    {
        _input?.ZoomTo(mode);
        Wake();
    }

    public void ZoomBy(double factor)
    {
        if (_zoom is not null)
        {
            _zoom.ZoomBy(factor, _width * _ratio / 2, _height * _ratio / 2);
            Wake();
        }
    }

    /// <summary>Gets the open document's session, or null.</summary>
    public DocumentSession? Session => _session;

    /// <summary>Gets the text selection of the open document.</summary>
    public TextSelection? Selection => _selection;

    /// <summary>Gets the find-in-document search of the open document.</summary>
    public DocumentSearch? Search => _search;

    /// <summary>Gets the annotation layer of the open document.</summary>
    public AnnotationLayer? Annotations => _annotations;

    /// <summary>Gets the form filling of the open document.</summary>
    public FormFilling? Forms => _filling;

    /// <summary>Applies new layout options to the open document, keeping the current page in view; options equal to the current ones change nothing.</summary>
    public void SetLayoutOptions(PageLayoutOptions? options)
    {
        LayoutKey key = LayoutKey.Of(options);
        if (key == LayoutKey.Of(_layoutOptions))
        {
            return;
        }

        _layoutOptions = options is null ? null : key.ToOptions();
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

    /// <summary>Scrolls the least distance that brings an area of a page into view, or centres it when it is not on the screen.</summary>
    public void Reveal(int pageIndex, PdfRectangle area)
    {
        if (_zoom is null || _input is null || (uint)pageIndex >= (uint)_metrics.Length)
        {
            return;
        }

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

    // ---- DOM events (called synchronously from JavaScript) ----

    public void Resize(double width, double height, double pixelRatio)
    {
        _width = width;
        _height = height;
        _ratio = pixelRatio;
        if (_input is not null)
        {
            _input.PixelRatio = pixelRatio;
            _input.Resize(width, height);
        }

        Wake();
    }

    public void PointerDown(int pointerId, PointerKind kind, double x, double y, int clicks, InputModifiers modifiers)
    {
        if (_input is null)
        {
            return;
        }

        _touched = kind != PointerKind.Mouse; // a finger leaves no hover behind: only a mouse's hover is drawn

        if (_mode == PointerMode.Pan)
        {
            // A further finger joins the pan: the input model turns two into a pinch.
            _panPointers++;
            _input.PointerDown(pointerId, kind, x, y);
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

                _mode = PointerMode.Select;
                Wake();
                return;
            }
        }

        if (kind == PointerKind.Mouse && !_selection!.IsEmpty)
        {
            _selection.Clear();
        }

        _mode = PointerMode.Pan;
        _panPointers = 1;
        _input.PointerDown(pointerId, kind, x, y);
        Wake();
    }

    /// <summary>A move; returns the cursor: 0 default, 1 pointer (link or field), 2 text, 3 grabbing.</summary>
    public int PointerMove(int pointerId, double x, double y)
    {
        if (_input is null)
        {
            return 0;
        }

        switch (_mode)
        {
            case PointerMode.Pan:
                _input.PointerMove(pointerId, x, y);
                Wake();
                return 3;
            case PointerMode.Select:
                if (TryGetPagePoint(x, y, out PagePoint selecting))
                {
                    _selection!.Extend(selecting);
                }

                return 2;
            case PointerMode.Annotation:
                if (TryGetPagePoint(x, y, out PagePoint dragging))
                {
                    _annotations!.PointerMove(dragging);
                }

                return 1;
        }

        if (!TryGetPagePoint(x, y, out PagePoint point))
        {
            return 0;
        }

        if (_annotations!.PointerMove(point) is { } hovered
            && (AllowAnnotationEditing || _annotations.GetRole(hovered) != AnnotationRole.Annotation))
        {
            return 1;
        }

        return IsOverText(point) ? 2 : 0;
    }

    public void PointerUp(int pointerId, double x, double y)
    {
        if (_input is null)
        {
            return;
        }

        PointerMode mode = _mode;
        _mode = mode == PointerMode.Pan && --_panPointers > 0 ? PointerMode.Pan : PointerMode.None;
        switch (mode)
        {
            case PointerMode.Annotation:
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
                break;
            case PointerMode.Select:
                // The release point ends the drag too: a last move the browser folded into the release, or one it never delivered
                // (a fast drag), still selects up to where the pointer let go.
                if (TryGetPagePoint(x, y, out PagePoint released))
                {
                    _selection!.Extend(released);
                }

                break;
        }

        Wake();
    }

    public void PointerCancel(int pointerId)
    {
        if (_mode == PointerMode.Annotation)
        {
            _annotations?.PointerCancel();
        }
        else if (_mode == PointerMode.Pan)
        {
            _input?.PointerCancel(pointerId);
            if (--_panPointers > 0)
            {
                Wake();
                return;
            }
        }

        _mode = PointerMode.None;
        Wake();
    }

    public void Wheel(double deltaX, double deltaY, WheelDeltaMode mode, InputModifiers modifiers, double x, double y)
    {
        _input?.Wheel(deltaX, deltaY, mode, modifiers, x, y);
        Wake();
    }

    /// <summary>A key press; returns whether the viewer handled it (the browser's default action is then prevented).</summary>
    public bool KeyDown(string key, InputModifiers modifiers)
    {
        if (_input is null)
        {
            return false;
        }

        bool shift = (modifiers & InputModifiers.Shift) != 0;
        bool command = (modifiers & (InputModifiers.Control | InputModifiers.Meta)) != 0;
        bool handled = HandleKey(key, shift, command, modifiers);
        if (handled)
        {
            Wake();
        }

        return handled;
    }

    private bool HandleKey(string key, bool shift, bool command, InputModifiers modifiers)
    {
        // Form fields first (#218): Tab commits the field's edit and moves on, Space toggles, arrows pick options, Enter commits.
        InputKey fieldKey = key switch
        {
            "Tab" => InputKey.Tab,
            "Enter" => InputKey.Enter,
            "Escape" => InputKey.Escape,
            " " => InputKey.Space,
            "ArrowUp" => InputKey.ArrowUp,
            "ArrowDown" => InputKey.ArrowDown,
            "Home" => InputKey.Home,
            "End" => InputKey.End,
            _ => InputKey.None,
        };
        if (fieldKey == InputKey.Tab)
        {
            return _filling!.KeyDown(fieldKey, modifiers);
        }

        if (fieldKey != InputKey.None && !command && _filling!.Kind is FieldEditKind.Button or FieldEditKind.Choice && _filling.KeyDown(fieldKey, modifiers))
        {
            return true;
        }

        switch (key)
        {
            case "Enter" when _annotations!.Focused is { } focused:
                _annotations.Activate(focused);
                return true;
            case "Escape":
                _selection!.Clear();
                _annotations!.Focus(null);
                _bridge!.SetFocus(null);
                _focusHost = true;
                return true;
            case "a" or "A" when command:
                _selection!.SelectAll();
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

    /// <summary>The selected text for a copy event, or <see langword="null"/> (nothing selected, or still loading: written later).</summary>
    public string? Copy()
    {
        if (_selection is null || _selection.IsEmpty)
        {
            return null;
        }

        ValueTask<string> text = _selection.GetTextAsync(_closing.Token);
        if (text.IsCompletedSuccessfully)
        {
            return text.Result;
        }

        _ = CopyLaterAsync(text);
        return null;
    }

    private static async Task CopyLaterAsync(ValueTask<string> text)
    {
        try
        {
            ViewerInterop.WriteClipboard(await text.ConfigureAwait(true));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void NodeFocused(string elementId)
    {
        if (_bridge is not null && NodesById.TryGetValue(elementId, out AccessibleNode? node) && !ReferenceEquals(_bridge.Focus, node))
        {
            _bridge.SetFocus(node);

            // A form field or link reached through the accessible tree takes the layer's keyboard focus too (#218), so a screen
            // reader's Tab opens the field's editor as the viewer's own Tab does.
            if (node.Source is Broadside.Objects.CosDictionary source && _annotations is { } layer && layer.FindTarget(node.PageIndex, source) is { } target)
            {
                layer.Focus(target);
            }

            Wake();
        }
    }

    // ---- Form fields (#218) ----

    /// <summary>The native editor's text changed (typing, an input method, a paste).</summary>
    public void EditorInput(string value, int caret)
    {
        // The editor already shows the text: the change it raises must not write it back (that would move the caret).
        _fromEditor = true;
        try
        {
            _filling?.SetText(value, caret);
        }
        finally
        {
            _fromEditor = false;
        }
    }

    /// <summary>The native picker's options changed: picked and committed at once.</summary>
    public void EditorSelect(string indices)
    {
        if (_filling is null)
        {
            return;
        }

        var picked = new List<int>();
        foreach (string part in indices.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int index))
            {
                picked.Add(index);
            }
        }

        if (_filling.SetSelectedOptions(picked))
        {
            _filling.Commit();
        }

        Wake();
    }

    /// <summary>The native editor lost focus: its edit is written through the form API.</summary>
    public void EditorCommit()
    {
        _filling?.Commit();
        Wake();
    }

    /// <summary>A key in the native editor that the viewer handles: Tab, Enter in a single line, Escape.</summary>
    public bool EditorKey(string key, InputModifiers modifiers)
    {
        if (_filling is null)
        {
            return false;
        }

        InputKey input = key switch
        {
            "Tab" => InputKey.Tab,
            "Enter" => InputKey.Enter,
            "Escape" => InputKey.Escape,
            _ => InputKey.None,
        };
        bool handled = input != InputKey.None && _filling.KeyDown(input, modifiers);
        if (input == InputKey.Escape && handled)
        {
            ViewerInterop.SetEditorValue(Id, _filling.Text);
        }

        Wake();
        return handled;
    }

    private void OnFillingChanged(object? sender, EventArgs e)
    {
        FormFilling filling = _filling!;
        AnnotationTarget? target = filling.IsReadOnly ? null : filling.Kind switch
        {
            FieldEditKind.Text or FieldEditKind.Choice => filling.Target,
            _ => null,
        };
        string shown = target is null ? string.Empty : filling.Text + "\u0001" + string.Join(',', filling.SelectedOptions);
        if (target == _editorTarget && (target is null || shown == _editorShown || _fromEditor))
        {
            _editorShown = shown;
            Wake();
            return;
        }

        _editorTarget = target;
        _editorShown = shown;
        if (target is { } t && filling.Field is { } field)
        {
            // 1 single line, 2 multi-line, 3 password, 4 combo box, 5 single-select list box, 6 multi-select list box.
            int kind = field switch
            {
                Broadside.Forms.PdfTextField { IsPassword: true } => 3,
                Broadside.Forms.PdfTextField { IsMultiline: true } => 2,
                Broadside.Forms.PdfTextField => 1,
                Broadside.Forms.PdfComboBoxField => 4,
                Broadside.Forms.PdfChoiceField { IsMultiSelect: true } => 6,
                _ => 5,
            };
            string[] options = field is Broadside.Forms.PdfChoiceField choice ? [.. choice.Options.Select(o => o.DisplayText)] : [];
            string selected = string.Join(',', filling.SelectedOptions.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            ViewerInterop.OpenEditor(Id, kind, filling.Text, filling.MaxLength ?? 0, options, selected, _annotations!.GetAccessibleName(t), filling.IsRequired);
        }
        else
        {
            ViewerInterop.CloseEditor(Id);
        }

        Wake();
    }

    // ---- Frames ----

    private async Task RunAsync()
    {
        while (!_closing.IsCancellationRequested)
        {
            if (!_needsFrame)
            {
                var wake = new TaskCompletionSource();
                _wake = wake;
                await wake.Task.ConfigureAwait(true);
                continue;
            }

            _needsFrame = false;
            await ViewerInterop.NextFrame(Id).ConfigureAwait(true);
            if (_closing.IsCancellationRequested)
            {
                break;
            }

            try
            {
                _needsFrame |= Frame();
            }
#pragma warning disable CA1031 // a failing frame is reported, and the next event runs another
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                _owner.ReportError(exception);
            }
        }
    }

    private void Wake()
    {
        _needsFrame = true;
        TaskCompletionSource? wake = _wake;
        _wake = null;
        wake?.TrySetResult();
    }

    private bool Frame()
    {
        if (_input is null || _zoom is null)
        {
            Span<double> empty = _commands.AsSpan(0, Stride);
            empty.Clear();
            ViewerInterop.Draw(Id, empty);
            return false;
        }

        long start = Stopwatch.GetTimestamp();
        if (_metricsDirty && Stopwatch.GetElapsedTime(_lastRelayout) >= RelayoutInterval)
        {
            Relayout();
        }

        bool more = _input.RunFrame(FrameBudget);
        Viewport viewport = _input.Viewport;
        _bridge!.Update();
        // A load starts in a cheap frame, or once no tile is left to draw: on a slow device every frame may take longer than IdleFrame,
        // and the page's links, fields and text must still load (#350).
        if (viewport != _lastViewport)
        {
            _lastViewport = viewport;
            _lastMove = Stopwatch.GetTimestamp();
        }

        bool settled = !_remoteRendering || Stopwatch.GetElapsedTime(_lastMove) >= SettleBeforeLoads;
        bool unloaded = RequestPageLoad(viewport, settled && (!more || Stopwatch.GetElapsedTime(start) < IdleFrame));

        if (_focusHost)
        {
            _focusHost = !ViewerInterop.FocusNode(Id, ElementId, false);
        }
        else if (_pendingFocus is { } node && NodeIds.TryGetValue(node, out string? id) && ViewerInterop.FocusNode(Id, id, _pendingFollow))
        {
            _pendingFocus = null;
        }

        Compose(viewport);
        SetState(new PdfViewerState(_input.CurrentPage, _metrics.Length, viewport.Zoom / _input.ActualSize, false));
        return more || unloaded || _metricsDirty || _input.IsAnimating;
    }

    private void Relayout()
    {
        _metricsDirty = false;
        _lastRelayout = Stopwatch.GetTimestamp();
        PageLayout layout = PageLayout.Create(_metrics, _layoutOptions);
        PageLayout previous = _zoom!.Layout;
        _scheduler!.SetLayout(layout);
        _zoom.SetViewport(layout.Anchor(previous, _zoom.Viewport)); // the page in view stays put as real sizes replace placeholders (#348)
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

    /// <summary>Starts loading the first visible page not yet requested when <paramref name="start"/>; returns whether one remains.</summary>
    private bool RequestPageLoad(in Viewport viewport, bool start)
    {
        PageLayout layout = _zoom!.Layout;
        int count = layout.GetVisiblePages(viewport, _visible);
        if (count > _visible.Length)
        {
            _visible = new VisiblePage[count * 2];
            count = layout.GetVisiblePages(viewport, _visible);
        }

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

    /// <summary>Loads a page's selectable text, annotations and accessible tree, so the page can be selected, clicked and read.</summary>
    private async Task SettledAsync(CancellationToken token)
    {
        while (_remoteRendering && Stopwatch.GetElapsedTime(_lastMove) < SettleBeforeLoads)
        {
            await Task.Delay(SettleBeforeLoads, token).ConfigureAwait(true);
        }
    }

    private async Task ReadPageAsync(int pageIndex, int generation)
    {
        // The open document's layers, held here: a document opened while this page loads replaces the fields, and the page index
        // belongs to this document, never to the next one (#358: a late page of a novel reached an 8-page textbook's layers).
        AnnotationLayer annotations = _annotations!;
        TextSelection selection = _selection!;
        AccessibilityBridge bridge = _bridge!;
        try
        {
            CancellationToken token = _closing.Token;
            // Links and fields first: they are cheap and make the page interactive; text selection extracts the page's layout (#350).
            await annotations.ReadPageAsync(pageIndex, token).ConfigureAwait(true);

            // Text selection builds the page's display list and accessibility extracts its layout on this thread, each one long task:
            // with a render worker they wait until the view is still, so a scroll that starts meanwhile is not held up (#357).
            await SettledAsync(token).ConfigureAwait(true);
            if (generation != _generation)
            {
                return;
            }

            await selection.ReadPageAsync(pageIndex, token).ConfigureAwait(true);
            await SettledAsync(token).ConfigureAwait(true);
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
                _owner.ReportError(exception);
            }
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

        _owner.RequestRender();
    }

    private void OnAccessibilityChange(AccessibilityChange change)
    {
        switch (change.Kind)
        {
            case AccessibilityChangeKind.FocusChanged:
                _pendingFocus = change.Node;
                _pendingFollow = change.FollowsViewer; // scrolling moves the keyboard focus only while it is on the text layer (#349)
                _focusHost = change.Node is null;
                Wake();
                break;
            case AccessibilityChangeKind.ChildrenChanged:
                RefreshAccessiblePages();
                Wake();
                break;
        }
    }

    private void OnActionRequested(object? sender, ViewerActionEventArgs e)
    {
        // Destinations inside the document are followed; everything else (URIs, launches, scripts) goes to the host and is never
        // performed here.
        if (e.Destination is { } destination && _zoom is not null && _input is not null)
        {
            _zoom.SetViewport(AnnotationLayer.GetDestinationViewport(destination, _zoom.Layout, _input.Viewport));
        }
        else if (e.PageIndex is int page && e.Uri is null)
        {
            _input?.GoToPage(page);
        }

        _owner.RaiseAction(e);
        Wake();
    }

    private void Compose(in Viewport viewport)
    {
        PageLayout layout = _zoom!.Layout;
        int records = 0;
        int count = _zoom.GetFrame(_parts);
        if (count > _parts.Length)
        {
            _parts = new FramePart[count * 2];
            count = _zoom.GetFrame(_parts);
        }

        // Paper and content tiles first, then overlay tiles (separate with a render worker) over all of them.
        foreach (FramePart part in _parts.AsSpan(0, count))
        {
            LayoutRectangle d = part.Destination;
            if (part.Kind == FramePartKind.Paper)
            {
                Add(ref records, 1, d.X, d.Y, d.Width, d.Height);
            }
            else if ((part.Tile.Layers & DisplayLayers.Content) != 0 && _surface.TryGetKey(part.Tile, out int key))
            {
                Add(ref records, 2, key, d.X, d.Y, d.Width, d.Height);
            }
        }

        foreach (FramePart part in _parts.AsSpan(0, count))
        {
            LayoutRectangle d = part.Destination;
            if (part.Kind != FramePartKind.Paper && (part.Tile.Layers & DisplayLayers.Content) == 0 && _surface.TryGetKey(part.Tile, out int key))
            {
                Add(ref records, 2, key, d.X, d.Y, d.Width, d.Height);
            }
        }

        // Search matches are an overlay over the tiles (#215): highlighting them draws no page again.
        if (_search!.MatchCount > 0)
        {
            SearchMatch? current = _search.Current;
            int visible = Math.Min(layout.GetVisiblePages(viewport, _visible), _visible.Length);
            foreach (VisiblePage page in _visible.AsSpan(0, visible))
            {
                int quads = _search.GetQuads(page.PageIndex, _quads);
                if (quads > _quads.Length)
                {
                    _quads = new Quad[quads * 2];
                    quads = _search.GetQuads(page.PageIndex, _quads);
                }

                AddQuads(ref records, 6, layout, viewport, page.PageIndex, _quads.AsSpan(0, quads));
            }

            if (current is not null)
            {
                IReadOnlyList<Quad> quads = current.Quads;
                for (int i = 0; i < quads.Count; i++)
                {
                    Quad quad = quads[i];
                    AddQuads(ref records, 7, layout, viewport, current.PageIndex, new ReadOnlySpan<Quad>(in quad));
                }
            }
        }

        if (!_selection!.IsEmpty)
        {
            int visible = Math.Min(layout.GetVisiblePages(viewport, _visible), _visible.Length);
            foreach (VisiblePage page in _visible.AsSpan(0, visible))
            {
                int quads = _selection.GetQuads(page.PageIndex, _quads);
                if (quads > _quads.Length)
                {
                    _quads = new Quad[quads * 2];
                    quads = _selection.GetQuads(page.PageIndex, _quads);
                }

                foreach (Quad quad in _quads.AsSpan(0, quads))
                {
                    PathPoint a = layout.PageToViewport(viewport, page.PageIndex, quad.BottomLeft.X, quad.BottomLeft.Y);
                    PathPoint b = layout.PageToViewport(viewport, page.PageIndex, quad.BottomRight.X, quad.BottomRight.Y);
                    PathPoint c = layout.PageToViewport(viewport, page.PageIndex, quad.TopRight.X, quad.TopRight.Y);
                    PathPoint e = layout.PageToViewport(viewport, page.PageIndex, quad.TopLeft.X, quad.TopLeft.Y);
                    Add(ref records, 3, a.X, a.Y, b.X, b.Y, c.X, c.Y, e.X, e.Y);
                }
            }
        }

        // Links and fields show they are live: pressed while a pointer holds them, hovered under a mouse (#350).
        AnnotationLayer annotations = _annotations!;
        if (annotations.Pressed is { } pressed && annotations.GetRole(pressed) != AnnotationRole.Annotation)
        {
            LayoutRectangle r = annotations.GetScreenRectangle(pressed, layout, viewport);
            Add(ref records, 5, r.X, r.Y, r.Width, r.Height, 2);
        }
        else if (!_touched && _mode == PointerMode.None && annotations.Hovered is { } hovered && annotations.GetRole(hovered) != AnnotationRole.Annotation)
        {
            LayoutRectangle r = annotations.GetScreenRectangle(hovered, layout, viewport);
            Add(ref records, 5, r.X, r.Y, r.Width, r.Height, 1);
        }

        // The native field editor sits over its widget (#218): op 8 (x, y, w, h) in canvas pixels.
        if (_editorTarget is { } editing && annotations.GetCount(editing.PageIndex) > editing.Index)
        {
            LayoutRectangle r = annotations.GetScreenRectangle(editing, layout, viewport);
            Add(ref records, 8, r.X, r.Y, r.Width, r.Height);
        }

        double scale = 1 / _ratio;
        foreach (int page in _accessiblePages)
        {
            Matrix m = layout.GetPageTransform(viewport, page);
            Add(ref records, 4, page, m.A * scale, m.B * scale, m.C * scale, m.D * scale, m.E * scale, m.F * scale);
        }

        Span<double> header = _commands.AsSpan(0, Stride);
        header.Clear();
        header[0] = records;
        header[1] = _metrics.Length;
        header[2] = _input!.CurrentPage;
        header[3] = viewport.Zoom / _input.ActualSize;
        header[4] = _surface.Presented;
        header[5] = _session?.ColorFilter == ColorFilter.DarkMode ? 1 : 0; // dark paper and surroundings
        ViewerInterop.Draw(Id, _commands.AsSpan(0, (records + 1) * Stride));
    }

    private void AddQuads(ref int records, double op, PageLayout layout, in Viewport viewport, int pageIndex, ReadOnlySpan<Quad> quads)
    {
        foreach (Quad quad in quads)
        {
            PathPoint a = layout.PageToViewport(viewport, pageIndex, quad.BottomLeft.X, quad.BottomLeft.Y);
            PathPoint b = layout.PageToViewport(viewport, pageIndex, quad.BottomRight.X, quad.BottomRight.Y);
            PathPoint c = layout.PageToViewport(viewport, pageIndex, quad.TopRight.X, quad.TopRight.Y);
            PathPoint e = layout.PageToViewport(viewport, pageIndex, quad.TopLeft.X, quad.TopLeft.Y);
            Add(ref records, op, a.X, a.Y, b.X, b.Y, c.X, c.Y, e.X, e.Y);
        }
    }

    private void Add(ref int records, double op, double a = 0, double b = 0, double c = 0, double d = 0, double e = 0, double f = 0, double g = 0, double h = 0)
    {
        int offset = (records + 1) * Stride;
        if (offset + Stride > _commands.Length)
        {
            Array.Resize(ref _commands, _commands.Length * 2);
        }

        Span<double> record = _commands.AsSpan(offset, Stride);
        record[0] = op;
        record[1] = a;
        record[2] = b;
        record[3] = c;
        record[4] = d;
        record[5] = e;
        record[6] = f;
        record[7] = g;
        record[8] = h;
        records++;
    }

    private bool TryGetPagePoint(double x, double y, out PagePoint point)
    {
        if (_zoom is null || _input is null)
        {
            point = default;
            return false;
        }

        return _zoom.Layout.TryGetPageAt(_input.Viewport, x * _ratio, y * _ratio, out point);
    }

    private bool IsOverText(in PagePoint point) =>
        _selection!.TryGetPage(point.PageIndex, out SelectableText? text) && text!.GetGlyphAt(new PathPoint(point.X, point.Y)) >= 0;

    private void SetState(PdfViewerState state)
    {
        if (state != _state)
        {
            _state = state;
            _owner.RaiseStateChanged(state);
        }
    }

    // ---- Lifetime ----

    private void Close()
    {
        _generation++;
        _scheduler?.Dispose();
        _scheduler = null;
        _zoom = null;
        _input = null;
        _selection = null;
        _search?.Dispose();
        _search = null;
        _annotations?.ActionRequested -= OnActionRequested;
        _annotations = null;
        _filling?.Changed -= OnFillingChanged;
        _filling = null;
        if (_editorTarget is not null)
        {
            _editorTarget = null;
            ViewerInterop.CloseEditor(Id);
        }
        _bridge = null;
        _surface.Clear();
        _requested.Clear();
        _accessiblePages.Clear();
        NodeIds.Clear();
        NodesById.Clear();
        _pendingFocus = null;
        _metrics = [];
        _metricsDirty = false;
        _mode = PointerMode.None;
        if (_session is not null && _ownsSession)
        {
            _session.Dispose();
        }

        _session = null;
        _owner.RequestRender();
        Wake();
    }

    public async ValueTask DisposeAsync()
    {
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        await _closing.CancelAsync().ConfigureAwait(true);
        Close();
        ViewerInterop.Detach(Id);
        ViewerInterop.Unregister(Id);
        if (_loop is not null)
        {
            await _loop.ConfigureAwait(true);
        }

        _closing.Dispose();
    }

    /// <summary>The values of a <see cref="PageLayoutOptions"/> (a mutable class), compared to tell whether a relayout is needed.</summary>
    private readonly record struct LayoutKey(PageLayoutMode Mode, bool ShowCover, double Gap, double Padding, int Rotation)
    {
        public static LayoutKey Of(PageLayoutOptions? options)
        {
            options ??= new PageLayoutOptions();
            return new LayoutKey(options.Mode, options.ShowCover, options.Gap, options.Padding, options.Rotation);
        }

        public PageLayoutOptions ToOptions() =>
            new PageLayoutOptions().WithMode(Mode).WithCover(ShowCover).WithGap(Gap).WithPadding(Padding).WithRotation(Rotation);
    }
}
