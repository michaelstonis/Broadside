using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Broadside.Rendering;

namespace Broadside.Viewer.Wpf;

/// <summary>
/// A WPF control that shows a PDF document on the shared viewer core. Tiles render off the UI thread with the fully managed software
/// rasterizer (unless the options name another backend) and are drawn as frozen bitmaps; the view composes them with the selection,
/// search highlights and focus rings every frame.
/// </summary>
/// <remarks>
/// <para>
/// Mouse, touch, pen, wheel and keyboard input go through one <see cref="InputModel"/>, so pan, fling, pinch and double-tap behave as
/// in the other Broadside viewers. A mouse drag over text selects it (double click: word, triple: line); on touch a long press
/// selects a word and handles extend it. Links and form fields go through the <see cref="AnnotationLayer"/>: links inside the
/// document navigate, every other action (URIs, launches, scripts) is raised as <see cref="ActionRequested"/> and never performed. A
/// focused text or choice field opens a text box, a combo box or (a multi-select list box) a multi-select list over the widget; an
/// edit redraws only the overlay tiles it touches.
/// Keys: arrows, Page Up/Down, Home/End, +/-/0, Tab between fields and links, Enter, Ctrl+A, Ctrl+C, F3 (find next) and the
/// single-letter quick navigation of screen readers (H headings, T tables, L lists, K links, F fields, P pages).
/// </para>
/// <para>
/// UI Automation sees a document whose children are its loaded pages, each with its text in reading order as its value and the
/// accessibility bridge's nodes (headings, paragraphs, lists, tables, figures, links, fields) as its children; the document supports
/// the scroll pattern.
/// </para>
/// <para>ISO 32000-2 §12.3.2 (destinations), §12.5.6.5 (link annotations), §12.7 (interactive forms), §14.8, §14.9.3 and §14.11.2.</para>
/// </remarks>
public sealed class PdfView : FrameworkElement, IViewerHost, IDisposable
{
    /// <summary>Identifies the <see cref="Source"/> property: a file path or an http(s) address of the document to show.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(string), typeof(PdfView), new PropertyMetadata(null, (d, _) => ((PdfView)d).Reopen()));

    /// <summary>Identifies the <see cref="Session"/> property: a document session the host opened and owns.</summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(DocumentSession), typeof(PdfView), new PropertyMetadata(null, (d, _) => ((PdfView)d).Reopen()));

    /// <summary>Identifies the <see cref="LayoutOptions"/> property.</summary>
    public static readonly DependencyProperty LayoutOptionsProperty = DependencyProperty.Register(
        nameof(LayoutOptions), typeof(PageLayoutOptions), typeof(PdfView), new PropertyMetadata(null, (d, e) => ((PdfView)d).Controller.SetLayoutOptions((PageLayoutOptions?)e.NewValue)));

    /// <summary>Identifies the <see cref="ShowAnnotations"/> property.</summary>
    public static readonly DependencyProperty ShowAnnotationsProperty = DependencyProperty.Register(
        nameof(ShowAnnotations), typeof(bool), typeof(PdfView), new PropertyMetadata(true, (d, e) => ((PdfView)d).Controller.SetShowAnnotations((bool)e.NewValue)));

    /// <summary>Identifies the <see cref="AllowAnnotationEditing"/> property.</summary>
    public static readonly DependencyProperty AllowAnnotationEditingProperty = DependencyProperty.Register(
        nameof(AllowAnnotationEditing), typeof(bool), typeof(PdfView), new PropertyMetadata(false, (d, e) => ((PdfView)d).Controller.AllowAnnotationEditing = (bool)e.NewValue));

    /// <summary>Identifies the <see cref="ColorFilter"/> property.</summary>
    public static readonly DependencyProperty ColorFilterProperty = DependencyProperty.Register(
        nameof(ColorFilter), typeof(ColorFilter?), typeof(PdfView), new PropertyMetadata(null, (d, e) => ((PdfView)d).OnColorFilterChanged((ColorFilter?)e.NewValue)));

