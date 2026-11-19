using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Broadside.Graphics;
using Broadside.Rendering;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Broadside.Viewer.Avalonia;

/// <summary>
/// An Avalonia control that shows a PDF document on the shared viewer core. Tiles render with the viewer core's software rasterizer
/// off the UI thread into Avalonia bitmaps, which the control composes in its render pass, so it runs on every desktop Avalonia
/// supports (Windows, macOS, Linux) and headless.
/// </summary>
/// <remarks>
/// <para>
/// Touch, mouse, pen and wheel input go through one <see cref="InputModel"/>, so pan, fling, pinch and double-tap behave the same on
/// every platform; a trackpad pinch zooms like Ctrl+wheel. Mouse drags over text select it (double click: word, triple: line); on touch a
/// long press selects a word and handles extend it. Links and form fields go through the <see cref="AnnotationLayer"/>: links inside
/// the document navigate, every other action (URIs, launches, scripts) is raised as <see cref="ActionRequested"/> and never performed.
/// A focused text or choice field opens an editor over the widget; an edit redraws only the overlay tiles it touches. Keys: arrows,
/// Page Up/Down, Space, Home/End, +/-/0, Tab between fields and links, Enter to activate, Ctrl/Cmd+A and +C, F3 (Shift+F3) for the
/// next (previous) search match, and single letters (H headings, T tables, L lists, K links, ...) for structure navigation.
/// </para>
/// <para>
/// <see cref="Find"/> searches the document with background indexing and highlights every match over the tiles;
/// <see cref="ColorFilter"/> switches to dark mode, drawing every tile again from the cached display lists. The automation peer
/// (<see cref="PdfViewAutomationPeer"/>) exposes each page's text in reading order to screen readers.
/// </para>
/// <para>ISO 32000-2 §12.3.2 (destinations), §12.5.6.5 (link annotations), §12.7 (interactive forms), §14.8 and §14.11.2.</para>
/// </remarks>
public sealed class PdfView : Control, IDisposable
{
    /// <summary>The <see cref="Source"/> property: a file path or an http(s) address of the document to show.</summary>
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<PdfView, string?>(nameof(Source));

    /// <summary>The <see cref="Session"/> property: a document session the host opened and owns.</summary>
    public static readonly StyledProperty<DocumentSession?> SessionProperty = AvaloniaProperty.Register<PdfView, DocumentSession?>(nameof(Session));

    /// <summary>The <see cref="LayoutOptions"/> property.</summary>
    public static readonly StyledProperty<PageLayoutOptions?> LayoutOptionsProperty = AvaloniaProperty.Register<PdfView, PageLayoutOptions?>(nameof(LayoutOptions));

    /// <summary>The <see cref="ShowAnnotations"/> property.</summary>
    public static readonly StyledProperty<bool> ShowAnnotationsProperty = AvaloniaProperty.Register<PdfView, bool>(nameof(ShowAnnotations), true);

    /// <summary>The <see cref="AllowAnnotationEditing"/> property.</summary>
    public static readonly StyledProperty<bool> AllowAnnotationEditingProperty = AvaloniaProperty.Register<PdfView, bool>(nameof(AllowAnnotationEditing));

