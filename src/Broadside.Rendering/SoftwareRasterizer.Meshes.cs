using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Function-based, triangle-mesh and patch-mesh shadings (ISO 32000-2 §8.7.4.5.2, §8.7.4.5.5 to §8.7.4.5.8).</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's mesh half: shadings whose colours are drawn as Gouraud-shaded triangles into a <see cref="MeshLayer"/>.</summary>
    private sealed partial class Backend
    {
        /// <summary>The most cells per side of the lattice a function-based shading is sampled on.</summary>
        private const int MaxFunctionCells = 256;

        /// <summary>The most times a triangle is split in four so its colours follow a non-linear conversion.</summary>
        private const int MaxSubdivision = 5;

        /// <summary>The largest difference, per RGB channel, between a converted midpoint and the RGB interpolation of its ends.</summary>
        private const float SubdivisionTolerance = 1.5f / 255;

        private readonly MeshLayer _meshLayer = new();
        private float[] _meshComponents = [];
        private float[] _meshRgb = [];
        private PdfShading? _meshRgbShading;
        private PdfColorConverter? _meshRgbConverter;
        private PixelModel _meshRgbModel;

        /// <summary>
        /// Turns <paramref name="count"/> colours converted to the pixel model's target (#114: RGB, or CMYK in a CMYK group) into the
        /// three channels the mesh layer interpolates and packs as RGB, in place: RGB as is, a gray group's level in all three, a CMYK
        /// group's complemented C, M, Y (pass 0) or complemented K (pass 1), as <see cref="PixelModel.Pack(ReadOnlySpan{float})"/> stores them.
        /// </summary>
        private void ToLayerChannels(Span<float> colors, int count)
        {
            PixelModel model = _pixelModel;
            if (model == PixelModel.Rgb)
            {
                return;
            }

            int stride = model.Components;
            for (int i = 0; i < count; i++)
            {
                ReadOnlySpan<float> c = colors.Slice(i * stride, stride);
                (float r, float g, float b) = model.Model switch
                {
                    DeviceColorModel.Gray => ((0.3f * c[0]) + (0.59f * c[1]) + (0.11f * c[2]), 0f, 0f),
                    DeviceColorModel.Cmyk when model.Pass == 0 => (1 - c[0], 1 - c[1], 1 - c[2]),
                    DeviceColorModel.Cmyk => (1 - c[3], 1 - c[3], 1 - c[3]),
                    _ => (c[0], c[1], c[2]),
                };
                if (model.Model == DeviceColorModel.Gray)
                {
                    (g, b) = (r, r);
                }

                colors[i * 3] = r;
                colors[(i * 3) + 1] = g;
                colors[(i * 3) + 2] = b;
            }
        }

        /// <summary>Whether <paramref name="shading"/> is painted through the mesh layer rather than <see cref="ShadingPainter"/>.</summary>
        private static bool PaintsAsMesh(PdfShading shading) => shading is PdfFunctionShading or PdfTriangleMeshShading or PdfPatchMeshShading;

        /// <summary>
        /// <c>sh</c> with a function-based, triangle-mesh or patch-mesh shading (§8.7.4.2): the clip's pixel bounds, or the BBox mapped to the
        /// device, painted with the shading's own points and nothing elsewhere (no Background).
        /// </summary>
        private void DrawMeshShading(in DisplayShading shading)
        {
            PdfRectangle? box = shading.Shading.BoundingBox;
            if (_clip.IsEmpty || _fillCompositor.PaintsNothing || !TryPrepareMesh(shading, out Matrix toDevice, out Matrix inverse))
            {
                return;
            }

            _scan.Reset(_width, _height);
            if (box is { } bounds)
            {
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

            PaintMeshShading(shading, toDevice, inverse, background: false, clipToBox: false, _fillCompositor, _deviceParameters.FillOverprint);
        }

        /// <summary>Fills the path with a function-based, triangle-mesh or patch-mesh shading pattern, the Background outside the shading's points (§8.7.4.3).</summary>
        private void FillPathWithMeshShading(PathView path, FillRule rule, in DisplayShading shading)
        {
            if (path.IsEmpty || _clip.IsEmpty || _fillCompositor.PaintsNothing || !TryPrepareMesh(shading, out Matrix toDevice, out Matrix inverse))
            {
                return;
            }

            _scan.Reset(_width, _height);
            AddPath(path);
            PaintMeshShading(shading, toDevice, inverse, background: true, clipToBox: true, _fillCompositor, _deviceParameters.FillOverprint, rule);
        }

        /// <summary>Paints the area a stroke covers with a function-based, triangle-mesh or patch-mesh shading pattern (§8.5.3.2).</summary>
        private void StrokePathWithMeshShading(PathView path, in StrokeStyle style, in DisplayShading shading)
        {
            if (path.IsEmpty || _clip.IsEmpty || _strokeCompositor.PaintsNothing || !TryPrepareMesh(shading, out Matrix toDevice, out Matrix inverse))
            {
                return;
            }

            _scan.Reset(_width, _height);
            _stroker.Stroke(path, style, _toDevice);
            PaintMeshShading(shading, toDevice, inverse, background: true, clipToBox: true, _strokeCompositor, _deviceParameters.StrokeOverprint);
        }

        /// <summary>The shading's transform to the device and back, and its colours; false when it cannot paint.</summary>
        private bool TryPrepareMesh(in DisplayShading shading, out Matrix toDevice, out Matrix inverse)
        {
            toDevice = shading.Matrix * _device;
            inverse = default;
            return IsFinite(toDevice)
                && toDevice.TryInvert(out inverse)
                && IsFinite(inverse)
                && _shadingSource.Prepare(_document!, shading, _pixelModel)
                && _shadingSource.Converter is { } converter && converter.OutputCount == _pixelModel.Components;
        }

        /// <summary>
        /// Draws the shading's triangles into the mesh layer over the shape's pixels and paints the shape (already in the scan
        /// converter) from it: the layer's colours, else the Background when <paramref name="background"/> is set and the shading has one.
        /// </summary>
        private void PaintMeshShading(in DisplayShading shading, in Matrix toDevice, in Matrix inverse, bool background, bool clipToBox, Compositor compositor, bool overprint, FillRule rule = FillRule.NonZero)
        {
            PdfShading model = shading.Shading;
            bool hasBackground = background && shading.PaintsBackground && _shadingSource.HasBackground;
            if (!_scan.TryGetBounds(out int left, out int top, out int right, out int bottom))
            {
                _scan.Reset(_width, _height);
                return;
            }

            _clip.GetBounds(out int clipLeft, out int clipTop, out int clipRight, out int clipBottom);
            left = Math.Max(left, clipLeft);
            top = Math.Max(top, clipTop);
            right = Math.Min(right, clipRight);
            bottom = Math.Min(bottom, clipBottom);
            if (TryGetMeshBounds(model, toDevice, out int meshLeft, out int meshTop, out int meshRight, out int meshBottom)
                && _meshLayer.Begin(Math.Max(left, meshLeft), Math.Max(top, meshTop), Math.Min(right, meshRight), Math.Min(bottom, meshBottom)))
            {
                if (clipToBox && model.BoundingBox is { } box)
                {
                    _meshLayer.UseBox(inverse, box);
                }

                switch (model)
                {
                    case PdfFunctionShading function:
                        DrawFunctionShading(function, toDevice);
                        break;
                    case PdfTriangleMeshShading mesh:
                        DrawTriangleMesh(mesh, toDevice);
                        break;
                    case PdfPatchMeshShading patches:
                        DrawPatchMesh(patches, toDevice);
                        break;
                }
            }
            else
            {
                _meshLayer.Begin(0, 0, 0, 0);
            }

            if (!_meshLayer.HasPaint && !hasBackground)
            {
                _scan.Reset(_width, _height);
                return;
            }

            if (_previewing)
            {
                // Overprint preview (#227): the mesh's RGB goes through a scratch layer and is converted to process colourants.
                _previewOutputIntent ??= shading.Conversion.OutputIntent;
                PreviewMesh(hasBackground, rule, compositor, overprint);
                return;
            }

            Bitmap target = _target!;
            var painter = new MeshPainter(target.Pixels, target.Stride, _meshLayer, hasBackground, _shadingSource.Background, compositor);
            _scan.Paint(rule, ref painter);
        }

        /// <summary>The device pixels the shading's own points can reach: its mapped domain (Type 1), its vertices' or its control points' bounds.</summary>
        private static bool TryGetMeshBounds(PdfShading model, in Matrix toDevice, out int left, out int top, out int right, out int bottom)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            switch (model)
            {
                case PdfFunctionShading function:
                    {
                        Matrix domainToDevice = function.Matrix * toDevice;
                        IReadOnlyList<double> domain = function.Domain;
                        Include(domainToDevice.Transform(domain[0], domain[2]), ref minX, ref minY, ref maxX, ref maxY);
                        Include(domainToDevice.Transform(domain[1], domain[2]), ref minX, ref minY, ref maxX, ref maxY);
                        Include(domainToDevice.Transform(domain[0], domain[3]), ref minX, ref minY, ref maxX, ref maxY);
                        Include(domainToDevice.Transform(domain[1], domain[3]), ref minX, ref minY, ref maxX, ref maxY);
                        break;
                    }

                case PdfTriangleMeshShading mesh:
                    foreach (PathPoint point in mesh.Vertices)
                    {
                        Include(toDevice.Transform(point.X, point.Y), ref minX, ref minY, ref maxX, ref maxY);
                    }

                    break;

                case PdfPatchMeshShading patches:
                    // A patch lies in the convex hull of its control points.
                    foreach (PathPoint point in patches.ControlPoints)
                    {
                        Include(toDevice.Transform(point.X, point.Y), ref minX, ref minY, ref maxX, ref maxY);
                    }

                    break;
            }

            left = PixelFloor(minX);
            top = PixelFloor(minY);
            right = PixelFloor(maxX) + 1;
            bottom = PixelFloor(maxY) + 1;
            return minX <= maxX && minY <= maxY;
        }

        private static void Include(PathPoint point, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (double.IsFinite(point.X) && double.IsFinite(point.Y))
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
        }

        private static int PixelFloor(double value) => (int)Math.Clamp(Math.Floor(value), -(1 << 30), 1 << 30);

        /// <summary>
        /// Samples a function-based shading (§8.7.4.5.2) on a lattice over the part of its domain the layer shows, about one cell
        /// per device pixel up to <see cref="MaxFunctionCells"/> per side, converts the samples in one batch and draws each cell as
        /// two Gouraud triangles. Points outside the domain get no triangle, so they stay the layer's empty pixels.
        /// </summary>
        private void DrawFunctionShading(PdfFunctionShading model, in Matrix toDevice)
        {
            Matrix domainToDevice = model.Matrix * toDevice;
            if (!IsFinite(domainToDevice) || !domainToDevice.TryInvert(out Matrix toDomain) || !IsFinite(toDomain))
            {
                return;
            }

            IReadOnlyList<double> domain = model.Domain;
            double x0 = Math.Min(domain[0], domain[1]);
            double x1 = Math.Max(domain[0], domain[1]);
            double y0 = Math.Min(domain[2], domain[3]);
            double y1 = Math.Max(domain[2], domain[3]);

            // The part of the domain behind the layer's rectangle.
            double minU = double.PositiveInfinity;
            double minV = double.PositiveInfinity;
            double maxU = double.NegativeInfinity;
            double maxV = double.NegativeInfinity;
            double layerRight = _meshLayer.Left + _meshLayer.Width;
            double layerBottom = _meshLayer.Top + _meshLayer.Height;
            Include(toDomain.Transform(_meshLayer.Left, _meshLayer.Top), ref minU, ref minV, ref maxU, ref maxV);
            Include(toDomain.Transform(layerRight, _meshLayer.Top), ref minU, ref minV, ref maxU, ref maxV);
            Include(toDomain.Transform(_meshLayer.Left, layerBottom), ref minU, ref minV, ref maxU, ref maxV);
            Include(toDomain.Transform(layerRight, layerBottom), ref minU, ref minV, ref maxU, ref maxV);
            double u0 = Math.Max(x0, minU);
            double u1 = Math.Min(x1, maxU);
            double v0 = Math.Max(y0, minV);
            double v1 = Math.Min(y1, maxV);
            if (!(u0 <= u1 && v0 <= v1))
            {
                return;
            }

            int columns = Cells(domainToDevice.A * (u1 - u0), domainToDevice.B * (u1 - u0));
            int rows = Cells(domainToDevice.C * (v1 - v0), domainToDevice.D * (v1 - v0));
            int across = columns + 1;
            int points = across * (rows + 1);
            PdfColorConverter converter = _shadingSource.Converter!;
            int n = model.ColorComponentCount;
            if (converter.InputCount != n)
            {
                return;
            }

            Pooled.Ensure(ref _meshComponents, points * n);
            Pooled.Ensure(ref _meshRgb, points * _pixelModel.Components);
            _meshRgbShading = null;
            Span<float> input = stackalloc float[2];
            for (int j = 0, k = 0; j <= rows; j++)
            {
                input[1] = (float)(v0 + ((v1 - v0) * j / rows));
                for (int i = 0; i <= columns; i++, k++)
                {
                    input[0] = (float)(u0 + ((u1 - u0) * i / columns));
                    model.EvaluateFunction(input, _meshComponents.AsSpan(k * n, n));
                }
            }

            converter.Convert(_meshComponents.AsSpan(0, points * n), _meshRgb.AsSpan(0, points * _pixelModel.Components), points);
            ToLayerChannels(_meshRgb, points);
            for (int j = 0; j < rows; j++)
            {
                double va = v0 + ((v1 - v0) * j / rows);
                double vb = v0 + ((v1 - v0) * (j + 1) / rows);
                for (int i = 0; i < columns; i++)
                {
                    double ua = u0 + ((u1 - u0) * i / columns);
                    double ub = u0 + ((u1 - u0) * (i + 1) / columns);
                    int k = (j * across) + i;
                    MeshVertex p00 = LatticeVertex(domainToDevice, ua, va, k);
                    MeshVertex p10 = LatticeVertex(domainToDevice, ub, va, k + 1);
                    MeshVertex p01 = LatticeVertex(domainToDevice, ua, vb, k + across);
                    MeshVertex p11 = LatticeVertex(domainToDevice, ub, vb, k + across + 1);
                    _meshLayer.FillTriangle(p00, p10, p01);
                    _meshLayer.FillTriangle(p10, p01, p11);
                }
            }
        }

        /// <summary>The cells along a lattice side whose device extent is (<paramref name="dx"/>, <paramref name="dy"/>): one per pixel, 1 to <see cref="MaxFunctionCells"/>.</summary>
        private static int Cells(double dx, double dy)
        {
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            return double.IsFinite(length) ? (int)Math.Clamp(Math.Ceiling(length), 1, MaxFunctionCells) : MaxFunctionCells;
        }

        private MeshVertex LatticeVertex(in Matrix domainToDevice, double u, double v, int index)
        {
            PathPoint p = domainToDevice.Transform(u, v);
            ReadOnlySpan<float> rgb = _meshRgb.AsSpan(index * 3, 3);
            return new MeshVertex(Clamp(p.X), Clamp(p.Y), rgb[0], rgb[1], rgb[2]);
        }

        /// <summary>
        /// Draws a free-form or lattice-form mesh (§8.7.4.5.5, §8.7.4.5.6) triangle by triangle in stream order. With a function the
        /// layer interpolates t and reads the colour from the shading's table of t; without one the vertices' colours are converted
        /// once, and a triangle whose conversion is not linear across it is split until RGB interpolation follows the shading's space.
        /// </summary>
        private void DrawTriangleMesh(PdfTriangleMeshShading model, in Matrix toDevice)
        {
            ReadOnlySpan<PathPoint> points = model.Vertices;
            ReadOnlySpan<int> triangles = model.Triangles;
            ReadOnlySpan<float> colors = model.VertexColors;
            if (model.HasFunction)
            {
                _meshLayer.UseTable(_shadingSource.Table);
                (double t0, double t1) = _shadingSource.ParameterDomain;
                double scale = t1 != t0 ? 1 / (t1 - t0) : 0;
                for (int k = 0; k + 2 < triangles.Length; k += 3)
                {
                    _meshLayer.FillTriangle(
                        ParametricVertex(toDevice, points[triangles[k]], (float)((colors[triangles[k]] - t0) * scale)),
                        ParametricVertex(toDevice, points[triangles[k + 1]], (float)((colors[triangles[k + 1]] - t0) * scale)),
                        ParametricVertex(toDevice, points[triangles[k + 2]], (float)((colors[triangles[k + 2]] - t0) * scale)));
                }

                return;
            }

            PdfColorConverter converter = _shadingSource.Converter!;
            int n = model.ColorStride;
            if (converter.InputCount != n || n > PdfColor.MaxComponents)
            {
                return;
            }

            if (!ReferenceEquals(_meshRgbShading, model) || !ReferenceEquals(_meshRgbConverter, converter) || _meshRgbModel != _pixelModel)
            {
                Pooled.Ensure(ref _meshRgb, points.Length * _pixelModel.Components);
                converter.Convert(colors, _meshRgb.AsSpan(0, points.Length * _pixelModel.Components), points.Length);
                ToLayerChannels(_meshRgb, points.Length);
                _meshRgbShading = model;
                _meshRgbConverter = converter;
                _meshRgbModel = _pixelModel;
            }

            for (int k = 0; k + 2 < triangles.Length; k += 3)
            {
                int a = triangles[k];
                int b = triangles[k + 1];
                int c = triangles[k + 2];
                FillGouraud(
                    ColorVertex(toDevice, points[a], a),
                    ColorVertex(toDevice, points[b], b),
                    ColorVertex(toDevice, points[c], c),
                    colors.Slice(a * n, n),
                    colors.Slice(b * n, n),
                    colors.Slice(c * n, n),
                    converter,
                    0);
            }
        }

        private static MeshVertex ParametricVertex(in Matrix toDevice, PathPoint point, float s)
        {
            PathPoint p = toDevice.Transform(point.X, point.Y);
            return new MeshVertex(Clamp(p.X), Clamp(p.Y), s, 0, 0);
        }

        private MeshVertex ColorVertex(in Matrix toDevice, PathPoint point, int index)
        {
            PathPoint p = toDevice.Transform(point.X, point.Y);
            ReadOnlySpan<float> rgb = _meshRgb.AsSpan(index * 3, 3);
            return new MeshVertex(Clamp(p.X), Clamp(p.Y), rgb[0], rgb[1], rgb[2]);
        }

        /// <summary>
        /// Draws a triangle whose vertices carry RGB (<paramref name="a"/>, ...) and the shading's own components
        /// (<paramref name="sa"/>, ...). Gouraud interpolation is defined in the shading's colour space (§8.7.4.5.5); RGB interpolation
        /// equals it only where the conversion is linear, so while the converted midpoint of an edge strays from the RGB midpoint by
        /// more than <see cref="SubdivisionTolerance"/> the triangle is split in four at its edges' midpoints, up to
        /// <see cref="MaxSubdivision"/> times, and never below two pixels across.
        /// </summary>
        private void FillGouraud(in MeshVertex a, in MeshVertex b, in MeshVertex c, ReadOnlySpan<float> sa, ReadOnlySpan<float> sb, ReadOnlySpan<float> sc, PdfColorConverter converter, int depth)
        {
            double minX = Math.Min(a.X, Math.Min(b.X, c.X));
            double maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            double minY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            double maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            if (maxX < _meshLayer.Left || minX > _meshLayer.Left + _meshLayer.Width || maxY < _meshLayer.Top || minY > _meshLayer.Top + _meshLayer.Height)
            {
                return;
            }

            if (depth >= MaxSubdivision || Math.Max(maxX - minX, maxY - minY) <= 2)
            {
                _meshLayer.FillTriangle(a, b, c);
                return;
            }

            int n = sa.Length;
            Span<float> sab = stackalloc float[n];
            Span<float> sbc = stackalloc float[n];
            Span<float> sca = stackalloc float[n];
            for (int i = 0; i < n; i++)
            {
                sab[i] = (sa[i] + sb[i]) / 2;
                sbc[i] = (sb[i] + sc[i]) / 2;
                sca[i] = (sc[i] + sa[i]) / 2;
            }

            Span<float> rgb = stackalloc float[9];
            converter.Convert(sab, rgb[..3]);
            converter.Convert(sbc, rgb[3..6]);
            converter.Convert(sca, rgb[6..]);
            if (Near(rgb[..3], a, b) && Near(rgb[3..6], b, c) && Near(rgb[6..], c, a))
            {
                _meshLayer.FillTriangle(a, b, c);
                return;
            }

            var ab = new MeshVertex((a.X + b.X) / 2, (a.Y + b.Y) / 2, rgb[0], rgb[1], rgb[2]);
            var bc = new MeshVertex((b.X + c.X) / 2, (b.Y + c.Y) / 2, rgb[3], rgb[4], rgb[5]);
            var ca = new MeshVertex((c.X + a.X) / 2, (c.Y + a.Y) / 2, rgb[6], rgb[7], rgb[8]);
            FillGouraud(a, ab, ca, sa, sab, sca, converter, depth + 1);
            FillGouraud(ab, b, bc, sab, sb, sbc, converter, depth + 1);
            FillGouraud(ca, bc, c, sca, sbc, sc, converter, depth + 1);
            FillGouraud(ab, bc, ca, sab, sbc, sca, converter, depth + 1);
        }

        private static bool Near(ReadOnlySpan<float> converted, in MeshVertex from, in MeshVertex to) =>
            Math.Abs(converted[0] - ((from.C0 + to.C0) / 2)) <= SubdivisionTolerance
            && Math.Abs(converted[1] - ((from.C1 + to.C1) / 2)) <= SubdivisionTolerance
            && Math.Abs(converted[2] - ((from.C2 + to.C2) / 2)) <= SubdivisionTolerance;

        /// <summary>Lets go of the render's mesh colours; the buffers stay for the next render.</summary>
        private void EndMeshes()
        {
            _meshRgbShading = null;
            _meshRgbConverter = null;
            _meshRgbModel = default;
        }

        /// <summary>Returns the mesh buffers to the pool.</summary>
        private void DisposeMeshes()
        {
            EndMeshes();
            DisposePatches();
            _meshLayer.Release();
            Pooled.Return(ref _meshComponents);
            Pooled.Return(ref _meshRgb);
        }
    }
}
