using Broadside.Graphics;
using Broadside.Rendering;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace Broadside.Viewer.Uno;

/// <summary>
/// An Uno Platform control that shows a PDF document on the shared viewer core. Tiles render off the UI thread on the desktop head
/// (a background session) and in cooperative slices on the page's thread on the WebAssembly head, and are shown as bitmap images
/// composed by Uno's renderer.
/// </summary>
/// <remarks>
/// <para>
/// Touch, mouse, pen and wheel input go through one <see cref="InputModel"/>, so pan, fling, pinch and double-tap behave the same on
/// every head. Mouse drags over text select it (double click: word, triple: line); on touch a long press selects a word and handles
/// extend it. Links and form fields go through the <see cref="AnnotationLayer"/>: links inside the document navigate, every other
/// action (URIs, launches, scripts) is raised as <see cref="ActionRequested"/> and never performed. A focused text or choice field
/// opens an editor over the widget; an edit redraws only the overlay tiles it touches. Keys: arrows, Page Up/Down, Home/End, +/-,
/// Tab between fields and links, Enter, Ctrl/Cmd+A and +C, F3 for the next search match, and screen-reader letters (H headings, T
/// tables, K links and so on). <see cref="Find"/> highlights every match over the tiles without drawing a page again, and
/// <see cref="ColorFilter"/> turns on smart dark mode.
/// </para>
/// <para>
/// Its automation peer exposes each loaded page's text in reading order as child peers (headings with their level, links, form
/// fields with their values), so Narrator, VoiceOver and the browser's accessibility tree read the page.
/// </para>
/// <para>ISO 32000-2 §12.3.2 (destinations), §12.5.6.5 (link annotations), §12.7 (interactive forms), §14.8 (tagged PDF) and §14.11.2.</para>
/// </remarks>
#pragma warning disable CA1001 // the controller is disposed by Close; Uno elements are not disposed by their owners
#pragma warning disable CA1010 // Uno's UIElement enumerates its children; the view adds no collection of its own
public sealed partial class PdfView : UserControl, IViewerHost
#pragma warning restore CA1010
#pragma warning restore CA1001
{
    /// <summary>The <see cref="Source"/> property: a file path or an http(s) address of the document to show.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(string), typeof(PdfView), new PropertyMetadata(null, (d, _) => ((PdfView)d).Reopen()));

    /// <summary>The <see cref="Session"/> property: a document session the host opened and owns.</summary>
    public static readonly DependencyProperty SessionProperty =
        DependencyProperty.Register(nameof(Session), typeof(DocumentSession), typeof(PdfView), new PropertyMetadata(null, (d, _) => ((PdfView)d).Reopen()));

    /// <summary>The <see cref="LayoutOptions"/> property.</summary>
    public static readonly DependencyProperty LayoutOptionsProperty =
        DependencyProperty.Register(nameof(LayoutOptions), typeof(PageLayoutOptions), typeof(PdfView), new PropertyMetadata(null, (d, e) => ((PdfView)d)._controller.SetLayoutOptions((PageLayoutOptions?)e.NewValue)));

    /// <summary>The <see cref="ShowAnnotations"/> property.</summary>
    public static readonly DependencyProperty ShowAnnotationsProperty =
        DependencyProperty.Register(nameof(ShowAnnotations), typeof(bool), typeof(PdfView), new PropertyMetadata(true, (d, e) => ((PdfView)d)._controller.SetShowAnnotations((bool)e.NewValue)));

    /// <summary>The <see cref="AllowAnnotationEditing"/> property.</summary>
    public static readonly DependencyProperty AllowAnnotationEditingProperty =
        DependencyProperty.Register(nameof(AllowAnnotationEditing), typeof(bool), typeof(PdfView), new PropertyMetadata(false, (d, e) => ((PdfView)d)._controller.AllowAnnotationEditing = (bool)e.NewValue));

    /// <summary>The <see cref="ColorFilter"/> property.</summary>
    public static readonly DependencyProperty ColorFilterProperty =
        DependencyProperty.Register(nameof(ColorFilter), typeof(object), typeof(PdfView), new PropertyMetadata(null, (d, e) => ((PdfView)d)._controller.SetColorFilter((Broadside.Rendering.ColorFilter?)e.NewValue)));

    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient());
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(15);

    private readonly ViewerController _controller;
    private readonly Grid _root = new();
    private readonly FrameView _frameView = new();
    private readonly Canvas _editorLayer = new();
    private readonly SolidColorBrush _background = new(FrameView.ToColor(ViewerColors.Background));
    private FrameworkElement? _editorView;
    private DispatcherTimerLike? _timer;
    private bool _framePending;
    private InputModifiers _modifiers;
    private PointerShape _shape = PointerShape.Arrow;
    private PdfViewAutomationPeer? _peer;
    private int _openGeneration;
    private bool _disposed;

    /// <summary>Creates an empty view; set <see cref="Source"/> or <see cref="Session"/> to show a document.</summary>
    public PdfView()
    {
        _controller = new ViewerController(this);
        _root.Background = _background;
        _root.Children.Add(_frameView.Root);
        _root.Children.Add(_editorLayer);
        Content = _root;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AutomationProperties.SetName(this, "Document");
        SizeChanged += (_, _) => UpdateSize();
        PointerWheelChanged += OnWheel;
        Loaded += (_, _) =>
        {
            if (XamlRoot is { } root)
            {
                root.Changed += (_, _) => UpdateSize();
            }

            UpdateSize();
        };
        Unloaded += (_, _) => _timer?.Stop();
    }

    /// <summary>Raised when the page, page count, zoom or loading state changes.</summary>
    public event EventHandler<PdfViewStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised for every action a link, widget or annotation asks for. Destinations inside the document have already been followed when
    /// this is raised; URIs, launches, named actions and scripts are never performed by the view.
    /// </summary>
    public event EventHandler<ViewerActionEventArgs>? ActionRequested;

    /// <summary>Raised when opening the document, loading a page's text or links, or drawing fails.</summary>
    public event EventHandler<PdfViewErrorEventArgs>? Error;

    /// <summary>Gets or sets the document to show: a file path (desktop), or an http(s) address read through range requests.</summary>
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

    /// <summary>Gets or sets whether annotations and form fields are drawn and interactive (default true).</summary>
    public bool ShowAnnotations
    {
        get => (bool)GetValue(ShowAnnotationsProperty);
        set => SetValue(ShowAnnotationsProperty, value);
    }

    /// <summary>Gets or sets whether markup annotations can be selected and dragged (default false; links and fields always work).</summary>
    public bool AllowAnnotationEditing
    {
        get => (bool)GetValue(AllowAnnotationEditingProperty);
        set => SetValue(AllowAnnotationEditingProperty, value);
    }

    /// <summary>
    /// Gets or sets the color filter the pages are drawn with, such as <see cref="Broadside.Rendering.ColorFilter.DarkMode"/> for smart
    /// dark mode; <see langword="null"/> (the default) keeps the session's own. A change draws the tiles again from the cached display
    /// lists; the paper and the area around the pages darken with it. See <see cref="DocumentSession.ColorFilter"/>.
    /// </summary>
    public Broadside.Rendering.ColorFilter? ColorFilter
    {
        get => (Broadside.Rendering.ColorFilter?)GetValue(ColorFilterProperty);
        set => SetValue(ColorFilterProperty, value);
    }

    /// <summary>Gets or sets the options for documents opened from <see cref="Source"/> (fonts, credentials, threading).</summary>
    /// <remarks>
    /// Left at <see cref="ViewerThreading.Automatic"/>, tiles, text and search render on worker threads on the desktop head and in
    /// cooperative slices on the WebAssembly head, which never uses .NET WebAssembly threads.
    /// </remarks>
    public ViewerOptions? Options { get; set; }

    /// <summary>Gets what the view shows.</summary>
    public PdfViewState State => _controller.State;

    /// <summary>Gets the open document's session (from <see cref="Source"/> or <see cref="Session"/>), or null.</summary>
    public DocumentSession? CurrentSession => _controller.Session;

    /// <summary>Gets the text selection of the open document, or null.</summary>
    public TextSelection? Selection => _controller.Selection;

    /// <summary>Gets the annotation and link layer of the open document, or null (form values and annotation edits go through it).</summary>
    public AnnotationLayer? Annotations => _controller.Annotations;

    /// <summary>Gets the accessibility bridge of the open document, or null.</summary>
    public AccessibilityBridge? Accessibility => _controller.Bridge;

    /// <summary>Gets the search over the open document, or null; <see cref="Find"/> drives it and highlights its matches.</summary>
    public DocumentSearch? Search => _controller.Search;

    /// <summary>Shows a page (zero-based).</summary>
    /// <param name="pageIndex">The page.</param>
    public void GoToPage(int pageIndex) => _controller.GoToPage(pageIndex);

    /// <summary>Shows the next page or spread.</summary>
    public void NextPage() => _controller.NextPage();

    /// <summary>Shows the previous page or spread.</summary>
    public void PreviousPage() => _controller.PreviousPage();

    /// <summary>Zooms to a fit mode or the actual size, animated.</summary>
    /// <param name="mode">The zoom mode.</param>
    public void ZoomTo(ZoomMode mode) => _controller.ZoomTo(mode);

    /// <summary>Zooms by a factor around the view's center.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    public void ZoomBy(double factor) => _controller.ZoomBy(factor);

    /// <summary>Scrolls the view by a distance in device-independent units, as a wheel or trackpad scroll does (no animation).</summary>
    /// <param name="deltaX">The horizontal distance; positive scrolls right.</param>
    /// <param name="deltaY">The vertical distance; positive scrolls down.</param>
    public void ScrollBy(double deltaX, double deltaY) => _controller.ScrollBy(deltaX, deltaY);

    /// <summary>Scrolls the least distance that shows an area of a page.</summary>
    /// <param name="pageIndex">The page (zero-based).</param>
    /// <param name="area">The area, in the page's default user space.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3 (user space) and §14.11.2.</remarks>
    public void Reveal(int pageIndex, PdfRectangle area) => _controller.Reveal(pageIndex, area);

    /// <summary>
    /// Finds text in the document from the current page: every match is highlighted over the pages as the pages are searched, and the
    /// first one is scrolled into view. An empty query clears the highlights.
    /// </summary>
    /// <param name="query">The text to find.</param>
    /// <param name="options">How to compare, or <see langword="null"/> to ignore case and diacritics.</param>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2. Spec #33 viewer capability 7.</remarks>
    public void Find(string query, TextSearchOptions? options = null) => _controller.Find(query, options);

    /// <summary>Makes the next match current and scrolls it into view.</summary>
    /// <returns>The match, or <see langword="null"/> when there is none.</returns>
    public SearchMatch? FindNext() => _controller.FindNext();

    /// <summary>Makes the previous match current and scrolls it into view.</summary>
    /// <returns>The match, or <see langword="null"/> when there is none.</returns>
    public SearchMatch? FindPrevious() => _controller.FindNext(backward: true);

    /// <summary>Clears the search and its highlights.</summary>
    public void ClearFind() => _controller.ClearFind();

    /// <summary>Copies the selected text to the clipboard.</summary>
    /// <returns>The copied text, or null when nothing is selected.</returns>
    public async Task<string?> CopySelectionAsync()
    {
        string? text = await _controller.GetSelectedTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
        {
            ((IViewerHost)this).CopyText(text);
        }

        return text;
    }

    /// <summary>Closes the document for good and releases the tiles; the view shows nothing afterwards.</summary>
    public void Close()
    {
        if (!_disposed)
        {
            _disposed = true;
            _timer?.Stop();
            _controller.Dispose();
            _frameView.Show(_controller.LatestFrame);
        }
    }

    /// <summary>The controller (the sample's UI tests read the composed frame through it).</summary>
    internal ViewerController Controller => _controller;

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => _peer = new PdfViewAutomationPeer(this, _controller);

    // ---- Input ----

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerPressed(e);
        PointerPoint point = e.GetCurrentPoint(this);
        PointerKind kind = KindOf(e);
        if (kind == PointerKind.Mouse && !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus(FocusState.Pointer);
        CapturePointer(e.Pointer);
        _modifiers = ModifiersOf(e.KeyModifiers);
        _controller.PointerDown(e.Pointer.PointerId, kind, point.Position.X, point.Position.Y, _modifiers);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerMoved(e);
        PointerPoint point = e.GetCurrentPoint(this);
        PointerKind kind = KindOf(e);
        _controller.PointerMove(e.Pointer.PointerId, kind, point.Position.X, point.Position.Y, point.IsInContact);
        if (kind == PointerKind.Mouse)
        {
            SetShape(_controller.GetPointerShape(point.Position.X, point.Position.Y));
        }

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerReleased(e);
        PointerPoint point = e.GetCurrentPoint(this);
        _controller.PointerUp(e.Pointer.PointerId, point.Position.X, point.Position.Y);
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerCanceled(e);
        _controller.PointerCancel(e.Pointer.PointerId);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPointerCaptureLost(e);
        _controller.PointerCancel(e.Pointer.PointerId);
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(this);

        // A wheel notch is 120, positive away from the user (scroll up); a notch is three lines.
        double lines = -point.Properties.MouseWheelDelta / 40.0;
        InputModifiers modifiers = ModifiersOf(e.KeyModifiers);
        if (point.Properties.IsHorizontalMouseWheel)
        {
            _controller.Wheel(-lines, 0, WheelDeltaMode.Line, modifiers, point.Position.X, point.Position.Y);
        }
        else
        {
            _controller.Wheel(0, lines, WheelDeltaMode.Line, modifiers, point.Position.X, point.Position.Y);
        }

        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (TrackModifier(e.Key, true) || _editorView is not null)
        {
            return;
        }

        if (KeyName(e.Key, (_modifiers & InputModifiers.Shift) != 0) is { } key && _controller.KeyDown(key, _modifiers))
        {
            e.Handled = true;
        }
    }

    /// <inheritdoc/>
    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyUp(e);
        TrackModifier(e.Key, false);
    }

    /// <inheritdoc/>
    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _modifiers = InputModifiers.None;
    }

    private bool TrackModifier(VirtualKey key, bool down)
    {
        InputModifiers modifier = key switch
        {
            VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => InputModifiers.Shift,
            VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => InputModifiers.Control,
            VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu => InputModifiers.Alt,
            VirtualKey.LeftWindows or VirtualKey.RightWindows => InputModifiers.Meta,
            _ => InputModifiers.None,
        };
        if (modifier == InputModifiers.None)
        {
            return false;
        }

        _modifiers = down ? _modifiers | modifier : _modifiers & ~modifier;
        return true;
    }

    /// <summary>A key as DOM <c>KeyboardEvent.key</c> names it, which the controller's key map uses on every shell.</summary>
    internal static string? KeyName(VirtualKey key, bool shift) => key switch
    {
        VirtualKey.Left => "ArrowLeft",
        VirtualKey.Right => "ArrowRight",
        VirtualKey.Up => "ArrowUp",
        VirtualKey.Down => "ArrowDown",
        VirtualKey.PageUp => "PageUp",
        VirtualKey.PageDown => "PageDown",
        VirtualKey.Home => "Home",
        VirtualKey.End => "End",
        VirtualKey.Space => " ",
        VirtualKey.Tab => "Tab",
        VirtualKey.Enter => "Enter",
        VirtualKey.Escape => "Escape",
        VirtualKey.F3 => "F3",
        VirtualKey.Add or (VirtualKey)187 => "+", // 187: the =/+ key
        VirtualKey.Subtract or (VirtualKey)189 => "-", // 189: the -/_ key
        VirtualKey.Number0 or VirtualKey.NumberPad0 => "0",
        >= VirtualKey.A and <= VirtualKey.Z => ((char)((shift ? 'A' : 'a') + (key - VirtualKey.A))).ToString(),
        _ => null,
    };

    private static PointerKind KindOf(PointerRoutedEventArgs e) => e.Pointer.PointerDeviceType switch
    {
        Microsoft.UI.Input.PointerDeviceType.Mouse => PointerKind.Mouse,
        Microsoft.UI.Input.PointerDeviceType.Pen => PointerKind.Pen,
        _ => PointerKind.Touch,
    };

    private static InputModifiers ModifiersOf(VirtualKeyModifiers keys)
    {
        InputModifiers modifiers = InputModifiers.None;
        modifiers |= (keys & VirtualKeyModifiers.Shift) != 0 ? InputModifiers.Shift : 0;
        modifiers |= (keys & VirtualKeyModifiers.Control) != 0 ? InputModifiers.Control : 0;
        modifiers |= (keys & VirtualKeyModifiers.Menu) != 0 ? InputModifiers.Alt : 0;
        modifiers |= (keys & VirtualKeyModifiers.Windows) != 0 ? InputModifiers.Meta : 0;
        return modifiers;
    }

    private void SetShape(PointerShape shape)
    {
        if (shape == _shape)
        {
            return;
        }

        _shape = shape;
        ProtectedCursor = InputSystemCursor.Create(shape switch
        {
            PointerShape.Text => InputSystemCursorShape.IBeam,
            PointerShape.Hand => InputSystemCursorShape.Hand,
            _ => InputSystemCursorShape.Arrow,
        });
    }

    private void UpdateSize()
    {
        double ratio = XamlRoot?.RasterizationScale is double scale && scale > 0 ? scale : 1;
        _frameView.SetClip(ActualWidth, ActualHeight);
        _controller.Resize(ActualWidth, ActualHeight, ratio);
    }

    // ---- IViewerHost ----

    void IViewerHost.RequestFrame()
    {
        _framePending = true;
        StartTimer();
    }

    void IViewerHost.RequestPaint()
    {
        ViewerFrame frame = _controller.LatestFrame;
        _background.Color = FrameView.ToColor(frame.IsDark ? ViewerColors.DarkBackground : ViewerColors.Background);
        _frameView.Show(frame);
    }

    void IViewerHost.StateChanged(PdfViewState state) => StateChanged?.Invoke(this, new PdfViewStateChangedEventArgs(state));

    void IViewerHost.ActionRequested(ViewerActionEventArgs e) => ActionRequested?.Invoke(this, e);

    void IViewerHost.Error(Exception exception) => Error?.Invoke(this, new PdfViewErrorEventArgs(exception));

    void IViewerHost.EditorChanged(FieldEditor? editor)
    {
        if (_editorView is not null)
        {
            _editorLayer.Children.Remove(_editorView);
            _editorView = null;
            Focus(FocusState.Programmatic);
        }

        if (editor is null)
        {
            return;
        }

        FrameworkElement view = editor.Kind == FieldEditorKind.Choice ? CreateChoiceEditor(editor) : CreateTextEditor(editor);
        view.Visibility = Visibility.Collapsed;
        _editorView = view;
        _editorLayer.Children.Add(view);
    }

    void IViewerHost.EditorMoved(double x, double y, double width, double height)
    {
        if (_editorView is not { } view)
        {
            return;
        }

        Canvas.SetLeft(view, x);
        Canvas.SetTop(view, y);
        view.Width = Math.Max(width, 44);
        view.Height = Math.Max(height, 28);
        if (view.Visibility != Visibility.Visible)
        {
            view.Visibility = Visibility.Visible;
            ((Control)view).Focus(FocusState.Programmatic);
        }
    }

    void IViewerHost.AccessibilityChanged(AccessibilityChange change) => _peer?.OnAccessibilityChanged(change);

    void IViewerHost.CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ---- Frames ----

    private void StartTimer()
    {
        if (_disposed)
        {
            return;
        }

        _timer ??= new DispatcherTimerLike(DispatcherQueue, FrameInterval, OnTick);
        _timer.Start();
    }

    private void OnTick()
    {
        _framePending = false;
        bool more;
        try
        {
            more = _controller.Frame();
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
            _timer?.Stop();
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
                await _controller.OpenAsync(_ => Task.FromResult(session), ownsSession: false, LayoutOptions).ConfigureAwait(true);
            }
            else if (!string.IsNullOrEmpty(source))
            {
                ViewerOptions options = Options ?? new ViewerOptions();
                await _controller.OpenAsync(token => OpenSourceAsync(source, options, token), ownsSession: true, LayoutOptions).ConfigureAwait(true);
            }
            else
            {
                _controller.CloseDocument();
            }
        }
