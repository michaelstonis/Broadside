using System.Runtime.InteropServices;
using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>Path fills (ISO 32000-2 §8.5.3), path interning and the colours fills carry (§8.6).</summary>
internal sealed partial class DisplayListBuilder
{
    private PathVerb[] _verbs = new PathVerb[256];
    private int _verbCount;
    private PathPoint[] _points = new PathPoint[256];
    private int _pointCount;

    private DisplayPath[] _paths = new DisplayPath[32];
    private int[] _pathHashes = new int[32];
    private int[] _pathNext = new int[32];
    private int _pathCount;

    // Open hash table over the interned paths: bucket -> first path index + 1, chained through _pathNext (index + 1, 0 ends).
    private int[] _buckets = new int[64];

    private DisplayColorEntry[] _colors = new DisplayColorEntry[16];
    private int _colorCount;
    private float[] _components = new float[64];
    private int _componentCount;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §8.5.3, Table 59: <c>f</c>, <c>F</c>, <c>f*</c> and the fill of <c>B</c>, <c>B*</c>, <c>b</c>, <c>b*</c> become a
    /// fill with the nonstroking colour (§8.5.3.3.1); <c>S</c>, <c>s</c> and the stroke of the <c>B</c> family, after its fill, become
    /// a stroke (§8.5.3.2, <c>DisplayListBuilder.Strokes.cs</c>). A paint in a shading pattern becomes a paint with its shading
    /// (§8.7.4.1, <c>DisplayListBuilder.Shadings.cs</c>); one in a tiling pattern becomes a paint with its cell (§8.7.3,
    /// <c>DisplayListBuilder.Tiling.cs</c>).
    /// </remarks>
    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        if (path.Paint == PathPaint.FillAndStroke)
        {
            // §11.7.4.4: one object, as if in a non-isolated knockout group, when transparency makes that differ.
            ref readonly GraphicsState state = ref context.State;
            int group = OpenFillAndStrokeGroup(state, context.DefaultColorSpaces, Bounds(path.Path.Points, StrokeReach(state)));
            FillPath(path, context);
            StrokePath(path, context);
            CloseFillAndStrokeGroup(group);
            return;
        }

        if (path.Paint == PathPaint.Fill)
        {
            FillPath(path, context);
        }

