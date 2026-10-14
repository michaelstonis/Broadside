using Broadside.Diagnostics;
using Broadside.Graphics;
using CoreGraphics;

namespace Broadside.Rendering.CoreGraphics;

/// <summary>Constant alpha, blend modes and annotations (ISO 32000-2 §11.3.5, §11.6.3, §11.6.4.4, §12.5.5) on a CoreGraphics context.</summary>
internal sealed partial class CoreGraphicsBackend
{
    private static readonly Diagnostic BlendModeApproximated = new(
        "BackendBlendModeApproximated",
        DiagnosticSeverity.Warning,
        "CoreGraphics' ColorDodge, ColorBurn and SoftLight blend modes are not the functions of ISO 32000-2 §11.3.5 Table 134; the page's paints in those modes were blended by CoreGraphics' own.");

    private Compositing _compositing;
    private bool _approximationReported;
    private AnnotationFrame[] _annotations = new AnnotationFrame[2];
    private int _annotationDepth;

    /// <summary>
    /// Sets the alpha constants and blend mode of the paints that follow (§11.6.4.4, §11.6.3): the alphas go into the paint colours
    /// (strokes take <paramref name="strokeAlpha"/>, everything else <paramref name="fillAlpha"/>), the blend mode into the context's
    /// graphics state, which <see cref="RestoreState"/> restores with the rest of it.
    /// </summary>
    /// <remarks>
    /// CoreGraphics' ColorDodge, ColorBurn and SoftLight differ from Table 134's functions (measured against the software rasterizer,
    /// which implements them as written); they are kept as the nearest native blend and reported once per render.
    /// </remarks>
    public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
    {
        _compositing = new Compositing(Alpha(strokeAlpha), Alpha(fillAlpha), blendMode);
        _context!.SetBlendMode(NativeBlendMode(blendMode));
    }

    /// <summary>Gets the diagnostics of the current or last replay.</summary>
    public List<Diagnostic> Diagnostics { get; } = new(1);

