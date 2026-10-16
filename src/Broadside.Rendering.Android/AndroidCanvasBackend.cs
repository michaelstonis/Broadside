using Broadside.Graphics;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidColor = Android.Graphics.Color;
using AndroidMatrix = Android.Graphics.Matrix;
using AndroidPath = Android.Graphics.Path;
using DashPathEffect = Android.Graphics.DashPathEffect;
using Matrix = Broadside.Graphics.Matrix;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;
using PaintHinting = Android.Graphics.PaintHinting;

namespace Broadside.Rendering.Android;

/// <summary>
/// The Android backend: renders a page or replays a <see cref="DisplayList"/> onto an Android <see cref="AndroidCanvas"/>, so pages
/// draw through the platform's own graphics library (a native backend). Its output matches the software rasterizer within the
/// golden-image threshold.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0006: it consumes display lists only. ISO 32000-2 §8.5.3: paths fill with the non-zero winding or even-odd rule
/// (§8.5.3.3.2, §8.5.3.3.3), open subpaths closed, a trailing lone moveto ignored and a degenerate subpath given the device pixel
/// under it (§8.5.3.3.1), as the software rasterizer does; strokes take the line parameters of §8.4.3 (width, cap, join, miter limit,
/// dash pattern) in user space, so a non-uniform transform gives an elliptical pen; a zero width (§8.4.3.2) is a one-pixel pen in
/// device space, and stroke adjustment (§10.7.5) rounds the width to whole device pixels and, like the thinnest line, moves the ends
/// of horizontal and vertical segments onto the pixel grid, as the software rasterizer does; a degenerate subpath strokes as a dot
/// only with round caps, and a zero-length dash is capped along the path, also at the very end of a subpath (§8.5.3.2, §8.4.3.6).
/// Clipping (§8.5.4) intersects the canvas clip with each clipping path, saved and restored with
/// the graphics state (§8.4.2). Colours convert to RGB through
/// <see cref="PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces?)"/>, so the engine's colour
/// management decides them (§8.6, §10). Every edge is anti-aliased by the canvas.
/// </para>
/// <para>
/// Constant alpha and blend modes (§11.3.6, §11.6.3, §11.6.4.4) are the paints' own: the alpha goes into the paint's colour and the
/// blend mode into the paint (Android 10, API level 29, and later; earlier versions composite as Normal and report it in
/// <see cref="Diagnostics"/>). Annotations (§12.5.5) are placed as the software rasterizer places them (§12.5.3 NoZoom, NoRotate),
/// clipped to their rectangle and composited as a layer with their opacity and blend mode.
/// </para>
/// <para>
/// Text (§9.3.6, §9.4.3, §9.9): glyphs of embedded TrueType and OpenType programs are drawn by Android's font engine from a font built
/// from the program's bytes, once per program per backend (Android 12, API level 31, and later), unhinted and positioned exactly, each
/// glyph checked once against its outline; every other program, a glyph the engine reads differently, and every glyph when
/// <see cref="RenderOptions.ForceOutlines"/> is set are filled from their outlines (§9.2.4) with the non-zero rule, so they match the
/// software rasterizer as paths do. Stroked modes stroke the outlines in user space. Glyphs drawn from their outlines where a native
/// font was expected are reported in <see cref="Diagnostics"/>.
/// </para>
/// <para>
/// Images (§8.9.4, §8.9.5) become Android bitmaps once per backend, kept within the document's image cache budget
/// (<see cref="PdfOptions.ImageCacheBudget"/>); reduced to few enough device pixels they are resampled as the software rasterizer
/// resamples them, otherwise the canvas samples them. Stencil masks (§8.9.6.2) are alpha bitmaps painted in the fill colour; soft and
/// explicit masks of another size (§8.9.6.3, §11.6.5.2) mask the image in a layer. Axial and radial shadings (§8.7.4.5.3,
/// §8.7.4.5.4) are linear and two-point conical gradient shaders (a radial shading other than growing concentric circles needs
/// Android 12, API level 31); function-based shadings (§8.7.4.5.2) a texture; meshes (§8.7.4.5.5 to §8.7.4.5.8) are rasterized by
/// the backend at the device's pixels and reported in <see cref="Diagnostics"/>. Tiling patterns (§8.7.3) are rendered once per scale
/// into a bitmap tile, repeated by a bitmap shader on the lattice the software rasterizer lays.
/// </para>
/// <para>
/// Not drawn yet: transparency groups and soft masks (#136), drawn as their content and reported.
/// </para>
/// <para>
/// Reuse one instance for many renders: its Android path, paints and matrix are created once and kept, so a warm replay allocates
/// nothing per operation. An instance renders one page at a time; use one per thread to render in parallel (a document is safe for
/// concurrent renders while nobody changes it). Dispose it to release its Android objects.
/// </para>
/// </remarks>
public sealed partial class AndroidCanvasBackend : IDisposable
{
    private static readonly RenderOptions DefaultOptions = new();
    private static readonly Content.ContentOptions DefaultContentOptions = new();

