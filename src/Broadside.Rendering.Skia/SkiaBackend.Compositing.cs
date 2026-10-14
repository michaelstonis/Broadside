using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>Constant alpha, blend modes and annotations (ISO 32000-2 §11.3.5, §11.6.3, §11.6.4.4, §12.5.5) on the Skia canvas.</summary>
    private sealed partial class Replayer
    {
        private readonly SKPaint _layer = new() { IsAntialias = true };
        private Compositing _compositing = Compositing.Initial;
        private AnnotationFrame[] _annotations = new AnnotationFrame[2];
        private int _annotationDepth;

        /// <summary>
        /// Sets the alpha constants and blend mode of the paints that follow (§11.6.4.4, §11.6.3): the alphas go into the paints' colours
        /// (strokes take <paramref name="strokeAlpha"/>, everything else <paramref name="fillAlpha"/>), the blend mode into the paints.
        /// Skia's sixteen separable and non-separable blend modes are the functions of §11.3.5, so every mode is native.
        /// </summary>
        public void SetCompositing(double strokeAlpha, double fillAlpha, BlendMode blendMode) =>
            _compositing = new Compositing(Alpha(strokeAlpha), Alpha(fillAlpha), NativeBlendMode(blendMode));

        /// <summary>
        /// Begins an annotation (§12.5.5): saves the state, places the appearance as the software rasterizer does (NoZoom and NoRotate
        /// keep the image of the rectangle's upper-left corner, §12.5.3), clips to the whole pixels the rectangle touches and, when it
        /// is not opaque or blends other than Normal, opens a layer that <see cref="EndAnnotation"/> composites with its opacity and
        /// blend mode.
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
            PathPoint a = _device.Transform(new PathPoint(rect.Left, rect.Bottom));
            PathPoint b = _device.Transform(new PathPoint(rect.Right, rect.Bottom));
            PathPoint c = _device.Transform(new PathPoint(rect.Right, rect.Top));
            PathPoint d = _device.Transform(new PathPoint(rect.Left, rect.Top));
            double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X))) - 1;
            double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) + 1;
            double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y))) - 1;
            double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) + 1;
            SKRect bounds = double.IsFinite(left + right + top + bottom) && annotation.Opacity > 0
                ? new SKRect(Clamp(left), Clamp(top), Clamp(right), Clamp(bottom))
                : SKRect.Empty;
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            canvas.ClipRect(bounds, SKClipOperation.Intersect, antialias: false);
            if ((annotation.Opacity >= 1 && annotation.BlendMode == BlendMode.Normal) || bounds.IsEmpty)
            {
                return;
            }

            // §12.5.5, §11.6.4.4 and §11.6.3: the appearance is a group composited with the annotation's alpha and blend mode.
            _layer.Color = SKColors.Black.WithAlpha(Alpha(annotation.Opacity));
            _layer.BlendMode = NativeBlendMode(annotation.BlendMode);
            frame = frame with { LayerCount = canvas.SaveLayer(bounds, _layer) };
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

        /// <summary>Resets the compositing state and the annotations at the start of a render.</summary>
        private void BeginCompositing()
        {
            _compositing = Compositing.Initial;
            _annotationDepth = 0;
        }

        private void DisposeCompositing() => _layer.Dispose();

        /// <summary>The Skia blend mode of a PDF blend mode (§11.3.5 Tables 134 and 135).</summary>
        private static SKBlendMode NativeBlendMode(BlendMode mode) => mode switch
        {
            BlendMode.Multiply => SKBlendMode.Multiply,
            BlendMode.Screen => SKBlendMode.Screen,
            BlendMode.Overlay => SKBlendMode.Overlay,
            BlendMode.Darken => SKBlendMode.Darken,
            BlendMode.Lighten => SKBlendMode.Lighten,
            BlendMode.ColorDodge => SKBlendMode.ColorDodge,
            BlendMode.ColorBurn => SKBlendMode.ColorBurn,
            BlendMode.HardLight => SKBlendMode.HardLight,
            BlendMode.SoftLight => SKBlendMode.SoftLight,
            BlendMode.Difference => SKBlendMode.Difference,
            BlendMode.Exclusion => SKBlendMode.Exclusion,
            BlendMode.Hue => SKBlendMode.Hue,
            BlendMode.Saturation => SKBlendMode.Saturation,
            BlendMode.Color => SKBlendMode.Color,
            BlendMode.Luminosity => SKBlendMode.Luminosity,
            _ => SKBlendMode.SrcOver,
        };

        /// <summary>An alpha constant as the 8-bit alpha the software rasterizer's compositor uses (NaN is opaque).</summary>
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
            double anchorX = (device.A * x) + (device.C * y) + device.E;
            double anchorY = (device.B * x) + (device.D * y) + device.F;
            return new Matrix(a, b, c, d, anchorX - ((a * x) + (c * y)), anchorY - ((b * x) + (d * y)));
        }

        /// <summary>The alpha constants (8-bit) and Skia blend mode of the paints, saved and restored with the graphics state.</summary>
        private readonly record struct Compositing(byte StrokeAlpha, byte FillAlpha, SKBlendMode BlendMode)
        {
            public static Compositing Initial { get; } = new(255, 255, SKBlendMode.SrcOver);
        }

        /// <summary>What <see cref="SaveState"/> keeps beside the canvas's own state: the transform and the compositing state.</summary>
        private readonly record struct SavedState(Matrix Ctm, Compositing Compositing);

        /// <summary>What an annotation changed at its start: the device mapping to restore and the canvas save count of its layer, or -1.</summary>
        private readonly record struct AnnotationFrame(Matrix Device, int LayerCount);
    }
}