    /// <summary>
    /// Begins an annotation (§12.5.5): saves the state, places the appearance as the software rasterizer does (NoZoom and NoRotate keep
    /// the image of the rectangle's upper-left corner, §12.5.3), clips to the whole device units the rectangle touches and, when it is
    /// not opaque or blends other than Normal, begins a transparency layer that <see cref="EndAnnotation"/> composites with its opacity
    /// and blend mode.
    /// </summary>
    public void BeginAnnotation(in DisplayAnnotation annotation)
    {
        SaveState();
        if (_annotationDepth == _annotations.Length)
        {
            Array.Resize(ref _annotations, _annotations.Length * 2);
        }

        ref AnnotationFrame frame = ref _annotations[_annotationDepth++];
        frame = new AnnotationFrame(_device, _depth, Layered: false);
        _device = Place(annotation, _device);
        SetTransform(in _ctm);

        PdfRectangle rect = annotation.Rectangle;
        PathPoint a = Map(_device, new PathPoint(rect.Left, rect.Bottom));
        PathPoint b = Map(_device, new PathPoint(rect.Right, rect.Bottom));
        PathPoint c = Map(_device, new PathPoint(rect.Right, rect.Top));
        PathPoint d = Map(_device, new PathPoint(rect.Left, rect.Top));
        double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X))) - 1;
        double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) + 1;
        double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y))) - 1;
        double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) + 1;
        CGRect bounds = annotation.Opacity > 0 ? new CGRect(left, top, right - left, bottom - top) : CGRect.Empty;
        CGContext context = _context!;
        context.ClipToRect(bounds);
        if ((annotation.Opacity >= 1 && annotation.BlendMode == BlendMode.Normal) || !(annotation.Opacity > 0))
        {
            return;
        }

        // §12.5.5, §11.6.4.4 and §11.6.3: the appearance is a group composited with the annotation's alpha and blend mode. The layer
        // starts at alpha 1 and Normal, so the paints' own blend mode is set again inside it.
        context.SetAlpha((nfloat)(Alpha(annotation.Opacity) / 255.0));
        context.SetBlendMode(NativeBlendMode(annotation.BlendMode));
        context.BeginTransparencyLayer(bounds, null);
        context.SetBlendMode(NativeBlendMode(_compositing.BlendMode));
        frame = frame with { Layered = true };
    }

    /// <summary>Ends the annotation: composites its transparency layer, if any, and restores the device mapping and the state.</summary>
    public void EndAnnotation()
    {
        if (_annotationDepth == 0)
        {
            return;
        }

        AnnotationFrame frame = _annotations[--_annotationDepth];
        while (_depth > frame.SaveDepth)
        {
            RestoreState();
        }

        if (frame.Layered)
        {
            _context!.EndTransparencyLayer();
        }

        _device = frame.Device;
        RestoreState();
    }

    /// <summary>Resets the compositing state and the annotations at the start of a replay.</summary>
    private void BeginCompositing()
    {
        _compositing = Compositing.Initial;
        _annotationDepth = 0;
        _approximationReported = false;
        _tilingReported = false;
        Diagnostics.Clear();
    }

    /// <summary>Ends the transparency layer an annotation opened at save depth <paramref name="depth"/>, when a replay is unwound early.</summary>
    private void EndLayerAt(int depth)
    {
        while (_annotationDepth > 0 && _annotations[_annotationDepth - 1].SaveDepth >= depth)
        {
            if (_annotations[--_annotationDepth].Layered)
            {
                _context!.EndTransparencyLayer();
            }
        }
    }

    /// <summary>The CoreGraphics blend mode of a PDF blend mode (§11.3.5): CoreGraphics has all sixteen, three of them with other functions.</summary>
    private CGBlendMode NativeBlendMode(BlendMode mode)
    {
        if (mode is BlendMode.ColorDodge or BlendMode.ColorBurn or BlendMode.SoftLight && !_approximationReported)
        {
            _approximationReported = true;
            Diagnostics.Add(BlendModeApproximated);
        }

        return NativeBlendModeOf(mode);
    }

    private static CGBlendMode NativeBlendModeOf(BlendMode mode) => mode switch
    {
        BlendMode.Multiply => CGBlendMode.Multiply,
        BlendMode.Screen => CGBlendMode.Screen,
        BlendMode.Overlay => CGBlendMode.Overlay,
        BlendMode.Darken => CGBlendMode.Darken,
        BlendMode.Lighten => CGBlendMode.Lighten,
        BlendMode.ColorDodge => CGBlendMode.ColorDodge,
        BlendMode.ColorBurn => CGBlendMode.ColorBurn,
        BlendMode.HardLight => CGBlendMode.HardLight,
        BlendMode.SoftLight => CGBlendMode.SoftLight,
        BlendMode.Difference => CGBlendMode.Difference,
        BlendMode.Exclusion => CGBlendMode.Exclusion,
        BlendMode.Hue => CGBlendMode.Hue,
        BlendMode.Saturation => CGBlendMode.Saturation,
        BlendMode.Color => CGBlendMode.Color,
        BlendMode.Luminosity => CGBlendMode.Luminosity,
        _ => CGBlendMode.Normal,
    };

    /// <summary>An alpha constant as the 8-bit alpha the software rasterizer's compositor uses (NaN is opaque).</summary>
    private static byte Alpha(double alpha) => (byte)(int)(((double.IsNaN(alpha) ? 1 : Math.Clamp(alpha, 0, 1)) * 255) + 0.5);

    /// <summary>
    /// The device mapping of an annotation, as the software rasterizer computes it: the current one, or for <c>NoZoom</c> and
    /// <c>NoRotate</c> (§12.5.3) one that keeps the image of the rectangle's upper-left corner and drops the scale (to one device unit
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

    /// <summary>The alpha constants (8-bit) and blend mode of the paints, saved and restored with the graphics state.</summary>
    private readonly record struct Compositing(byte StrokeAlpha, byte FillAlpha, BlendMode BlendMode)
    {
        public static Compositing Initial { get; } = new(255, 255, BlendMode.Normal);
    }

    /// <summary>What <see cref="SaveState"/> keeps beside the context's graphics state: the transform and the compositing state.</summary>
    private readonly record struct SavedState(Matrix Ctm, Matrix ToDevice, Compositing Compositing);

    /// <summary>What an annotation changed at its start: the device mapping to restore, the save depth it began at and whether it opened a layer.</summary>
    private readonly record struct AnnotationFrame(Matrix Device, int SaveDepth, bool Layered);
}
