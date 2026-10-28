using System.Diagnostics;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Broadside.Viewer.Maui;

/// <summary>
/// A .NET MAUI control that shows a PDF document on the shared viewer core, drawn with SkiaSharp: tiles render through
/// <see cref="SkiaTileBackend"/> off the UI thread and are composed on the GPU (Metal on iOS and Mac Catalyst, OpenGL ES on Android
/// through <see cref="SKGLView"/>, Direct3D on Windows) unless <see cref="UseHardwareAcceleration"/> is off (a CPU raster canvas).
/// </summary>
/// <remarks>
/// <para>
/// Touch, mouse, pen and wheel input go through one <see cref="InputModel"/>, so pan, fling, pinch and double-tap behave the same on
/// every platform. Mouse drags over text select it (double click: word, triple: line); on touch a long press selects a word and
/// handles extend it. Links and form fields go through the <see cref="AnnotationLayer"/>: links inside the document navigate, every
/// other action (URIs, launches, scripts) is raised as <see cref="ActionRequested"/> and never performed. A tapped text or choice field
/// opens a native editor over the widget; an edit redraws only the overlay tiles it touches. With <see cref="MauiAppBuilderExtensions.UseBroadsideViewer"/>
/// the view also takes keyboard input on desktops and hardware keyboards (arrows, Page Up/Down, Home/End, +/-, Tab between fields,
/// Ctrl/Cmd+A and +C).
/// </para>
/// <para>ISO 32000-2 §12.3.2 (destinations), §12.5.6.5 (link annotations), §12.7 (interactive forms), §14.8 and §14.11.2.</para>
/// </remarks>
public sealed class PdfView : ContentView, IViewerHost, IDisposable
{
    /// <summary>The <see cref="Source"/> property: a file path or an http(s) address of the document to show.</summary>
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(PdfView), null, propertyChanged: (b, _, _) => ((PdfView)b).Reopen());

    /// <summary>The <see cref="Session"/> property: a document session the host opened and owns.</summary>
    public static readonly BindableProperty SessionProperty =
        BindableProperty.Create(nameof(Session), typeof(DocumentSession), typeof(PdfView), null, propertyChanged: (b, _, _) => ((PdfView)b).Reopen());

    /// <summary>The <see cref="LayoutOptions"/> property.</summary>
    public static readonly BindableProperty LayoutOptionsProperty =
        BindableProperty.Create(nameof(LayoutOptions), typeof(PageLayoutOptions), typeof(PdfView), null, propertyChanged: (b, _, n) => ((PdfView)b)._controller.SetLayoutOptions((PageLayoutOptions?)n));

    /// <summary>The <see cref="ShowAnnotations"/> property.</summary>
    public static readonly BindableProperty ShowAnnotationsProperty =
        BindableProperty.Create(nameof(ShowAnnotations), typeof(bool), typeof(PdfView), true, propertyChanged: (b, _, n) => ((PdfView)b)._controller.SetShowAnnotations((bool)n));

    /// <summary>The <see cref="AllowAnnotationEditing"/> property.</summary>
    public static readonly BindableProperty AllowAnnotationEditingProperty =
        BindableProperty.Create(nameof(AllowAnnotationEditing), typeof(bool), typeof(PdfView), false, propertyChanged: (b, _, n) => ((PdfView)b)._controller.AllowAnnotationEditing = (bool)n);

    /// <summary>The <see cref="UseHardwareAcceleration"/> property.</summary>
    public static readonly BindableProperty UseHardwareAccelerationProperty =
        BindableProperty.Create(nameof(UseHardwareAcceleration), typeof(bool), typeof(PdfView), true, propertyChanged: (b, _, _) => ((PdfView)b).ReplaceCanvas());

    private static readonly Lazy<HttpClient> SharedHttp = new(() => new HttpClient());
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan SemanticInterval = TimeSpan.FromMilliseconds(250);

    private readonly ViewerController _controller;
    private readonly Grid _root = new();
    private readonly SemanticLayer _semantics = new();
    private readonly AbsoluteLayout _editorLayer = new() { InputTransparent = true, CascadeInputTransparent = false };
    private View _canvas;
    private View? _editorView;
    private IDispatcherTimer? _timer;
    private bool _framePending;
    private long _lastSemantics;
    private bool _semanticsDirty;
    private TaskCompletionSource<byte[]>? _capture;
    private int _openGeneration;
    private bool _disposed;

    /// <summary>Creates an empty view; set <see cref="Source"/> or <see cref="Session"/> to show a document.</summary>
    public PdfView()
    {
        _controller = new ViewerController(this);
        _canvas = CreateCanvas();
        _root.Add(_canvas);
        _root.Add(_semantics);
        _root.Add(_editorLayer);
        Content = _root;
        BackgroundColor = Microsoft.Maui.Graphics.Color.FromRgb(ViewerFrame.Background.Red, ViewerFrame.Background.Green, ViewerFrame.Background.Blue);
        SemanticProperties.SetDescription(this, "Document");
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

    /// <summary>Gets or sets the document to show: a file path, or an http(s) address read through range requests.</summary>
    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets a document session the host opened (and disposes); it takes precedence over <see cref="Source"/>.</summary>
    /// <remarks>Open it with <c>ViewerOptions.UseBackend(() =&gt; new SkiaTileBackend())</c> to draw with Skia; otherwise its own backend draws.</remarks>
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

    /// <summary>Gets or sets whether the view composes on the GPU (<see cref="SKGLView"/>, default) or on the CPU (<see cref="SKCanvasView"/>).</summary>
    public bool UseHardwareAcceleration
    {
        get => (bool)GetValue(UseHardwareAccelerationProperty);
        set => SetValue(UseHardwareAccelerationProperty, value);
    }

    /// <summary>Gets or sets the options for documents opened from <see cref="Source"/>; without a backend factory, tiles draw with <see cref="SkiaTileBackend"/>.</summary>
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

    /// <summary>Zooms by a factor around the view's centre.</summary>
    /// <param name="factor">The factor (above 1 zooms in).</param>
    public void ZoomBy(double factor) => _controller.ZoomBy(factor);

    /// <summary>Scrolls the view by a distance in device-independent units, as a wheel or trackpad scroll does (no animation).</summary>
    /// <param name="deltaX">The horizontal distance; positive scrolls right.</param>
    /// <param name="deltaY">The vertical distance; positive scrolls down.</param>
    public void ScrollBy(double deltaX, double deltaY) => HandleScroll(deltaX, deltaY, InputModifiers.None, Width / 2, Height / 2);

    /// <summary>Copies the selected text to the clipboard.</summary>
    /// <returns>The copied text, or null when nothing is selected.</returns>
    public async Task<string?> CopySelectionAsync()
    {
        string? text = await _controller.GetSelectedTextAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(text))
        {
            await Clipboard.Default.SetTextAsync(text).ConfigureAwait(true);
        }

        return text;
    }

    /// <summary>Captures the next frame the canvas draws (read back from the GPU surface when hardware accelerated) as PNG bytes.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The PNG file's bytes, at the canvas's pixel size.</returns>
    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<byte[]> capture = _capture ??= new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => capture.TrySetCanceled(cancellationToken));
        Invalidate();
        return capture.Task;
    }

    /// <summary>Closes the document and releases the tiles and GPU images.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _timer?.Stop();
            _controller.Dispose();
        }
    }

    /// <summary>Routes a key press from the platform (DOM key names); returns whether the viewer used it.</summary>
    internal bool HandleKey(string key, InputModifiers modifiers) => _editorView is null && _controller.KeyDown(key, modifiers);

    /// <summary>Routes a platform scroll (trackpad or wheel) in device-independent units.</summary>
    internal void HandleScroll(double deltaX, double deltaY, InputModifiers modifiers, double x, double y) =>
        _controller.Wheel(deltaX, deltaY, WheelDeltaMode.Pixel, modifiers, x, y);

    /// <summary>Routes a platform magnify gesture step.</summary>
    internal void HandleMagnify(double factor, double x, double y) => _controller.Magnify(factor, x, y);

    /// <inheritdoc/>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        double density = 1;
        try
        {
            density = DeviceDisplay.Current.MainDisplayInfo.Density;
        }