#pragma warning disable CA1031 // the host hears why the document did not open
        catch (Exception exception) when (generation == _openGeneration)
#pragma warning restore CA1031
        {
            Error?.Invoke(this, new PdfViewErrorEventArgs(exception));
        }
    }

    private static async Task<DocumentSession> OpenSourceAsync(string source, ViewerOptions options, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await DocumentSession.OpenAsync(SharedHttp.Value, uri, options, cancellationToken).ConfigureAwait(true);
        }

        // Opening reads the trailer and the page tree: off the UI thread where there are threads.
        if (OperatingSystem.IsBrowser())
        {
            return DocumentSession.Open(source, options);
        }

        return await Task.Run(() => DocumentSession.Open(source, options), cancellationToken).ConfigureAwait(true);
    }

    // ---- Form-field editors ----

    private TextBox CreateTextEditor(FieldEditor editor)
    {
        var input = new TextBox
        {
            Text = editor.Text,
            AcceptsReturn = editor.IsMultiline,
            TextWrapping = editor.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MaxLength = editor.MaxLength is int max && max > 0 ? max : 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
            Padding = new Thickness(2, 0, 2, 0),
            MinHeight = 0,
        };
        AutomationProperties.SetName(input, editor.IsPassword ? "Password field" : "Text field");
        bool committed = false;
        void Commit()
        {
            if (!committed && ReferenceEquals(_editorView, input))
            {
                committed = true;
                _controller.CommitFieldText(input.Text);
                _controller.EndFieldEdit();
            }
        }

        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && !editor.IsMultiline)
            {
                e.Handled = true;
                Commit();
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                _controller.EndFieldEdit();
            }
        };
        input.LostFocus += (_, _) => Commit();
        return input;
    }

    private ComboBox CreateChoiceEditor(FieldEditor editor)
    {
        var choice = new ComboBox
        {
            ItemsSource = editor.Options.ToList(),
            SelectedIndex = editor.SelectedIndex,
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        };
        choice.SelectionChanged += (_, _) =>
        {
            if (choice.SelectedIndex >= 0)
            {
                _controller.CommitFieldChoice(choice.SelectedIndex);
            }
        };
        choice.DropDownClosed += (_, _) =>
        {
            if (ReferenceEquals(_editorView, choice))
            {
                _controller.EndFieldEdit();
            }
        };
        return choice;
    }

    /// <summary>A repeating UI-thread timer that stops when no frame is wanted.</summary>
    private sealed class DispatcherTimerLike(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Action tick)
    {
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;

        public void Start()
        {
            if (_timer is null)
            {
                _timer = queue.CreateTimer();
                _timer.Interval = interval;
                _timer.IsRepeating = true;
                _timer.Tick += (_, _) => tick();
            }

            if (!_timer.IsRunning)
            {
                _timer.Start();
            }
        }

        public void Stop() => _timer?.Stop();
    }
}
