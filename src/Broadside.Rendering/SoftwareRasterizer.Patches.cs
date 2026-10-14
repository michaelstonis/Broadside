using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Coons and tensor-product patch mesh shadings (ISO 32000-2 §8.7.4.5.7, §8.7.4.5.8).</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>Gets the number of triangles the last render cut its patch meshes into, before any split for a non-linear colour conversion.</summary>
    /// <remarks>Observes the adaptive subdivision of §8.7.4.5.7 and §8.7.4.5.8 (see <see cref="Backend.DrawPatchMesh"/>).</remarks>
    internal long PatchTriangleCount => _backend.PatchTriangles;

    /// <summary>The backend's patch half: each patch is cut into a grid of Gouraud triangles drawn into the mesh layer.</summary>
    private sealed partial class Backend
    {
        /// <summary>The most grid cells along each side of a patch.</summary>
        private const int MaxPatchSteps = 64;

        /// <summary>The most triangles one paint cuts its visible patches into; past it every patch gets fewer cells.</summary>
        private const int MaxPatchTriangles = 1 << 21;

        /// <summary>The largest distance, in device pixels, between a patch's surface and the triangles that stand for it.</summary>
        private const double PatchFlatness = 0.25;

        /// <summary>The largest difference, per RGB channel, between the bilinear colour of a grid cell and its two triangles'.</summary>
        private const double PatchColorTolerance = 1.0 / 255;

        private PathPoint[] _patchGrid = [];
        private float[] _patchComponents = [];
        private float[] _patchRgb = [];
        private double[] _patchWeights = [];

        /// <summary>Gets the triangles cut from patches since <see cref="Begin"/>.</summary>
        public long PatchTriangles { get; private set; }

        /// <summary>
        /// Draws a Coons or tensor-product patch mesh (§8.7.4.5.7, §8.7.4.5.8). Every patch is held as its 16 tensor control points
        /// (a Coons patch's interior four computed by the decoder), mapped to the device, where S(u, v) = Σ p<sub>ij</sub>
        /// B<sub>i</sub>(u) B<sub>j</sub>(v) is evaluated on a grid of N × N cells and each cell drawn as two triangles; colours (or
        /// t) are the bilinear blend of the four corners' at each grid point.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Adaptive subdivision: N is the smallest count that keeps the triangles within <see cref="PatchFlatness"/> device pixels of
        /// the surface (the chord bound 6/8 · |Δ²p| / N² along u and v plus the twist bound 9/4 · |Δ<sub>u</sub>Δ<sub>v</sub>p| / N²
        /// of the control net) and the bilinear colour within <see cref="PatchColorTolerance"/> of each cell's triangles (the
        /// corners' colour twist / 4N²), taken over every patch the layer shows, so N grows as the square root of the device scale
        /// and the triangle count at most linearly with it: from 72 to 300 DPI by at most (⌈√(300/72) · N₇₂⌉ / N₇₂)², which is 9
        /// for N₇₂ = 1 and tends to 300/72 ≈ 4.17. N is capped at <see cref="MaxPatchSteps"/> and so that the paint cuts no more than
        /// <see cref="MaxPatchTriangles"/> triangles.
        /// </para>
        /// <para>
        /// No cracks: one N serves every patch of the paint, and a grid point on a patch's edge is computed from that edge's four
        /// control points alone, by an evaluation that gives the same bits whichever way the edge is traversed. Two patches sharing an
        /// edge (by an edge flag or by repeating its points) therefore share its grid points exactly, and the mesh layer's half-open
        /// spans give each pixel centre on the edge to exactly one of them.
        /// </para>
        /// <para>
        /// Order: patches in stream order, and in each patch rows of increasing v, cells of increasing u, so where a patch folds over
        /// itself the point with the larger v, then the larger u, paints last and stays (§8.7.4.5.7), to the resolution of a cell.
        /// </para>
        /// </remarks>
        private void DrawPatchMesh(PdfPatchMeshShading model, in Matrix toDevice)
        {
            int patches = model.PatchCount;
            ReadOnlySpan<PathPoint> control = model.ControlPoints;
            ReadOnlySpan<float> colors = model.CornerColors;
            PdfColorConverter converter = _shadingSource.Converter!;
            int n = model.ColorStride;
            double t0 = 0;
            double scale = 0;
            if (model.HasFunction)
            {
                _meshLayer.UseTable(_shadingSource.Table);
                (double first, double last) = _shadingSource.ParameterDomain;
                t0 = first;
                scale = last != first ? 1 / (last - first) : 0;
            }
            else
            {
                if (converter.InputCount != n || n > PdfColor.MaxComponents)
                {
                    return;
                }

                int corners = patches * 4;
                if (!ReferenceEquals(_meshRgbShading, model) || !ReferenceEquals(_meshRgbConverter, converter))
                {
                    Pooled.Ensure(ref _meshRgb, corners * 3);
                    converter.Convert(colors, _meshRgb.AsSpan(0, corners * 3), corners);
                    _meshRgbShading = model;
                    _meshRgbConverter = converter;
                }
            }

            // The grid every visible patch is cut into.
            Span<PathPoint> net = stackalloc PathPoint[16];
            int visible = 0;
            int steps = 1;
            for (int patch = 0; patch < patches; patch++)
            {
                if (MapPatch(control.Slice(patch * 16, 16), toDevice, net))
                {
                    visible++;
                    double twist = model.HasFunction
                        ? Math.Abs(((colors[patch * 4] - colors[(patch * 4) + 1] + colors[(patch * 4) + 2] - colors[(patch * 4) + 3]) * scale))
                        : RgbTwist(_meshRgb.AsSpan(patch * 12, 12));
                    steps = Math.Max(steps, PatchSteps(net, twist));
                }
            }

            if (visible == 0)
            {
                return;
            }

            steps = Math.Min(steps, Math.Max(1, (int)Math.Sqrt(MaxPatchTriangles / (2.0 * visible))));
            int across = steps + 1;
            int points = across * across;
            Pooled.Ensure(ref _patchGrid, points);
            Pooled.Ensure(ref _patchComponents, points * n);
            Pooled.Ensure(ref _patchRgb, points * 3);
            Pooled.Ensure(ref _patchWeights, across * 4);
            for (int k = 0; k <= steps; k++)
            {
                // Written so that k and steps − k give the same weights in reverse, bit for bit.
                double s = (double)k / steps;
                double t = (double)(steps - k) / steps;
                _patchWeights[k * 4] = t * t * t;
                _patchWeights[(k * 4) + 1] = 3 * s * (t * t);
                _patchWeights[(k * 4) + 2] = 3 * t * (s * s);
                _patchWeights[(k * 4) + 3] = s * s * s;
            }

            for (int patch = 0; patch < patches; patch++)
            {
                if (!MapPatch(control.Slice(patch * 16, 16), toDevice, net))
                {
                    continue;
                }

                EvaluateGrid(net, steps);
                ReadOnlySpan<float> corners = colors.Slice(patch * 4 * n, 4 * n);
                BlendCorners(corners, n, steps);
                if (model.HasFunction)
                {
                    DrawParametricGrid(steps, t0, scale);
                }
                else
                {
                    converter.Convert(_patchComponents.AsSpan(0, points * n), _patchRgb.AsSpan(0, points * 3), points);
                    DrawColorGrid(steps, n, converter);
                }

                PatchTriangles += 2L * steps * steps;
            }
        }

        /// <summary>Maps a patch's control points to the device; false when they are not finite or their hull misses the layer.</summary>
        private bool MapPatch(ReadOnlySpan<PathPoint> points, in Matrix toDevice, Span<PathPoint> net)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            for (int k = 0; k < 16; k++)
            {
                PathPoint p = toDevice.Transform(points[k].X, points[k].Y);
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
                {
                    return false;
                }

                net[k] = new PathPoint(Clamp(p.X), Clamp(p.Y));
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            return maxX >= _meshLayer.Left && minX <= _meshLayer.Left + _meshLayer.Width
                && maxY >= _meshLayer.Top && minY <= _meshLayer.Top + _meshLayer.Height;
        }

        /// <summary>The grid cells along each side that keep a patch within <see cref="PatchFlatness"/> and <see cref="PatchColorTolerance"/>.</summary>
        private static int PatchSteps(ReadOnlySpan<PathPoint> net, double colorTwist)
        {
            // p_ij at 4i + j: second differences along u (i) and v (j), and the twist of each control-net cell.
            double uu = 0;
            double vv = 0;
            double uv = 0;
            for (int a = 0; a < 4; a++)
            {
                for (int b = 0; b < 2; b++)
                {
                    uu = Math.Max(uu, Length(net[(b * 4) + a], net[((b + 1) * 4) + a], net[((b + 2) * 4) + a]));
                    vv = Math.Max(vv, Length(net[(a * 4) + b], net[(a * 4) + b + 1], net[(a * 4) + b + 2]));
                }
            }

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    PathPoint p00 = net[(i * 4) + j];
                    PathPoint p10 = net[((i + 1) * 4) + j];
                    PathPoint p01 = net[(i * 4) + j + 1];
                    PathPoint p11 = net[((i + 1) * 4) + j + 1];
                    double x = p00.X - p10.X - p01.X + p11.X;
                    double y = p00.Y - p10.Y - p01.Y + p11.Y;
                    uv = Math.Max(uv, Math.Sqrt((x * x) + (y * y)));
                }
            }

            double geometry = Math.Sqrt(((0.75 * (uu + vv)) + (2.25 * uv)) / PatchFlatness);
            double color = Math.Sqrt(colorTwist / (4 * PatchColorTolerance));
            double steps = Math.Ceiling(Math.Max(geometry, color));
            return double.IsFinite(steps) ? (int)Math.Clamp(steps, 1, MaxPatchSteps) : MaxPatchSteps;
        }

        private static double Length(PathPoint a, PathPoint b, PathPoint c)
        {
            double x = a.X - (2 * b.X) + c.X;
            double y = a.Y - (2 * b.Y) + c.Y;
            return Math.Sqrt((x * x) + (y * y));
        }

        /// <summary>The largest channel of c00 − c03 + c33 − c30, the corners' converted colours in that order.</summary>
        private static double RgbTwist(ReadOnlySpan<float> rgb)
        {
            double twist = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                twist = Math.Max(twist, Math.Abs(rgb[channel] - rgb[3 + channel] + rgb[6 + channel] - rgb[9 + channel]));
            }

            return twist;
        }

        /// <summary>
        /// Fills the grid with S(u<sub>i</sub>, v<sub>j</sub>) at index j × (steps + 1) + i: the four column curves at v<sub>j</sub>, then
        /// the curve through them at u<sub>i</sub>. On an edge every term but one has a zero weight, so the point is the edge curve's
        /// own, evaluated by <see cref="Bezier"/> alone.
        /// </summary>
        private void EvaluateGrid(ReadOnlySpan<PathPoint> net, int steps)
        {
            int across = steps + 1;
            for (int j = 0; j <= steps; j++)
            {
                ReadOnlySpan<double> wv = _patchWeights.AsSpan(j * 4, 4);
                PathPoint q0 = Bezier(net[0], net[1], net[2], net[3], wv);
                PathPoint q1 = Bezier(net[4], net[5], net[6], net[7], wv);
                PathPoint q2 = Bezier(net[8], net[9], net[10], net[11], wv);
                PathPoint q3 = Bezier(net[12], net[13], net[14], net[15], wv);
                Span<PathPoint> row = _patchGrid.AsSpan(j * across, across);
                for (int i = 0; i <= steps; i++)
                {
                    row[i] = Bezier(q0, q1, q2, q3, _patchWeights.AsSpan(i * 4, 4));
                }
            }
        }

        /// <summary>A cubic Bézier point from its Bernstein weights, summed outer pair then inner pair, so the reversed curve at the reversed weights gives the same bits.</summary>
        private static PathPoint Bezier(PathPoint p0, PathPoint p1, PathPoint p2, PathPoint p3, ReadOnlySpan<double> w) => new(
            ((p0.X * w[0]) + (p3.X * w[3])) + ((p1.X * w[1]) + (p2.X * w[2])),
            ((p0.Y * w[0]) + (p3.Y * w[3])) + ((p1.Y * w[1]) + (p2.Y * w[2])));

        /// <summary>The bilinear blend of the corner colours (c00, c03, c33, c30) at every grid point, n components each.</summary>
        private void BlendCorners(ReadOnlySpan<float> corners, int n, int steps)
        {
            int across = steps + 1;
            for (int j = 0; j <= steps; j++)
            {
                double v = (double)j / steps;
                for (int i = 0; i <= steps; i++)
                {
                    double u = (double)i / steps;
                    Span<float> target = _patchComponents.AsSpan(((j * across) + i) * n, n);
                    for (int c = 0; c < n; c++)
                    {
                        double left = ((1 - v) * corners[c]) + (v * corners[n + c]);
                        double right = ((1 - v) * corners[(3 * n) + c]) + (v * corners[(2 * n) + c]);
                        target[c] = (float)(((1 - u) * left) + (u * right));
                    }
                }
            }
        }

        /// <summary>Draws the grid's cells, rows of increasing v and cells of increasing u, with t read through the shading's table.</summary>
        private void DrawParametricGrid(int steps, double t0, double scale)
        {
            int across = steps + 1;
            for (int j = 0; j < steps; j++)
            {
                for (int i = 0; i < steps; i++)
                {
                    int k = (j * across) + i;
                    MeshVertex a = GridVertex(k, (float)((_patchComponents[k] - t0) * scale));
                    MeshVertex b = GridVertex(k + 1, (float)((_patchComponents[k + 1] - t0) * scale));
                    MeshVertex c = GridVertex(k + across, (float)((_patchComponents[k + across] - t0) * scale));
                    MeshVertex d = GridVertex(k + across + 1, (float)((_patchComponents[k + across + 1] - t0) * scale));
                    _meshLayer.FillTriangle(a, b, c);
                    _meshLayer.FillTriangle(b, c, d);
                }
            }
        }

        /// <summary>Draws the grid's cells, rows of increasing v and cells of increasing u, split where the colour conversion bends.</summary>
        private void DrawColorGrid(int steps, int n, PdfColorConverter converter)
        {
            int across = steps + 1;
            for (int j = 0; j < steps; j++)
            {
                for (int i = 0; i < steps; i++)
                {
                    int k = (j * across) + i;
                    MeshVertex a = GridColorVertex(k);
                    MeshVertex b = GridColorVertex(k + 1);
                    MeshVertex c = GridColorVertex(k + across);
                    MeshVertex d = GridColorVertex(k + across + 1);
                    ReadOnlySpan<float> sa = _patchComponents.AsSpan(k * n, n);
                    ReadOnlySpan<float> sb = _patchComponents.AsSpan((k + 1) * n, n);
                    ReadOnlySpan<float> sc = _patchComponents.AsSpan((k + across) * n, n);
                    ReadOnlySpan<float> sd = _patchComponents.AsSpan((k + across + 1) * n, n);
                    FillGouraud(a, b, c, sa, sb, sc, converter, 0);
                    FillGouraud(b, c, d, sb, sc, sd, converter, 0);
                }
            }
        }

        private MeshVertex GridVertex(int index, float s)
        {
            PathPoint p = _patchGrid[index];
            return new MeshVertex(p.X, p.Y, s, 0, 0);
        }

        private MeshVertex GridColorVertex(int index)
        {
            PathPoint p = _patchGrid[index];
            ReadOnlySpan<float> rgb = _patchRgb.AsSpan(index * 3, 3);
            return new MeshVertex(p.X, p.Y, rgb[0], rgb[1], rgb[2]);
        }

        /// <summary>Starts the render's patch count.</summary>
        private void BeginPatches() => PatchTriangles = 0;

        /// <summary>Returns the patch buffers to the pool.</summary>
        private void DisposePatches()
        {
            Pooled.Return(ref _patchGrid);
            Pooled.Return(ref _patchComponents);
            Pooled.Return(ref _patchRgb);
            Pooled.Return(ref _patchWeights);
        }
    }
}