    private const long MousePointer = 1;
    private const long TouchPointers = 1L << 32;
    private const long PenPointers = 2L << 32;
    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient());

    private readonly Canvas _editorLayer = new() { Background = null };
    private FrameworkElement? _editorView;
    private bool _rendering;
    private bool _framePending;
    private bool _mouseDown;
    private int _openGeneration;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PdfView"/> class; set <see cref="Source"/> or <see cref="Session"/> to show a document.</summary>
    public PdfView()
    {
        Controller = new ViewerController(this, FrameRenderer.CreateImage);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        AddVisualChild(_editorLayer);
        AddLogicalChild(_editorLayer);
        Unloaded += (_, _) => StopRendering();
        Loaded += (_, _) =>
        {
            if (_framePending)
            {
                StartRendering();
            }
        };
    }

    /// <summary>Raised when the page, page count, zoom or loading state changes.</summary>
    public event EventHandler<PdfViewStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised for every action a link, widget or annotation asks for. Destinations inside the document have already been followed when
    /// this is raised; URIs, launches, named actions and scripts are never performed by the view.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.6 (actions) and §12.5.6.5 (link annotations).</remarks>
    public event EventHandler<ViewerActionEventArgs>? ActionRequested;

    /// <summary>Raised when opening the document, loading a page's text or links, or drawing fails.</summary>
    public event EventHandler<PdfViewErrorEventArgs>? Error;

    /// <summary>Gets or sets the document to show: a file path, or an http(s) address read through range requests.</summary>
    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets a document session the host opened (and disposes); it takes precedence over <see cref="Source"/>.</summary>
    public DocumentSession? Session
    {
        get => (DocumentSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Gets or sets the page layout (continuous by default); changes apply at once, keeping the current page.</summary>
    public PageLayoutOptions? LayoutOptions
    {
        get => (PageLayoutOptions?)GetValue(LayoutOptionsProperty);
        set => SetValue(LayoutOptionsProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether annotations and form fields are drawn and interactive (default true).</summary>
    /// <remarks>ISO 32000-2 §12.5.</remarks>
    public bool ShowAnnotations
    {
        get => (bool)GetValue(ShowAnnotationsProperty);
        set => SetValue(ShowAnnotationsProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether markup annotations can be selected and dragged (default false; links and fields always work).</summary>
    public bool AllowAnnotationEditing
    {
        get => (bool)GetValue(AllowAnnotationEditingProperty);
        set => SetValue(AllowAnnotationEditingProperty, value);
    }

    /// <summary>
    /// Gets or sets the color filter the pages are drawn with, such as <see cref="Rendering.ColorFilter.DarkMode"/> for smart dark mode;
    /// <see langword="null"/> (the default) keeps the session's own (<see cref="ViewerOptions.ColorFilter"/>).
    /// </summary>
    /// <remarks>
    /// Outside ISO 32000-2 (spec #33 viewer capability 9). A change draws every tile again from the cached display lists; the paper
    /// and the space around the pages turn dark with it. See <see cref="DocumentSession.ColorFilter"/>.
    /// </remarks>
    public ColorFilter? ColorFilter
    {
        get => (ColorFilter?)GetValue(ColorFilterProperty);
        set => SetValue(ColorFilterProperty, value);
    }

    /// <summary>Gets or sets the options for documents opened from <see cref="Source"/>; without a backend factory, tiles draw with the software rasterizer.</summary>
    public ViewerOptions? Options { get; set; }

    /// <summary>Gets what the view shows.</summary>
    public PdfViewState State => Controller.State;

    /// <summary>Gets the open document's session (from <see cref="Source"/> or <see cref="Session"/>), or null.</summary>
    public DocumentSession? CurrentSession => Controller.Session;

    /// <summary>Gets the text selection of the open document, or null.</summary>
    public TextSelection? Selection => Controller.Selection;

    /// <summary>
    /// Gets the find-in-document search of the open document, or null. Its matches are highlighted over the pages (the current one
    /// distinctly) without drawing any page again; scroll a match into view with <see cref="Reveal"/>, or press F3.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10 (extraction of text content). Spec #33 viewer capability 7.</remarks>
    public DocumentSearch? Search => Controller.Search;

    /// <summary>Gets the annotation and link layer of the open document, or null (form values and annotation edits go through it).</summary>
    public AnnotationLayer? Annotations => Controller.Annotations;

    /// <summary>
    /// Gets the form filling of the open document, or null: the field the keyboard edits, Tab order (each page's Tabs entry), and one
    /// commit per edit through the form API (filling through the view saves the same file as filling through the API).
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7 (interactive forms), §7.7.3.3 Table 31 (Tabs). Spec #33 viewer capability 12.</remarks>
    public FormFilling? Forms => Controller.Forms;

    /// <summary>Gets the accessibility bridge of the open document, or null.</summary>
    /// <remarks>ISO 32000-2 §14.8.</remarks>
    public AccessibilityBridge? Accessibility => Controller.Bridge;

    internal ViewerController Controller { get; }

    /// <inheritdoc/>
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc/>
    protected override System.Collections.IEnumerator LogicalChildren => new[] { _editorLayer }.GetEnumerator();

    /// <summary>Shows a page (zero-based).</summary>
    /// <param name="pageIndex">The page.</param>
    public void GoToPage(int pageIndex) => Controller.GoToPage(pageIndex);

    /// <summary>Shows the next page or spread.</summary>
    public void NextPage() => Controller.NextPage();

    /// <summary>Shows the previous page or spread.</summary>
    public void PreviousPage() => Controller.PreviousPage();

    /// <summary>Zooms to a fit mode or the actual size, animated.</summary>
    /// <param name="mode">The zoom mode.</param>
    public void ZoomTo(ZoomMode mode) => Controller.ZoomTo(mode);

    /// <summary>Zooms by a factor around the view's centre.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    public void ZoomBy(double factor) => Controller.ZoomBy(factor);

    /// <summary>Scrolls the view by a distance in device-independent units, as a wheel or touchpad scroll does (no animation).</summary>
    /// <param name="deltaX">The horizontal distance; positive scrolls right.</param>
    /// <param name="deltaY">The vertical distance; positive scrolls down.</param>
    public void ScrollBy(double deltaX, double deltaY) =>
        Controller.Wheel(deltaX, deltaY, WheelDeltaMode.Pixel, InputModifiers.None, ActualWidth / 2, ActualHeight / 2);

    /// <summary>Scrolls so that an area of a page is in view (with a margin), such as a search match's bounds.</summary>
    /// <param name="pageIndex">The page (zero-based).</param>
    /// <param name="area">The area, in the page's default user space.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3 (user space).</remarks>
    public void Reveal(int pageIndex, PdfRectangle area) => Controller.Reveal(pageIndex, area);

    /// <summary>Copies the selected text to the clipboard.</summary>
    /// <returns>The copied text, or null when nothing is selected or the document closed before the selection's pages loaded.</returns>
    public async Task<string?> CopySelectionAsync()
    {
        string? text = await Controller.GetSelectedTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
        {
            SetClipboard(text);
        }

        return text;
    }

    /// <summary>Closes the document and releases its tiles.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            StopRendering();
            Controller.Dispose();
        }
    }

    /// <summary>Runs one frame now (tests): the work the next composition tick would do. Returns whether another frame is needed.</summary>
    internal bool RunFrame()
    {
        _framePending = false;
        return Controller.Frame();
    }

    /// <inheritdoc/>
    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _editorLayer : throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        _editorLayer.Measure(availableSize);
        return default;
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        _editorLayer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <inheritdoc/>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        ResizeController();
    }

    /// <inheritdoc/>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResizeController();
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        FrameRenderer.Draw(drawingContext, Controller.LatestFrame, ActualWidth, ActualHeight, Controller.PixelRatio);
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => new PdfViewAutomationPeer(this);

    // ---- Input ----

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);
        if (e.StylusDevice is not null)
        {
            return; // promoted from touch or a pen: their own events drive the view
        }

        Focus();
        if (e.ChangedButton != MouseButton.Left || _editorView?.IsMouseOver == true)
        {
            return;
        }

        Point p = e.GetPosition(this);
        _mouseDown = true;
        CaptureMouse();
        Controller.PointerDown(MousePointer, PointerKind.Mouse, p.X, p.Y, e.ClickCount, KeyNames.Modifiers(Keyboard.Modifiers));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (e.StylusDevice is not null)
        {
            return;
        }

        Point p = e.GetPosition(this);
        if (_mouseDown)
        {
            Controller.PointerMove(MousePointer, p.X, p.Y);
        }
        else
        {
            Controller.Hover(p.X, p.Y);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseUp(e);
        if (e.StylusDevice is not null || e.ChangedButton != MouseButton.Left || !_mouseDown)
        {
            return;
        }

        Point p = e.GetPosition(this);
        _mouseDown = false;
        Controller.PointerUp(MousePointer, p.X, p.Y);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_mouseDown)
        {
            _mouseDown = false;
            Controller.PointerCancel(MousePointer);
        }
    }

    /// <inheritdoc/>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        Point p = e.GetPosition(this);

        // Windows reports 120 per notch, positive away from the user (scroll up); a notch is three lines.
        Controller.Wheel(0, -e.Delta / 40.0, WheelDeltaMode.Line, KeyNames.Modifiers(Keyboard.Modifiers), p.X, p.Y);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnTouchDown(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnTouchDown(e);
        Focus();
        Point p = e.GetTouchPoint(this).Position;
        CaptureTouch(e.TouchDevice);
        Controller.PointerDown(TouchPointers + e.TouchDevice.Id, PointerKind.Touch, p.X, p.Y, 1, InputModifiers.None);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnTouchMove(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnTouchMove(e);
        Point p = e.GetTouchPoint(this).Position;
        Controller.PointerMove(TouchPointers + e.TouchDevice.Id, p.X, p.Y);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnTouchUp(TouchEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnTouchUp(e);
        Point p = e.GetTouchPoint(this).Position;
        Controller.PointerUp(TouchPointers + e.TouchDevice.Id, p.X, p.Y);
        ReleaseTouchCapture(e.TouchDevice);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStylusDown(e);
        if (e.StylusDevice.TabletDevice?.Type != TabletDeviceType.Stylus)
        {
            return; // touch arrives as touch events
        }

        Focus();
        Point p = e.GetPosition(this);
        e.StylusDevice.Capture(this);
        Controller.PointerDown(PenPointers + e.StylusDevice.Id, PointerKind.Pen, p.X, p.Y, 1, KeyNames.Modifiers(Keyboard.Modifiers));
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnStylusMove(StylusEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStylusMove(e);
        if (e.StylusDevice.TabletDevice?.Type == TabletDeviceType.Stylus && !e.InAir)
        {
            Point p = e.GetPosition(this);
            Controller.PointerMove(PenPointers + e.StylusDevice.Id, p.X, p.Y);
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnStylusUp(StylusEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStylusUp(e);
        if (e.StylusDevice.TabletDevice?.Type == TabletDeviceType.Stylus)
        {
            Point p = e.GetPosition(this);
            Controller.PointerUp(PenPointers + e.StylusDevice.Id, p.X, p.Y);
            e.StylusDevice.Capture(null);
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || _editorView is not null)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (KeyNames.Name(key) is { } name && Controller.KeyDown(name, KeyNames.Modifiers(Keyboard.Modifiers)))
        {
            e.Handled = true;
        }
    }

    // ---- IViewerHost ----

    void IViewerHost.RequestFrame()
    {
        _framePending = true;
        StartRendering();
    }

    void IViewerHost.RequestPaint() => InvalidateVisual();

    void IViewerHost.StateChanged(PdfViewState state) => StateChanged?.Invoke(this, new PdfViewStateChangedEventArgs(state));

    void IViewerHost.ActionRequested(ViewerActionEventArgs e) => ActionRequested?.Invoke(this, e);

    void IViewerHost.Error(Exception exception) => Error?.Invoke(this, new PdfViewErrorEventArgs(exception));

    void IViewerHost.EditorChanged(FieldEditor? editor)
    {
        if (_editorView is not null)
        {
            _editorLayer.Children.Remove(_editorView);
            _editorView = null;
        }

        if (editor is null)
        {
            return;
        }

        FrameworkElement view = editor switch
        {
            { Kind: FieldEditorKind.Choice, IsMultiSelect: true } => CreateMultiSelectEditor(editor),
            { Kind: FieldEditorKind.Choice } => CreateChoiceEditor(editor),
            _ => CreateTextEditor(editor),
        };
        view.Visibility = Visibility.Hidden;
        _editorView = view;
        _editorLayer.Children.Add(view);
    }

    /// <summary>The native editor over the focused form field, or <see langword="null"/>.</summary>
    internal FrameworkElement? EditorView => _editorView;

    void IViewerHost.EditorMoved(double x, double y, double width, double height)
    {
        if (_editorView is not { } view)
        {
            return;
        }

        Canvas.SetLeft(view, x);
        Canvas.SetTop(view, y);
        view.Width = Math.Max(width, 44);
        view.Height = Math.Max(height, 22);
        if (view.Visibility != Visibility.Visible)
        {
            view.Visibility = Visibility.Visible;
            view.Focus();
        }
    }

    void IViewerHost.AccessibilityChanged(AccessibilityChange change)
    {
        if (UIElementAutomationPeer.FromElement(this) is PdfViewAutomationPeer peer)
        {
            peer.OnAccessibilityChanged(change);
        }
    }

    void IViewerHost.CopyText(string text) => SetClipboard(text);

    // ---- Frames ----

    private void StartRendering()
    {
        if (_disposed || _rendering)
        {
            return;
        }

        _rendering = true;
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void StopRendering()
    {
        if (_rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        _framePending = false;
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

        if (!more && !_framePending)
        {
            StopRendering();
        }
    }

    private void ResizeController()
    {
        double ratio = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Controller.Resize(ActualWidth, ActualHeight, ratio > 0 ? ratio : 1);
    }

    private void OnColorFilterChanged(ColorFilter? filter)
    {
        Controller.SetColorFilter(filter);
        InvalidateVisual();
    }

    private static void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (ExternalException)
        {
            // Another process holds the clipboard; the copy is dropped, as a failed Ctrl+C is in other applications.
        }
    }

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

        return await Task.Run(() => options is null ? DocumentSession.Open(source) : DocumentSession.Open(source, options), cancellationToken).ConfigureAwait(true);
    }

    // ---- Form-field editors ----

    private Control CreateTextEditor(FieldEditor editor)
    {
        // The box only takes input: the form filling (#218) holds the text (cut at MaxLen, one line unless multi-line) and writes it
        // once when the edit ends (#543). Enter and focus loss commit; Escape abandons the edit.
        Control input;
        Func<string> read;
        if (editor.IsPassword)
        {
            var password = new PasswordBox { Password = editor.Text };
            if (editor.MaxLength is int max && max > 0)
            {
                password.MaxLength = max;
            }

            password.PasswordChanged += (_, _) => Input(password, password.Password);
            input = password;
            read = () => password.Password;
        }
        else
        {
            var text = new TextBox { Text = editor.Text, AcceptsReturn = editor.IsMultiline, TextWrapping = editor.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
            if (editor.MaxLength is int max && max > 0)
            {
                text.MaxLength = max;
            }

            text.TextChanged += (_, _) => Input(text, text.Text);
            input = text;
            read = () => text.Text;
        }

        input.Background = Brushes.White;
        input.Foreground = Brushes.Black;
        bool closed = false;
        void Input(Control source, string value)
        {
            if (ReferenceEquals(_editorView, source))
            {
                Controller.EditorInput(value);
            }
        }

        void Close(bool commit)
        {
            if (!closed && ReferenceEquals(_editorView, input))
            {
                closed = true;
                _ = commit ? Controller.CommitFieldText(read()) : Controller.CancelFieldEdit();
                Controller.EndFieldEdit();
                Focus();
            }
        }

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !editor.IsMultiline)
            {
                Close(commit: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(commit: false);
                e.Handled = true;
            }
        };
        input.LostKeyboardFocus += (_, _) => Close(commit: true);
        return input;
    }

    private ComboBox CreateChoiceEditor(FieldEditor editor)
    {
        var combo = new ComboBox
        {
            ItemsSource = editor.Options.ToList(),
            SelectedIndex = editor.SelectedIndex,
            IsEditable = editor.IsEditable,
            Background = Brushes.White,
            Foreground = Brushes.Black,
        };
        if (editor.IsEditable)
        {
            combo.Text = editor.Text;

            // An editable combo box's typed text is held by the form filling until the edit ends.
            combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
            {
                if (ReferenceEquals(_editorView, combo))
                {
                    Controller.EditorInput(combo.Text);
                }
            }));
            combo.LostKeyboardFocus += (_, _) =>
            {
                if (ReferenceEquals(_editorView, combo) && !combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen)
                {
                    Controller.CommitFieldEdit();
                    Controller.EndFieldEdit();
                }
            };
        }

        // A pick commits through the form filling at once.
        combo.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, combo) && combo.SelectedIndex >= 0 && combo.SelectedIndex != editor.SelectedIndex)
            {
                Controller.CommitFieldChoice(combo.SelectedIndex);
            }
        };
        combo.DropDownClosed += (_, _) =>
        {
            if (ReferenceEquals(_editorView, combo) && !editor.IsEditable)
            {
                Controller.EndFieldEdit();
                Focus();
            }
        };
        return combo;
    }

    private ListBox CreateMultiSelectEditor(FieldEditor editor)
    {
        // A multi-select list box (§12.7.5.4, MultiSelect): a click toggles its option. The items are containers of their own, so
        // options with the same text stay apart.
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Multiple,
            Background = Brushes.White,
            Foreground = Brushes.Black,
        };
        foreach (string option in editor.Options)
        {
            list.Items.Add(new ListBoxItem { Content = option, Padding = new Thickness(2, 0, 2, 0) });
        }

        foreach (int index in editor.SelectedIndices)
        {
            ((ListBoxItem)list.Items[index]).IsSelected = true;
        }

        // Every change hands the whole selection to the form filling, which holds it until focus leaves the list and then writes it
        // once through the form API.
        list.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, list))
            {
                Controller.EditorSelectedOptions(list.SelectedItems.Cast<object>().Select(list.Items.IndexOf));
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
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(commit: false);
                Focus();
            }
        };
        list.LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is not DependencyObject next || !(ReferenceEquals(next, list) || list.IsAncestorOf(next)))
            {
                Close(commit: true);
            }
        };
        return list;
    }
}
