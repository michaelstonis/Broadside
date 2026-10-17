using Broadside.Graphics;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// Turns paths into triangles for the stencil buffer: fills as fans from one anchor (the stencil counts the winding, so the triangles
/// need not be a tessellation), strokes as one quad per flattened segment plus joins and caps, all drawn in the "union" stencil mode
/// (overlaps paint once). Curves are flattened to 0.2 device pixel. Scratch buffers are kept, so a warm render allocates nothing.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.5.3.3 (filling, non-zero and even-odd rules), §8.5.3.2 and §8.4.3.2 to §8.4.3.6 (stroking: line width, caps, joins,
/// miter limit, dash pattern), §8.5.2.2 (Bézier curves). A line narrower than one device pixel is drawn one pixel wide (§8.4.3.2: the
/// thinnest line the device can render).
/// </remarks>
internal sealed class Tessellator
{
    private double[] _xs = new double[1024];
    private double[] _ys = new double[1024];
    private int _points;
    private int[] _starts = new int[64];
    private bool[] _closed = new bool[64];
    private int _contours;

    // Dash output.
    private double[] _dx = new double[1024];
    private double[] _dy = new double[1024];
    private int _dashPoints;
    private int[] _dashStarts = new int[64];
    private bool[] _dashSkip = new bool[64];
    private int _dashes;

    private CommandList _out = null!;
    private Matrix _emit;
    private double _left;
    private double _top;
    private double _right;
    private double _bottom;

    public double Left => _left;

    public double Top => _top;

    public double Right => _right;

    public double Bottom => _bottom;

    public int ContourCount => _contours;

    public void Begin(CommandList output)
    {
        _out = output;
        _left = double.MaxValue;
        _top = double.MaxValue;
        _right = double.MinValue;
        _bottom = double.MinValue;
    }

