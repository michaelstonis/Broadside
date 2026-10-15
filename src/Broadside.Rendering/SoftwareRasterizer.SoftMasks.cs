using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Soft masks (ISO 32000-2 §11.5, §11.6.4.3, §11.6.5.1) in the software rasterizer: a mask's group is rendered once per mask and
/// transform into a <see cref="SoftMaskPlane"/> of mask values, which every later paint's coverage goes through in the clip stage
/// (<see cref="RasterClip.SetSoftMask"/>) and a group's result when it composites.
/// </summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's soft-mask half.</summary>
    private sealed partial class Backend
    {
        /// <summary>How many rendered masks are kept before the least recently used one that nothing references is replaced.</summary>
        private const int KeptMasks = 8;

        /// <summary>Pixels added around a mask group's mapped bounding box (anti-aliased edges).</summary>
        private const int MaskMargin = 2;

        // The soft mask the paints go through (null for none), whether it is shape, and the ones SaveState saved.
        private SoftMaskPlane? _softMask;
        private bool _softMaskShape;
        private (SoftMaskPlane? Mask, bool Shape)[] _savedSoftMasks = new (SoftMaskPlane?, bool)[16];
        private int _savedSoftMaskCount;

        // Rendered masks (free when their Content is null), the size of the render, and the clock for least recently used.
        private SoftMaskPlane[] _maskPlanes = [];
        private int _maskPlaneCount;
        private int _renderWidth;
        private int _renderHeight;
        private long _maskClock;

        // What a mask's group is rendered with: a child backend over a pooled bitmap.
        private Backend? _maskBackend;
        private byte[] _maskPixels = [];
        private Bitmap? _maskBitmap;

        /// <summary>
        /// Sets the soft mask the paints that follow go through (§11.6.4.3): its values multiply each paint's shape or opacity after
        /// the clip, rendered once for this mask and transform.
        /// </summary>
        public void SetSoftMask(in DisplaySoftMask mask)
        {
            _softMask = mask.IsNone ? null : GetPlane(mask);
            _softMaskShape = !mask.IsNone && mask.IsShape;
            _clip.SetSoftMask(_softMask, _softMaskShape);
        }

        /// <summary>Returns the rendered values of <paramref name="mask"/> under the current transform to render pixels, rendering them the first time.</summary>
        private SoftMaskPlane GetPlane(in DisplaySoftMask mask)
        {
            // Inside a group the device transform is the layer's; the plane is in the render's pixels, as the clip is.
            Matrix toDevice = mask.Matrix * _device * Matrix.CreateTranslation(_originX, _originY);
            for (int index = 0; index < _maskPlaneCount; index++)
            {
                SoftMaskPlane plane = _maskPlanes[index];
                if (ReferenceEquals(plane.Content, mask.Content) && plane.ToDevice == toDevice)
                {
                    plane.LastUse = ++_maskClock;
                    return plane;
                }
            }

            SoftMaskPlane free = TakePlane();
            RenderMask(free, mask, toDevice);
            free.Content = mask.Content;
            free.ToDevice = toDevice;
            free.LastUse = ++_maskClock;
            return free;
        }

        /// <summary>Returns a plane to render into: a free one, a new one while few are kept, else the least recently used one nothing references.</summary>
        private SoftMaskPlane TakePlane()
        {
            SoftMaskPlane? oldest = null;
            for (int index = 0; index < _maskPlaneCount; index++)
            {
                SoftMaskPlane plane = _maskPlanes[index];
                if (plane.Content is null)
                {
                    return plane;
                }

                if (plane.Pins == 0 && !IsReferenced(plane) && (oldest is null || plane.LastUse < oldest.LastUse))
                {
                    oldest = plane;
                }
            }

            if (_maskPlaneCount < KeptMasks || oldest is null)
            {
                if (_maskPlaneCount == _maskPlanes.Length)
                {
                    Array.Resize(ref _maskPlanes, Math.Max(4, _maskPlanes.Length * 2));
                }

                return _maskPlanes[_maskPlaneCount++] = new SoftMaskPlane();
            }

            return oldest;
        }

        /// <summary>Whether the current state or a saved one goes through <paramref name="plane"/>.</summary>
        private bool IsReferenced(SoftMaskPlane plane)
        {
            if (ReferenceEquals(_softMask, plane))
            {
                return true;
            }

            for (int index = 0; index < _savedSoftMaskCount; index++)
            {
                if (ReferenceEquals(_savedSoftMasks[index].Mask, plane))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Renders a mask's values: its group list replayed (§11.5.2) over a transparent backdrop, the value its alpha, or (§11.5.3) over
        /// an opaque backdrop of the backdrop colour, the value its luminosity; then the transfer function (§11.6.5.1). Only the group's
        /// bounding box is rendered; outside it the value is the transfer function of 0 or of the backdrop's luminosity.
        /// </summary>
        private void RenderMask(SoftMaskPlane plane, in DisplaySoftMask mask, in Matrix toDevice)
        {
            FillTransfer(mask.TransferFunction, plane.Transfer);
            bool luminosity = mask.Subtype == PdfSoftMaskType.Luminosity;
            uint backdrop = 0;
            if (luminosity && mask.ColorSpace is { } space)
            {
                Span<float> rgb = stackalloc float[4];
                _document!.GetColorConverter(space, mask.Conversion, mask.DefaultColorSpaces).Convert(mask.Backdrop, rgb);
                backdrop = PixelModel.Rgb.Pack(rgb);
            }

            byte outside = plane.Transfer[luminosity ? Luminosity(backdrop) : 0];
            GetMaskArea(mask, toDevice, out int left, out int top, out int right, out int bottom);
            plane.Open(left, top, right - left, bottom - top, outside);
            int width = plane.Width;
            int height = plane.Height;
            if (width == 0 || height == 0)
            {
                return;
            }

            int length = width * height * 4;
            Pooled.Ensure(ref _maskPixels, length);
            Memory<byte> pixels = _maskPixels.AsMemory(0, length);
            MemoryMarshal.Cast<byte, uint>(pixels.Span).Fill(backdrop);
            if (_maskBitmap is null)
            {
                _maskBitmap = new Bitmap(pixels, width, height, width * 4);
            }
            else
            {
                _maskBitmap.Attach(pixels, width, height, width * 4);
            }

            Backend child = _maskBackend ??= new Backend();
            child.Begin(_document!, _maskBitmap, toDevice * Matrix.CreateTranslation(-left, -top), width, height, preview: false, _antiAliasing);
            try
            {
                mask.Content.Replay(child);
            }
            finally
            {
                child.End();
            }

            byte[] transfer = plane.Transfer;
            for (int row = 0; row < height; row++)
            {
                ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(pixels.Span.Slice(row * width * 4, width * 4));
                Span<byte> values = plane.Row(top + row);
                for (int x = 0; x < width; x++)
                {
                    uint pixel = source[x];
                    values[x] = transfer[luminosity ? Luminosity(pixel) : (int)(pixel >> 24)];
                }
            }
        }

        /// <summary>The render pixels a mask's group can reach: its bounding box mapped to the device and widened, within the render.</summary>
        private void GetMaskArea(in DisplaySoftMask mask, in Matrix toDevice, out int left, out int top, out int right, out int bottom)
        {
            (left, top, right, bottom) = (0, 0, _renderWidth, _renderHeight);
            if (!mask.HasBoundingBox)
            {
                return;
            }

            PdfRectangle box = mask.BoundingBox;
            PathPoint a = toDevice.Transform(box.Left, box.Bottom);
            PathPoint b = toDevice.Transform(box.Right, box.Bottom);
            PathPoint c = toDevice.Transform(box.Right, box.Top);
            PathPoint d = toDevice.Transform(box.Left, box.Top);
            double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - MaskMargin;
            double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + MaskMargin;
            double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - MaskMargin;
            double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + MaskMargin;
            if (!double.IsFinite(minX + maxX + minY + maxY))
            {
                return;
            }

            left = Math.Max(left, (int)Math.Clamp(Math.Floor(minX), int.MinValue, int.MaxValue));
            top = Math.Max(top, (int)Math.Clamp(Math.Floor(minY), int.MinValue, int.MaxValue));
            right = Math.Max(left, Math.Min(right, (int)Math.Clamp(Math.Ceiling(maxX), int.MinValue, int.MaxValue)));
            bottom = Math.Max(top, Math.Min(bottom, (int)Math.Clamp(Math.Ceiling(maxY), int.MinValue, int.MaxValue)));
        }

        /// <summary>
        /// The luminosity of an opaque pixel: for a device space the gray it converts to, Y = 0.30 R + 0.59 G + 0.11 B (§11.5.3
        /// EXAMPLE 2), here of the colour as the rasterizer holds it (RGB).
        /// </summary>
        private static int Luminosity(uint pixel) =>
            ((30 * (int)((pixel >> 16) & 0xFF)) + (59 * (int)((pixel >> 8) & 0xFF)) + (11 * (int)(pixel & 0xFF)) + 50) / 100;

        /// <summary>Fills a 256-entry table with the transfer function at each 8-bit input, clamped to 0..1 (Table 142, TR).</summary>
        private static void FillTransfer(PdfFunction? function, byte[] table)
        {
            if (function is not { InputCount: 1, OutputCount: 1 })
            {
                for (int index = 0; index < 256; index++)
                {
                    table[index] = (byte)index;
                }

                return;
            }

            Span<float> inputs = stackalloc float[256];
            Span<float> outputs = stackalloc float[256];
            for (int index = 0; index < 256; index++)
            {
                inputs[index] = index / 255f;
            }

            function.Evaluate(inputs, outputs, 256);
            for (int index = 0; index < 256; index++)
            {
                float value = outputs[index];
                table[index] = (byte)Math.Round((float.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1)) * 255);
            }
        }

        private void SaveSoftMask()
        {
            if (_savedSoftMaskCount == _savedSoftMasks.Length)
            {
                Array.Resize(ref _savedSoftMasks, _savedSoftMasks.Length * 2);
            }

            _savedSoftMasks[_savedSoftMaskCount++] = (_softMask, _softMaskShape);
        }

        private void RestoreSoftMask()
        {
            if (_savedSoftMaskCount > 0)
            {
                (_softMask, _softMaskShape) = _savedSoftMasks[--_savedSoftMaskCount];
                _savedSoftMasks[_savedSoftMaskCount] = default;
                _clip.SetSoftMask(_softMask, _softMaskShape);
            }
        }

        /// <summary>Starts a render with no mask and every kept plane free.</summary>
        private void BeginSoftMasks(int width, int height)
        {
            (_renderWidth, _renderHeight) = (width, height);
            _softMask = null;
            _softMaskShape = false;
            Array.Clear(_savedSoftMasks, 0, _savedSoftMaskCount);
            _savedSoftMaskCount = 0;
            _clip.SetSoftMask(null, false);
            for (int index = 0; index < _maskPlaneCount; index++)
            {
                _maskPlanes[index].Content = null;
                _maskPlanes[index].Pins = 0;
            }
        }

        /// <summary>Lets go of the lists the render's masks referenced; their buffers stay for the next render.</summary>
        private void EndSoftMasks() => BeginSoftMasks(0, 0);

        private void DisposeSoftMasks()
        {
            for (int index = 0; index < _maskPlaneCount; index++)
            {
                _maskPlanes[index].Dispose();
            }

            _maskPlanes = [];
            _maskPlaneCount = 0;
            _maskBackend?.Dispose();
            _maskBackend = null;
            _maskBitmap?.Dispose();
            _maskBitmap = null;
            Pooled.Return(ref _maskPixels);
        }
    }
}
