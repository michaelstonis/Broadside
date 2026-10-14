using Broadside.Diagnostics;
using Broadside.Graphics;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;

namespace Broadside.Rendering.Direct2D;

/// <summary>Constant alpha, blend modes, annotations and tiling patterns (ISO 32000-2 §11.3.5, §11.6.4.4, §12.5.5, §8.7.3) on Direct2D.</summary>
internal sealed unsafe partial class Direct2DBackend
{
    private static readonly Diagnostic BlendModeUnsupported = new(
        "BackendBlendModeUnsupported",
        DiagnosticSeverity.Warning,
        "The Direct2D backend does not blend other than Normal yet (ISO 32000-2 §11.3.5; Direct2D's primitive blend is source-over only); the page's paints and annotations in other blend modes were composited as Normal.");

    private static readonly Diagnostic TilingPatternsSkipped = Skipped("tiling patterns", 130);

    private Compositing _compositing = Compositing.Initial;
    private Compositing[] _savedCompositing = new Compositing[16];
    private Matrix[] _annotationDevices = new Matrix[2];
    private int _annotationDepth;

    /// <summary>
    /// Sets the alpha constants and blend mode of the paints that follow (§11.6.4.4, §11.6.3): the alphas go into the brush colour
    /// (strokes take <paramref name="strokeAlpha"/>, everything else <paramref name="fillAlpha"/>). Direct2D's primitive blend is
    /// source-over only, so a blend mode other than Normal composites as Normal and is reported once per render.
    /// </summary>
    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
    {
        _compositing = new Compositing(Alpha(strokeAlpha), Alpha(fillAlpha));
        ReportBlendMode(blendMode);
    }

    /// <summary>
    /// Begins an annotation (§12.5.5): saves the state, places the appearance as the software rasterizer does (NoZoom and NoRotate keep
    /// the image of the rectangle's upper-left corner, §12.5.3), clips to the whole pixels the rectangle touches and, when it is not
    /// opaque, pushes a layer with its opacity, which <see cref="EndAnnotation"/> pops with the rest of the state.
    /// </summary>
    public void BeginAnnotation(in DisplayAnnotation annotation)
    {
        SaveState();
        if (_annotationDepth == _annotationDevices.Length)
        {
            Array.Resize(ref _annotationDevices, _annotationDevices.Length * 2);
        }

        _annotationDevices[_annotationDepth++] = _device;
        _device = Place(annotation, _device);
        SetTransform(in _ctm);

        PdfRectangle rect = annotation.Rectangle;
        PathPoint a = _device.Transform(new PathPoint(rect.Left, rect.Bottom));
        PathPoint b = _device.Transform(new PathPoint(rect.Right, rect.Bottom));
        PathPoint c = _device.Transform(new PathPoint(rect.Right, rect.Top));
        PathPoint d = _device.Transform(new PathPoint(rect.Left, rect.Top));
        double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X))) - 1;
        double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) + 1;
        double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y))) - 1;
        double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) + 1;
        D2D_RECT_F bounds = default;
        if (double.IsFinite(left + right + top + bottom) && annotation.Opacity > 0)
        {
            bounds = new D2D_RECT_F
            {
                left = Interop.Point(left, 0).x,
                right = Interop.Point(right, 0).x,
                top = Interop.Point(0, top).y,
                bottom = Interop.Point(0, bottom).y,
            };
        }

        PushRectangle(bounds);
        if (bounds.right <= bounds.left)
        {
            return;
        }

        ReportBlendMode(annotation.BlendMode);
        byte opacity = Alpha(annotation.Opacity);
        if (opacity == 255)
        {
            return;
        }

        // §12.5.5 and §11.6.4.4: the appearance is a group composited with the annotation's alpha.
        var parameters = new D2D1_LAYER_PARAMETERS1
        {
            contentBounds = bounds,
            geometricMask = null,
            maskAntialiasMode = D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            maskTransform = Interop.Matrix(Matrix.Identity),
            opacity = opacity / 255f,
            opacityBrush = null,
            layerOptions = D2D1_LAYER_OPTIONS1.D2D1_LAYER_OPTIONS1_NONE,
        };
        SetWorldTransform(Matrix.Identity);
        _context->PushLayer(&parameters, null);
        AddClip(new Clip(null, isLayer: true));
    }

    /// <summary>Ends the annotation: pops its layer and clip with the state and restores the device mapping.</summary>
    public void EndAnnotation()
    {
        if (_annotationDepth == 0)
        {
            return;
        }

        _device = _annotationDevices[--_annotationDepth];
        RestoreState();
    }

    /// <summary>Skips a fill with a tiling pattern (§8.7.3.1), reported once per render.</summary>
    public void FillPathWithTilingPattern(PathView path, FillRule rule, in DisplayTilingPattern pattern) =>
        Skip(SkippedOperations.TilingPatterns, TilingPatternsSkipped);

    /// <summary>Skips a stroke with a tiling pattern (§8.7.3.1), reported once per render.</summary>
    public void StrokePathWithTilingPattern(PathView path, in StrokeStyle style, in DisplayTilingPattern pattern) =>
        Skip(SkippedOperations.TilingPatterns, TilingPatternsSkipped);

    /// <summary>Resets the compositing state and the annotations at the start of a replay.</summary>
    private void BeginCompositing()
    {
        _compositing = Compositing.Initial;
        _annotationDepth = 0;
    }

    private void ReportBlendMode(BlendMode mode)
    {
        if (mode != BlendMode.Normal)
        {
            Skip(SkippedOperations.BlendModes, BlendModeUnsupported);
        }
    }

    /// <summary>An alpha constant as the 8-bit alpha the software rasterizer's compositor uses (NaN is opaque).</summary>
    private static byte Alpha(double alpha) => (byte)(int)(((double.IsNaN(alpha) ? 1 : Math.Clamp(alpha, 0, 1)) * 255) + 0.5);

    /// <summary>
    /// The device mapping of an annotation, as the software rasterizer computes it: the current one, or for <c>NoZoom</c> and
    /// <c>NoRotate</c> (§12.5.3) one that keeps the image of the rectangle's upper-left corner and drops the scale (to one device pixel
    /// per unit) or the rotation (to upright).
    /// </summary>
    private static Matrix Place(in DisplayAnnotation annotation, in Matrix device)
    {
        if (!annotation.NoZoom && !annotation.NoRotate)
        {
            return device;
        }

        double determinant = (device.A * device.D) - (device.B * device.C);
        double scale = Math.Sqrt(Math.Abs(determinant));
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            return device;
        }

        double a;
        double b;
        double c;
        double d;
        if (annotation.NoRotate)
        {
            double size = annotation.NoZoom ? 1 : scale;
            (a, b, c, d) = (size, 0, 0, determinant < 0 ? -size : size);
        }
        else
        {
            (a, b, c, d) = (device.A / scale, device.B / scale, device.C / scale, device.D / scale);
        }

        double x = annotation.Rectangle.Left;
        double y = annotation.Rectangle.Top;
        double anchorX = (device.A * x) + (device.C * y) + device.E;
        double anchorY = (device.B * x) + (device.D * y) + device.F;
        return new Matrix(a, b, c, d, anchorX - ((a * x) + (c * y)), anchorY - ((b * x) + (d * y)));
    }

    /// <summary>The alpha constants (8-bit) of the paints, saved and restored with the graphics state.</summary>
    private readonly record struct Compositing(byte StrokeAlpha, byte FillAlpha)
    {
        public static Compositing Initial { get; } = new(255, 255);
    }
}
