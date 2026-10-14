using System.Runtime.InteropServices;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

/// <summary>A path being built in managed memory, in single precision, before it is handed to Skia through a <see cref="PathCache"/>.</summary>
internal sealed class PathRecorder
{
    private byte[] _verbs = new byte[64];
    private float[] _coordinates = new float[256];

    /// <summary>The verbs of a recorded path.</summary>
    public enum Verb : byte
    {
        MoveTo,
        LineTo,
        QuadTo,
        CubicTo,
        Close,
    }

    /// <summary>Gets or sets the rule that decides the inside of the path when it is filled or clipped to; part of its identity.</summary>
    public SKPathFillType FillType { get; set; }

    /// <summary>Gets the number of verbs recorded.</summary>
    public int VerbCount { get; private set; }

    /// <summary>Gets the number of coordinates recorded (two per point).</summary>
    public int CoordinateCount { get; private set; }

    /// <summary>Gets a value indicating whether nothing is recorded.</summary>
    public bool IsEmpty => VerbCount == 0;

    /// <summary>Gets the recorded verbs.</summary>
    public ReadOnlySpan<byte> Verbs => _verbs.AsSpan(0, VerbCount);

    /// <summary>Gets the recorded coordinates, x then y for each point.</summary>
    public ReadOnlySpan<float> Coordinates => _coordinates.AsSpan(0, CoordinateCount);

    public void Clear()
    {
        VerbCount = 0;
        CoordinateCount = 0;
    }

    public void MoveTo(float x, float y)
    {
        Add(Verb.MoveTo, 2);
        Point(x, y);
    }

    public void LineTo(float x, float y)
    {
        Add(Verb.LineTo, 2);
        Point(x, y);
    }

    public void QuadTo(float x1, float y1, float x2, float y2)
    {
        Add(Verb.QuadTo, 4);
        Point(x1, y1);
        Point(x2, y2);
    }

    public void CubicTo(float x1, float y1, float x2, float y2, float x3, float y3)
    {
        Add(Verb.CubicTo, 6);
        Point(x1, y1);
        Point(x2, y2);
        Point(x3, y3);
    }

    public void Close() => Add(Verb.Close, 0);

    public void MoveTo(SKPoint point) => MoveTo(point.X, point.Y);

    public void LineTo(SKPoint point) => LineTo(point.X, point.Y);

    public void QuadTo(SKPoint control, SKPoint end) => QuadTo(control.X, control.Y, end.X, end.Y);

    public void CubicTo(SKPoint control1, SKPoint control2, SKPoint end) =>
        CubicTo(control1.X, control1.Y, control2.X, control2.Y, end.X, end.Y);

    /// <summary>Maps every recorded point through an affine transform, in double precision.</summary>
    public void Transform(double a, double b, double c, double d, double e, double f, double limit)
    {
        Span<float> coordinates = _coordinates.AsSpan(0, CoordinateCount);
        for (int i = 0; i < coordinates.Length; i += 2)
        {
            double x = coordinates[i];
            double y = coordinates[i + 1];
            coordinates[i] = (float)Math.Clamp((a * x) + (c * y) + e, -limit, limit);
            coordinates[i + 1] = (float)Math.Clamp((b * x) + (d * y) + f, -limit, limit);
        }
    }

    private void Add(Verb verb, int coordinates)
    {
        if (VerbCount == _verbs.Length)
        {
            Array.Resize(ref _verbs, _verbs.Length * 2);
        }

        if (CoordinateCount + coordinates > _coordinates.Length)
        {
            Array.Resize(ref _coordinates, Math.Max(CoordinateCount + coordinates, _coordinates.Length * 2));
        }

        _verbs[VerbCount++] = (byte)verb;
    }

    // A coordinate that is not a number (an overflowing transform) is recorded as 0, so that equal geometry always compares equal.
    private void Point(float x, float y)
    {
        _coordinates[CoordinateCount++] = float.IsNaN(x) ? 0 : x;
        _coordinates[CoordinateCount++] = float.IsNaN(y) ? 0 : y;
    }
}

