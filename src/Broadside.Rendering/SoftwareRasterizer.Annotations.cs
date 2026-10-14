using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Broadside.Graphics;

// The target is the caller's bitmap or, inside a translucent annotation, the backend's layer (disposed through _layer).
[assembly: SuppressMessage("Reliability", "CA2213:Disposable fields should be disposed", Justification = "Borrowed: the caller's bitmap or the annotation layer, which _layer owns.", Scope = "member", Target = "~F:Broadside.Rendering.SoftwareRasterizer.Backend._target")]

namespace Broadside.Rendering;

/// <summary>Annotations in the software rasterizer: NoZoom and NoRotate placement, and the appearance group's opacity and blend mode.</summary>
public sealed partial class SoftwareRasterizer
{
    private sealed partial class Backend
    {
        private AnnotationFrame[] _annotationFrames = new AnnotationFrame[2];
        private int _annotationDepth;
        private Bitmap? _layer;

        /// <summary>
        /// Begins an annotation (ISO 32000-2 §12.5.5): saves the state; for <c>NoZoom</c> or <c>NoRotate</c> (§12.5.3, Figure 78) maps
        /// default user space to the device about the rectangle's upper-left corner without the device transform's scale or
        /// rotation; clips to the whole pixels the rectangle touches; and, when the annotation is not opaque or blends other than
        /// Normal, paints into a transparent layer that <see cref="EndAnnotation"/> composites with its opacity and blend mode.
        /// </summary>
        public void BeginAnnotation(in DisplayAnnotation annotation)
        {
            // An overprint preview covers the page content; annotations are drawn over its result (RenderOptions.OverprintPreview).
            FinishPreview();
            SaveState();
            if (_annotationDepth == _annotationFrames.Length)
            {
                Array.Resize(ref _annotationFrames, _annotationFrames.Length * 2);
            }

            ref AnnotationFrame frame = ref _annotationFrames[_annotationDepth++];
            frame = new AnnotationFrame(_device, _target!, default, Layered: false, 0, 0, 0, 0);
            _device = Place(annotation, _device);
            SetTransform(in _ctm);

            // The appearance is drawn inside its rectangle (§12.5.5); whole pixels keep the clip from touching its own edges.
            PdfRectangle rect = annotation.Rectangle;
            Device a = MapDevice(rect.Left, rect.Bottom);
            Device b = MapDevice(rect.Right, rect.Bottom);
            Device c = MapDevice(rect.Right, rect.Top);
            Device d = MapDevice(rect.Left, rect.Top);
            double left = Math.Floor(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X))) - 1;
            double right = Math.Ceiling(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X))) + 1;
            double top = Math.Floor(Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y))) - 1;
            double bottom = Math.Ceiling(Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y))) + 1;
            if (!double.IsFinite(left + right + top + bottom) || annotation.Opacity <= 0)
            {
                left = right = top = bottom = 0;
            }

            _clip.IntersectRectangle(left, top, right, bottom);
            if ((annotation.Opacity >= 1 && annotation.BlendMode == BlendMode.Normal) || _clip.IsEmpty)
            {
                return;
            }

            // §12.5.5, §11.6.4.4 and §11.6.3: the appearance is a group composited with the annotation's nonstroking alpha and
            // blend mode, through the same Compositor every paint goes through (§11.3.6).
            _clip.GetBounds(out int x0, out int y0, out int x1, out int y1);
            Bitmap layer = Layer();
            for (int y = y0; y < y1; y++)
            {
                layer.GetRow(y).Slice(x0 * 4, (x1 - x0) * 4).Clear();
            }

            frame = frame with { Compositor = new Compositor(annotation.Opacity, annotation.BlendMode), Layered = true, Left = x0, Top = y0, Right = x1, Bottom = y1 };
            _target = layer;
        }

        /// <summary>Ends the annotation: composites its layer, if any, and restores the device mapping and the state.</summary>
        public void EndAnnotation()
        {
            if (_annotationDepth == 0)
            {
                return;
            }

            AnnotationFrame frame = _annotationFrames[--_annotationDepth];
            if (frame.Layered)
            {
                Composite(_target!, frame.Target, frame);
            }

            _target = frame.Target;
            _device = frame.Device;
            RestoreState();
        }

        /// <summary>Forgets annotation frames at the start of a render.</summary>
        private void BeginAnnotations()
        {
            _annotationDepth = 0;
            Array.Clear(_annotationFrames);
        }

        private void DisposeAnnotations()
        {
            _layer?.Dispose();
            _layer = null;
        }

        /// <summary>
        /// Returns the device mapping for an annotation: the current one, or for <c>NoZoom</c> and <c>NoRotate</c> one that keeps the
        /// image of the rectangle's upper-left corner and drops the scale (to one device pixel per unit) or the rotation (to upright).
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
                // Upright as on the unrotated page: x to the right, y up the page (down the device when the device flips y).
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

        private Device MapDevice(double x, double y) =>
            new((_device.A * x) + (_device.C * y) + _device.E, (_device.B * x) + (_device.D * y) + _device.F);

        /// <summary>Returns the layer bitmap, the size of the render, kept between annotations and renders.</summary>
        private Bitmap Layer()
        {
            if (_layer is null || _layer.Width != _width || _layer.Height != _height)
            {
                _layer?.Dispose();
                _layer = null;
                _layer = new Bitmap(_width, _height);
            }

            return _layer;
        }

        /// <summary>
        /// Composites the layer's premultiplied pixels over the target within the frame's bounds, with its alpha and blend mode
        /// (§11.3.6, through <see cref="Compositor"/>).
        /// </summary>
        private static void Composite(Bitmap layer, Bitmap target, in AnnotationFrame frame)
        {
            Compositor compositor = frame.Compositor;
            if (compositor.PaintsNothing)
            {
                return;
            }

            for (int y = frame.Top; y < frame.Bottom; y++)
            {
                ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(layer.GetRow(y).Slice(frame.Left * 4, (frame.Right - frame.Left) * 4));
                Span<uint> destination = MemoryMarshal.Cast<byte, uint>(target.GetRow(y).Slice(frame.Left * 4, (frame.Right - frame.Left) * 4));
                for (int i = 0; i < source.Length; i++)
                {
                    uint pixel = source[i];
                    if (pixel != 0)
                    {
                        destination[i] = compositor.Composite(pixel, destination[i], 255);
                    }
                }
            }
        }

        /// <summary>What an annotation changed at its start: the device mapping and target to restore, and its layer's bounds and compositor.</summary>
        private readonly record struct AnnotationFrame(Matrix Device, Bitmap Target, Compositor Compositor, bool Layered, int Left, int Top, int Right, int Bottom);
    }
}
