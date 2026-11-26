using Broadside.Forms;
using Broadside.Rendering;
using Microsoft.UI;
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

namespace Broadside.Viewer.WinUI;

/// <summary>
/// A WinUI 3 control that shows a PDF document on the shared viewer core: tiles render off the UI thread through the session's
/// backend (the managed software rasterizer unless <see cref="ViewerOptions.BackendFactory"/> says otherwise) and are composed by
/// the XAML compositor as image elements.
/// </summary>
/// <remarks>
/// <para>
/// Mouse, touch, pen, touchpad and wheel input go through one <see cref="InputModel"/>, so pan, fling, pinch and double-tap behave
/// as in every other shell. Mouse drags over text select it (double click: word, triple: line); on touch a long press selects a
/// word and handles extend it. Links and form fields go through the <see cref="AnnotationLayer"/>: links inside the document
/// navigate, every other action (URIs, launches, scripts) is raised as <see cref="ActionRequested"/> and never performed. A tapped
/// text or choice field opens a native editor over the widget. Keys: arrows, Page Up/Down, Home/End, +/-, Tab between fields, letter
/// keys for quick navigation, Ctrl+A and Ctrl+C. <see cref="Search"/> highlights matches over the pages without drawing them again,
/// and <see cref="ColorFilter"/> turns on smart dark mode.
/// </para>
/// <para>
/// The control's UI Automation peer exposes the loaded pages' accessible nodes (<see cref="AccessibilityBridge"/>) as a tree of peers
/// in reading order: headings with their level, lists, tables, figures with their alternate description, links and form fields.
/// </para>
/// <para>ISO 32000-2 §12.3.2 (destinations), §12.5.6.5 (link annotations), §12.7 (interactive forms), §14.8, §14.9.3 and §14.11.2.</para>
/// </remarks>
public sealed partial class PdfView : UserControl, IDisposable
{
    /// <summary>The <see cref="Source"/> dependency property: a file path or an http(s) address of the document to show.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(string), typeof(PdfView), new PropertyMetadata(null, (d, _) => ((PdfView)d).Reopen()));

    /// <summary>The <see cref="ShowAnnotations"/> dependency property.</summary>
    public static readonly DependencyProperty ShowAnnotationsProperty =
        DependencyProperty.Register(nameof(ShowAnnotations), typeof(bool), typeof(PdfView), new PropertyMetadata(true, (d, e) => ((PdfView)d)._controller.SetShowAnnotations((bool)e.NewValue)));

