using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Transparency groups (ISO 32000-2 §11.4, §11.6.6, §11.7.4.4) in the software rasterizer: a group's content paints into a layer the
/// size of its bounds (<see cref="GroupLayer"/>), whose result then composites onto the backdrop through the clip in force.
/// </summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's group half.</summary>
    private sealed partial class Backend
    {
        /// <summary>Pixels added around a group's mapped bounding box, for anti-aliasing, hairlines and stroke adjustment.</summary>
        private const int GroupMargin = 2;

        private readonly GroupProbe _probe = new();
        private GroupLayer[] _layers = [];
        private int _layerDepth;

        // The pixel model the target holds, and where the target's pixel (0, 0) is in the render's pixels.
        private PixelModel _pixelModel;
        private int _originX;
        private int _originY;

        // Working rows for converting a group's result to its parent's colour space.
        private float[] _groupComponents = [];
        private float[] _groupConverted = [];
        private uint[] _groupRow = [];

        // The backdrop a non-isolated tiling cell is rendered over, and the bitmap over it.
        private byte[] _groundPixels = [];
        private Bitmap? _groundBitmap;

        private GroupLayer? CurrentLayer => _layerDepth > 0 ? _layers[_layerDepth - 1] : null;

        /// <summary>
        /// Paints a transparency group (ISO 32000-2 §11.4): directly into the backdrop when that gives the same result (a non-isolated,
        /// non-knockout group in the Normal blend mode at opacity 1 outside a knockout group, §11.4.4 NOTE 5, or an isolated one whose
        /// content does not blend), otherwise into a layer composited onto the backdrop afterwards.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The layer covers the group's bounding box (its BBox mapped to the device, widened by <see cref="GroupMargin"/>) within the
        /// clip. Its initial backdrop is transparent for an isolated group and a copy of the backdrop otherwise (in a knockout parent,
        /// the parent's initial backdrop, §11.6.6). A non-isolated group whose content uses only the Normal blend mode gives the same
        /// colour, shape and alpha as an isolated one (blending with the backdrop is what tells them apart, §11.4.5), so it is
        /// painted as one when its own compositing needs its colour without the backdrop.
        /// </para>
        /// <para>
        /// A group with a colour space of another model (gray, RGB or CMYK; §11.6.6, §11.3.4) paints in that model: gray as the level
        /// in all three channels, CMYK as two planes of complemented colorants painted in two replays of the content
        /// (<see cref="PixelModel"/>). The result is converted to the parent's model with the group's conversion; the page group is
        /// composited onto the page's white in its own space first (§11.4.7).
        /// </para>
        /// </remarks>
        public void DrawGroup(in DisplayGroup group)
        {
            if (_clip.IsEmpty || !TryGroupBounds(group, out int left, out int top, out int right, out int bottom))
            {
                return;
            }

            GroupLayer? parent = CurrentLayer;
            bool parentKnockout = parent?.Knockout == true;
            bool sameModel = group.ColorSpace is not { } space || ModelOf(space) == _pixelModel.Model;
            var compositor = new Compositor(group.Opacity, group.BlendMode).WithKnockout(parentKnockout);
            if (compositor.PaintsNothing)
            {
                return;
            }

            bool normal = compositor.IsNormal && compositor.Alpha == 255;
            GroupContent content = default;
            bool probed = false;
            if (!parentKnockout && !group.IsKnockout && normal)
            {
                bool direct = !group.IsIsolated && sameModel;
                if (!direct)
                {
                    content = _probe.Inspect(group);
                    probed = true;
                    direct = !content.Blends && (sameModel || !content.Transparency);
                }

                if (direct)
                {
                    group.Replay(this);
                    return;
                }
            }

            bool isolated = group.IsIsolated;
            bool tracksAlpha = false;
            if (!isolated && (!compositor.IsNormal || parent?.TracksAlpha == true))
            {
                if (!probed)
                {
                    content = _probe.Inspect(group);
                }

                isolated = !content.Blends;
                tracksAlpha = content.Blends;
            }

            PixelModel model = sameModel ? _pixelModel : new PixelModel(ModelOf(group.ColorSpace!), 0);
            int firstPass = 0;
            int lastPass = 0;
            if (model.Model == DeviceColorModel.Cmyk)
            {
                (firstPass, lastPass) = _pixelModel.Model == DeviceColorModel.Cmyk ? (_pixelModel.Pass, _pixelModel.Pass) : (0, 1);
            }

            GroupLayer layer = OpenLayer(
                left,
                top,
                right - left,
                bottom - top,
                isolated,
                group.IsKnockout,
                tracksAlpha,
                model,
                compositor,
                group.Kind == DisplayGroupKind.Page,
                sameModel ? null : group.ColorSpace,
                group.DefaultColorSpaces,
                group.Conversion);
            for (int pass = firstPass; pass <= lastPass; pass++)
            {
                BeginPass(layer, pass, pass == firstPass);
                group.Replay(this);
                EndPass(layer);
            }

            CloseLayer(layer);
        }

        /// <summary>
        /// Renders a tiling pattern's cell over <paramref name="area"/> as the non-isolated group §11.6.7 makes of it, every tile into
        /// one group (NOTE 2): the cell list replays once per tile into a layer that starts as the backdrop under the area, so its blend
        /// modes see the page, and the group's own colour and alpha (the backdrop removed, §11.4.4) land in <paramref name="result"/>,
        /// premultiplied, for the paint to composite through its shape.
        /// </summary>
        private void PaintNonIsolatedTiles(DisplayList cell, in TilingLayout layout, PixelBox area, Span<uint> result)
        {
            int width = area.Width;
            int height = area.Height;
            int length = width * height * 4;
            Pooled.Ensure(ref _groundPixels, length);
            Memory<byte> pixels = _groundPixels.AsMemory(0, length);
            Bitmap target = _target!;
            for (int row = 0; row < height; row++)
            {
                target.Pixels.Slice(((area.Top + row) * target.Stride) + (area.Left * 4), width * 4).CopyTo(pixels.Span.Slice(row * width * 4, width * 4));
            }

            if (_groundBitmap is null)
            {
                _groundBitmap = new Bitmap(pixels, width, height, width * 4);
            }
            else
            {
                _groundBitmap.Attach(pixels, width, height, width * 4);
            }

            Backend child = _cellBackend ??= new Backend();
            child.Begin(_document!, _groundBitmap, Matrix.Identity, width, height);
            child._pixelModel = _pixelModel;
            try
            {
                int pass = _pixelModel.Model == DeviceColorModel.Cmyk ? _pixelModel.Pass : 0;
                GroupLayer layer = child.OpenLayer(
                    0,
                    0,
                    width,
                    height,
                    isolated: false,
                    knockout: false,
                    tracksAlpha: true,
                    _pixelModel,
                    default,
                    ontoWhite: false,
                    colorSpace: null,
                    defaults: null,
                    conversion: default);
                child.BeginPass(layer, pass, first: true);
                for (long j = layout.JFirst; j <= layout.JLast; j++)
                {
                    for (long i = layout.IFirst; i <= layout.ILast; i++)
                    {
                        layout.GetOffset(i, j, out long offsetX, out long offsetY);
                        child._device = layout.CellMatrix * Matrix.CreateTranslation(offsetX - area.Left, offsetY - area.Top);
                        child.SaveState();
                        child.SetTransform(in child._ctm);
                        cell.Replay(child);
                        child.RestoreState();
                    }
                }

                child.EndPass(layer);
                for (int row = 0; row < height; row++)
                {
                    Span<uint> plane = layer.PlaneRow(pass, row, 0, width);
                    Span<uint> backdrop = layer.BackdropRow(pass, row, 0, width);
                    Span<byte> alpha = layer.AlphaRow(row, 0, width);
                    Span<uint> destination = result.Slice(row * width, width);
                    for (int x = 0; x < width; x++)
                    {
                        destination[x] = Compositor.RemoveBackdrop(plane[x], backdrop[x], alpha[x]);
                    }
                }

                layer.Forget();
                child._layerDepth--;
            }
            finally
            {
                child.End();
            }
        }

        /// <summary>Forgets open groups at the start of a render.</summary>
        private void BeginGroups()
        {
            _layerDepth = 0;
            _pixelModel = PixelModel.Rgb;
            _originX = 0;
            _originY = 0;
            _scan.Hook = null;
        }

        /// <summary>Lets go of what the render's groups referenced; their buffers stay for the next render.</summary>
        private void EndGroups()
        {
            foreach (GroupLayer? layer in _layers)
            {
                layer?.Forget();
            }

            _layerDepth = 0;
            _scan.Hook = null;
        }

        private void DisposeGroups()
        {
            foreach (GroupLayer? layer in _layers)
            {
                layer?.Dispose();
            }

            _layers = [];
            Pooled.Return(ref _groupComponents);
            Pooled.Return(ref _groupConverted);
            Pooled.Return(ref _groupRow);
            _groundBitmap?.Dispose();
            _groundBitmap = null;
            Pooled.Return(ref _groundPixels);
        }

        /// <summary>
        /// Opens a layer over a rectangle of the target's pixels: the next free <see cref="GroupLayer"/>, keeping its shape when the
        /// parent needs it (a knockout parent replaces by shape; a parent that keeps its shape unites its children's).
        /// </summary>
        private GroupLayer OpenLayer(
            int left,
            int top,
            int width,
            int height,
            bool isolated,
            bool knockout,
            bool tracksAlpha,
            PixelModel model,
            Compositor compositor,
            bool ontoWhite,
            PdfColorSpace? colorSpace,
            PdfDefaultColorSpaces? defaults,
            ColorConversion conversion)
        {
            GroupLayer? parent = CurrentLayer;
            bool tracksShape = parent is not null && (parent.Knockout || parent.TracksShape);
            if (_layerDepth == _layers.Length)
            {
                Array.Resize(ref _layers, Math.Max(4, _layers.Length * 2));
            }

            GroupLayer layer = _layers[_layerDepth] ??= new GroupLayer();
            _layerDepth++;
            layer.Open(
                left + _originX,
                top + _originY,
                width,
                height,
                isolated,
                knockout,
                tracksAlpha,
                tracksShape,
                model,
                compositor,
                ontoWhite,
                colorSpace,
                defaults,
                conversion);
            return layer;
        }

        /// <summary>
        /// Starts one pass of a layer: fills its plane with its initial backdrop (transparent when isolated; otherwise the parent's
        /// pixels, or a knockout parent's initial backdrop, §11.6.6), keeps a copy of that backdrop when needed, and points the
        /// paints at the plane in the layer's own pixel coordinates.
        /// </summary>
        private void BeginPass(GroupLayer layer, int pass, bool first)
        {
            if (first)
            {
                layer.Saved = new GroupLayer.SavedState(_target, _device, _width, _height, _originX, _originY, _pixelModel, _scan.Hook);
            }

            GroupLayer.SavedState saved = layer.Saved;
            GroupLayer? parent = _layerDepth > 1 ? _layers[_layerDepth - 2] : null;
            int dx = layer.Left - saved.OriginX;
            int dy = layer.Top - saved.OriginY;
            for (int row = 0; row < layer.Height; row++)
            {
                Span<uint> plane = layer.PlaneRow(pass, row, 0, layer.Width);
                if (layer.Isolated)
                {
                    plane.Clear();
                }
                else if (parent is { Knockout: true })
                {
                    parent.BackdropRow(pass, row + dy, dx, layer.Width).CopyTo(plane);
                }
                else
                {
                    MemoryMarshal.Cast<byte, uint>(saved.Target!.Pixels.Slice(((row + dy) * saved.Target.Stride) + (dx * 4), layer.Width * 4)).CopyTo(plane);
                }

                if (layer.KeepsBackdrop)
                {
                    plane.CopyTo(layer.BackdropRow(pass, row, 0, layer.Width));
                }
            }

            layer.Pass = pass;
            layer.Records = first;
            _target = layer.Plane(pass);
            _device = saved.Device * Matrix.CreateTranslation(-dx, -dy);
            _width = layer.Width;
            _height = layer.Height;
            _originX = layer.Left;
            _originY = layer.Top;
            _clip.SetOrigin(_originX, _originY);
            _pixelModel = new PixelModel(layer.Model.Model, pass);
            _scan.Hook = layer.Knockout || layer.TracksAlpha || layer.TracksShape ? layer : null;
            _fillCompositor = _fillCompositor.WithKnockout(layer.Knockout);
            _strokeCompositor = _strokeCompositor.WithKnockout(layer.Knockout);
            SetTransform(in _ctm);
        }

        /// <summary>Ends a pass: the paints go back to the parent's target.</summary>
        private void EndPass(GroupLayer layer)
        {
            GroupLayer.SavedState saved = layer.Saved;
            _target = saved.Target;
            _device = saved.Device;
            _width = saved.Width;
            _height = saved.Height;
            _originX = saved.OriginX;
            _originY = saved.OriginY;
            _clip.SetOrigin(_originX, _originY);
            _pixelModel = saved.Model;
            _scan.Hook = saved.Hook;
            bool knockout = _layerDepth > 1 && _layers[_layerDepth - 2].Knockout;
            _fillCompositor = _fillCompositor.WithKnockout(knockout);
            _strokeCompositor = _strokeCompositor.WithKnockout(knockout);
            SetTransform(in _ctm);
        }

        /// <summary>
        /// Composites a layer's result onto the parent through the clip in force (§8.5.4), as one element of the parent (§11.4.4,
        /// §11.4.8), then closes it.
        /// </summary>
        private void CloseLayer(GroupLayer layer)
        {
            GroupLayer? parent = _layerDepth > 1 ? _layers[_layerDepth - 2] : null;
            // Gray pixels are already RGB; RGB becomes gray by luminosity; anything to or from CMYK goes through the converter.
            bool converts = layer.Model.Model != _pixelModel.Model
                && (layer.Model.Model == DeviceColorModel.Cmyk || _pixelModel.Model == DeviceColorModel.Cmyk);
            PdfColorConverter? converter = converts ? ResultConverter(layer) : null;
            Pooled.Ensure(ref _groupRow, layer.Width);
            int dx = layer.Left - _originX;
            int dy = layer.Top - _originY;
            for (int row = 0; row < layer.Height; row++)
            {
                Span<uint> source = _groupRow.AsSpan(0, layer.Width);
                if (converter is not null)
                {
                    ConvertResult(layer, row, converter, source);
                }
                else if (layer.Model.Model != _pixelModel.Model && _pixelModel.Model == DeviceColorModel.Gray)
                {
                    GrayResult(layer, row, source);
                }
                else
                {
                    int pass = layer.Model.Model == DeviceColorModel.Cmyk ? _pixelModel.Pass : 0;
                    layer.PlaneRow(pass, row, 0, layer.Width).CopyTo(source);
                }

                var sink = new GroupSink(layer, parent, _target!, _pixelModel.Pass, source, row, dx, dy, converts);
                _clip.Apply(row + dy, dx, layer.Width, 255, ref sink);
            }

            layer.Forget();
            _layerDepth--;
        }

        /// <summary>The converter from a layer's colour space (as its pixels hold it) to the parent's model.</summary>
        private PdfColorConverter ResultConverter(GroupLayer layer)
        {
            PdfColorSpace source = layer.Model.Model switch
            {
                DeviceColorModel.Cmyk => layer.ColorSpace is { Family: PdfColorSpaceFamily.DeviceCmyk or PdfColorSpaceFamily.IccBased } cmyk ? cmyk : PdfDeviceCmykColorSpace.Instance,
                _ => PdfDeviceRgbColorSpace.Instance,
            };
            PdfDefaultColorSpaces? defaults = source is PdfDeviceCmykColorSpace ? layer.DefaultColorSpaces : null;
            return _document!.GetColorConverter(source, _pixelModel.Convert(layer.Conversion), defaults);
        }

        /// <summary>
        /// Converts one row of a layer's result to the parent's model, premultiplied; the page group is composited onto white in its own
        /// space first (§11.4.7), so its pixels come out opaque.
        /// </summary>
        private void ConvertResult(GroupLayer layer, int row, PdfColorConverter converter, Span<uint> destination)
        {
            int width = layer.Width;
            bool cmyk = layer.Model.Model == DeviceColorModel.Cmyk;
            int inputs = cmyk ? 4 : 3;
            int outputs = _pixelModel.Components;
            Pooled.Ensure(ref _groupComponents, width * inputs);
            Pooled.Ensure(ref _groupConverted, width * outputs);
            Span<float> components = _groupComponents.AsSpan(0, width * inputs);
            Span<uint> first = layer.PlaneRow(0, row, 0, width);
            Span<uint> black = cmyk ? layer.PlaneRow(1, row, 0, width) : default;
            for (int x = 0; x < width; x++)
            {
                uint pixel = first[x];
                float alpha = (pixel >> 24) / 255f;
                Span<float> c = components.Slice(x * inputs, inputs);
                if (alpha <= 0 && !layer.OntoWhite)
                {
                    c.Clear();
                    continue;
                }

                // The plane holds α × value; onto white, value' = α × value + (1 − α) × white, white being 1 (no ink, complemented).
                float scale = layer.OntoWhite ? 1 : 1 / alpha;
                float white = layer.OntoWhite ? 1 - alpha : 0;
                float r = ((((pixel >> 16) & 0xFF) / 255f) + white) * scale;
                float g = ((((pixel >> 8) & 0xFF) / 255f) + white) * scale;
                float b = (((pixel & 0xFF) / 255f) + white) * scale;
                if (cmyk)
                {
                    float k = (((black[x] & 0xFF) / 255f) + white) * scale;
                    (c[0], c[1], c[2], c[3]) = (1 - Math.Clamp(r, 0, 1), 1 - Math.Clamp(g, 0, 1), 1 - Math.Clamp(b, 0, 1), 1 - Math.Clamp(k, 0, 1));
                }
                else
                {
                    (c[0], c[1], c[2]) = (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
                }
            }

            Span<float> converted = _groupConverted.AsSpan(0, width * outputs);
            converter.Convert(components, converted, width);
            for (int x = 0; x < width; x++)
            {
                int alpha = layer.OntoWhite ? 255 : (int)(first[x] >> 24);
                destination[x] = alpha == 0 ? 0 : Premultiply(_pixelModel.Pack(converted.Slice(x * outputs, outputs)), alpha);
            }
        }

        /// <summary>One row of an RGB layer's result as gray, for a gray parent: the luminosity of each (premultiplied) pixel.</summary>
        private void GrayResult(GroupLayer layer, int row, Span<uint> destination)
        {
            Span<uint> plane = layer.PlaneRow(0, row, 0, layer.Width);
            Span<byte> rgb = stackalloc byte[3];
            for (int x = 0; x < plane.Length; x++)
            {
                uint pixel = plane[x];
                (rgb[0], rgb[1], rgb[2]) = ((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
                destination[x] = (_pixelModel.Pack(rgb) & 0x00FFFFFFu) | (pixel & 0xFF000000u);
            }
        }

        /// <summary>Scales an opaque pixel by <paramref name="alpha"/> (0 to 255).</summary>
        private static uint Premultiply(uint opaque, int alpha)
        {
            if (alpha >= 255)
            {
                return opaque;
            }

            uint result = (uint)alpha << 24;
            for (int shift = 0; shift < 24; shift += 8)
            {
                result |= (uint)Compositor.Divide255((int)((opaque >> shift) & 0xFF) * alpha) << shift;
            }

            return result;
        }

        /// <summary>
        /// The pixels a group can paint, in target pixels: its bounding box (when it has one) mapped to the device and widened by
        /// <see cref="GroupMargin"/>, within the clip's bounds and the target.
        /// </summary>
        private bool TryGroupBounds(in DisplayGroup group, out int left, out int top, out int right, out int bottom)
        {
            _clip.GetBounds(out left, out top, out right, out bottom);
            left = Math.Max(left, 0);
            top = Math.Max(top, 0);
            right = Math.Min(right, _width);
            bottom = Math.Min(bottom, _height);
            if (group.HasBoundingBox)
            {
                PdfRectangle box = group.BoundingBox;
                Matrix toDevice = group.Matrix * _device;
                PathPoint a = toDevice.Transform(box.Left, box.Bottom);
                PathPoint b = toDevice.Transform(box.Right, box.Bottom);
                PathPoint c = toDevice.Transform(box.Right, box.Top);
                PathPoint d = toDevice.Transform(box.Left, box.Top);
                double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - GroupMargin;
                double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + GroupMargin;
                double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - GroupMargin;
                double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + GroupMargin;
                if (double.IsFinite(minX + maxX + minY + maxY))
                {
                    left = Math.Max(left, (int)Math.Clamp(Math.Floor(minX), int.MinValue, int.MaxValue));
                    top = Math.Max(top, (int)Math.Clamp(Math.Floor(minY), int.MinValue, int.MaxValue));
                    right = Math.Min(right, (int)Math.Clamp(Math.Ceiling(maxX), int.MinValue, int.MaxValue));
                    bottom = Math.Min(bottom, (int)Math.Clamp(Math.Ceiling(maxY), int.MinValue, int.MaxValue));
                }
            }

            return left < right && top < bottom;
        }

        /// <summary>The pixel model of a group colour space: gray, RGB or CMYK by its number of components (§11.3.4).</summary>
        private static DeviceColorModel ModelOf(PdfColorSpace space) => space.ComponentCount switch
        {
            1 => DeviceColorModel.Gray,
            4 => DeviceColorModel.Cmyk,
            _ => DeviceColorModel.Rgb,
        };

        /// <summary>
        /// Composites a closed layer's result onto its parent, run by run of the clip: as one element of the parent group (§11.4.8),
        /// knocked out when the parent is a knockout group, with the parent's alpha and shape kept when it keeps them.
        /// </summary>
        private readonly ref struct GroupSink : ICoverageSink
        {
            private readonly GroupLayer _layer;
            private readonly GroupLayer? _parent;
            private readonly Bitmap _target;
            private readonly int _parentPass;
            private readonly Span<uint> _source;
            private readonly int _row;
            private readonly int _dx;
            private readonly int _dy;
            private readonly bool _isolatedSource;

            public GroupSink(GroupLayer layer, GroupLayer? parent, Bitmap target, int parentPass, Span<uint> source, int row, int dx, int dy, bool converted)
            {
                _layer = layer;
                _parent = parent;
                _target = target;
                _parentPass = parentPass;
                _source = source;
                _row = row;
                _dx = dx;
                _dy = dy;
                _isolatedSource = converted || layer.Isolated || layer.TracksAlpha;
            }

            public void Span(int row, int x, int count, int coverage)
            {
                GroupLayer layer = _layer;
                Compositor compositor = layer.Compositor;
                int start = x - _dx;
                Span<uint> target = MemoryMarshal.Cast<byte, uint>(_target.Pixels.Slice((row * _target.Stride) + (x * 4), count * 4));
                Span<uint> backdrop = _parent is { Knockout: true } knockout ? knockout.BackdropRow(_parentPass, row, x, count) : default;
                bool tracksAlpha = _parent is { TracksAlpha: true, Records: true };
                bool tracksShape = _parent is { TracksShape: true, Records: true };
                Span<byte> parentAlpha = tracksAlpha ? _parent!.AlphaRow(row, x, count) : default;
                Span<byte> parentShape = tracksShape ? _parent!.ShapeRow(row, x, count) : default;
                Span<byte> shape = layer.TracksShape ? layer.ShapeRow(_row, start, count) : default;
                Span<byte> alpha = layer.TracksAlpha ? layer.AlphaRow(_row, start, count) : default;
                Span<uint> initial = !layer.Isolated && layer.TracksAlpha ? layer.BackdropRow(layer.Pass, _row, start, count) : default;
                for (int i = 0; i < count; i++)
                {
                    uint complete = _source[start + i];
                    uint source = complete;
                    if (!layer.Isolated && layer.TracksAlpha)
                    {
                        // §11.4.4: the group's own colour and alpha, without the backdrop it was composited over.
                        source = Compositor.RemoveBackdrop(complete, initial[i], alpha[i]);
                    }

                    int fs = layer.TracksShape ? Compositor.Divide255(shape[i] * coverage) : coverage;
                    if (backdrop.IsEmpty)
                    {
                        target[i] = _isolatedSource
                            ? compositor.Composite(source, target[i], coverage)
                            : Compositor.Mix(target[i], complete, compositor.Scale(coverage));
                    }
                    else
                    {
                        uint composited = _isolatedSource
                            ? compositor.Composite(source, backdrop[i], coverage)
                            : Compositor.Mix(backdrop[i], complete, compositor.Scale(coverage));
                        target[i] = Compositor.KnockOut(target[i], composited, backdrop[i], fs);
                    }

                    if (tracksAlpha)
                    {
                        int alphaS = Compositor.Divide255(compositor.Scale(coverage) * (int)(source >> 24));
                        parentAlpha[i] = (byte)(_parent!.Knockout
                            ? Math.Min(255, Compositor.Divide255(parentAlpha[i] * (255 - fs)) + alphaS)
                            : Compositor.Union(parentAlpha[i], alphaS));
                    }

                    if (tracksShape)
                    {
                        parentShape[i] = (byte)Compositor.Union(parentShape[i], fs);
                    }
                }
            }
        }
    }
}