    /// <summary>Flattens <paramref name="path"/> mapped through <paramref name="transform"/> into contours (in the target space).</summary>
    public void Flatten(PathView path, in Matrix transform, double tolerance)
    {
        _points = 0;
        _contours = 0;
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        int p = 0;
        double cx = 0;
        double cy = 0;
        double sx = 0;
        double sy = 0;
        bool open = false;
        foreach (PathVerb verb in verbs)
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    {
                        PathPoint q = transform.Transform(points[p++]);
                        cx = sx = q.X;
                        cy = sy = q.Y;
                        StartContour();
                        AddPoint(cx, cy);
                        open = true;
                        break;
                    }

                case PathVerb.LineTo:
                    {
                        if (!open)
                        {
                            StartContour();
                            AddPoint(cx, cy);
                            open = true;
                        }

                        PathPoint q = transform.Transform(points[p++]);
                        cx = q.X;
                        cy = q.Y;
                        AddPoint(cx, cy);
                        break;
                    }

                case PathVerb.QuadTo:
                    {
                        if (!open)
                        {
                            StartContour();
                            AddPoint(cx, cy);
                            open = true;
                        }

                        PathPoint c1 = transform.Transform(points[p]);
                        PathPoint end = transform.Transform(points[p + 1]);
                        p += 2;
                        double ddx = cx - (2 * c1.X) + end.X;
                        double ddy = cy - (2 * c1.Y) + end.Y;
                        int n = Segments(0.25 * Math.Sqrt((ddx * ddx) + (ddy * ddy)), tolerance);
                        for (int i = 1; i <= n; i++)
                        {
                            double t = (double)i / n;
                            double u = 1 - t;
                            AddPoint((u * u * cx) + (2 * u * t * c1.X) + (t * t * end.X), (u * u * cy) + (2 * u * t * c1.Y) + (t * t * end.Y));
                        }

                        cx = end.X;
                        cy = end.Y;
                        break;
                    }

                case PathVerb.CubicTo:
                    {
                        if (!open)
                        {
                            StartContour();
                            AddPoint(cx, cy);
                            open = true;
                        }

                        PathPoint c1 = transform.Transform(points[p]);
                        PathPoint c2 = transform.Transform(points[p + 1]);
                        PathPoint end = transform.Transform(points[p + 2]);
                        p += 3;
                        double ax = cx - (2 * c1.X) + c2.X;
                        double ay = cy - (2 * c1.Y) + c2.Y;
                        double bx = c1.X - (2 * c2.X) + end.X;
                        double by = c1.Y - (2 * c2.Y) + end.Y;
                        double dd = Math.Max(Math.Sqrt((ax * ax) + (ay * ay)), Math.Sqrt((bx * bx) + (by * by)));
                        int n = Segments(0.75 * dd, tolerance);
                        for (int i = 1; i <= n; i++)
                        {
                            double t = (double)i / n;
                            double u = 1 - t;
                            double a = u * u * u;
                            double b = 3 * u * u * t;
                            double c = 3 * u * t * t;
                            double d = t * t * t;
                            AddPoint((a * cx) + (b * c1.X) + (c * c2.X) + (d * end.X), (a * cy) + (b * c1.Y) + (c * c2.Y) + (d * end.Y));
                        }

                        cx = end.X;
                        cy = end.Y;
                        break;
                    }

                case PathVerb.Close:
                    if (open)
                    {
                        _closed[_contours - 1] = true;
                    }

                    cx = sx;
                    cy = sy;
                    open = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Snaps the flattened points (in device space) as stroke adjustment does: to <c>floor(v) + offset</c>, or to the nearest pixel
    /// boundary when <paramref name="offset"/> is 0; then maps them by <paramref name="back"/> (to pen space).
    /// </summary>
    public void Snap(double offset, in Matrix back)
    {
        for (int i = 0; i < _points; i++)
        {
            double x = offset > 0 ? Math.Floor(_xs[i]) + offset : Math.Floor(_xs[i] + 0.5);
            double y = offset > 0 ? Math.Floor(_ys[i]) + offset : Math.Floor(_ys[i] + 0.5);
            _xs[i] = (x * back.A) + (y * back.C) + back.E;
            _ys[i] = (x * back.B) + (y * back.D) + back.F;
        }
    }

    /// <summary>Copies the flattened contours: <c>[contours, start0 .. startN-1, points, x0, y0, x1, y1 ..]</c> (for the glyph cache).</summary>
    public float[] SaveContours()
    {
        var saved = new float[2 + _contours + (2 * _points)];
        saved[0] = _contours;
        for (int c = 0; c < _contours; c++)
        {
            saved[1 + c] = _starts[c];
        }

        saved[1 + _contours] = _points;
        int o = 2 + _contours;
        for (int i = 0; i < _points; i++)
        {
            saved[o++] = (float)_xs[i];
            saved[o++] = (float)_ys[i];
        }

        return saved;
    }

    /// <summary>Loads contours saved by <see cref="SaveContours"/>, mapped through <paramref name="transform"/>.</summary>
    public void LoadContours(float[] saved, in Matrix transform)
    {
        _contours = 0;
        _points = 0;
        int contours = (int)saved[0];
        int points = (int)saved[1 + contours];
        for (int c = 0; c < contours; c++)
        {
            StartContour();
            _starts[c] = (int)saved[1 + c];
        }

        int o = 2 + contours;
        for (int i = 0; i < points; i++)
        {
            double x = saved[o++];
            double y = saved[o++];
            AddPoint((x * transform.A) + (y * transform.C) + transform.E, (x * transform.B) + (y * transform.D) + transform.F);
        }
    }

    /// <summary>Whether the flattened path is one contour of three or four points turning one way (drawn without the stencil).</summary>
    public bool IsSimpleConvex()
    {
        if (_contours != 1)
        {
            return false;
        }

        int n = _points;
        if (n > 1 && _xs[n - 1] == _xs[0] && _ys[n - 1] == _ys[0])
        {
            n--;
        }

        if (n is < 3 or > 4)
        {
            return false;
        }

        int sign = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int k = (i + 2) % n;
            double cross = ((_xs[j] - _xs[i]) * (_ys[k] - _ys[j])) - ((_ys[j] - _ys[i]) * (_xs[k] - _xs[j]));
            int s = cross > 1e-9 ? 1 : cross < -1e-9 ? -1 : 0;
            if (s == 0)
            {
                continue;
            }

            if (sign != 0 && s != sign)
            {
                return false;
            }

            sign = s;
        }

        return sign != 0;
    }

