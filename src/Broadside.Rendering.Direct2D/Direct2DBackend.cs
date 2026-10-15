using System.Collections.ObjectModel;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// The backend <see cref="Direct2DRenderer"/> replays display lists through (ADR 0006): graphics state, path geometry, clips and
/// colour on a Direct2D device context.
/// </summary>
/// <remarks>
/// <para>
/// Fills (ISO 32000-2 §8.5.3.3) build a path geometry in user space with the list's curves as Direct2D Bézier segments, every
/// subpath closed for filling, a trailing lone moveto left out, and fill it with the winding or alternate fill mode under the
/// current transform times the device transform. A degenerate subpath also paints the device pixel under its point
/// (§8.5.3.3.1), as the software rasterizer does.
/// </para>
/// <para>
/// Clips (§8.5.4) are pushed on the device context: an axis-aligned anti-aliased clip for a rectangle that stays aligned with the
/// pixel axes, a layer with an anti-aliased geometric mask for any other path. <see cref="RestoreState"/> pops the clips pushed since
/// the matching <see cref="SaveState"/>.
/// </para>
/// <para>
/// Hot path (CLAUDE.md): working arrays grow by doubling and are kept, the colour converter is cached, and diagnostics are shared
/// instances, so a warm replay allocates nothing managed per operation. Direct2D's own objects (geometries, stroke styles) are native.
/// </para>
/// </remarks>
internal sealed unsafe partial class Direct2DBackend : IRenderingBackend
{
    private const string SkippedCode = "RenderingOperationSkipped";

    private static readonly Diagnostic ImagesSkipped = Skipped("images", 130);

    private static readonly Diagnostic ImageMasksSkipped = Skipped("stencil image masks", 130);

    private static readonly Diagnostic ShadingsSkipped = Skipped("shadings and shading patterns", 130);

    private static readonly Diagnostic GlyphsSkipped = Skipped("glyph runs (text)", 131);

    private readonly List<Diagnostic> _diagnostics = [];
    private SkippedOperations _skipped;

    private ID2D1Factory1* _factory;
    private ID2D1DeviceContext* _context;
    private ID2D1SolidColorBrush* _brush;
    private PdfDocument? _document;
    private Matrix _device;
    private Matrix _ctm;
    private Matrix _toDevice;

    private Matrix[] _transforms = new Matrix[16];
    private int[] _clipMarks = new int[16];
    private int _depth;

    // The clips pushed on the device context, innermost last: a geometric mask's layer (kept alive until it is popped) or, with no
    // mask, an axis-aligned clip.
    private Clip[] _clips = new Clip[16];
    private int _clipCount;

    // Degenerate subpaths of the fill being built, in user space (§8.5.3.3.1).
    private PathPoint[] _pixels = new PathPoint[8];
    private int _pixelCount;

    private PdfColorSpace? _space;
    private ColorConversion _conversion;
    private PdfDefaultColorSpaces? _defaults;
    private PdfColorConverter? _converter;

    public Direct2DBackend() => Diagnostics = new ReadOnlyCollection<Diagnostic>(_diagnostics);

    [Flags]
    private enum SkippedOperations
    {
        None = 0,
        Images = 1,
        ImageMasks = 2,
        Shadings = 4,
        Glyphs = 8,
        TilingPatterns = 16,
        BlendModes = 32,
        Groups = 64,
        SoftMasks = 128,
    }

    /// <summary>Gets the diagnostics of the last replay: one for each kind of operation the backend skipped.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Starts a replay onto the surface's device context, which is between <c>BeginDraw</c> and <c>EndDraw</c>.</summary>
    public void Begin(Direct2DSurface surface, PdfDocument document, in Matrix device)
    {
        _factory = surface.Factory;
        _context = surface.Context;
        _brush = surface.Brush;
        _document = document;
        _device = device;
        _ctm = Matrix.Identity;
        _toDevice = device;
        _depth = 0;
        _clipCount = 0;
        _diagnostics.Clear();
        _skipped = SkippedOperations.None;
        BeginCompositing();
    }

    /// <summary>Pops every clip still pushed and lets go of the document and the surface.</summary>
    public void End()
    {
        PopClips(0);
        _document = null;
        _space = null;
        _defaults = null;
        _converter = null;
        _factory = null;
        _context = null;
        _brush = null;
        EndStrokes();
    }

    public void SaveState()
    {
        if (_depth == _transforms.Length)
        {
            Array.Resize(ref _transforms, _depth * 2);
            Array.Resize(ref _clipMarks, _depth * 2);
            Array.Resize(ref _savedCompositing, _depth * 2);
        }

        _transforms[_depth] = _ctm;
        _savedCompositing[_depth] = _compositing;
        _clipMarks[_depth] = _clipCount;
        _depth++;
    }

