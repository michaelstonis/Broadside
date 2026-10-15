using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Graphics;
using AndroidBlendMode = Android.Graphics.BlendMode;
using AndroidCanvas = Android.Graphics.Canvas;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;
using Matrix = Broadside.Graphics.Matrix;

namespace Broadside.Rendering.Android;

/// <summary>Constant alpha, blend modes and annotations (ISO 32000-2 §11.3.6, §11.6.3, §11.6.4.4, §12.5.5) on the Android canvas.</summary>
public sealed partial class AndroidCanvasBackend
{
    /// <summary>Gets what the last render could not draw as the file asks: one diagnostic per kind.</summary>
    /// <remarks>ADR 0005: nothing is approximated silently. Android before 10 (API level 29) has no blend modes for paints and layers.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics => _backend.Diagnostics;

    private sealed partial class Backend
    {
        private static readonly Diagnostic BlendModeUnsupported = new(
            "BackendBlendModeUnsupported",
            DiagnosticSeverity.Warning,
            "This Android version (before 10, API level 29) has no blend modes for the canvas; the page's paints that blend other than Normal (ISO 32000-2 §11.3.5) were composited as Normal.");

        private static AndroidBlendMode?[]? s_blendModes;

        private readonly Paint _layerPaint = new(PaintFlags.AntiAlias);
        private Compositing _compositing;
        private BlendMode _fillBlendMode;
        private BlendMode _strokeBlendMode;
        private BlendMode _layerBlendMode;
        private AnnotationFrame[] _annotations = new AnnotationFrame[2];
        private int _annotationDepth;
        private bool _blendReported;

        /// <summary>Gets the diagnostics of the current or last render.</summary>
        public List<Diagnostic> Diagnostics { get; } = new(2);

        /// <summary>Takes the overprint parameters and halftone of the paints that follow, and ignores them (§8.6.7, §10.6).</summary>
        /// <remarks>
        /// A composite RGB device that does not overprint ignores the overprint parameters (§8.6.7), and a continuous-tone device needs no
        /// halftone (§10.6.1). Transfer functions come with each colour's conversion (§10.5).
        /// </remarks>
        public void SetDeviceParameters(in DisplayDeviceParameters parameters)
        {
        }

        /// <summary>
        /// Sets the alpha constants and blend mode of the paints that follow (§11.6.4.4, §11.6.3): the alphas go into the paints' colours
        /// (strokes take <paramref name="strokeAlpha"/>, everything else <paramref name="fillAlpha"/>), the blend mode into the paints.
        /// </summary>
        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode)
        {
            _compositing = new Compositing(Alpha(strokeAlpha), Alpha(fillAlpha), blendMode);
            ReportBlendMode(blendMode);
        }

        /// <summary>
        /// Begins an annotation (§12.5.5): saves the state, places the appearance as the software rasterizer does (NoZoom and NoRotate
        /// keep the image of the rectangle's upper-left corner, §12.5.3), clips to the whole pixels the rectangle touches and, when it
        /// is not opaque or blends other than Normal, opens a canvas layer that <see cref="EndAnnotation"/> composites with its opacity
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
            frame = new AnnotationFrame(_device, -1);
            _device = Place(annotation, _device);
            SetTransform(in _ctm);

            PdfRectangle rect = annotation.Rectangle;
            (double ax, double ay) = Map(_device, rect.Left, rect.Bottom);
            (double bx, double by) = Map(_device, rect.Right, rect.Bottom);
            (double cx, double cy) = Map(_device, rect.Right, rect.Top);
            (double dx, double dy) = Map(_device, rect.Left, rect.Top);
            double left = Math.Floor(Math.Min(Math.Min(ax, bx), Math.Min(cx, dx))) - 1;
            double right = Math.Ceiling(Math.Max(Math.Max(ax, bx), Math.Max(cx, dx))) + 1;
            double top = Math.Floor(Math.Min(Math.Min(ay, by), Math.Min(cy, dy))) - 1;
            double bottom = Math.Ceiling(Math.Max(Math.Max(ay, by), Math.Max(cy, dy))) + 1;
            if (!double.IsFinite(left + right + top + bottom) || annotation.Opacity <= 0)
            {
                left = right = top = bottom = 0;
            }

            left = Math.Clamp(left, -CoordinateLimit, CoordinateLimit);
            right = Math.Clamp(right, -CoordinateLimit, CoordinateLimit);
            top = Math.Clamp(top, -CoordinateLimit, CoordinateLimit);
            bottom = Math.Clamp(bottom, -CoordinateLimit, CoordinateLimit);
            AndroidCanvas canvas = _canvas!;
            canvas.ClipRect((float)left, (float)top, (float)right, (float)bottom);
            if (annotation.Opacity >= 1 && annotation.BlendMode == BlendMode.Normal)
            {
                return;
            }