    /// <summary>Emits the flattened contours (already in device space) as fans from one anchor; returns the vertex count.</summary>
    public int EmitFill()
    {
        int start = _out.VertexCount;
        if (_points == 0)
        {
            return 0;
        }

        double ax = _xs[0];
        double ay = _ys[0];
        for (int c = 0; c < _contours; c++)
        {
            int first = _starts[c];
            int end = c + 1 < _contours ? _starts[c + 1] : _points;
            int n = end - first;
            if (n < 2)
            {
                continue;
            }

            for (int i = first; i < end; i++)
            {
                int j = i + 1 < end ? i + 1 : first;
                Bound(_xs[i], _ys[i]);
                _out.Add((float)ax, (float)ay);
                _out.Add((float)_xs[i], (float)_ys[i]);
                _out.Add((float)_xs[j], (float)_ys[j]);
            }
        }

        return _out.VertexCount - start;
    }

    /// <summary>Emits the single convex contour as a fan from its first point; returns the vertex count.</summary>
    public int EmitConvex()
    {
        int start = _out.VertexCount;
        int n = _points;
        if (n > 1 && _xs[n - 1] == _xs[0] && _ys[n - 1] == _ys[0])
        {
            n--;
        }

        for (int i = 1; i + 1 < n; i++)
        {
            Bound(_xs[0], _ys[0]);
            Bound(_xs[i], _ys[i]);
            Bound(_xs[i + 1], _ys[i + 1]);
            _out.Add((float)_xs[0], (float)_ys[0]);
            _out.Add((float)_xs[i], (float)_ys[i]);
            _out.Add((float)_xs[i + 1], (float)_ys[i + 1]);
        }

        return _out.VertexCount - start;
    }

    /// <summary>
    /// Strokes the flattened contours (in pen space) with half-width <paramref name="half"/>, emitting device-space triangles through
    /// <paramref name="toDevice"/>; returns the vertex count.
    /// </summary>
    public int EmitStroke(in StrokeStyle style, double half, in Matrix toDevice, double deviceScale, bool device)
    {
        int start = _out.VertexCount;
        _emit = toDevice;
        double radius = half * deviceScale;
        int disk = radius <= 0.5 ? 8 : Math.Clamp((int)Math.Ceiling(Math.PI / Math.Acos(Math.Max(-1, 1 - (0.25 / radius)))), 8, 128);
        ReadOnlySpan<double> dashes = style.DashArray;
        double total = 0;
        foreach (double d in dashes)
        {
            total += Math.Max(0, d);
        }

        bool dashed = dashes.Length > 0 && total > 0;
        if (dashed && !device)
        {
            Dash(dashes, total, style.DashPhase);
            StrokeDashes(style, half, disk);
        }
        else if (dashed)
        {
            // The dash lengths are in user space but a thin line is stroked in device space: dash with the lengths scaled.
            Span<double> scaled = dashes.Length <= 32 ? stackalloc double[dashes.Length] : new double[dashes.Length];
            for (int i = 0; i < dashes.Length; i++)
            {
                scaled[i] = dashes[i] * deviceScale;
            }

            Dash(scaled, total * deviceScale, style.DashPhase * deviceScale);
            StrokeDashes(style, half, disk);
        }
        else
        {
            for (int c = 0; c < _contours; c++)
            {
                int first = _starts[c];
                int end = c + 1 < _contours ? _starts[c + 1] : _points;
                StrokePolyline(_xs, _ys, first, end, _closed[c], style, half, disk);
            }
        }

        return _out.VertexCount - start;
    }