    public void RestoreState()
    {
        if (_depth == 0)
        {
            return;
        }

        _depth--;
        PopClips(_clipMarks[_depth]);
        _compositing = _savedCompositing[_depth];
        SetTransform(in _transforms[_depth]);
    }

    public void SetTransform(in Matrix transform)
    {
        _ctm = transform;
        _toDevice = transform * _device;
    }

    /// <summary>Fills the path with the colour (ISO 32000-2 §8.5.3.3).</summary>
    public void FillPath(PathView path, FillRule rule, in DisplayColor color)
    {
        PdfColorConverter converter = Converter(color);
        if (converter.PaintsNothing || path.IsEmpty || _compositing.FillAlpha == 0)
        {
            return;
        }

        SetColor(converter, color.Components, _compositing.FillAlpha);
        _pixelCount = 0;
        ID2D1PathGeometry* geometry = CreateFillGeometry(path, rule, collectPixels: true, out bool hasFigures);
        try
        {
            if (hasFigures)
            {
                SetWorldTransform(_toDevice);
                _context->FillGeometry((ID2D1Geometry*)geometry, (ID2D1Brush*)_brush, null);
            }
        }
        finally
        {
            geometry->Release();
        }

        FillPixels();
    }

    /// <summary>
    /// Intersects the clip with the inside of the path under the current transform (ISO 32000-2 §8.5.4): a rectangle that stays
    /// aligned with the pixel axes becomes an axis-aligned clip, any other path a layer with the path as its geometric mask.
    /// </summary>
    public void IntersectClip(PathView path, FillRule rule)
    {
        if (TryMapRectangle(path, out D2D_RECT_F rectangle))
        {
            PushRectangle(rectangle);
            return;
        }

        ID2D1PathGeometry* geometry = CreateFillGeometry(path, rule, collectPixels: false, out bool hasFigures);
        if (!hasFigures)
        {
            // A path with no area: nothing is inside the clip any more.
            geometry->Release();
            PushRectangle(default);
            return;
        }

        var parameters = new D2D1_LAYER_PARAMETERS1
        {
            contentBounds = Interop.InfiniteRect,
            geometricMask = (ID2D1Geometry*)geometry,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = Interop.Matrix(_toDevice),
            opacity = 1,
            opacityBrush = null,
            layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
        };

        // The mask carries the whole user-to-device transform, so the world transform is the identity: the mask lands in the same
        // place whether Direct2D applies the world transform to it or not.
        SetWorldTransform(Matrix.Identity);
        try
        {
            _context->PushLayer(&parameters, null);
        }
        catch
        {
            geometry->Release();
            throw;
        }

        AddClip(new Clip((ID2D1Geometry*)geometry));
    }

    public void DrawImage(DisplayImage image, PdfDefaultColorSpaces defaultColorSpaces, ColorConversion conversion) =>
        Skip(SkippedOperations.Images, ImagesSkipped);

    public void FillImageMask(DisplayImageMask mask, in DisplayColor color) => Skip(SkippedOperations.ImageMasks, ImageMasksSkipped);

    public void DrawGlyphs(in GlyphRun run) => Skip(SkippedOperations.Glyphs, GlyphsSkipped);

    public void DrawShading(in DisplayShading shading) => Skip(SkippedOperations.Shadings, ShadingsSkipped);

