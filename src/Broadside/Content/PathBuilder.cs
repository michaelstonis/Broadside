using System.Buffers;
using Broadside.Graphics;

namespace Broadside.Content;

/// <summary>
/// The current path of a run: segments in user space, built by the path construction operators, normalized to move, line, cubic
/// and close, in pooled buffers that are reused from one path to the next.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.5.2, Table 58. The current path is not part of the graphics state: <c>q</c> and <c>Q</c> leave it alone, and a
/// painting operator ends it (§8.5.2.1). When no processor wants paths or clips the builder tracks only whether a path and a current
/// point exist, so diagnostics stay the same while no geometry is stored.
/// </remarks>
internal sealed class PathBuilder
{
    private PathVerb[] _verbs = ArrayPool<PathVerb>.Shared.Rent(32);
    private PathPoint[] _points = ArrayPool<PathPoint>.Shared.Rent(64);
    private int _verbCount;
    private int _pointCount;
    private PathPoint _current;
    private PathPoint _subpathStart;
    private bool _subpathClosed;
    private bool _lastWasMoveTo;

    /// <summary>Gets or sets a value indicating whether segments are stored, or only the path's existence and current point.</summary>
    public bool Accumulate { get; set; } = true;

    /// <summary>Gets a value indicating whether a path object is open: <c>m</c> or <c>re</c> began one and no painting operator ended it.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets the current point: the end of the last segment (§8.5.2.1); undefined when no path is open.</summary>
    public PathPoint CurrentPoint => _current;

    /// <summary>Gets the path built so far.</summary>
    public PathView View => new(_verbs.AsSpan(0, _verbCount), _points.AsSpan(0, _pointCount));

    /// <summary>Ends the path object: the path becomes undefined.</summary>
    public void Reset()
    {
        _verbCount = 0;
        _pointCount = 0;
        IsOpen = false;
        _subpathClosed = false;
        _lastWasMoveTo = false;
    }

    /// <summary>Gives oversized buffers back to the pool.</summary>
    public void Trim()
    {
        Reset();
        if (_points.Length > 1 << 16)
        {
            ArrayPool<PathVerb>.Shared.Return(_verbs);
            ArrayPool<PathPoint>.Shared.Return(_points);
            _verbs = ArrayPool<PathVerb>.Shared.Rent(32);
            _points = ArrayPool<PathPoint>.Shared.Rent(64);
        }
    }

    /// <summary><c>m</c>: begins a subpath; a moveto right after a moveto replaces it (Table 58).</summary>
    public void MoveTo(double x, double y)
    {
        var point = new PathPoint(x, y);
        if (_lastWasMoveTo)
        {
            if (Accumulate)
            {
                _points[_pointCount - 1] = point;
            }
        }
        else
        {
            Append(PathVerb.MoveTo, point);
            _lastWasMoveTo = true;
        }

        IsOpen = true;
        _current = point;
        _subpathStart = point;
        _subpathClosed = false;
    }

    /// <summary><c>l</c>: a line from the current point, which the caller has checked exists.</summary>
    public void LineTo(double x, double y)
    {
        BeginSegment();
        var point = new PathPoint(x, y);
        Append(PathVerb.LineTo, point);
        _current = point;
    }

    /// <summary><c>c</c>, <c>v</c>, <c>y</c>: a cubic Bézier curve from the current point, which the caller has checked exists.</summary>
    public void CurveTo(PathPoint control1, PathPoint control2, PathPoint end)
    {
        BeginSegment();
        if (Accumulate)
        {
            EnsureCapacity(1, 3);
            _verbs[_verbCount++] = PathVerb.CubicTo;
            _points[_pointCount++] = control1;
            _points[_pointCount++] = control2;
            _points[_pointCount++] = end;
        }

        _current = end;
    }

    /// <summary><c>h</c>: closes the current subpath; nothing when it is already closed (Table 58).</summary>
    public void Close()
    {
        if (_subpathClosed)
        {
            return;
        }

        if (Accumulate)
        {
            EnsureCapacity(1, 0);
            _verbs[_verbCount++] = PathVerb.Close;
        }

        _current = _subpathStart;
        _subpathClosed = true;
        _lastWasMoveTo = false;
    }

    /// <summary><c>re</c>: <c>x y m</c>, three lines, <c>h</c>, keeping the signs of the width and height (Table 58).</summary>
    public void Rectangle(double x, double y, double width, double height)
    {
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        Close();
    }

    /// <summary>Starts a segment: after <c>h</c>, a new subpath begins at the closed subpath's first point (Table 58).</summary>
    private void BeginSegment()
    {
        if (_subpathClosed)
        {
            Append(PathVerb.MoveTo, _subpathStart);
            _subpathClosed = false;
        }

        _lastWasMoveTo = false;
    }

    private void Append(PathVerb verb, PathPoint point)
    {
        if (!Accumulate)
        {
            return;
        }

        EnsureCapacity(1, 1);
        _verbs[_verbCount++] = verb;
        _points[_pointCount++] = point;
    }

    private void EnsureCapacity(int verbs, int points)
    {
        if (_verbCount + verbs > _verbs.Length)
        {
            Grow(ref _verbs, _verbCount);
        }

        if (_pointCount + points > _points.Length)
        {
            Grow(ref _points, _pointCount);
        }
    }

    private static void Grow<T>(ref T[] array, int used)
    {
        T[] larger = ArrayPool<T>.Shared.Rent(array.Length * 2);
        array.AsSpan(0, used).CopyTo(larger);
        ArrayPool<T>.Shared.Return(array);
        array = larger;
    }
}
