using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// Renders display lists with WebGL 2 in the browser: the list is recorded into triangles and commands in .NET, then drawn by the GPU
/// in one call into a canvas the page shares, and read back into a <see cref="Bitmap"/> (or left in <see cref="Canvas"/> for a caller
/// that composites canvases). What it cannot draw natively makes the software rasterizer draw that render instead, with a diagnostic.
/// </summary>
/// <remarks>
/// ISO 32000-2 §10.7.1 (rendering) through the display list. The backend is opt-in and falls back to the software rasterizer. Call
/// <see cref="InitializeAsync"/> once before rendering. One render at a time per instance; instances share one WebGL context, so
/// renders on one page are sequential (WebAssembly is single-threaded).
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed partial class WebGLRenderer : IDisposable
{
    private static readonly Diagnostic Unavailable = new(
        "BackendWebGLUnavailable",
        DiagnosticSeverity.Warning,
        "WebGL 2 is not available in this browser (or InitializeAsync was not awaited); the software rasterizer drew the page.");

    private static Task<bool>? _initializing;

    private readonly CommandList _commands = new();
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Recorder _recorder;
    private SoftwareRasterizer? _software;
    private byte[] _readBack = [];
    private double _imageSamplesPerPixel = RenderOptions.DefaultImageSamplesPerPixel;

    /// <summary>Initializes a new instance of the <see cref="WebGLRenderer"/> class.</summary>
    public WebGLRenderer() => _recorder = new Recorder(_commands, _diagnostics);

    /// <summary>Gets a value indicating whether WebGL 2 is available (after <see cref="InitializeAsync"/>).</summary>
    public static bool IsAvailable { get; private set; }

    /// <summary>Gets the GPU, sample count and texture limit the context reports, or null before initialization or without WebGL 2.</summary>
    public static string? Description { get; private set; }

    /// <summary>Gets the bytes of texture memory images use on the GPU (mipmaps included).</summary>
    public static long TextureBytes => TextureCache.Bytes;

    /// <summary>Gets or sets the most texture memory images keep on the GPU before the least recently used are released (192 MiB).</summary>
    public static long TextureBudget
    {
        get => TextureCache.Budget;
        set => TextureCache.Budget = Math.Max(0, value);
    }

    /// <summary>Gets the canvas that holds the last render drawn without read-back (its top-left corner), to draw into a 2D canvas.</summary>
    public static JSObject Canvas => WebGLInterop.GetCanvas();

    /// <summary>Gets the diagnostics of the last render: fallbacks to the software rasterizer and groups it rasterized.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>Loads the backend's JavaScript module and creates the WebGL 2 context; completes with false when WebGL 2 is unavailable.</summary>
    /// <returns>Whether WebGL 2 is available.</returns>
    public static Task<bool> InitializeAsync() => _initializing ??= InitializeCoreAsync();

    /// <summary>Renders a page at <paramref name="options"/> onto white, like <see cref="SoftwareRasterizer.Render(PdfPage, RenderOptions)"/>.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">Resolution, scale and rotation.</param>
    /// <returns>The pixels (premultiplied BGRA).</returns>
    public Bitmap Render(PdfPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        PixelSize size = options.GetPixelSize(page);
        var target = new Bitmap(size.Width, size.Height);
        target.Pixels.Fill(0xFF);
        double samplesPerPixel = options.ScaledImageDecode && !options.OverprintPreview ? options.ImageSamplesPerPixel : 0;
        using WebGLRender render = Begin(page.CreateDisplayList(), target, options.GetDeviceTransform(page), readBack: true, samplesPerPixel, CancellationToken.None);
        while (!render.Continue(Timeout.InfiniteTimeSpan))
        {
        }

        return target;
    }

    /// <summary>Renders a display list into <paramref name="target"/> (which holds the background) and reads the pixels back.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The pixels, holding a uniform background on entry.</param>
    /// <param name="deviceTransform">Default user space to the target's pixels.</param>
    public void Render(DisplayList list, Bitmap target, in Matrix deviceTransform)
    {
        using WebGLRender render = BeginRender(list, target, deviceTransform, readBack: true, CancellationToken.None);
        while (!render.Continue(Timeout.InfiniteTimeSpan))
        {
        }
    }

    /// <summary>Starts a render that records in time slices and draws on the GPU when the recording completes.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="target">The pixels, holding a uniform background on entry (white paper, or transparent).</param>
    /// <param name="deviceTransform">Default user space to the target's pixels.</param>
    /// <param name="cancellationToken">Stops the render.</param>
    /// <param name="readBack">
    /// Whether the GPU's pixels are copied into <paramref name="target"/>; when false they stay in <see cref="Canvas"/> until the next
    /// render (unless <see cref="WebGLRender.IsInCanvas"/> is false: the software rasterizer drew the render into the target).
    /// </param>
    /// <returns>The render in progress; the caller disposes it.</returns>
    public WebGLRender BeginRender(DisplayList list, Bitmap target, in Matrix deviceTransform, bool readBack, CancellationToken cancellationToken) =>
        Begin(list, target, deviceTransform, readBack, ScaledImageDecode ? _imageSamplesPerPixel : 0, cancellationToken);

    /// <summary>
    /// Gets or sets a value indicating whether the renders of a display list given to this renderer decode images at the resolution
    /// they are drawn at rather than their own; <see langword="false"/> by default. Page renders take
    /// <see cref="RenderOptions.ScaledImageDecode"/> instead.
    /// </summary>
    /// <remarks>
    /// For viewers only, never for printing or export (see <see cref="RenderOptions.ScaledImageDecode"/>): an image is uploaded at the
    /// largest power-of-two reduction that keeps <see cref="ImageSamplesPerPixel"/> samples per device pixel
    /// (<see cref="DisplayImage.GetReduction"/>, capped for a curved colour conversion), a stencil mask as 8-bit coverage at its own;
    /// the GPU then samples the smaller texture. ISO 32000-2 §8.9.4 (the image unit square), §8.9.5.3.
    /// </remarks>
    public bool ScaledImageDecode { get; set; }

    /// <summary>
    /// Gets or sets the fewest decoded samples per device pixel a reduced image keeps when <see cref="ScaledImageDecode"/> is on:
    /// <see cref="RenderOptions.DefaultImageSamplesPerPixel"/> (2) by default; 1 is faster and coarser.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number of at least 1.</exception>
    /// <remarks>See <see cref="RenderOptions.ImageSamplesPerPixel"/>; ISO 32000-2 §8.9.4.</remarks>
    public double ImageSamplesPerPixel
    {
        get => _imageSamplesPerPixel;
        set => _imageSamplesPerPixel = double.IsFinite(value) && value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The samples per pixel must be a finite number of at least 1.");
    }

    private WebGLRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, bool readBack, double samplesPerPixel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        _diagnostics.Clear();
        _recorder.ImageSamplesPerPixel = samplesPerPixel;
        return new WebGLRender(this, list, target, deviceTransform, readBack, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _software?.Dispose();
        _recorder.DisposeRasterizer();
    }

    /// <summary>
    /// Reads the last render drawn without read-back (still in <see cref="Canvas"/>) into <paramref name="target"/>, for a caller that
    /// could not composite the canvas before the next render.
    /// </summary>
    /// <param name="target">Pixels of the render's size.</param>
    public static void ReadCanvas(Bitmap target)
    {
        ArgumentNullException.ThrowIfNull(target);
        byte[] pixels = System.Buffers.ArrayPool<byte>.Shared.Rent(target.Width * target.Height * 4);
        try
        {
            Span<byte> span = pixels.AsSpan(0, target.Width * target.Height * 4);
            WebGLInterop.ReadLast(target.Width, target.Height, span);
            CopyFlipped(span, target);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    /// <summary>Copies GL pixels (RGBA, bottom row first) into a bitmap's premultiplied BGRA rows.</summary>
    internal static void CopyFlipped(ReadOnlySpan<byte> pixels, Bitmap target)
    {
        int width = target.Width;
        int height = target.Height;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = pixels.Slice((height - 1 - y) * width * 4, width * 4);
            Span<byte> row = target.GetRow(y);
            for (int i = 0; i < source.Length; i += 4)
            {
                uint rgba = BinaryPrimitives.ReadUInt32LittleEndian(source[i..]);
                BinaryPrimitives.WriteUInt32LittleEndian(row[i..], (rgba & 0xFF00FF00u) | ((rgba >> 16) & 0xFFu) | ((rgba & 0xFFu) << 16));
            }
        }
    }

    internal Recorder Recorder => _recorder;

    internal CommandList Commands => _commands;

    internal List<Diagnostic> DiagnosticList => _diagnostics;

    internal static Diagnostic UnavailableDiagnostic => Unavailable;

    /// <summary>Gets the software rasterizer a render falls back to, decoding images as this render's recorder does (issue #362).</summary>
    internal SoftwareRasterizer Software
    {
        get
        {
            SoftwareRasterizer software = _software ??= new SoftwareRasterizer();
            double samplesPerPixel = _recorder.ImageSamplesPerPixel;
            software.ScaledImageDecode = samplesPerPixel > 0;
            if (samplesPerPixel > 0)
            {
                software.ImageSamplesPerPixel = samplesPerPixel;
            }

            return software;
        }
    }

    internal Span<byte> ReadBackBuffer(int length)
    {
        if (_readBack.Length < length)
        {
            _readBack = new byte[length];
        }

        return _readBack.AsSpan(0, length);
    }

    private static async Task<bool> InitializeCoreAsync()
    {
        try
        {
            await JSHost.ImportAsync(WebGLInterop.Module, "../_content/Broadside.Rendering.WebGL/broadside-webgl.js").ConfigureAwait(true);
            string description = WebGLInterop.Initialize();
            IsAvailable = description.Length > 0;
            Description = IsAvailable ? description : null;
            if (IsAvailable)
            {
                TextureCache.MaxSize = WebGLInterop.GetMaxTextureSize();
            }
        }
        catch (JSException)
        {
            IsAvailable = false;
        }

        return IsAvailable;
    }
}