    public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading) => Skip(SkippedOperations.Shadings, ShadingsSkipped);

    public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading) =>
        Skip(SkippedOperations.Shadings, ShadingsSkipped);

    private static Diagnostic Skipped(string what, int issue) => new(
        SkippedCode,
        DiagnosticSeverity.Warning,
        string.Create(CultureInfo.InvariantCulture, $"The Direct2D backend does not draw {what} yet (issue #{issue}); the render leaves them out."));

    private static byte Channel(float value) => (byte)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);

    private void Skip(SkippedOperations kind, Diagnostic diagnostic)
    {
        if ((_skipped & kind) == 0)
        {
            _skipped |= kind;
            _diagnostics.Add(diagnostic);
        }
    }

    private void SetWorldTransform(in Matrix transform)
    {
        D2D_MATRIX_3X2_F matrix = Interop.Matrix(transform);
        _context->SetTransform(&matrix);
    }

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
    /// Sets the brush to the colour, rounded to eight bits a channel like the software rasterizer's (§8.6), with
    /// <paramref name="alpha"/> (the constant alpha, 0 to 255, §11.6.4.4) as its alpha.
    /// </summary>
    private void SetColor(PdfColorConverter converter, ReadOnlySpan<float> components, byte alpha)
    {
        Span<float> rgb = stackalloc float[4];
        converter.Convert(components, rgb);
        var color = new D2D1_COLOR_F
        {
            r = Channel(rgb[0]) / 255f,
            g = Channel(rgb[1]) / 255f,
            b = Channel(rgb[2]) / 255f,
            a = alpha / 255f,
        };
        _brush->SetColor(&color);
    }

    /// <summary>
    /// Builds a path geometry of <paramref name="path"/> in user space for filling or clipping (§8.5.3.3.1): every subpath closed,
    /// a lone moveto left out; with <paramref name="collectPixels"/>, the points of degenerate subpaths go to <see cref="_pixels"/>.
    /// </summary>
    private ID2D1PathGeometry* CreateFillGeometry(PathView path, FillRule rule, bool collectPixels, out bool hasFigures)
    {
        ID2D1PathGeometry* geometry;
        ((ID2D1Factory*)_factory)->CreatePathGeometry(&geometry);
        try
        {
            ID2D1GeometrySink* sink;
            geometry->Open(&sink);
            try
            {
                sink->SetFillMode(rule == FillRule.EvenOdd ? D2D1_FILL_MODE.D2D1_FILL_MODE_ALTERNATE : D2D1_FILL_MODE.D2D1_FILL_MODE_WINDING);
                hasFigures = WriteFillFigures(sink, path, collectPixels);
                sink->Close();
            }
            finally
            {
                sink->Release();
            }
        }
        catch
        {
            geometry->Release();
            throw;
        }

        return geometry;
    }

    private bool WriteFillFigures(ID2D1GeometrySink* sink, PathView path, bool collectPixels)
    {
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        var figure = default(FillFigure);
        bool hasFigures = false;
        int p = 0;
        foreach (PathVerb verb in verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    Finish(sink, ref figure, collectPixels);
                    figure = new FillFigure(points[p++]);
                    break;
                case PathVerb.LineTo when !figure.IsOpen:
                    p++;
                    break;
                case PathVerb.QuadTo when !figure.IsOpen:
                    p += 2;
                    break;
                case PathVerb.CubicTo when !figure.IsOpen:
                    p += 3;
                    break;
                case PathVerb.LineTo:
                    hasFigures |= Start(sink, ref figure);
                    figure.Touch(points[p]);
                    sink->AddLine(Interop.Point(points[p]));
                    p++;
                    break;
                case PathVerb.QuadTo:
                    {
                        hasFigures |= Start(sink, ref figure);
                        figure.Touch(points[p]);
                        figure.Touch(points[p + 1]);
                        var segment = new D2D1_QUADRATIC_BEZIER_SEGMENT { point1 = Interop.Point(points[p]), point2 = Interop.Point(points[p + 1]) };
                        sink->AddQuadraticBezier(&segment);
                        p += 2;
                        break;
                    }

                case PathVerb.CubicTo:
                    {
                        hasFigures |= Start(sink, ref figure);
                        figure.Touch(points[p]);
                        figure.Touch(points[p + 1]);
                        figure.Touch(points[p + 2]);
                        var segment = new D2D1_BEZIER_SEGMENT
                        {
                            point1 = Interop.Point(points[p]),
                            point2 = Interop.Point(points[p + 1]),
                            point3 = Interop.Point(points[p + 2]),
                        };
                        sink->AddBezier(&segment);
                        p += 3;
                        break;
                    }

                case PathVerb.Close when figure.IsOpen:
                    // A closed single point (m h) is a degenerate subpath too.
                    figure.HasSegments = true;
                    Finish(sink, ref figure, collectPixels);
                    break;
            }
        }

        Finish(sink, ref figure, collectPixels);
        return hasFigures;
    }

    private static bool Start(ID2D1GeometrySink* sink, ref FillFigure figure)
    {
        figure.HasSegments = true;
        if (figure.Begun)
        {
            return false;
        }

        sink->BeginFigure(Interop.Point(figure.First), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
        figure.Begun = true;
        return true;
    }

    private void Finish(ID2D1GeometrySink* sink, ref FillFigure figure, bool collectPixels)
    {
        if (!figure.IsOpen)
        {
            return;
        }

        if (figure.Begun)
        {
            sink->EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
        }

        if (collectPixels && figure.HasSegments && figure.IsDegenerate)
        {
            if (_pixelCount == _pixels.Length)
            {
                Array.Resize(ref _pixels, _pixelCount * 2);
            }

            _pixels[_pixelCount++] = figure.First;
        }

        figure.IsOpen = false;
    }

    /// <summary>Paints the device pixel under each degenerate subpath of the fill just drawn (§8.5.3.3.1).</summary>
    private void FillPixels()
    {
        if (_pixelCount == 0)
        {
            return;
        }

        SetWorldTransform(Matrix.Identity);
        for (int i = 0; i < _pixelCount; i++)
        {
            PathPoint device = _toDevice.Transform(_pixels[i]);
            if (!(double.IsFinite(device.X) && double.IsFinite(device.Y)) || Math.Abs(device.X) > int.MaxValue || Math.Abs(device.Y) > int.MaxValue)
            {
                continue;
            }

            float left = (float)Math.Floor(device.X);
            float top = (float)Math.Floor(device.Y);
            var pixel = new D2D_RECT_F { left = left, top = top, right = left + 1, bottom = top + 1 };
            _context->FillRectangle(&pixel, (ID2D1Brush*)_brush);
        }

        _pixelCount = 0;
    }

    private void PushRectangle(D2D_RECT_F rectangle)
    {
        SetWorldTransform(Matrix.Identity);
        _context->PushAxisAlignedClip(&rectangle, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        AddClip(new Clip(null));
    }

    private void AddClip(Clip clip)
    {
        if (_clipCount == _clips.Length)
        {
            Array.Resize(ref _clips, _clipCount * 2);
        }

        _clips[_clipCount++] = clip;
    }

    private void PopClips(int count)
    {
        while (_clipCount > count)
        {
            Clip clip = _clips[--_clipCount];
            if (!clip.IsLayer)
            {
                _context->PopAxisAlignedClip();
            }
            else
            {
                _context->PopLayer();
                if (clip.Mask is not null)
                {
                    clip.Mask->Release();
                }
            }

            _clips[_clipCount] = default;
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is one rectangle (four points, as <c>re</c> makes it, optionally back to its start and closed)
    /// whose sides map to device lines along the pixel axes, and its device rectangle if so; the software rasterizer's test.
    /// </summary>
    private bool TryMapRectangle(PathView path, out D2D_RECT_F rectangle)
    {
        rectangle = default;
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        int segments = verbs.Length > 0 && verbs[^1] == PathVerb.Close ? verbs.Length - 1 : verbs.Length;
        if (segments is not (4 or 5) || verbs[0] != PathVerb.MoveTo || points.Length != segments)
        {
            return false;
        }

        for (int index = 1; index < segments; index++)
        {
            if (verbs[index] != PathVerb.LineTo)
            {
                return false;
            }
        }

        if (segments == 5 && points[4] != points[0])
        {
            return false;
        }

        PathPoint a = _toDevice.Transform(points[0]);
        PathPoint b = _toDevice.Transform(points[1]);
        PathPoint c = _toDevice.Transform(points[2]);
        PathPoint d = _toDevice.Transform(points[3]);
        bool horizontalFirst = a.Y == b.Y && b.X == c.X && c.Y == d.Y && d.X == a.X;
        bool verticalFirst = a.X == b.X && b.Y == c.Y && c.X == d.X && d.Y == a.Y;
        if (!(horizontalFirst || verticalFirst) || !double.IsFinite(a.X + a.Y + c.X + c.Y) || (a == b && b == c))
        {
            return false;
        }

        rectangle = new D2D_RECT_F
        {
            left = Interop.Point(Math.Min(a.X, c.X), 0).x,
            right = Interop.Point(Math.Max(a.X, c.X), 0).x,
            top = Interop.Point(0, Math.Min(a.Y, c.Y)).y,
            bottom = Interop.Point(0, Math.Max(a.Y, c.Y)).y,
        };
        return true;
    }

    /// <summary>A clip pushed on the device context: the geometric mask of its layer, or none for an axis-aligned clip.</summary>
    private readonly struct Clip(ID2D1Geometry* mask, bool isLayer = false)
    {
        public ID2D1Geometry* Mask { get; } = mask;

        /// <summary>Gets a value indicating whether the clip is a layer (with a geometric mask, or an annotation's opacity layer).</summary>
        public bool IsLayer { get; } = isLayer || mask is not null;
    }

    /// <summary>The subpath being written for a fill: its first point, whether a figure has begun, and whether it is degenerate.</summary>
    private struct FillFigure(PathPoint first)
    {
        public PathPoint First { get; } = first;

        public bool IsOpen { get; set; } = true;

        public bool Begun { get; set; }

        public bool HasSegments { get; set; }

        public bool IsDegenerate { get; private set; } = true;

        public void Touch(PathPoint point) => IsDegenerate &= point == First;
    }
}