            // §12.5.5, §11.6.4.4 and §11.6.3: the appearance is a group composited with the annotation's alpha and blend mode.
            _layerPaint.Alpha = Alpha(annotation.Opacity);
            ReportBlendMode(annotation.BlendMode);
            SetBlendMode(_layerPaint, ref _layerBlendMode, annotation.BlendMode);
            frame = frame with { LayerCount = canvas.SaveLayer((float)left, (float)top, (float)right, (float)bottom, _layerPaint) };
        }

        /// <summary>Ends the annotation: composites its layer, if any, and restores the device mapping and the state.</summary>
        public void EndAnnotation()
        {
            if (_annotationDepth == 0)
            {
                return;
            }

            AnnotationFrame frame = _annotations[--_annotationDepth];
            if (frame.LayerCount >= 0)
            {
                _canvas!.RestoreToCount(frame.LayerCount);
            }

            _device = frame.Device;
            RestoreState();
        }

        /// <summary>Resets the compositing state and the diagnostics at the start of a render.</summary>
        private void BeginCompositing()
        {
            _compositing = Compositing.Initial;
            _annotationDepth = 0;
            _blendReported = false;
            _tilingReported = false;
            _groupReported = false;
            _softMaskReported = false;
            Diagnostics.Clear();
        }

        /// <summary>Gives <paramref name="paint"/> the blend mode of the current compositing state, when it does not have it already.</summary>
        private void SetBlendMode(Paint paint, ref BlendMode current) => SetBlendMode(paint, ref current, _compositing.BlendMode);

        private static void SetBlendMode(Paint paint, ref BlendMode current, BlendMode mode)
        {
            if (current == mode || !OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                return;
            }

            current = mode;
            paint.BlendMode = NativeBlendMode(mode);
        }

        private void ReportBlendMode(BlendMode mode)
        {
            if (mode != BlendMode.Normal && !_blendReported && !OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                _blendReported = true;
                Diagnostics.Add(BlendModeUnsupported);
            }
        }

        /// <summary>
        /// The Android blend mode of a PDF blend mode (§11.3.5 Tables 134 and 135; Android's are the same separable and non-separable
        /// functions), <see langword="null"/> for Normal (source-over). Read once: each read of Android's constants goes through JNI.
        /// </summary>
        [SupportedOSPlatform("android29.0")]
        private static AndroidBlendMode? NativeBlendMode(BlendMode mode)
        {
            AndroidBlendMode?[] modes = s_blendModes ??=
            [
                null,
                AndroidBlendMode.Multiply,
                AndroidBlendMode.Screen,
                AndroidBlendMode.Overlay,
                AndroidBlendMode.Darken,
                AndroidBlendMode.Lighten,
                AndroidBlendMode.ColorDodge,
                AndroidBlendMode.ColorBurn,
                AndroidBlendMode.HardLight,
                AndroidBlendMode.SoftLight,
                AndroidBlendMode.Difference,
                AndroidBlendMode.Exclusion,
                AndroidBlendMode.Hue,
                AndroidBlendMode.Saturation,
                AndroidBlendMode.Color,
                AndroidBlendMode.Luminosity,
            ];
            return (uint)mode < (uint)modes.Length ? modes[(int)mode] : null;
        }

        /// <summary>An alpha constant as the 8-bit alpha the rasterizer's compositor uses (NaN is opaque).</summary>
        private static byte Alpha(double alpha) => (byte)(int)(((double.IsNaN(alpha) ? 1 : Math.Clamp(alpha, 0, 1)) * 255) + 0.5);

        /// <summary>
        /// The device mapping of an annotation, as the software rasterizer computes it: the current one, or for <c>NoZoom</c> and
        /// <c>NoRotate</c> (§12.5.3) one that keeps the image of the rectangle's upper-left corner and drops the scale (to one device
        /// pixel per unit) or the rotation (to upright).
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
            (double anchorX, double anchorY) = Map(device, x, y);
            return new Matrix(a, b, c, d, anchorX - ((a * x) + (c * y)), anchorY - ((b * x) + (d * y)));
        }

        private static (double X, double Y) Map(in Matrix m, double x, double y) =>
            ((m.A * x) + (m.C * y) + m.E, (m.B * x) + (m.D * y) + m.F);

        /// <summary>The alpha constants (8-bit) and blend mode of the paints, saved and restored with the graphics state.</summary>
        private readonly record struct Compositing(byte StrokeAlpha, byte FillAlpha, BlendMode BlendMode)
        {
            public static Compositing Initial { get; } = new(255, 255, BlendMode.Normal);
        }

        /// <summary>What <see cref="SaveState"/> keeps: the transform and the compositing state (the canvas keeps the clip).</summary>
        private readonly record struct SavedState(Matrix Ctm, Matrix ToDevice, Compositing Compositing);

        /// <summary>What an annotation changed at its start: the device mapping to restore and the canvas save count of its layer, or -1.</summary>
        private readonly record struct AnnotationFrame(Matrix Device, int LayerCount);
    }
}