    private readonly Backend _backend = new();
    private bool _disposed;

    /// <summary>Renders <paramref name="page"/> at 72 DPI into a new ARGB_8888 bitmap, on white.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The bitmap; the caller owns it.</returns>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public AndroidBitmap Render(PdfPage page) => Render(page, DefaultOptions);

    /// <summary>Renders <paramref name="page"/> into a new ARGB_8888 bitmap of <see cref="RenderOptions.GetPixelSize"/> pixels, on white.</summary>
    /// <param name="page">The page.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <returns>The bitmap; the caller owns it.</returns>
    /// <exception cref="ArgumentException">The page would need more pixels than a bitmap can hold.</exception>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public AndroidBitmap Render(PdfPage page, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        AndroidBitmap bitmap = AndroidBitmap.CreateBitmap(size.Width, size.Height, AndroidBitmap.Config.Argb8888!);
        try
        {
            using var canvas = new AndroidCanvas(bitmap);
            Render(page, canvas, options);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return bitmap;
    }

    /// <summary>
    /// Renders <paramref name="page"/> onto <paramref name="canvas"/>, on white, in the area of <see cref="RenderOptions.GetPixelSize"/>
    /// pixels at the canvas's origin; drawing is clipped to that area and the canvas is left as it was found.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="canvas">The canvas, for example one over a view's or a caller's bitmap; its current matrix and clip apply.</param>
    /// <param name="options">The scale and rotation.</param>
    /// <remarks>ISO 32000-2 §8.3.2.3, §7.7.3.3 Table 31 and §14.11.2 for the page mapping (see <see cref="RenderOptions"/>).</remarks>
    public void Render(PdfPage page, AndroidCanvas canvas, RenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PixelSize size = options.GetPixelSize(page);
        Matrix device = options.GetDeviceTransform(page);
        DisplayList list = page.CreateDisplayList(options.Layers, options.Intent, DefaultContentOptions);
        int count = canvas.Save();
        try
        {
            canvas.ClipRect(0, 0, size.Width, size.Height);
            canvas.DrawColor(AndroidColor.White);
            Render(list, canvas, device, options.ForceOutlines);
        }
        finally
        {
            canvas.RestoreToCount(count);
        }
    }

    /// <summary>Draws <paramref name="list"/> onto <paramref name="canvas"/>, without a background; the canvas is left as it was found.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="canvas">The canvas; its current matrix and clip apply.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the canvas's coordinates (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space). Glyphs of TrueType and OpenType programs are drawn natively.</remarks>
    public void Render(DisplayList list, AndroidCanvas canvas, in Matrix deviceTransform) => Render(list, canvas, deviceTransform, forceOutlines: false);