    /// <summary>The <see cref="ColorFilter"/> property.</summary>
    public static readonly StyledProperty<ColorFilter?> ColorFilterProperty = AvaloniaProperty.Register<PdfView, ColorFilter?>(nameof(ColorFilter));

    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient());
    private readonly HashSet<long> _pointers = [];
    private PdfViewAutomationPeer? _peer;
    private Control? _editorView;
    private Rect _editorBounds;
    private bool _frameRequested;
    private int _openGeneration;
    private bool _disposed;
    private Cursor? _handCursor;
    private Cursor? _textCursor;

    static PdfView()
    {
        FocusableProperty.OverrideDefaultValue<PdfView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<PdfView>(true);
    }

    /// <summary>Creates an empty viewer; set <see cref="Source"/> or <see cref="Session"/> to show a document.</summary>
    public PdfView()
        : this(null)
    {
    }

    /// <summary>Creates a viewer on a clock (tests drive gestures and animations on a manual one).</summary>
    internal PdfView(TimeProvider? time)
    {
        Controller = new ViewerController(new Host(this), time);
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnify, RoutingStrategies.Bubble);
    }

    /// <summary>Raised when the page, page count, zoom or loading state changes.</summary>
    public event EventHandler<PdfViewStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised for every action a link, widget or annotation asks for. Destinations inside the document have already been followed when
    /// this is raised; URIs, launches, named actions and scripts are never performed by the viewer.
    /// </summary>
    public event EventHandler<ViewerActionEventArgs>? ActionRequested;

    /// <summary>Raised when opening the document, loading a page's text or links, or drawing fails.</summary>
    public event EventHandler<PdfViewErrorEventArgs>? Error;

    /// <summary>Gets or sets the document to show: a file path, or an http(s) address read through range requests.</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets a document session the host opened (and disposes); it takes precedence over <see cref="Source"/>.</summary>
    public DocumentSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets or sets the page layout (continuous by default); changes apply at once, keeping the current page.</summary>
    /// <remarks>ISO 32000-2 §12.2 (Table 29, PageLayout).</remarks>
    public PageLayoutOptions? LayoutOptions
    {
        get => GetValue(LayoutOptionsProperty);
        set => SetValue(LayoutOptionsProperty, value);
    }

    /// <summary>Gets or sets whether annotations and form fields are drawn and interactive (default true).</summary>
    /// <remarks>ISO 32000-2 §12.5 (annotations), §12.7 (interactive forms).</remarks>
    public bool ShowAnnotations
    {
        get => GetValue(ShowAnnotationsProperty);
        set => SetValue(ShowAnnotationsProperty, value);
    }

    /// <summary>Gets or sets whether markup annotations can be selected and dragged (default false; links and fields always work).</summary>
    public bool AllowAnnotationEditing
    {
        get => GetValue(AllowAnnotationEditingProperty);
        set => SetValue(AllowAnnotationEditingProperty, value);
    }

    /// <summary>
    /// Gets or sets the color filter tiles are drawn with (<see cref="Rendering.ColorFilter.DarkMode"/> for dark mode), applied to the
    /// open document and to those opened later; null (the default) keeps each session's own. A change draws every tile again from the
    /// cached display lists; the tiles on screen stay until their replacements arrive.
    /// </summary>
    public ColorFilter? ColorFilter
    {
        get => GetValue(ColorFilterProperty);
        set => SetValue(ColorFilterProperty, value);
    }

    /// <summary>Gets or sets the options for documents opened from <see cref="Source"/> (fonts, credentials, threading, backend).</summary>
    /// <remarks>
    /// Without options a document opens with the defaults: tiles render on background threads with the software rasterizer, which
    /// draws the same pixels on every platform.
    /// </remarks>
    public ViewerOptions? Options { get; set; }

    /// <summary>Gets what the viewer shows.</summary>
    public PdfViewState State => Controller.State;

    /// <summary>Gets the open document's session (from <see cref="Source"/> or <see cref="Session"/>), or null.</summary>
    public DocumentSession? CurrentSession => Controller.Session;

    /// <summary>Gets the text selection of the open document, or null.</summary>
    public TextSelection? Selection => Controller.Selection;

    /// <summary>Gets the annotation and link layer of the open document, or null (form values and annotation edits go through it).</summary>
    public AnnotationLayer? Annotations => Controller.Annotations;

    /// <summary>
    /// Gets the form filling of the open document, or null: the field the keyboard edits, Tab order (each page's Tabs entry), and one
    /// commit per edit through the form API (filling through the view saves the same file as filling through the API).
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7 (interactive forms), §7.7.3.3 Table 31 (Tabs). Spec #33 viewer capability 12.</remarks>
    public FormFilling? Forms => Controller.Forms;

    /// <summary>Gets the accessibility bridge of the open document, or null.</summary>
    public AccessibilityBridge? Accessibility => Controller.Bridge;

    /// <summary>Gets the search of the open document, or null; <see cref="Find"/> runs it and the viewer highlights its matches.</summary>
    public DocumentSearch? Search => Controller.Search;

    /// <summary>Gets the open document's page labels once they are read, or null; the automation peers name pages by them.</summary>
    /// <remarks>ISO 32000-2 §12.4.2 (page labels).</remarks>
    public PageLabelTable? PageLabels => Controller.PageLabels;

    internal ViewerController Controller { get; }

    /// <summary>Gets whether a frame is requested and has not run yet (tests).</summary>
    internal bool IsFramePending => _frameRequested;

    /// <summary>Gets the form-field editor shown over the focused widget, or null (tests).</summary>
    internal Control? EditorView => _editorView;

    /// <summary>Shows a page (zero-based).</summary>
    /// <param name="pageIndex">The page.</param>
    public void GoToPage(int pageIndex) => Controller.GoToPage(pageIndex);

    /// <summary>
    /// Shows a resolved destination (an outline entry's <see cref="OutlineEntry.Target"/>, a named destination): its page, the zoom and
    /// the position its view asks for, at once.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
    public void GoTo(ViewTarget target) => Controller.GoTo(target);

    /// <summary>Shows the next page or spread.</summary>
    public void NextPage() => Controller.NextPage();

    /// <summary>Shows the previous page or spread.</summary>
    public void PreviousPage() => Controller.PreviousPage();

    /// <summary>Zooms to a fit mode or the actual size, animated.</summary>
    /// <param name="mode">The zoom mode.</param>
    public void ZoomTo(ZoomMode mode) => Controller.ZoomTo(mode);

    /// <summary>Zooms by a factor around the viewer's centre.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    public void ZoomBy(double factor) => Controller.ZoomBy(factor);

    /// <summary>Scrolls by a distance in device-independent units, as a wheel or trackpad scroll does (no animation).</summary>
    /// <param name="deltaX">The horizontal distance; positive scrolls right.</param>
    /// <param name="deltaY">The vertical distance; positive scrolls down.</param>
    public void ScrollBy(double deltaX, double deltaY) =>
        Controller.Wheel(deltaX, deltaY, WheelDeltaMode.Pixel, InputModifiers.None, Bounds.Width / 2, Bounds.Height / 2);

    /// <summary>Scrolls the least distance that shows an area of a page.</summary>
    /// <param name="pageIndex">The zero-based page.</param>
    /// <param name="area">The area, in the page's default user space.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3 (user space).</remarks>
    public void Reveal(int pageIndex, PdfRectangle area) => Controller.Reveal(pageIndex, area);

    /// <summary>
    /// Searches the open document from the current page and highlights every match; matches arrive while the remaining pages are
    /// indexed in the background. The first match found becomes current and is scrolled into view. An empty query clears the search.
    /// </summary>
    /// <param name="query">The text to find.</param>
    /// <param name="options">How to compare, or null to ignore case and diacritics.</param>
    /// <remarks>ISO 32000-2 §9.10 (extraction of text content), §14.8.2.5 (reading order). Spec #33 viewer capability 7.</remarks>
    public void Find(string query, TextSearchOptions? options = null) => Controller.Find(query, options);

    /// <summary>Makes the next match current and scrolls it into view (wrapping to the first).</summary>
    /// <returns>The match, or null when there is none yet.</returns>
    public SearchMatch? FindNext() => Controller.FindNext(backward: false);

    /// <summary>Makes the previous match current and scrolls it into view (wrapping to the last).</summary>
    /// <returns>The match, or null when there is none yet.</returns>
    public SearchMatch? FindPrevious() => Controller.FindNext(backward: true);

    /// <summary>Clears the search and its highlights.</summary>
    public void ClearSearch() => Controller.ClearSearch();

    /// <summary>Draws every tile again (after the host changed the document behind the viewer's back).</summary>
    public void Redraw() => Controller.Redraw();

    /// <summary>Copies the selected text to the clipboard.</summary>
    /// <returns>The copied text, or null when nothing is selected or the document closed before the selection's pages loaded.</returns>
    public async Task<string?> CopySelectionAsync()
    {
        string? text = await Controller.GetSelectedTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text) && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text).ConfigureAwait(true);
        }

        return text;
    }

    /// <summary>Closes the document and releases its tiles.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Controller.Dispose();
            _handCursor?.Dispose();
            _textCursor?.Dispose();
        }
    }

    // ---- Avalonia ----

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => _peer = new PdfViewAutomationPeer(this);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == SessionProperty)
        {
            Reopen();
        }
        else if (change.Property == LayoutOptionsProperty)
        {
            Controller.SetLayoutOptions(LayoutOptions);
        }
        else if (change.Property == ShowAnnotationsProperty)
        {
            Controller.SetShowAnnotations(ShowAnnotations);
        }
        else if (change.Property == AllowAnnotationEditingProperty)
        {
            Controller.AllowAnnotationEditing = AllowAnnotationEditing;
        }
        else if (change.Property == ColorFilterProperty)
        {
            Controller.SetColorFilter(ColorFilter);
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.ScalingChanged += OnScalingChanged;
        }

        UpdateSize();
        if (_frameRequested)
        {
            _frameRequested = false;
            RequestFrame();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.ScalingChanged -= OnScalingChanged;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        _editorView?.Measure(_editorBounds.Size);
        double width = double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height;
        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _editorView?.Arrange(_editorBounds);
        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateSize();
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Controller.Draw(context, Bounds.Size);
    }

    // ---- Input ----

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        PointerKind kind = KindOf(e.Pointer.Type);
        if (kind == PointerKind.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_editorView is not null && _editorView.Bounds.Contains(point.Position))
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
        e.Pointer.Capture(this);
        _pointers.Add(e.Pointer.Id);
        int clicks = kind == PointerKind.Mouse ? Controller.CountClick(point.Position.X, point.Position.Y) : 1;
        Controller.PointerDown(e.Pointer.Id, kind, point.Position.X, point.Position.Y, clicks, ModifiersOf(e.KeyModifiers));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        Point position = e.GetPosition(this);
        Controller.PointerMove(e.Pointer.Id, position.X, position.Y);
        if (!_pointers.Contains(e.Pointer.Id) && e.Pointer.Type == PointerType.Mouse)
        {
            Cursor = Controller.GetHoverTarget(position.X, position.Y) switch
            {
                HoverTarget.Link => _handCursor ??= new Cursor(StandardCursorType.Hand),
                HoverTarget.Text => _textCursor ??= new Cursor(StandardCursorType.Ibeam),
                _ => Cursor.Default,
            };
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        if (_pointers.Remove(e.Pointer.Id))
        {
            Point position = e.GetPosition(this);
            Controller.PointerUp(e.Pointer.Id, position.X, position.Y);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerCaptureLost(e);

        // Capture lost without a release (a window switch, a popup): the gesture is cancelled, never completed.
        if (_pointers.Remove(e.Pointer.Id))
        {
            Controller.PointerCancel(e.Pointer.Id);
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerWheelChanged(e);

        // Avalonia reports one unit per wheel notch, positive away from the user (scroll up or left); a notch is three lines.
        Point position = e.GetPosition(this);
        Controller.Wheel(-e.Delta.X * 3, -e.Delta.Y * 3, WheelDeltaMode.Line, ModifiersOf(e.KeyModifiers), position.X, position.Y);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || (_editorView is not null && _editorView.IsKeyboardFocusWithin))
        {
            return;
        }

        if (KeyName(e.Key, e.KeyModifiers) is { } key && Controller.KeyDown(key, ModifiersOf(e.KeyModifiers)))
        {
            e.Handled = true;
        }
    }

    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        Point position = e.GetPosition(this);
        Controller.Magnify(1 + e.Delta.X, position.X, position.Y);
        e.Handled = true;
    }

    /// <summary>A key as DOM <c>KeyboardEvent.key</c> names it, which the controller (shared with the other shells) understands.</summary>
    internal static string? KeyName(Key key, AvaloniaKeyModifiers modifiers) => key switch
    {
        Key.Left => "ArrowLeft",
        Key.Right => "ArrowRight",
        Key.Up => "ArrowUp",
        Key.Down => "ArrowDown",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Home => "Home",
        Key.End => "End",
        Key.Space => " ",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Escape => "Escape",
        Key.F3 => "F3",
        Key.OemPlus or Key.Add => "+",
        Key.OemMinus or Key.Subtract => "-",
        Key.D0 or Key.NumPad0 => "0",
        >= Key.A and <= Key.Z => ((modifiers & AvaloniaKeyModifiers.Shift) != 0 ? (char)('A' + (key - Key.A)) : (char)('a' + (key - Key.A))).ToString(),
        _ => null,
    };

    private static PointerKind KindOf(PointerType type) => type switch
    {
        PointerType.Touch => PointerKind.Touch,
        PointerType.Pen => PointerKind.Pen,
        _ => PointerKind.Mouse,
    };

    private static InputModifiers ModifiersOf(AvaloniaKeyModifiers modifiers)
    {
        InputModifiers result = InputModifiers.None;
        result |= (modifiers & AvaloniaKeyModifiers.Shift) != 0 ? InputModifiers.Shift : InputModifiers.None;
        result |= (modifiers & AvaloniaKeyModifiers.Control) != 0 ? InputModifiers.Control : InputModifiers.None;
        result |= (modifiers & AvaloniaKeyModifiers.Alt) != 0 ? InputModifiers.Alt : InputModifiers.None;
        result |= (modifiers & AvaloniaKeyModifiers.Meta) != 0 ? InputModifiers.Meta : InputModifiers.None;
        return result;
    }

    // ---- Frames ----

    private void UpdateSize()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        Controller.Resize(Bounds.Width, Bounds.Height, scaling > 0 ? scaling : 1);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => UpdateSize();

    private void RequestFrame()
    {
        if (_disposed || _frameRequested)
        {
            return;
        }

        _frameRequested = true;
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            top.RequestAnimationFrame(OnFrame);
        }
    }

    /// <summary>Runs one frame of the viewer (the animation-frame callback; tests call it directly).</summary>
    /// <returns>Whether another frame is needed.</returns>
    internal bool RunFrame()
    {
        _frameRequested = false;
        bool more;
        try
        {
            more = Controller.Frame();
        }
#pragma warning disable CA1031 // a failing frame is reported; the next event runs another
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Error?.Invoke(this, new PdfViewErrorEventArgs(exception));
            more = false;
        }

        if (more)
        {
            RequestFrame();
        }

        return more;
    }

    private void OnFrame(TimeSpan time) => RunFrame();

    // ---- Documents ----

    private async void Reopen()
    {
        int generation = ++_openGeneration;
        DocumentSession? session = Session;
        string? source = Source;
        try
        {
            if (session is not null)
            {
                await Controller.OpenAsync(_ => Task.FromResult(session), ownsSession: false, LayoutOptions).ConfigureAwait(true);
            }
            else if (!string.IsNullOrEmpty(source))
            {
                ViewerOptions? options = Options;
                await Controller.OpenAsync(token => OpenSourceAsync(source, options, token), ownsSession: true, LayoutOptions).ConfigureAwait(true);
            }
            else
            {
                Controller.CloseDocument();
            }
        }
#pragma warning disable CA1031 // the host hears why the document did not open
        catch (Exception exception) when (generation == _openGeneration)
#pragma warning restore CA1031
        {
            Error?.Invoke(this, new PdfViewErrorEventArgs(exception));
        }
    }

    private static async Task<DocumentSession> OpenSourceAsync(string source, ViewerOptions? options, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await DocumentSession.OpenAsync(SharedHttp.Value, uri, options, cancellationToken).ConfigureAwait(true);
        }

        return await Task.Run(() => DocumentSession.Open(source, options), cancellationToken).ConfigureAwait(true);
    }

    // ---- Form-field editors ----

    private void ShowEditor(FieldEditor? editor)
    {
        if (_editorView is not null)
        {
            LogicalChildren.Remove(_editorView);
            VisualChildren.Remove(_editorView);
            _editorView = null;
        }

        if (editor is not null)
        {
            Control view = editor switch
            {
                { Kind: FieldEditorKind.Choice, IsMultiSelect: true } => CreateListBox(editor),
                { Kind: FieldEditorKind.Choice } => CreateComboBox(editor),
                _ => CreateTextBox(editor),
            };
            view.IsVisible = false;
            _editorView = view;
            VisualChildren.Add(view);
            LogicalChildren.Add(view);
        }

        InvalidateMeasure();
    }

    private void MoveEditor(double x, double y, double width, double height)
    {
        if (_editorView is not { } view)
        {
            return;
        }

        _editorBounds = new Rect(x, y, Math.Max(width, 44), Math.Max(height, 24));
        InvalidateArrange();
        if (!view.IsVisible)
        {
            view.IsVisible = true;
            view.Focus();
        }
    }

    private TextBox CreateTextBox(FieldEditor editor)
    {
        var input = new TextBox
        {
            Text = editor.Text,
            AcceptsReturn = editor.IsMultiline,
            TextWrapping = editor.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            PasswordChar = editor.IsPassword ? '•' : default,
            MaxLength = editor.MaxLength is int max && max > 0 ? max : 0,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            Padding = new Thickness(2, 0),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        bool closed = false;
        void Close(bool commit)
        {
            if (!closed && ReferenceEquals(_editorView, input))
            {
                closed = true;
                // The box's text once more: its TextChanged may still be queued when Enter or focus loss ends the edit.
                _ = commit ? Controller.CommitFieldText(input.Text) : Controller.CancelFieldEdit();
                Controller.EndFieldEdit();
                Focus();
            }
        }

        // The text box only takes input: the form filling (#218) holds the text (cut at MaxLen, one line unless multi-line) and
        // writes it once when the edit ends. Enter and focus loss commit; Escape abandons the edit.
        input.TextChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, input))
            {
                Controller.EditorInput(input.Text ?? string.Empty);
            }
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !editor.IsMultiline)
            {
                e.Handled = true;
                Close(commit: true);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(commit: false);
            }
        };
        input.LostFocus += (_, _) => Close(commit: true);
        return input;
    }

    private ComboBox CreateComboBox(FieldEditor editor)
    {
        var picker = new ComboBox
        {
            ItemsSource = editor.Options,
            SelectedIndex = editor.SelectedIndex,
            IsEditable = editor.IsEditable,
            Background = Brushes.White,
            Foreground = Brushes.Black,
        };
        if (editor.IsEditable)
        {
            picker.Text = editor.Text;
        }

        // A pick commits through the form filling at once; an editable combo box's typed text is held there until focus leaves.
        picker.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, picker) && picker.SelectedIndex >= 0 && picker.SelectedIndex != editor.SelectedIndex)
            {
                Controller.CommitFieldChoice(picker.SelectedIndex);
            }
        };
        picker.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty && editor.IsEditable && ReferenceEquals(_editorView, picker))
            {
                Controller.EditorInput(picker.Text ?? string.Empty);
            }
        };
        picker.LostFocus += (_, _) =>
        {
            if (ReferenceEquals(_editorView, picker) && !picker.IsDropDownOpen && !picker.IsKeyboardFocusWithin)
            {
                Controller.CommitFieldEdit();
                Controller.EndFieldEdit();
            }
        };
        return picker;
    }

    private ListBox CreateListBox(FieldEditor editor)
    {
        // A multi-select list box (§12.7.5.4, MultiSelect): a click toggles its option, as Ctrl+click does in a desktop list.
        var list = new ListBox
        {
            ItemsSource = editor.Options,
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            Background = Brushes.White,
            Foreground = Brushes.Black,
        };

        // Rows as compact as a PDF list box's lines, so the widget's options fit without scrolling.
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(4, 1)),
                new Setter(Layoutable.MinHeightProperty, 0d),
            },
        });
        foreach (int index in editor.SelectedIndices)
        {
            list.Selection.Select(index);
        }

        // Every change hands the whole selection to the form filling, which holds it until focus leaves the list and then writes it
        // once through the form API.
        list.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, list))
            {
                Controller.EditorSelectedOptions(list.Selection.SelectedIndexes);
            }
        };
        void Close(bool commit)
        {
            if (ReferenceEquals(_editorView, list))
            {
                _ = commit ? Controller.CommitFieldEdit() : Controller.CancelFieldEdit();
                Controller.EndFieldEdit();
            }
        }

        // Escape abandons the picks; focus leaving the list commits them.
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(commit: false);
                Focus();
            }
        };
        list.LostFocus += (_, _) =>
        {
            if (!list.IsKeyboardFocusWithin)
            {
                Close(commit: true);
            }
        };
        return list;
    }

    // ---- Host ----

    /// <summary>The control's side of the controller: frames, invalidation, events, editors, accessibility and the clipboard.</summary>
    private sealed class Host(PdfView viewer) : IViewerHost
    {
        public void RequestFrame() => viewer.RequestFrame();

        public void RequestPaint() => viewer.InvalidateVisual();

        public void StateChanged(PdfViewState state) => viewer.StateChanged?.Invoke(viewer, new PdfViewStateChangedEventArgs(state));

        public void ActionRequested(ViewerActionEventArgs e) => viewer.ActionRequested?.Invoke(viewer, e);

        public void Error(Exception exception) => viewer.Error?.Invoke(viewer, new PdfViewErrorEventArgs(exception));

        public void EditorChanged(FieldEditor? editor) => viewer.ShowEditor(editor);

        public void EditorMoved(double x, double y, double width, double height) => viewer.MoveEditor(x, y, width, height);

        public void AccessibilityChanged(AccessibilityChange change)
        {
            if (viewer._peer is not { } peer)
            {
                return;
            }

            if (change.Kind == AccessibilityChangeKind.FocusChanged)
            {
                // Focus that only follows scrolling (page changes) does not pull the screen reader away from what it is reading.
                if (!change.FollowsViewer)
                {
                    peer.OnFocusChanged(change.Node);
                }
            }
            else
            {
                peer.OnChildrenChanged(change.Node);
            }
        }

        public void CopyText(string text)
        {
            if (TopLevel.GetTopLevel(viewer)?.Clipboard is { } clipboard)
            {
                _ = clipboard.SetTextAsync(text);
            }
        }
    }
}