/// <summary>
/// The Skia paths a backend has drawn, keyed by their geometry, so that replaying a list again (a repaint, a benchmark loop, a cached
/// display list) draws the same <see cref="SKPath"/> objects instead of building new ones.
/// </summary>
/// <remarks>
/// SkiaSharp 4 builds paths through <see cref="SKPathBuilder"/>, and every path handed to the canvas is a new managed
/// <see cref="SKPath"/> (its in-place construction methods create a builder per build, too), so building per operation would allocate
/// per operation. Here a path is built once per distinct geometry and per surface; Skia's own caches key on the path too, so a GPU
/// surface reuses its tessellations. The cache keeps at most <see cref="MaxCoordinates"/> coordinates and <see cref="MaxPaths"/> paths
/// (a deterministic memory budget): past either it is emptied and starts again.
/// </remarks>
internal sealed class PathCache : IDisposable
{
    /// <summary>The most coordinates the cached paths hold together (8 MB of managed copies).</summary>
    public const int MaxCoordinates = 1 << 21;

    /// <summary>The most paths cached.</summary>
    public const int MaxPaths = 1 << 16;

    private readonly Dictionary<int, Entry> _entries = [];
    private readonly SKPathBuilder _builder = new();
    private int _coordinates;
    private int _count;

    /// <summary>Returns the Skia path with the geometry of <paramref name="recorder"/>, building it on first use.</summary>
    public SKPath Get(PathRecorder recorder)
    {
        ReadOnlySpan<byte> verbs = recorder.Verbs;
        ReadOnlySpan<float> coordinates = recorder.Coordinates;
        SKPathFillType fillType = recorder.FillType;
        var hash = default(HashCode);
        hash.Add(fillType);
        hash.AddBytes(verbs);
        hash.AddBytes(MemoryMarshal.AsBytes(coordinates));
        int key = hash.ToHashCode();

        Entry? first = _entries.GetValueOrDefault(key);
        for (Entry? entry = first; entry is not null; entry = entry.Next)
        {
            if (entry.FillType == fillType && verbs.SequenceEqual(entry.Verbs) && coordinates.SequenceEqual(entry.Coordinates))
            {
                return entry.Path;
            }
        }

        if (_count >= MaxPaths || _coordinates + coordinates.Length > MaxCoordinates)
        {
            Clear();
            first = null;
        }

        SKPath path = Build(fillType, verbs, coordinates);
        _entries[key] = new Entry(fillType, verbs.ToArray(), coordinates.ToArray(), path, first);
        _coordinates += coordinates.Length;
        _count++;
        return path;
    }

    public void Dispose()
    {
        Clear();
        _builder.Dispose();
    }

    private void Clear()
    {
        foreach (Entry first in _entries.Values)
        {
            for (Entry? entry = first; entry is not null; entry = entry.Next)
            {
                entry.Path.Dispose();
            }
        }

        _entries.Clear();
        _coordinates = 0;
        _count = 0;
    }

    private SKPath Build(SKPathFillType fillType, ReadOnlySpan<byte> verbs, ReadOnlySpan<float> c)
    {
        _builder.Reset();
        _builder.FillType = fillType;
        int p = 0;
        foreach (byte verb in verbs)
        {
            switch ((PathRecorder.Verb)verb)
            {
                case PathRecorder.Verb.MoveTo:
                    _builder.MoveTo(c[p], c[p + 1]);
                    p += 2;
                    break;
                case PathRecorder.Verb.LineTo:
                    _builder.LineTo(c[p], c[p + 1]);
                    p += 2;
                    break;
                case PathRecorder.Verb.QuadTo:
                    _builder.QuadTo(c[p], c[p + 1], c[p + 2], c[p + 3]);
                    p += 4;
                    break;
                case PathRecorder.Verb.CubicTo:
                    _builder.CubicTo(c[p], c[p + 1], c[p + 2], c[p + 3], c[p + 4], c[p + 5]);
                    p += 6;
                    break;
                case PathRecorder.Verb.Close:
                    _builder.Close();
                    break;
            }
        }

        return _builder.Detach();
    }

    private sealed class Entry(SKPathFillType fillType, byte[] verbs, float[] coordinates, SKPath path, Entry? next)
    {
        public SKPathFillType FillType { get; } = fillType;

        public byte[] Verbs { get; } = verbs;

        public float[] Coordinates { get; } = coordinates;

        public SKPath Path { get; } = path;

        public Entry? Next { get; } = next;
    }
}
