using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Shadings (ISO 32000-2 §8.7.4): <c>sh</c> and fills and strokes in a shading pattern.</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's shading half.</summary>
    private sealed partial class Backend
    {
        private readonly ShadingSource _shadingSource = new();

        /// <summary>
        /// Paints the shading over the whole clip (§8.7.4.2): the clip's pixel bounds, or the shading's BBox mapped to the device
        /// when it has one (§8.7.4.3), are scan converted and painted through the clip, without the Background.
        /// </summary>
        /// <remarks>
        /// Axial and radial shadings (Types 2 and 3) are painted here; function-based, triangle-mesh and patch-mesh shadings (Types 1
        /// and 4 to 7) through the mesh layer (<see cref="DrawMeshShading"/>).
        /// </remarks>
        public void DrawShading(in DisplayShading shading)
        {
            if (PaintsAsMesh(shading.Shading))
            {
                DrawMeshShading(shading);
                return;
            }

            // The BBox, when there is one, is the painted shape, so its edges are anti-aliased like a fill's and need no test per pixel.
            PdfRectangle? box = shading.Shading.BoundingBox;
            if (_clip.IsEmpty || _fillCompositor.PaintsNothing || !TryPrepareShading(shading, clipToBox: box is null, out Matrix inverse, out ShadingGeometry geometry) || geometry.IsEmpty)
            {
                return;
            }

            _scan.Reset(_width, _height);
            if (box is { } bounds)
            {
                Matrix toDevice = shading.Matrix * _device;
                PathPoint p0 = toDevice.Transform(bounds.Left, bounds.Bottom);
                PathPoint p1 = toDevice.Transform(bounds.Right, bounds.Bottom);
                PathPoint p2 = toDevice.Transform(bounds.Right, bounds.Top);
                PathPoint p3 = toDevice.Transform(bounds.Left, bounds.Top);
                AddDeviceLine(p0, p1);
                AddDeviceLine(p1, p2);
                AddDeviceLine(p2, p3);
                AddDeviceLine(p3, p0);
            }
            else
            {
                _clip.GetBounds(out int left, out int top, out int right, out int bottom);
                _scan.AddLine(right, top, right, bottom);
                _scan.AddLine(left, bottom, left, top);
            }

            PaintShading(shading, inverse, geometry, background: false, _fillCompositor);
        }

        /// <summary>Fills the path with the shading pattern's shading and its Background (§8.7.4.1, §8.7.4.3).</summary>
        public void FillPathWithShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            if (PaintsAsMesh(shading.Shading))
            {
                FillPathWithMeshShading(path, rule, shading);
                return;
            }

            if (path.IsEmpty || _clip.IsEmpty || _fillCompositor.PaintsNothing || !TryPrepareShading(shading, clipToBox: true, out Matrix inverse, out ShadingGeometry geometry))
            {
                return;
            }

            _scan.Reset(_width, _height);
            AddPath(path);
            PaintShading(shading, inverse, geometry, background: true, _fillCompositor, rule);
        }

        /// <summary>Paints the area the stroke covers (§8.5.3.2, outlined like <see cref="StrokePath"/>) as a fill with the shading pattern's shading.</summary>
        public void StrokePathWithShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            if (PaintsAsMesh(shading.Shading))
            {
                StrokePathWithMeshShading(path, style, shading);
                return;
            }

            if (path.IsEmpty || _clip.IsEmpty || _strokeCompositor.PaintsNothing || !TryPrepareShading(shading, clipToBox: true, out Matrix inverse, out ShadingGeometry geometry))
            {
                return;
            }

            _scan.Reset(_width, _height);
            _stroker.Stroke(path, style, _toDevice);
            PaintShading(shading, inverse, geometry, background: true, _strokeCompositor);
        }

        /// <summary>
        /// Reads the shading's geometry, inverts its transform to the device and builds its colour table; false when it cannot paint
        /// (a type not rendered yet, a singular transform, colours that paint nothing).
        /// </summary>
        private bool TryPrepareShading(in DisplayShading shading, bool clipToBox, out Matrix inverse, out ShadingGeometry geometry)
        {
            inverse = default;
            if (!ShadingGeometry.TryCreate(shading.Shading, clipToBox, out geometry)
                || !(shading.Matrix * _device).TryInvert(out inverse)
                || !IsFinite(inverse))
            {
                return false;
            }

            return _shadingSource.Prepare(_document!, shading);
        }

        /// <summary>Hands the shape's coverage to a <see cref="ShadingPainter"/>; the Background applies when <paramref name="background"/> is set and the shading has one.</summary>
        private void PaintShading(in DisplayShading shading, in Matrix inverse, in ShadingGeometry geometry, bool background, Compositor compositor, FillRule rule = FillRule.NonZero)
        {
            bool hasBackground = background && shading.PaintsBackground && _shadingSource.HasBackground;
            if (geometry.IsEmpty && !hasBackground)
            {
                _scan.Reset(_width, _height);
                return;
            }

            Bitmap target = _target!;
            var painter = new ShadingPainter(
                target.Pixels,
                target.Stride,
                inverse,
                geometry,
                _shadingSource.Table,
                hasBackground,
                _shadingSource.Background,
                shading.Shading.AntiAlias,
                compositor);
            _scan.Paint(rule, ref painter);
        }

        private void AddDeviceLine(PathPoint from, PathPoint to) =>
            _scan.AddLine(Clamp(from.X), Clamp(from.Y), Clamp(to.X), Clamp(to.Y));

        private static double Clamp(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, -CoordinateLimit, CoordinateLimit);

        /// <summary>Lets go of the render's shading; the table's buffers stay for the next render.</summary>
        private void EndShadings() => _shadingSource.Forget();
    }
}