    private void StrokeDashes(in StrokeStyle style, double half, int disk)
    {
        for (int d = 0; d < _dashes; d++)
        {
            if (_dashSkip[d])
            {
                continue;
            }

            int first = _dashStarts[d];
            int end = d + 1 < _dashes ? _dashStarts[d + 1] : _dashPoints;
            StrokePolyline(_dx, _dy, first, end, closed: false, style, half, disk);
        }
    }

    private void StrokePolyline(double[] xs, double[] ys, int first, int end, bool closed, in StrokeStyle style, double half, int disk)
    {
        // Drop repeated points.
        int n = 0;
        Span<int> keep = end - first <= 256 ? stackalloc int[end - first] : new int[end - first];
        for (int i = first; i < end; i++)
        {
            if (n == 0 || Math.Abs(xs[i] - xs[keep[n - 1]]) > 1e-12 || Math.Abs(ys[i] - ys[keep[n - 1]]) > 1e-12)
            {
                keep[n++] = i;
            }
        }

        if (closed && n > 1 && Math.Abs(xs[keep[n - 1]] - xs[keep[0]]) <= 1e-12 && Math.Abs(ys[keep[n - 1]] - ys[keep[0]]) <= 1e-12)
        {
            n--;
        }

        if (n == 0)
        {
            return;
        }

        if (n == 1)
        {
            // A degenerate subpath (a point with at least one segment) paints a dot only with round caps; a lone moveto paints nothing.
            if (style.LineCap == LineCap.Round && (end - first >= 2 || closed))
            {
                Disk(xs[keep[0]], ys[keep[0]], half, disk);
            }

            return;
        }

        int segments = closed ? n : n - 1;
        for (int s = 0; s < segments; s++)
        {
            int a = keep[s];
            int b = keep[(s + 1) % n];
            double ax = xs[a];
            double ay = ys[a];
            double bx = xs[b];
            double by = ys[b];
            double dx = bx - ax;
            double dy = by - ay;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            dx /= length;
            dy /= length;
            if (!closed && style.LineCap == LineCap.ProjectingSquare)
            {
                if (s == 0)
                {
                    ax -= dx * half;
                    ay -= dy * half;
                }

                if (s == segments - 1)
                {
                    bx += dx * half;
                    by += dy * half;
                }
            }

            double nx = -dy * half;
            double ny = dx * half;
            Triangle(ax + nx, ay + ny, bx + nx, by + ny, bx - nx, by - ny);
            Triangle(ax + nx, ay + ny, bx - nx, by - ny, ax - nx, ay - ny);
        }

        int joins = closed ? n : n - 2;
        for (int j = 0; j < joins; j++)
        {
            int vi = closed ? j : j + 1;
            int prev = keep[(vi - 1 + n) % n];
            int at = keep[vi];
            int next = keep[(vi + 1) % n];
            Join(xs[prev], ys[prev], xs[at], ys[at], xs[next], ys[next], style, half, disk);
        }

        if (!closed && style.LineCap == LineCap.Round)
        {
            Disk(xs[keep[0]], ys[keep[0]], half, disk);
            Disk(xs[keep[n - 1]], ys[keep[n - 1]], half, disk);
        }
    }