#pragma warning disable CA1031 // no display information (a unit test host): one pixel per unit until the canvas reports its size
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        _controller.Resize(width, height, density > 0 ? density : 1);
    }

    /// <inheritdoc/>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null)
        {
            _timer?.Stop();
            _timer = null;
        }
        else if (_framePending)
        {
            StartTimer();
        }
    }

    // ---- IViewerHost ----

    void IViewerHost.RequestFrame()
    {
        _framePending = true;
        StartTimer();
    }

    void IViewerHost.RequestPaint()
    {
        Invalidate();
        if (_semanticsDirty || Stopwatch.GetElapsedTime(_lastSemantics) >= SemanticInterval)
        {
            _semanticsDirty = false;
            _lastSemantics = Stopwatch.GetTimestamp();
            _semantics.Reposition(_controller);
        }
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
        }

        if (editor is null)
        {
            return;
        }

        View view = editor.Kind == FieldEditorKind.Choice ? CreatePicker(editor) : CreateTextEditor(editor);
        view.IsVisible = false;
        _editorView = view;
        _editorLayer.Children.Add(view);
    }

    void IViewerHost.EditorMoved(double x, double y, double width, double height)
    {
        if (_editorView is not { } view)
        {
            return;
        }

        AbsoluteLayout.SetLayoutBounds((BindableObject)view, new Rect(x, y, Math.Max(width, 44), Math.Max(height, 28)));
        if (!view.IsVisible)
        {
            view.IsVisible = true;
            view.Focus();
        }
    }

    void IViewerHost.AccessibilityChanged(AccessibilityChange change)
    {
        if (change.Kind == AccessibilityChangeKind.FocusChanged)
        {
            // Focus that only follows scrolling (page changes) does not pull the screen reader away from what it is reading.
            if (!change.FollowsViewer)
            {
                _semantics.Focus(change.Node);
            }
        }
        else
        {
            _semantics.Rebuild(_controller, change.Node);
            _semanticsDirty = true;
        }
    }

    void IViewerHost.CopyText(string text) => _ = Clipboard.Default.SetTextAsync(text);

    // ---- Frames and painting ----

    private void StartTimer()
    {
        if (_disposed || Dispatcher is not { } dispatcher)
        {
            return;
        }

        if (_timer is null)
        {
            _timer = dispatcher.CreateTimer();
            _timer.Interval = FrameInterval;
            _timer.IsRepeating = true;
            _timer.Tick += OnTick;
        }

        if (!_timer.IsRunning)
        {
            _timer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
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
            _semantics.Reposition(_controller);
        }
    }

    private void Invalidate()
    {
        switch (_canvas)
        {
            case MetalCanvas metal:
                metal.InvalidateSurface();
                break;
            case SKGLView gl:
                gl.InvalidateSurface();
                break;
            case SKCanvasView cpu:
                cpu.InvalidateSurface();
                break;
        }
    }

    private View CreateCanvas()
    {
        if (UseHardwareAcceleration && MetalCanvas.IsSupported)
        {
            // iOS: Metal, as on Mac Catalyst (SKGLView is OpenGL ES there: deprecated, and emulated slowly on the simulator).
            var metal = new MetalCanvas();
            metal.Painted += Paint;
            metal.Touch += OnTouch;
            return metal;
        }

        if (UseHardwareAcceleration)
        {
            var gl = new SKGLView { EnableTouchEvents = true, HasRenderLoop = false };
            gl.PaintSurface += OnPaintGpu;
            gl.Touch += OnTouch;
            return gl;
        }

        var cpu = new SKCanvasView { EnableTouchEvents = true, IgnorePixelScaling = false };
        cpu.PaintSurface += OnPaintCpu;
        cpu.Touch += OnTouch;
        return cpu;
    }

    private void ReplaceCanvas()
    {
        View old = _canvas;
        switch (old)
        {
            case MetalCanvas metal:
                metal.Painted -= Paint;
                metal.Touch -= OnTouch;
                break;
            case SKGLView gl:
                gl.PaintSurface -= OnPaintGpu;
                gl.Touch -= OnTouch;
                break;
            case SKCanvasView cpu:
                cpu.PaintSurface -= OnPaintCpu;
                cpu.Touch -= OnTouch;
                break;
        }

        _canvas = CreateCanvas();
        _root.Remove(old);
        _root.Insert(0, _canvas);
    }

    private void OnTouch(object? sender, SKTouchEventArgs e) => _controller.Touch(e);

    private void OnPaintGpu(object? sender, SKPaintGLSurfaceEventArgs e) => Paint(e.Surface, e.BackendRenderTarget.Width);

    private void OnPaintCpu(object? sender, SKPaintSurfaceEventArgs e) => Paint(e.Surface, e.Info.Width);

    // May run on the GL thread (Android): it only draws the published frame and reads back a capture. Metal (iOS) paints on the UI thread.
    private void Paint(SKSurface surface, int pixelWidth)
    {
        _controller.Presenter.Draw(surface.Canvas);
        surface.Canvas.Flush();
        double width = Width;
        if (width > 0 && pixelWidth > 0 && Math.Abs((pixelWidth / width) - _controller.PixelRatio) > 0.01)
        {
            // The canvas knows its true pixels per unit (Mac Catalyst scales its iPad idiom); correct the controller on the UI thread.
            double ratio = pixelWidth / width;
            Dispatcher?.Dispatch(() => _controller.Resize(Width, Height, ratio));
        }

        if (Interlocked.Exchange(ref _capture, null) is { } capture)
        {
            Capture(surface, capture);
        }
    }

    // Reads the drawn pixels back (from the GPU surface when accelerated) and encodes them as PNG. A surface that cannot be read
    // back (OpenGL ES on the iOS simulator) gets the same frame drawn again into a raster surface.
    private void Capture(SKSurface surface, TaskCompletionSource<byte[]> capture)
    {
        try
        {
            SKRectI bounds = surface.Canvas.DeviceClipBounds;
            var info = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            if (!surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            {
                using var raster = SKSurface.Create(info);
                _controller.Presenter.Draw(raster.Canvas);
                if (!raster.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
                {
                    throw new InvalidOperationException("The canvas's pixels could not be read back.");
                }
            }

            using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            capture.TrySetResult(png.ToArray());
        }
#pragma warning disable CA1031 // the capture's awaiter hears why it failed; the paint goes on
        catch (Exception exception)
#pragma warning restore CA1031
        {
            capture.TrySetException(exception);
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
                ViewerOptions options = WithSkia(Options);
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

        return await Task.Run(() => DocumentSession.Open(source, options), cancellationToken).ConfigureAwait(true);
    }

    private static ViewerOptions WithSkia(ViewerOptions? options)
    {
        // Issue #428: the Skia tiles decode images as the options say (at the resolution they are drawn at, by default).
        bool scaled = options?.ScaledImageDecode ?? true;
        double samplesPerPixel = options?.ImageSamplesPerPixel ?? Broadside.Rendering.RenderOptions.DefaultImageSamplesPerPixel;
        var copy = new ViewerOptions
        {
            Engine = options?.Engine,
            DocumentOptions = options?.DocumentOptions,
            Credentials = options?.Credentials,
            Threading = options?.Threading ?? ViewerThreading.Automatic,
            Intent = options?.Intent ?? default,
            BackendFactory = options?.BackendFactory ?? (() => new SkiaTileBackend(scaled, samplesPerPixel)),
            ScaledImageDecode = scaled,
            ImageSamplesPerPixel = samplesPerPixel,
            Yield = options?.Yield,
        };
        if (options is not null)
        {
            copy.SliceBudget = options.SliceBudget;
            copy.MaxConcurrency = options.MaxConcurrency;
        }

        return copy;
    }

    // ---- Form-field editors ----

    private InputView CreateTextEditor(FieldEditor editor)
    {
        InputView input = editor.IsMultiline
            ? new Editor { Text = editor.Text, AutoSize = EditorAutoSizeOption.Disabled }
            : new Entry { Text = editor.Text, IsPassword = editor.IsPassword, ReturnType = ReturnType.Done, ClearButtonVisibility = ClearButtonVisibility.Never };
        input.MaxLength = editor.MaxLength is int max && max > 0 ? max : int.MaxValue;
        input.BackgroundColor = Colors.White;
        input.TextColor = Colors.Black;
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

        if (input is Entry entry)
        {
            entry.Completed += (_, _) => Commit();
        }

        input.Unfocused += (_, _) => Commit();
        return input;
    }

    private Picker CreatePicker(FieldEditor editor)
    {
        var picker = new Picker { ItemsSource = editor.Options.ToList(), SelectedIndex = editor.SelectedIndex, BackgroundColor = Colors.White, TextColor = Colors.Black };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
            {
                _controller.CommitFieldChoice(picker.SelectedIndex);
            }
        };
        picker.Unfocused += (_, _) =>
        {
            if (ReferenceEquals(_editorView, picker))
            {
                _controller.EndFieldEdit();
            }
        };
        return picker;
    }
}