    /// <summary>Draws <paramref name="list"/> onto <paramref name="canvas"/>, without a background; the canvas is left as it was found.</summary>
    /// <param name="list">The display list.</param>
    /// <param name="canvas">The canvas; its current matrix and clip apply.</param>
    /// <param name="deviceTransform">
    /// The transform from the list's default user space to the canvas's coordinates (origin top-left, y down), such as
    /// <see cref="RenderOptions.GetDeviceTransform"/>; a tile renderer adds its own offset.
    /// </param>
    /// <param name="forceOutlines">
    /// Whether to fill every glyph from its outline, never through Android's font engine (<see cref="RenderOptions.ForceOutlines"/>).
    /// </param>
    /// <remarks>ADR 0006; ISO 32000-2 §8.3.2.2 (device space), §9.2.4 (glyph outlines).</remarks>
    public void Render(DisplayList list, AndroidCanvas canvas, in Matrix deviceTransform, bool forceOutlines)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(canvas);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _backend.ForceOutlines = forceOutlines;
        _backend.Begin(list.Document, canvas, deviceTransform);
        try
        {
            list.Replay(_backend);
        }
        finally
        {
            _backend.End();
        }
    }

    /// <summary>Releases the backend's Android path, paints, matrix, dash effects, fonts, bitmaps and shaders; it renders no more after this.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _backend.Dispose();
        }
    }

    /// <summary>The backend the display list replays through: graphics state, path building, paints and colour.</summary>
    private sealed partial class Backend : IRenderingBackend, IDisposable
    {
        /// <summary>Device coordinates are kept within ±2^30 so that they stay exact enough as floats for the canvas.</summary>
        private const double CoordinateLimit = 1073741824.0;

        /// <summary>
        /// The thinnest line's pen: the float just above one pixel, because the canvas draws a stroke of at most one device pixel as a
        /// hairline, whose coverage is not the area the pen sweeps (the software rasterizer's, and §10.7.4's model).
        /// </summary>
        private static readonly float OnePixel = MathF.BitIncrement(1f);

        /// <summary>The most dash patterns kept as Android path effects.</summary>
        private const int DashCacheSize = 32;

        // Android's enum constants, read once: each read of the static property goes through JNI. They are shared, never disposed.
        private static readonly AndroidPath.FillType Winding = AndroidPath.FillType.Winding!;
        private static readonly AndroidPath.FillType EvenOdd = AndroidPath.FillType.EvenOdd!;
        private static readonly AndroidPath.Direction Clockwise = AndroidPath.Direction.Cw!;
        private static readonly Paint.Cap ButtCap = Paint.Cap.Butt!;
        private static readonly Paint.Cap RoundCap = Paint.Cap.Round!;
        private static readonly Paint.Cap SquareCap = Paint.Cap.Square!;
        private static readonly Paint.Join MiterJoin = Paint.Join.Miter!;
        private static readonly Paint.Join RoundJoin = Paint.Join.Round!;
        private static readonly Paint.Join BevelJoin = Paint.Join.Bevel!;

        private readonly AndroidPath _path = new();
        // Fills draw paths and native glyphs: unhinted, positioned exactly (subpixel), with linear metrics.
        private readonly Paint _fill = new(PaintFlags.AntiAlias | PaintFlags.SubpixelText | PaintFlags.LinearText);
        private readonly Paint _stroke = new(PaintFlags.AntiAlias);
        private readonly AndroidMatrix _matrix = new();
        private readonly Dash[] _dashes = new Dash[DashCacheSize];
        private int _dashCount;
        private int _nextDash;
#pragma warning disable CA2213 // The effect set on the stroke paint belongs to the dash cache, which disposes it.
        private DashPathEffect? _currentDash;
#pragma warning restore CA2213
        private byte[] _snapFlags = new byte[64];
        private bool _snapping;
        private double _snapOffset;
        private Matrix _fromDevice;
        private double _scale;
        private bool _strokeInDevice;

        private SavedState[] _stack = new SavedState[16];
        private int _depth;
        private Matrix _device;
        private Matrix _ctm;
        private Matrix _toDevice;
        private AndroidCanvas? _canvas;
        private PdfDocument? _document;
        private int _baseCount;

        private PdfColorSpace? _space;
        private ColorConversion _conversion;
        private PdfDefaultColorSpaces? _defaults;
        private PdfColorConverter? _converter;

        public Backend()
            : this(null, 0)
        {
        }

        /// <summary>Initializes a backend; one that renders pattern cells for <paramref name="owner"/> shares its image and shading caches.</summary>
        private Backend(Backend? owner, int patternDepth)
        {
            _owner = owner;
            _patternDepth = patternDepth;
            _images = owner?._images ?? new ImageCache();
            _shadings = owner?._shadings ?? new ShadingCache();
            _maskInPaint.SetXfermode(_dstIn);
            _insidePaint.SetXfermode(_srcIn);
            _fill.SetStyle(Paint.Style.Fill);
            _fill.Hinting = PaintHinting.Off;
            _fill.TextSize = DrawSize;
            _checkPaint.Hinting = PaintHinting.Off;
            _checkPaint.TextSize = CheckSize;
            _stroke.SetStyle(Paint.Style.Stroke);
        }

        public void Begin(PdfDocument document, AndroidCanvas canvas, in Matrix device)
        {
            _document = document;
            _canvas = canvas;
            _device = device;
            _ctm = Matrix.Identity;
            _toDevice = device;
            _depth = 0;
            BeginCompositing();
            BeginText();
            BeginPaints(canvas, document);
            _baseCount = canvas.Save();
        }

        public void End()
        {
            _canvas?.RestoreToCount(_baseCount);
            _canvas = null;
            _document = null;
            _space = null;
            _defaults = null;
            _converter = null;
        }

        /// <summary>Saves the transform and the canvas's clip (ISO 32000-2 §8.4.2 <c>q</c>).</summary>
        public void SaveState()
        {
            if (_depth == _stack.Length)
            {
                Array.Resize(ref _stack, _stack.Length * 2);
            }

            _stack[_depth++] = new SavedState(_ctm, _toDevice, _compositing);
            _canvas!.Save();
        }

        /// <summary>Restores the transform and the clip of the matching <see cref="SaveState"/> (§8.4.2 <c>Q</c>).</summary>
        public void RestoreState()
        {
            if (_depth > 0)
            {
                SavedState saved = _stack[--_depth];
                _ctm = saved.Ctm;
                _toDevice = saved.ToDevice;
                _compositing = saved.Compositing;
                _canvas!.Restore();
            }
        }

        public void SetTransform(in Matrix transform)
        {
            _ctm = transform;
            _toDevice = transform * _device;
        }

        /// <summary>Fills the path mapped to the device with the rule (§8.5.3.3).</summary>
        public void FillPath(PathView path, FillRule rule, in DisplayColor color)
        {
            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing || path.IsEmpty || _compositing.FillAlpha == 0)
            {
                return;
            }

            SetColor(_fill, converter, color.Components, _compositing.FillAlpha);
            SetBlendMode(_fill, ref _fillBlendMode);
            BuildDevicePath(path, rule);
            _canvas!.DrawPath(_path, _fill);
        }

        /// <summary>
        /// Strokes the path (§8.5.3.2, §8.4.3, §10.7.5): in user space under the current transform, so the pen follows it; the thinnest
        /// line (width 0, or under half a pixel with stroke adjustment) in device space with a one-pixel pen, as the software
        /// rasterizer draws it, its dash pattern scaled by the transform's largest stretch.
        /// </summary>
        public void StrokePath(PathView path, in StrokeStyle style, in DisplayColor color)
        {
            PdfColorConverter converter = Converter(color);
            if (converter.PaintsNothing || path.IsEmpty || _compositing.StrokeAlpha == 0)
            {
                return;
            }

            SetColor(_stroke, converter, color.Components, _compositing.StrokeAlpha);
            SetBlendMode(_stroke, ref _strokeBlendMode);
            DrawStroke(path, style);
        }

        /// <summary>Draws the stroke with the stroke paint's colour and blend mode (see <see cref="StrokePath"/>).</summary>
        /// <returns>Whether anything was drawn.</returns>
        private bool DrawStroke(PathView path, in StrokeStyle style)
        {
            double width = StrokeWidth(style);
            _strokeInDevice = width == 0;
            if (!BuildStrokePath(path, style.LineCap == LineCap.Round, extendEnds: !style.DashArray.IsEmpty && style.LineCap != LineCap.Butt))
            {
                return false;
            }

            _stroke.StrokeWidth = _strokeInDevice ? OnePixel : (float)width;
            _stroke.StrokeCap = style.LineCap switch
            {
                LineCap.Round => RoundCap,
                LineCap.ProjectingSquare => SquareCap,
                _ => ButtCap,
            };
            _stroke.StrokeJoin = style.LineJoin switch
            {
                LineJoin.Round => RoundJoin,
                LineJoin.Bevel => BevelJoin,
                _ => MiterJoin,
            };
            _stroke.StrokeMiter = (float)style.MiterLimit;
#pragma warning disable CA2000 // The effect belongs to the dash cache, which disposes it.
            DashPathEffect? dash = style.DashArray.IsEmpty ? null : DashEffect(style.DashArray, style.DashPhase, _strokeInDevice ? _scale : 1);
#pragma warning restore CA2000
            if (!ReferenceEquals(dash, _currentDash))
            {
                _stroke.SetPathEffect(dash);
                _currentDash = dash;
            }

            AndroidCanvas canvas = _canvas!;
            if (_strokeInDevice)
            {
                canvas.DrawPath(_path, _stroke);
                return true;
            }

            if (!SetMatrix(_toDevice))
            {
                return false;
            }

            int count = canvas.Save();
            canvas.Concat(_matrix);
            canvas.DrawPath(_path, _stroke);
            canvas.RestoreToCount(count);
            return true;
        }

        /// <summary>Intersects the canvas clip with the inside of the path under the rule (§8.5.4); <see cref="RestoreState"/> undoes it.</summary>
        public void IntersectClip(PathView path, FillRule rule)
        {
            BuildDevicePath(path, rule);
            _canvas!.ClipPath(_path);
        }

        public void Dispose()
        {
            DisposePaints();
            _stroke.SetPathEffect(null);
            for (int i = 0; i < _dashCount; i++)
            {
                _dashes[i].Effect.Dispose();
            }

            _path.Dispose();
            _fill.Dispose();
            _stroke.Dispose();
            _layerPaint.Dispose();
            _matrix.Dispose();
            DisposeText();
        }

        /// <summary>
        /// Puts <paramref name="m"/> into the Android matrix through calls that take only numbers (Android's <c>setValues</c> takes an
        /// array, which JNI copies into a new Java array on every call): rotation, scale and skew from the QR decomposition of its
        /// linear part, then the translation. A matrix whose first column is zero maps everything onto a line and draws nothing.
        /// </summary>
        /// <returns>Whether the matrix was set.</returns>
        private bool SetMatrix(in Matrix m)
        {
            double r = Math.Sqrt((m.A * m.A) + (m.B * m.B));
            if (!(r > 0) || !double.IsFinite(r))
            {
                return false;
            }

            double skew = ((m.A * m.C) + (m.B * m.D)) / r;
            double scaleY = m.Determinant / r;
            _matrix.SetSinCos((float)(m.B / r), (float)(m.A / r));
            _matrix.PreScale((float)r, (float)scaleY);
            _matrix.PreSkew((float)(skew / r), 0);
            _matrix.PostTranslate((float)m.E, (float)m.F);
            return true;
        }

        /// <summary>
        /// The stroke's width in user space, and whether and how its vertices snap: zero for the thinnest line the canvas draws
        /// (§8.4.3.2); with stroke adjustment (§10.7.5) the device width rounded to whole pixels, at least one, or the thinnest line
        /// when it is under half a pixel. The thinnest line and an adjusted one snap like the software rasterizer's: the ends of
        /// horizontal and vertical device segments move to half pixels for an odd number of pixels, to whole ones for an even number.
        /// </summary>
        private double StrokeWidth(in StrokeStyle style)
        {
            double width = style.LineWidth;
            Matrix m = _toDevice;
            double sum = (m.A * m.A) + (m.B * m.B) + (m.C * m.C) + (m.D * m.D);
            double determinant = m.Determinant;
            double scale = Math.Sqrt((sum + Math.Sqrt(Math.Max(0, (sum * sum) - (4 * determinant * determinant)))) / 2);
            double device = width * scale;
            _scale = scale;
            bool thinnest = !(width > 0) || (style.StrokeAdjustment && device < 0.5);
            _snapping = thinnest || (style.StrokeAdjustment && m.TryInvert(out _fromDevice));
            _snapOffset = 0.5;
            if (thinnest)
            {
                return 0;
            }

            if (!style.StrokeAdjustment || !(scale > 0) || !double.IsFinite(device))
            {
                return width;
            }

            double pixels = Math.Max(1, Math.Round(device, MidpointRounding.AwayFromZero));
            _snapOffset = pixels % 2 == 1 ? 0.5 : 0;
            return pixels / scale;
        }

        private static void SetColor(Paint paint, PdfColorConverter converter, ReadOnlySpan<float> components, byte alpha)
        {
            Span<float> rgb = stackalloc float[4];
            converter.Convert(components, rgb);
            paint.SetARGB(alpha, Channel(rgb[0]), Channel(rgb[1]), Channel(rgb[2]));
        }

        private static int Channel(float value) => (int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

        private PdfColorConverter Converter(in DisplayColor color)
        {
            if (_converter is null || !ReferenceEquals(_space, color.ColorSpace) || !ReferenceEquals(_defaults, color.DefaultColorSpaces) || _conversion != color.Conversion)
            {
                _space = color.ColorSpace;
                _defaults = color.DefaultColorSpaces;
                _conversion = color.Conversion;
                _converter = _document!.GetColorConverter(color.ColorSpace, color.Conversion with { Target = DeviceColorModel.Rgb }, color.DefaultColorSpaces);
            }

            return _converter;
        }

        /// <summary>
        /// The Android dash effect of a dash pattern (§8.4.3.6) with its lengths multiplied by <paramref name="factor"/>, kept per
        /// backend so that a warm replay creates none; <see langword="null"/> (a solid line) when the scaled pattern has no length.
        /// Android takes an even number of intervals, so an odd-length array is repeated once, which describes the same pattern. A
        /// zero-length dash becomes 1/256 of a device pixel long, taken from the gap after it: the canvas then caps it along the path
        /// (a projecting square turns with the path, §8.5.3.2), where a dash of no length would give an upright square.
        /// </summary>
        private DashPathEffect? DashEffect(ReadOnlySpan<double> array, double phase, double factor)
        {
            // Only a pattern with a zero-length dash depends on the device scale (the length it is given).
            double scale = 0;
            for (int i = 0; i < array.Length; i += array.Length % 2 == 0 ? 2 : 1)
            {
                if (array[i] == 0)
                {
                    scale = _scale;
                    break;
                }
            }

            for (int i = 0; i < _dashCount; i++)
            {
                ref Dash cached = ref _dashes[i];
                if (cached.Phase == phase && cached.Factor == factor && cached.Scale == scale && array.SequenceEqual(cached.Array))
                {
                    return cached.Effect;
                }
            }

            int length = array.Length % 2 == 0 ? array.Length : array.Length * 2;
            float[] intervals = new float[length];
            double sum = 0;
            for (int i = 0; i < length; i++)
            {
                double interval = array[i % array.Length] * factor;
                intervals[i] = (float)interval;
                sum += interval;
            }

            if (!(sum > 0) || !double.IsFinite(sum) || !double.IsFinite(phase * factor))
            {
                return null;
            }

            double epsilon = _scale > 0 ? factor / (256 * _scale) : 0;
            for (int i = 0; i < length; i += 2)
            {
                if (intervals[i] == 0 && intervals[i + 1] >= epsilon && epsilon > 0)
                {
                    intervals[i] = (float)epsilon;
                    intervals[i + 1] -= (float)epsilon;
                }
            }

            var effect = new DashPathEffect(intervals, (float)(phase * factor));
            int slot = _dashCount < DashCacheSize ? _dashCount++ : _nextDash++ % DashCacheSize;
            if (_dashes[slot].Effect is { } old)
            {
                if (ReferenceEquals(old, _currentDash))
                {
                    _stroke.SetPathEffect(null);
                    _currentDash = null;
                }

                old.Dispose();
            }

            _dashes[slot] = new Dash(array.ToArray(), phase, factor, scale, effect);
            return effect;
        }

        /// <summary>
        /// Puts <paramref name="path"/>, mapped to the device, into the Android path with the fill rule: open subpaths are closed by
        /// the canvas, a trailing lone moveto adds nothing, and a degenerate subpath (one with segments whose points all equal its
        /// first) becomes the device pixel under it, as in the software rasterizer (§8.5.3.3.1).
        /// </summary>
        private void BuildDevicePath(PathView path, FillRule rule, bool degenerateDots = true)
        {
            AndroidPath android = _path;
            android.Reset();
            android.SetFillType(rule == FillRule.EvenOdd ? EvenOdd : Winding);
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            var subpath = default(Subpath);
            int p = 0;
            foreach (PathVerb verb in verbs)
            {
                switch (verb)
                {
                    case PathVerb.MoveTo:
                        FinishDevice(ref subpath, degenerateDots);
                        subpath = new Subpath(points[p]);
                        MoveTo(points[p++]);
                        break;
                    case PathVerb.LineTo:
                        subpath.Touch(points[p]);
                        LineTo(points[p++]);
                        break;
                    case PathVerb.QuadTo:
                        subpath.Touch(points[p]);
                        subpath.Touch(points[p + 1]);
                        QuadTo(points[p], points[p + 1]);
                        p += 2;
                        break;
                    case PathVerb.CubicTo:
                        subpath.Touch(points[p]);
                        subpath.Touch(points[p + 1]);
                        subpath.Touch(points[p + 2]);
                        CubicTo(points[p], points[p + 1], points[p + 2]);
                        p += 3;
                        break;
                    case PathVerb.Close:
                        subpath.HasSegments |= subpath.IsOpen;
                        android.Close();
                        break;
                }
            }

            FinishDevice(ref subpath, degenerateDots);
        }

        /// <summary>Gives a finished degenerate subpath the device pixel under its point, when <paramref name="dots"/>.</summary>
        private void FinishDevice(ref Subpath subpath, bool dots)
        {
            if (dots && subpath.IsOpen && subpath.HasSegments && subpath.IsDegenerate)
            {
                (double x, double y) = Map(subpath.First);
                double left = Math.Floor(x);
                double top = Math.Floor(y);
                _path.AddRect((float)left, (float)top, (float)(left + 1), (float)(top + 1), Clockwise);
            }

            subpath.IsOpen = false;
        }

        /// <summary>
        /// Puts <paramref name="path"/>, in user space, into the Android path for stroking (§8.5.3.2): each subpath as constructed,
        /// a degenerate one (a single point closed, or segments whose points all equal the first) as a zero-length line, which the
        /// canvas draws as a dot, when <paramref name="roundCaps"/> and as nothing otherwise; a lone moveto adds nothing. When the
        /// stroke snaps (see <see cref="StrokeWidth"/>), the ends of its horizontal and vertical device line segments move.
        /// </summary>
        /// <returns>Whether anything was added.</returns>
        private bool BuildStrokePath(PathView path, bool roundCaps, bool extendEnds)
        {
            AndroidPath android = _path;
            android.Reset();
            android.SetFillType(Winding);
            ReadOnlySpan<PathVerb> verbs = path.Verbs;
            ReadOnlySpan<PathPoint> points = path.Points;
            bool any = false;
            int v = 0;
            int p = 0;
            while (v < verbs.Length)
            {
                // One subpath: from its moveto to the verb before the next moveto.
                PathPoint first = points[p];
                int end = v + 1;
                int pointsEnd = p + 1;
                bool degenerate = true;
                bool segments = false;
                while (end < verbs.Length && verbs[end] != PathVerb.MoveTo)
                {
                    int count = PointCount(verbs[end]);
                    segments = true;
                    for (int i = 0; i < count; i++)
                    {
                        degenerate &= points[pointsEnd + i] == first;
                    }

                    pointsEnd += count;
                    end++;
                }

                if (segments && degenerate)
                {
                    if (roundCaps)
                    {
                        (float x, float y) = Emit(first);
                        android.MoveTo(x, y);
                        android.LineTo(x, y);
                        any = true;
                    }
                }
                else if (segments)
                {
                    AddStrokeSubpath(verbs[v..end], points[p..pointsEnd], extendEnds);
                    any = true;
                }

                v = end;
                p = pointsEnd;
            }

            return any;
        }

        /// <summary>Adds one subpath that is not degenerate, snapping its line ends when the stroke snaps.</summary>
        private void AddStrokeSubpath(ReadOnlySpan<PathVerb> verbs, ReadOnlySpan<PathPoint> points, bool extendEnd)
        {
            AndroidPath android = _path;
            Span<byte> snap = _snapping ? SnapFlags(verbs, points) : default;
            (float x, float y) = Point(points, snap, 0);
            android.MoveTo(x, y);
            int q = 1;
            for (int i = 1; i < verbs.Length; i++)
            {
                switch (verbs[i])
                {
                    case PathVerb.LineTo:
                        (x, y) = Point(points, snap, q++);
                        android.LineTo(x, y);
                        break;
                    case PathVerb.QuadTo:
                        (float x1, float y1) = Emit(points[q]);
                        (x, y) = Emit(points[q + 1]);
                        android.QuadTo(x1, y1, x, y);
                        q += 2;
                        break;
                    case PathVerb.CubicTo:
                        (x1, y1) = Emit(points[q]);
                        (float x2, float y2) = Emit(points[q + 1]);
                        (x, y) = Emit(points[q + 2]);
                        android.CubicTo(x1, y1, x2, y2, x, y);
                        q += 3;
                        break;
                    case PathVerb.Close:
                        android.Close();
                        break;
                }
            }

            if (extendEnd && snap.IsEmpty && verbs[^1] != PathVerb.Close)
            {
                ExtendEnd(points);
            }
        }

        /// <summary>
        /// Continues an open dashed subpath by 1/256 of a device pixel along its end direction. The canvas's dasher drops a dash that
        /// starts exactly at the end of a subpath, while §8.4.3.6 (and the software rasterizer) paints it: a zero-length dash there is
        /// a dot or a square. The extension is invisible otherwise and straight on, so it adds no join.
        /// </summary>
        private void ExtendEnd(ReadOnlySpan<PathPoint> points)
        {
            (double endX, double endY) = Place(points[^1]);
            double pixel = _strokeInDevice ? 1 : _scale;
            for (int i = points.Length - 2; i >= 0; i--)
            {
                (double x, double y) = Place(points[i]);
                double dx = endX - x;
                double dy = endY - y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                if (length > 0 && double.IsFinite(length) && pixel > 0)
                {
                    double step = 1 / (256 * pixel * length);
                    _path.LineTo((float)(endX + (dx * step)), (float)(endY + (dy * step)));
                    return;
                }
            }
        }

        private static int PointCount(PathVerb verb) => verb switch
        {
            PathVerb.LineTo => 1,
            PathVerb.QuadTo => 2,
            PathVerb.CubicTo => 3,
            _ => 0,
        };

        /// <summary>
        /// Marks, per point of one subpath, the device axes to snap: bit 1 (x) for an end of a vertical device line segment, bit 2 (y)
        /// for an end of a horizontal one; the closing segment of a closed subpath counts. Curve points are never moved.
        /// </summary>
        private Span<byte> SnapFlags(ReadOnlySpan<PathVerb> verbs, ReadOnlySpan<PathPoint> points)
        {
            if (_snapFlags.Length < points.Length)
            {
                _snapFlags = new byte[Math.Max(points.Length, _snapFlags.Length * 2)];
            }

            Span<byte> flags = _snapFlags.AsSpan(0, points.Length);
            flags.Clear();
            int current = 0;
            int q = 1;
            for (int i = 1; i < verbs.Length; i++)
            {
                switch (verbs[i])
                {
                    case PathVerb.LineTo:
                        Mark(flags, points, current, q);
                        current = q++;
                        break;
                    case PathVerb.Close:
                        Mark(flags, points, current, 0);
                        current = 0;
                        break;
                    default:
                        q += PointCount(verbs[i]);
                        current = q - 1;
                        break;
                }
            }

            return flags;
        }

        private void Mark(Span<byte> flags, ReadOnlySpan<PathPoint> points, int a, int b)
        {
            (double ax, double ay) = Map(points[a]);
            (double bx, double by) = Map(points[b]);
            byte axes = (byte)((Math.Abs(ax - bx) <= 1e-6 ? 1 : 0) | (Math.Abs(ay - by) <= 1e-6 ? 2 : 0));
            flags[a] |= axes;
            flags[b] |= axes;
        }

        /// <summary>Returns point <paramref name="index"/> of a subpath in user space, snapped on the device axes its flags name.</summary>
        private (float X, float Y) Point(ReadOnlySpan<PathPoint> points, ReadOnlySpan<byte> snap, int index)
        {
            PathPoint point = points[index];
            if (snap.IsEmpty || snap[index] == 0)
            {
                return Emit(point);
            }

            (double x, double y) = Map(point);
            if ((snap[index] & 1) != 0)
            {
                x = SnapCoordinate(x);
            }

            if ((snap[index] & 2) != 0)
            {
                y = SnapCoordinate(y);
            }

            if (_strokeInDevice)
            {
                return ((float)x, (float)y);
            }

            PathPoint user = _fromDevice.Transform(x, y);
            return ((float)user.X, (float)user.Y);
        }

        /// <summary>Returns a stroke point where the stroke is built: device space for the thinnest line, user space otherwise.</summary>
        private (double X, double Y) Place(PathPoint point) => _strokeInDevice ? Map(point) : (point.X, point.Y);

        private (float X, float Y) Emit(PathPoint point)
        {
            (double x, double y) = Place(point);
            return ((float)x, (float)y);
        }

        private double SnapCoordinate(double value) =>
            Math.Abs(value) >= CoordinateLimit ? value : _snapOffset > 0 ? Math.Floor(value) + _snapOffset : Math.Floor(value + 0.5);

        private void MoveTo(PathPoint point)
        {
            (double x, double y) = Map(point);
            _path.MoveTo((float)x, (float)y);
        }

        private void LineTo(PathPoint point)
        {
            (double x, double y) = Map(point);
            _path.LineTo((float)x, (float)y);
        }

        private void QuadTo(PathPoint control, PathPoint to)
        {
            (double x1, double y1) = Map(control);
            (double x2, double y2) = Map(to);
            _path.QuadTo((float)x1, (float)y1, (float)x2, (float)y2);
        }

        private void CubicTo(PathPoint control1, PathPoint control2, PathPoint to)
        {
            (double x1, double y1) = Map(control1);
            (double x2, double y2) = Map(control2);
            (double x3, double y3) = Map(to);
            _path.CubicTo((float)x1, (float)y1, (float)x2, (float)y2, (float)x3, (float)y3);
        }

        private (double X, double Y) Map(PathPoint point)
        {
            Matrix m = _toDevice;
            double x = (m.A * point.X) + (m.C * point.Y) + m.E;
            double y = (m.B * point.X) + (m.D * point.Y) + m.F;
            return (Math.Clamp(x, -CoordinateLimit, CoordinateLimit), Math.Clamp(y, -CoordinateLimit, CoordinateLimit));
        }
    }

    /// <summary>A dash pattern and the Android effect made for it.</summary>
    private readonly record struct Dash(double[] Array, double Phase, double Factor, double Scale, DashPathEffect Effect);

    /// <summary>The subpath being added to a device path: its first point, and whether every point so far equals it.</summary>
    private struct Subpath
    {
        public Subpath(PathPoint first)
        {
            First = first;
            IsOpen = true;
            IsDegenerate = true;
        }

        public PathPoint First { get; }

        public bool IsOpen { get; set; }

        public bool HasSegments { get; set; }

        public bool IsDegenerate { get; private set; }

        /// <summary>Records a point of a segment: the subpath has a segment, and is degenerate only while every point is its first.</summary>
        public void Touch(PathPoint point)
        {
            HasSegments = true;
            IsDegenerate &= point == First;
        }
    }
}