    private void Join(double px, double py, double vx, double vy, double qx, double qy, in StrokeStyle style, double half, int disk)
    {
        double d1x = vx - px;
        double d1y = vy - py;
        double l1 = Math.Sqrt((d1x * d1x) + (d1y * d1y));
        double d2x = qx - vx;
        double d2y = qy - vy;
        double l2 = Math.Sqrt((d2x * d2x) + (d2y * d2y));
        d1x /= l1;
        d1y /= l1;
        d2x /= l2;
        d2y /= l2;
        double cross = (d1x * d2y) - (d1y * d2x);
        double dot = (d1x * d2x) + (d1y * d2y);
        if (Math.Abs(cross) < 1e-9 && dot > 0)
        {
            return;
        }

        if (style.LineJoin == LineJoin.Round)
        {
            Disk(vx, vy, half, disk);
            return;
        }

        double side = cross > 0 ? -1 : 1;
        double n1x = -d1y * half * side;
        double n1y = d1x * half * side;
        double n2x = -d2y * half * side;
        double n2y = d2x * half * side;
        Triangle(vx, vy, vx + n1x, vy + n1y, vx + n2x, vy + n2y);
        if (style.LineJoin != LineJoin.Miter)
        {
            return;
        }

        double sinHalf = Math.Sqrt(Math.Max(0, (1 + dot) / 2));
        if (sinHalf < 1e-9 || 1 / sinHalf > style.MiterLimit)
        {
            return;
        }

        double mx = n1x + n2x;
        double my = n1y + n2y;
        double ml = Math.Sqrt((mx * mx) + (my * my));
        if (ml < 1e-12)
        {
            return;
        }

        double scale = half / sinHalf / ml;
        double tipX = vx + (mx * scale);
        double tipY = vy + (my * scale);
        Triangle(vx + n1x, vy + n1y, tipX, tipY, vx + n2x, vy + n2y);
    }

    private void Disk(double cx, double cy, double radius, int segments)
    {
        double step = 2 * Math.PI / segments;
        double px = cx + radius;
        double py = cy;
        for (int i = 1; i <= segments; i++)
        {
            double angle = i * step;
            double x = cx + (radius * Math.Cos(angle));
            double y = cy + (radius * Math.Sin(angle));
            Triangle(cx, cy, px, py, x, y);
            px = x;
            py = y;
        }
    }

    private void Triangle(double ax, double ay, double bx, double by, double cx, double cy)
    {
        Vertex(ax, ay);
        Vertex(bx, by);
        Vertex(cx, cy);
    }

    private void Vertex(double x, double y)
    {
        double dx = (x * _emit.A) + (y * _emit.C) + _emit.E;
        double dy = (x * _emit.B) + (y * _emit.D) + _emit.F;
        Bound(dx, dy);
        _out.Add((float)dx, (float)dy);
    }