        if (path.Paint == PathPaint.Stroke)
        {
            StrokePath(path, context);
        }
    }

    /// <summary>Records the fill of a path with the nonstroking colour.</summary>
    private void FillPath(in PathEvent path, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        ref readonly PdfColor fill = ref state.FillColor;
        if (fill.ColorSpace.Family == PdfColorSpaceFamily.Pattern)
        {
            if (context.GetPattern(fill) is PdfTilingPattern)
            {
                FillPathWithTilingPattern(path, context);
                return;
            }

            FillPathWithShading(path, context);
            return;
        }

        int pathIndex = InternPath(path.Path);
        int colorIndex = AddColor(fill, context.DefaultColorSpaces, ColorConversion.FromState(state, DeviceColorModel.Rgb));
        SetCompositing(state);
        SetTransform(state.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.FillPath, path.FillRule, pathIndex, colorIndex));
    }

    /// <summary>Records the fill of a path in a shading pattern (§8.7.4.1).</summary>
    private void FillPathWithShading(in PathEvent path, ContentContext context)
    {
        int shading = AddPatternShading(context.State.FillColor, context);
        if (shading < 0)
        {
            return;
        }

        int pathIndex = InternPath(path.Path);
        SetPatternCompositing(context.State.FillColor, context, stroke: false);
        SetTransform(context.State.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.FillPathWithShading, path.FillRule, pathIndex, shading));
    }

    /// <summary>Returns the index of <paramref name="path"/> in the path table, adding it when no equal path is there yet.</summary>
    private int InternPath(PathView path)
    {
        ReadOnlySpan<PathVerb> verbs = path.Verbs;
        ReadOnlySpan<PathPoint> points = path.Points;
        var hasher = default(HashCode);
        hasher.AddBytes(MemoryMarshal.AsBytes(verbs));
        hasher.AddBytes(MemoryMarshal.AsBytes(points));
        int hash = hasher.ToHashCode();

        int bucket = hash & (_buckets.Length - 1);
        for (int candidate = _buckets[bucket] - 1; candidate >= 0; candidate = _pathNext[candidate] - 1)
        {
            DisplayPath existing = _paths[candidate];
            if (_pathHashes[candidate] == hash
                && _verbs.AsSpan(existing.VerbStart, existing.VerbCount).SequenceEqual(verbs)
                && MemoryMarshal.AsBytes(_points.AsSpan(existing.PointStart, existing.PointCount)).SequenceEqual(MemoryMarshal.AsBytes(points)))
            {
                return candidate;
            }
        }

        EnsureCapacity(ref _verbs, _verbCount + verbs.Length);
        EnsureCapacity(ref _points, _pointCount + points.Length);
        verbs.CopyTo(_verbs.AsSpan(_verbCount));
        points.CopyTo(_points.AsSpan(_pointCount));
        int index = _pathCount;
        Append(ref _paths, ref _pathCount, new DisplayPath(_verbCount, verbs.Length, _pointCount, points.Length));
        _verbCount += verbs.Length;
        _pointCount += points.Length;
        EnsureCapacity(ref _pathHashes, _pathCount);
        EnsureCapacity(ref _pathNext, _pathCount);
        _pathHashes[index] = hash;

        if (_pathCount > _buckets.Length)
        {
            Rehash(_buckets.Length * 2);
        }
        else
        {
            _pathNext[index] = _buckets[bucket];
            _buckets[bucket] = index + 1;
        }

        return index;
    }

    /// <summary>Rebuilds the hash table with <paramref name="size"/> buckets (a power of two).</summary>
    private void Rehash(int size)
    {
        _buckets = new int[size];
        for (int index = 0; index < _pathCount; index++)
        {
            int bucket = _pathHashes[index] & (size - 1);
            _pathNext[index] = _buckets[bucket];
            _buckets[bucket] = index + 1;
        }
    }

    /// <summary>Returns the index of the colour in the colour table, reusing the last entry when the colour is the same.</summary>
    private int AddColor(in PdfColor color, PdfDefaultColorSpaces defaults, ColorConversion conversion)
    {
        ReadOnlySpan<float> components = color.Components;
        if (_colorCount > 0)
        {
            DisplayColorEntry last = _colors[_colorCount - 1];
            if (ReferenceEquals(last.ColorSpace, color.ColorSpace)
                && ReferenceEquals(last.DefaultColorSpaces, defaults)
                && last.Conversion == conversion
                && _components.AsSpan(last.ComponentStart, last.ComponentCount).SequenceEqual(components))
            {
                return _colorCount - 1;
            }
        }

        EnsureCapacity(ref _components, _componentCount + components.Length);
        components.CopyTo(_components.AsSpan(_componentCount));
        Append(ref _colors, ref _colorCount, new DisplayColorEntry(color.ColorSpace, _componentCount, components.Length, defaults, conversion));
        _componentCount += components.Length;
        return _colorCount - 1;
    }

    /// <summary>Forgets the paths and colours, releasing the colour spaces they referenced.</summary>
    private void ResetPaths()
    {
        _verbCount = 0;
        _pointCount = 0;
        _pathCount = 0;
        Array.Clear(_buckets);
        Array.Clear(_colors, 0, _colorCount);
        _colorCount = 0;
        _componentCount = 0;
    }

    /// <summary>Grows <paramref name="table"/> to hold at least <paramref name="needed"/> items, at least doubling it.</summary>
    private static void EnsureCapacity<T>(ref T[] table, int needed)
    {
        if (needed > table.Length)
        {
            Array.Resize(ref table, Math.Max(needed, table.Length * 2));
        }
    }
}