    /// <summary>The <see cref="AllowAnnotationEditing"/> dependency property.</summary>
    public static readonly DependencyProperty AllowAnnotationEditingProperty =
        DependencyProperty.Register(nameof(AllowAnnotationEditing), typeof(bool), typeof(PdfView), new PropertyMetadata(false, (d, e) => ((PdfView)d)._controller.AllowAnnotationEditing = (bool)e.NewValue));

    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient());

    private readonly ViewerController _controller;
    private readonly Canvas _surface = new();
    private readonly Canvas _editorLayer = new();
    private readonly FrameLayers _layers;
    private readonly SolidColorBrush _lightBackground = new(ColorHelper.FromArgb(0xFF, 0xE8, 0xE8, 0xEC));
    private readonly SolidColorBrush _darkBackground = new(ColorHelper.FromArgb(0xFF, 0x2B, 0x2B, 0x2B));
    private readonly RectangleGeometry _clip = new();
    private DocumentSession? _session;
    private PageLayoutOptions? _layoutOptions;
    private ColorFilter? _colorFilter;
    private PdfViewAutomationPeer? _peer;
    private FrameworkElement? _editorView;
    private XamlRoot? _xamlRoot;
    private bool _framePending;
    private bool _renderingHooked;
    private bool _loaded;
    private int _openGeneration;
    private bool _disposed;

    /// <summary>Creates an empty view; set <see cref="Source"/> or <see cref="Session"/> to show a document.</summary>
    public PdfView()
    {
        _controller = new ViewerController(new Host(this), new WriteableTileImageFactory());
        _surface.Background = _lightBackground;
        _surface.Clip = _clip;
        _layers = new FrameLayers(_surface);
        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(_editorLayer);
        Content = root;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AutomationProperties.SetName(this, "Document");

        _surface.PointerPressed += OnPointerPressed;
        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        _surface.PointerCanceled += OnPointerCanceled;
        _surface.PointerCaptureLost += OnPointerCanceled;
        _surface.PointerExited += OnPointerExited;
        _surface.PointerWheelChanged += OnPointerWheelChanged;
        SizeChanged += (_, _) => UpdateSize();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Raised when the page, page count, zoom or loading state changes.</summary>
    public event EventHandler<PdfViewStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Raised for every action a link, widget or annotation asks for. Destinations inside the document have already been followed when
    /// this is raised; URIs, launches, named actions and scripts are never performed by the view.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.6 (actions).</remarks>
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
        get => _session;
        set
        {
            if (!ReferenceEquals(_session, value))
            {
                _session = value;
                Reopen();
            }
        }
    }

    /// <summary>Gets or sets the page layout (continuous by default); changes apply at once, keeping the current page.</summary>
    public PageLayoutOptions? LayoutOptions
    {
        get => _layoutOptions;
        set
        {
            _layoutOptions = value;
            _controller.SetLayoutOptions(value);
        }
    }

    /// <summary>
    /// Gets or sets the color filter the pages are drawn with, such as <see cref="Rendering.ColorFilter.DarkMode"/> for smart dark
    /// mode; null (the default) keeps the session's own (<see cref="ViewerOptions.ColorFilter"/>). A change draws the visible tiles
    /// again from the cached display lists; the paper and the surroundings turn dark with it.
    /// </summary>
    /// <remarks>ISO 32000-2 §10.7.1 (the filter post-processes the rendered pixels). Spec #33 viewer capability 9.</remarks>
    public ColorFilter? ColorFilter
    {
        get => _colorFilter;
        set
        {
            _colorFilter = value;
            _controller.SetColorFilter(value);
        }
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

    /// <summary>Gets or sets the options for documents opened from <see cref="Source"/> (default: background rendering with the software rasterizer).</summary>
    public ViewerOptions? Options { get; set; }

    /// <summary>Gets what the view shows.</summary>
    public PdfViewState State => _controller.State;

    /// <summary>Gets the open document's session (from <see cref="Source"/> or <see cref="Session"/>), or null.</summary>
    public DocumentSession? CurrentSession => _controller.Session;

    /// <summary>Gets the text selection of the open document, or null.</summary>
    public TextSelection? Selection => _controller.Selection;

    /// <summary>Gets the annotation and link layer of the open document, or null (form values and annotation edits go through it).</summary>
    public AnnotationLayer? Annotations => _controller.Annotations;

    /// <summary>
    /// Gets the form filling of the open document, or null: the field the keyboard edits, Tab order (each page's Tabs entry), and one
    /// commit per edit through the form API (filling through the view saves the same file as filling through the API).
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7 (interactive forms), §7.7.3.3 Table 31 (Tabs). Spec #33 viewer capability 12.</remarks>
    public FormFilling? Forms => _controller.Forms;

    /// <summary>Gets the accessibility bridge of the open document, or null.</summary>
    public AccessibilityBridge? Accessibility => _controller.Bridge;

    /// <summary>
    /// Gets the text search of the open document, or null while nothing is open: its matches are highlighted over the pages (the
    /// current one distinctly) without drawing any page again; scroll a match into view with <see cref="Reveal"/>.
    /// </summary>
    /// <remarks>
    /// Its <see cref="DocumentSearch.Changed"/> event is raised on the indexing thread; marshal to the UI thread before touching
    /// controls. Spec #33 viewer capability 7.
    /// </remarks>
    public DocumentSearch? Search => _controller.Search;

    internal ViewerController Controller => _controller;

    internal FrameworkElement? EditorView => _editorView;

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

    /// <summary>Scrolls the view by a distance in device-independent units, as a wheel or touchpad scroll does (no animation).</summary>
    /// <param name="deltaX">The horizontal distance; positive scrolls right.</param>
    /// <param name="deltaY">The vertical distance; positive scrolls down.</param>
    public void ScrollBy(double deltaX, double deltaY) =>
        _controller.Wheel(deltaX, deltaY, WheelDeltaMode.Pixel, InputModifiers.None, ActualWidth / 2, ActualHeight / 2);

    /// <summary>Scrolls the least distance that shows an area of a page, such as a search match's bounds.</summary>
    /// <param name="pageIndex">The page (zero-based).</param>
    /// <param name="area">The area in the page's default user space.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3 (user space).</remarks>
    public void Reveal(int pageIndex, PdfRectangle area) => _controller.Reveal(pageIndex, area);

    /// <summary>Copies the selected text to the clipboard.</summary>
    /// <param name="cancellationToken">Stops waiting for the selection's pages to load; nothing is copied then.</param>
    /// <returns>The copied text, or null when nothing is selected or the document closed before the selection's pages loaded.</returns>
    public async Task<string?> CopySelectionAsync(CancellationToken cancellationToken = default)
    {
        string? text = await _controller.GetSelectedTextAsync().WaitAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
        {
            SetClipboardText(text);
        }

        return text;
    }

    /// <summary>Closes the document and releases the tiles.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Unhook();
            _controller.Dispose();
        }
    }

    /// <inheritdoc/>
    protected override AutomationPeer OnCreateAutomationPeer() => _peer ??= new PdfViewAutomationPeer(this);

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_editorView is null && KeyNames.Name(e.Key) is { } name && _controller.KeyDown(name, KeyNames.Current()))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    // ---- Pointers ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(_surface);
        PointerKind kind = Kind(point.PointerDeviceType);
        bool primary = kind != PointerKind.Mouse || point.Properties.IsLeftButtonPressed;
        Focus(FocusState.Pointer);
        if (_controller.PointerPressed(e.Pointer.PointerId, kind, point.Position.X, point.Position.Y, KeyNames.Modifiers(e.KeyModifiers), primary))
        {
            _surface.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(_surface);
        _controller.PointerMoved(e.Pointer.PointerId, Kind(point.PointerDeviceType), point.Position.X, point.Position.Y, point.IsInContact);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(_surface);
        _controller.PointerUp(e.Pointer.PointerId, point.Position.X, point.Position.Y);
        _surface.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e) => _controller.PointerCancel(e.Pointer.PointerId);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // A mouse leaving the view ends its hover (a link's ring goes away); a drag keeps its capture and is not affected.
        PointerPoint point = e.GetCurrentPoint(_surface);
        if (!point.IsInContact)
        {
            _controller.PointerMoved(e.Pointer.PointerId, Kind(point.PointerDeviceType), -1, -1, inContact: false);
        }
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(_surface);
        _controller.MouseWheel(point.Properties.MouseWheelDelta, point.Properties.IsHorizontalMouseWheel, KeyNames.Modifiers(e.KeyModifiers), point.Position.X, point.Position.Y);
        e.Handled = true;
    }

    private static PointerKind Kind(PointerDeviceType type) => type switch
    {
        PointerDeviceType.Touch => PointerKind.Touch,
        PointerDeviceType.Pen => PointerKind.Pen,
        _ => PointerKind.Mouse, // a mouse or a touchpad's pointer (touchpad scrolls and pinches arrive as wheel events)
    };

    // ---- Size, scale and frames ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        if (XamlRoot is { } root && !ReferenceEquals(root, _xamlRoot))
        {
            _xamlRoot?.Changed -= OnXamlRootChanged;

            _xamlRoot = root;
            root.Changed += OnXamlRootChanged;
        }

        UpdateSize();
        if (_framePending)
        {
            Hook();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        Unhook();
        _xamlRoot?.Changed -= OnXamlRootChanged;
        _xamlRoot = null;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateSize();

    private void UpdateSize()
    {
        double width = ActualWidth;
        double height = ActualHeight;
        _clip.Rect = new Rect(0, 0, Math.Max(width, 0), Math.Max(height, 0));
        double scale = XamlRoot?.RasterizationScale ?? 1;
        _controller.Resize(width, height, scale > 0 ? scale : 1);
    }

    private void Hook()
    {
        if (!_renderingHooked && _loaded && !_disposed)
        {
            _renderingHooked = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void Unhook()
    {
        if (_renderingHooked)
        {
            _renderingHooked = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    // Runs once per composition frame while the controller asks for frames: input animation, tile work, page loads, composition.
    private void OnRendering(object? sender, object e)
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
            Unhook();
        }
    }

    private void Present(ViewerFrame frame)
    {
        _surface.Background = frame.IsDark ? _darkBackground : _lightBackground;
        _layers.Apply(frame, _controller.PixelRatio);
    }

    // ---- Documents ----

    private async void Reopen()
    {
        int generation = ++_openGeneration;
        DocumentSession? session = _session;
        string? source = Source;
        try
        {
            if (session is not null)
            {
                await _controller.OpenAsync(_ => Task.FromResult(session), ownsSession: false, _layoutOptions).ConfigureAwait(true);
            }
            else if (!string.IsNullOrEmpty(source))
            {
                ViewerOptions options = Options ?? new ViewerOptions();
                await _controller.OpenAsync(token => OpenSourceAsync(source, options, token), ownsSession: true, _layoutOptions).ConfigureAwait(true);
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

        return await Task.Run(() => DocumentSession.Open(source, options), cancellationToken).ConfigureAwait(true);
    }

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    // ---- Form-field editors ----

    private void ShowEditor(FieldEditor? editor)
    {
        if (_editorView is not null)
        {
            _editorLayer.Children.Remove(_editorView);
            _editorView = null;
            _peer?.OnEditorChanged();
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
        view.Visibility = Visibility.Collapsed;
        _editorView = view;
        _editorLayer.Children.Add(view);
    }

    private void MoveEditor(double x, double y, double width, double height)
    {
        if (_editorView is not Control view)
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
            _peer?.OnEditorChanged();

            // An editor made visible this tick has no layout yet and may refuse focus: take it once layout has run.
            if (!view.Focus(FocusState.Programmatic))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ReferenceEquals(_editorView, view))
                    {
                        view.Focus(FocusState.Programmatic);
                    }
                });
            }
        }
    }

    private Control CreateTextEditor(FieldEditor editor)
    {
        // The box only takes input: the form filling (#218) holds the text (cut at MaxLen, one line unless multi-line) and writes it
        // once when the edit ends (#543). Enter and focus loss commit; Escape abandons the edit.
        int maxLength = editor.MaxLength is int max && max > 0 ? max : 0;
        bool closed = false;
        Control input;
        Func<string> read;
        if (editor.IsPassword)
        {
            var password = new PasswordBox { Password = editor.Text, MaxLength = maxLength };
            password.PasswordChanged += (_, _) => Input(password, password.Password);
            input = password;
            read = () => password.Password;
        }
        else
        {
            var text = new TextBox { Text = editor.Text, MaxLength = maxLength, AcceptsReturn = editor.IsMultiline, TextWrapping = editor.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
            text.TextChanged += (_, _) => Input(text, text.Text);
            input = text;
            read = () => text.Text;
        }

        input.Padding = new Thickness(2, 0, 2, 0);
        input.MinHeight = 0;
        input.MinWidth = 0;
        void Input(Control source, string value)
        {
            if (ReferenceEquals(_editorView, source))
            {
                _controller.EditorInput(value);
            }
        }

        void Close(bool commit)
        {
            if (!closed && ReferenceEquals(_editorView, input))
            {
                closed = true;
                _ = commit ? _controller.CommitFieldText(read()) : _controller.CancelFieldEdit();
                _controller.EndFieldEdit();
                Focus(FocusState.Programmatic);
            }
        }

        input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter && !editor.IsMultiline)
            {
                e.Handled = true;
                Close(commit: true);
            }
            else if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Close(commit: false);
            }
        };
        input.LostFocus += (_, _) => Close(commit: true);
        AutomationProperties.SetName(input, "Form field");
        return input;
    }

    private ComboBox CreateChoiceEditor(FieldEditor editor)
    {
        var choice = new ComboBox { MinHeight = 0, MinWidth = 0, IsEditable = editor.IsEditable };
        foreach (string option in editor.Options)
        {
            choice.Items.Add(option);
        }

        choice.SelectedIndex = editor.SelectedIndex;
        if (editor.IsEditable)
        {
            choice.Text = editor.Text;

            // Typed text (Enter or focus loss submits it) goes to the form filling, which picks the option it names or keeps the text.
            choice.TextSubmitted += (_, e) =>
            {
                if (ReferenceEquals(_editorView, choice))
                {
                    e.Handled = true;
                    _controller.CommitFieldText(e.Text);
                }
            };
        }

        // A pick commits through the form filling at once.
        choice.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, choice) && choice.SelectedIndex >= 0 && choice.SelectedIndex != editor.SelectedIndex)
            {
                _controller.CommitFieldChoice(choice.SelectedIndex);
            }
        };
        choice.DropDownClosed += (_, _) =>
        {
            if (ReferenceEquals(_editorView, choice) && !editor.IsEditable)
            {
                _controller.EndFieldEdit();
                Focus(FocusState.Programmatic);
            }
        };
        AutomationProperties.SetName(choice, "Form field");
        return choice;
    }

    private ListBox CreateMultiSelectEditor(FieldEditor editor)
    {
        // A multi-select list box (§12.7.5.4, MultiSelect): a click toggles its option. The items are containers of their own, so
        // options with the same text stay apart.
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, MinHeight = 0, MinWidth = 0 };
        foreach (string option in editor.Options)
        {
            var item = new ListBoxItem { Content = option, Padding = new Thickness(4, 1, 4, 1), MinHeight = 0 };
            AutomationProperties.SetName(item, option);
            list.Items.Add(item);
        }

        // Through the selector's own selection: an item's IsSelected set before the list has applied its template does not stick.
        foreach (int index in editor.SelectedIndices)
        {
            list.SelectedItems.Add(list.Items[index]);
        }

        // Every change hands the whole selection to the form filling, which holds it until focus leaves the list and then writes it
        // once through the form API.
        list.SelectionChanged += (_, _) =>
        {
            if (ReferenceEquals(_editorView, list))
            {
                _controller.EditorSelectedOptions(list.SelectedItems.Select(list.Items.IndexOf));
            }
        };

        void Close(bool commit)
        {
            if (ReferenceEquals(_editorView, list))
            {
                _ = commit ? _controller.CommitFieldEdit() : _controller.CancelFieldEdit();
                _controller.EndFieldEdit();
            }
        }

        // Escape abandons the picks; focus leaving the list commits them.
        list.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                Close(commit: false);
                Focus(FocusState.Programmatic);
            }
        };
        // Focus moves between the list and its items while it is used: decide once the move has settled, and keep the list open when
        // focus went nowhere (the window deactivated).
        list.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (list.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is DependencyObject focused && !Contains(list, focused))
            {
                Close(commit: true);
            }
        });
        AutomationProperties.SetName(list, "Form field");
        return list;

        static bool Contains(DependencyObject ancestor, DependencyObject? element)
        {
            for (; element is not null; element = VisualTreeHelper.GetParent(element))
            {
                if (ReferenceEquals(element, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // ---- The controller's host ----

    private sealed class Host(PdfView view) : IViewerHost
    {
        public void RequestFrame()
        {
            view._framePending = true;
            view.Hook();
        }

        public void Present(ViewerFrame frame) => view.Present(frame);

        public void StateChanged(PdfViewState state) => view.StateChanged?.Invoke(view, new PdfViewStateChangedEventArgs(state));

        public void ActionRequested(ViewerActionEventArgs e) => view.ActionRequested?.Invoke(view, e);

        public void Error(Exception exception) => view.Error?.Invoke(view, new PdfViewErrorEventArgs(exception));

        public void EditorChanged(FieldEditor? editor) => view.ShowEditor(editor);

        public void EditorMoved(double x, double y, double width, double height) => view.MoveEditor(x, y, width, height);

        public void AccessibilityChanged(AccessibilityChange change) => view._peer?.OnAccessibilityChanged(change);

        public void CopyText(string text) => SetClipboardText(text);
    }
}