    /// <summary>
    /// Cuts the contours into dashes as the software rasterizer does (§8.4.3.6): a negative phase counts as 0, an odd-length array
    /// alternates over two periods, a zero-length dash becomes a 1e-3 segment along the path (so its caps are oriented), and on a closed
    /// contour that starts and ends inside a dash the two ends join.
    /// </summary>
    private void Dash(ReadOnlySpan<double> pattern, double total, double phase)
    {
        _dashPoints = 0;
        _dashes = 0;
        int budget = 1 << 20;
        double period = pattern.Length % 2 == 1 ? 2 * total : total;
        for (int c = 0; c < _contours; c++)
        {
            int first = _starts[c];
            int end = c + 1 < _contours ? _starts[c + 1] : _points;
            bool closed = _closed[c];
            int index = 0;
            double remaining = Math.Max(0, pattern[0]);
            double offset = double.IsFinite(phase) && phase > 0 ? phase % period : 0;
            while (offset > 0)
            {
                if (offset >= remaining)
                {
                    offset -= remaining;
                    index++;
                    remaining = Math.Max(0, pattern[index % pattern.Length]);
                }
                else
                {
                    remaining -= offset;
                    offset = 0;
                }
            }

            bool on = index % 2 == 0;
            bool toggled = false;
            int head = -1;
            if (on)
            {
                head = _dashes;
                StartDash(_xs[first], _ys[first]);
            }

            int count = end - first + (closed ? 1 : 0);
            for (int i = 1; i < count; i++)
            {
                int a = first + i - 1;
                int b = i < end - first ? first + i : first;
                double ax = _xs[a];
                double ay = _ys[a];
                double sx = _xs[b] - ax;
                double sy = _ys[b] - ay;
                double length = Math.Sqrt((sx * sx) + (sy * sy));
                double position = 0;
                while (remaining <= length - position)
                {
                    position += remaining;
                    double t = length > 0 ? position / length : 1;
                    double x = ax + (sx * t);
                    double y = ay + (sy * t);
                    if (on)
                    {
                        EndDash(x, y, sx, sy, length);
                    }
                    else
                    {
                        StartDash(x, y);
                    }

                    toggled = true;
                    on = !on;
                    index++;
                    if (--budget <= 0)
                    {
                        // Too many dashes: the rest of the stroke is solid.
                        if (!on)
                        {
                            StartDash(x, y);
                            on = true;
                        }

                        remaining = double.PositiveInfinity;
                        break;
                    }

                    remaining = Math.Max(0, pattern[index % pattern.Length]);
                }

                remaining -= length - position;
                if (on)
                {
                    AddDash(_xs[b], _ys[b]);
                }
            }

            if (on && closed && head >= 0 && toggled && head < _dashes - 1)
            {
                // The last dash runs into the first: move the first dash's points (after its start) to the end of the last.
                int headStart = _dashStarts[head];
                int headEnd = _dashStarts[head + 1];
                for (int p = headStart + 1; p < headEnd; p++)
                {
                    AddDash(_dx[p], _dy[p]);
                }

                _dashSkip[head] = true;
            }
        }
    }

    private void EndDash(double x, double y, double sx, double sy, double length)
    {
        int start = _dashStarts[_dashes - 1];
        if (_dashPoints - start == 1 && _dx[start] == x && _dy[start] == y && length > 0)
        {
            // A zero-length dash: a short segment along the path so that its caps have a direction.
            AddDash(x + (sx / length * 1e-3), y + (sy / length * 1e-3));
            return;
        }

        AddDash(x, y);
    }

    private void StartDash(double x, double y)
    {
        if (_dashes == _dashStarts.Length)
        {
            Array.Resize(ref _dashStarts, _dashes * 2);
        }

        if (_dashSkip.Length < _dashStarts.Length)
        {
            Array.Resize(ref _dashSkip, _dashStarts.Length);
        }

        _dashSkip[_dashes] = false;
        _dashStarts[_dashes++] = _dashPoints;
        AddDash(x, y);
    }

    private void AddDash(double x, double y)
    {
        if (_dashPoints == _dx.Length)
        {
            Array.Resize(ref _dx, _dashPoints * 2);
            Array.Resize(ref _dy, _dashPoints * 2);
        }

        _dx[_dashPoints] = x;
        _dy[_dashPoints] = y;
        _dashPoints++;
    }

    private void StartContour()
    {
        if (_contours == _starts.Length)
        {
            Array.Resize(ref _starts, _contours * 2);
            Array.Resize(ref _closed, _contours * 2);
        }

        _starts[_contours] = _points;
        _closed[_contours] = false;
        _contours++;
    }

    private void AddPoint(double x, double y)
    {
        if (_points == _xs.Length)
        {
            Array.Resize(ref _xs, _points * 2);
            Array.Resize(ref _ys, _points * 2);
        }

        _xs[_points] = Math.Clamp(x, -1e7, 1e7);
        _ys[_points] = Math.Clamp(y, -1e7, 1e7);
        _points++;
    }

    private void Bound(double x, double y)
    {
        _left = Math.Min(_left, x);
        _top = Math.Min(_top, y);
        _right = Math.Max(_right, x);
        _bottom = Math.Max(_bottom, y);
    }

    private static int Segments(double deviation, double tolerance) =>
        deviation <= tolerance ? 1 : Math.Clamp((int)Math.Ceiling(Math.Sqrt(deviation / tolerance)), 1, 256);
}
