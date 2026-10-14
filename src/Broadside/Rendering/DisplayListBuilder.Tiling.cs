using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// Tiling patterns (ISO 32000-2 §8.7.3): fills and strokes in a tiling pattern, whose cell is built once per pattern per page into
/// a display list of its own, relative to pattern space.
/// </summary>
/// <remarks>
/// A cell is built by a child builder (one per nesting level, kept with its parent so warm builds reuse its tables) that receives
/// the cell's events from <see cref="ContentContext.RunPatternCell"/>. The interpreter runs the cell with the CTM mapping pattern
/// space to the page's default space (§8.7.3.1 step b); the child records every transform and absolute matrix relative to that
/// pattern space instead (<see cref="Relative"/>), so its list replays with pattern space as its default user space and does not
/// depend on where the pattern is painted. The cells of one page build are cached in a <see cref="TilingCells"/> shared by the root
/// builder and its children.
/// </remarks>
internal sealed partial class DisplayListBuilder
{
    private DisplayTilingPaint[] _tilingPaints = new DisplayTilingPaint[4];
    private int _tilingPaintCount;

    // The cells of the page being built: the root builder's own, borrowed by its children while they build a cell.
    private TilingCells? _ownCells;
    private TilingCells? _cells;
    private DisplayListBuilder? _cellBuilder;

    // While this builder records a cell: pattern space (in the page's default space) and its inverse.
    private bool _inCell;
    private Matrix _cellSpace = Matrix.Identity;
    private Matrix _toCell = Matrix.Identity;

    /// <summary>Records the fill of a path in a tiling pattern (§8.7.3.1).</summary>
    private void FillPathWithTilingPattern(in PathEvent path, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        int paint = AddTilingPaint(state.FillColor, context);
        if (paint < 0)
        {
            return;
        }

        int pathIndex = InternPath(path.Path);
        SetCompositing(state);
        SetTransform(state.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.FillPathWithTilingPattern, path.FillRule, pathIndex, paint));
    }

    /// <summary>Records the stroke of a path in a tiling pattern (§8.7.3.1, §8.5.3.2); the stroke record's colour index is the paint's.</summary>
    private void StrokePathWithTilingPattern(in PathEvent path, ContentContext context)
    {
        ref readonly GraphicsState state = ref context.State;
        int paint = AddTilingPaint(state.StrokeColor, context);
        if (paint < 0)
        {
            return;
        }

        int pathIndex = InternPath(path.Path);
        int strokeIndex = AddStroke(paint, state);
        SetCompositing(state);
        SetTransform(state.Ctm);
        AddOperation(new DisplayOperation(DisplayOperationKind.StrokePathWithTilingPattern, default, pathIndex, strokeIndex));
    }

    /// <summary>
    /// Returns the tiling paint of a fill or stroke in <paramref name="color"/>, building the pattern's cell when this page has not
    /// built it yet, or −1 when the colour selects no usable tiling pattern or its cell cannot run (recursion and nesting too deep
    /// are recorded by the interpreter).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §8.7.3.1 and §8.7.2: pattern space is the pattern matrix followed by the space of the stream that selected the
    /// pattern, never the CTM at the paint. A pattern space that cannot be inverted paints nothing.
    /// </remarks>
    private int AddTilingPaint(in PdfColor color, ContentContext context)
    {
        if (context.GetPattern(color) is not PdfTilingPattern { IsValid: true } pattern)
        {
            return -1;
        }

        Matrix space = pattern.GetPatternSpace(color);
        if (!space.TryInvert(out Matrix inverse) || !IsFinite(inverse))
        {
            return -1;
        }

        DisplayList? cell = GetCell(pattern, color, space, inverse, context);
        if (cell is null)
        {
            return -1;
        }

        var paint = new DisplayTilingPaint(pattern, Relative(space), cell);
        if (_tilingPaintCount > 0 && _tilingPaints[_tilingPaintCount - 1] == paint)
        {
            return _tilingPaintCount - 1;
        }

        Append(ref _tilingPaints, ref _tilingPaintCount, paint);
        return _tilingPaintCount - 1;
    }

    /// <summary>Returns the cell of the pattern for this colour, from the page's cells or built now; null when it cannot be built.</summary>
    private DisplayList? GetCell(PdfTilingPattern pattern, in PdfColor color, in Matrix space, in Matrix inverse, ContentContext context)
    {
        _cells ??= _ownCells ??= new TilingCells();
        bool uncolored = pattern.IsPaintTypeKnown ? pattern.PaintType == PdfTilingPaintType.Uncolored : color.ComponentCount > 0;
        ReadOnlySpan<float> components = uncolored ? color.Components : [];
        PdfColorSpace? stencil = uncolored ? color.ColorSpace : null;
        if (_cells.TryFind(pattern.Stream, color.PatternMatrix, stencil, components, out int entry))
        {
            DisplayList? built = _cells.GetList(entry);
            if (built is null)
            {
                // A cell still being built paints with itself: the interpreter refuses to run it inside itself and records the
                // recursion (§8.7.3.1), and it paints nothing there.
                context.RunPatternCell(color, this);
            }

            return built;
        }

        entry = _cells.Add(pattern.Stream, color.PatternMatrix, stencil, components);
        DisplayListBuilder child = _cellBuilder ??= new DisplayListBuilder();
        child._cells = _cells;
        child._inCell = true;
        child._cellSpace = space;
        child._toCell = inverse;
        try
        {
            if (context.RunPatternCell(color, child))
            {
                _cells.SetList(entry, child.ToDisplayList(context.Document));
            }
        }
        finally
        {
            child.Reset();
        }

        return _cells.GetList(entry);
    }

    /// <summary>Returns <paramref name="matrix"/>, a matrix to the page's default space, as one to the space this builder records in.</summary>
    /// <remarks>The identity for a page; while building a cell, the matrix followed by the inverse of pattern space (§8.7.3.1).</remarks>
    private Matrix Relative(in Matrix matrix)
    {
        if (!_inCell)
        {
            return matrix;
        }

        return matrix == _cellSpace ? Matrix.Identity : matrix * _toCell;
    }

    private static bool IsFinite(in Matrix m) =>
        double.IsFinite(m.A) && double.IsFinite(m.B) && double.IsFinite(m.C) && double.IsFinite(m.D) && double.IsFinite(m.E) && double.IsFinite(m.F);

    /// <summary>Forgets the tiling paints and, for the root builder, the page's cells.</summary>
    private void ResetTiling()
    {
        Array.Clear(_tilingPaints, 0, _tilingPaintCount);
        _tilingPaintCount = 0;
        if (ReferenceEquals(_cells, _ownCells))
        {
            _ownCells?.Clear();
        }

        _cells = null;
        _inCell = false;
        _cellSpace = Matrix.Identity;
        _toCell = Matrix.Identity;
    }

    /// <summary>
    /// The cells built for one page: one entry per pattern stream, selecting stream (its base matrix, which also fixes the cell's
    /// starting state) and, for an uncoloured pattern, colour.
    /// </summary>
    private sealed class TilingCells
    {
        private Entry[] _entries = new Entry[8];
        private int _count;
        private float[] _components = new float[16];
        private int _componentCount;

        public bool TryFind(CosStream stream, in Matrix baseMatrix, PdfColorSpace? stencil, ReadOnlySpan<float> components, out int index)
        {
            for (index = 0; index < _count; index++)
            {
                ref readonly Entry entry = ref _entries[index];
                if (ReferenceEquals(entry.Stream, stream)
                    && entry.BaseMatrix == baseMatrix
                    && ReferenceEquals(entry.Stencil, stencil)
                    && _components.AsSpan(entry.ComponentStart, entry.ComponentCount).SequenceEqual(components))
                {
                    return true;
                }
            }

            return false;
        }

        public int Add(CosStream stream, in Matrix baseMatrix, PdfColorSpace? stencil, ReadOnlySpan<float> components)
        {
            EnsureCapacity(ref _components, _componentCount + components.Length);
            components.CopyTo(_components.AsSpan(_componentCount));
            Append(ref _entries, ref _count, new Entry(stream, baseMatrix, stencil, _componentCount, components.Length, null));
            _componentCount += components.Length;
            return _count - 1;
        }

        public DisplayList? GetList(int index) => _entries[index].List;

        public void SetList(int index, DisplayList list) => _entries[index] = _entries[index] with { List = list };

        public void Clear()
        {
            Array.Clear(_entries, 0, _count);
            _count = 0;
            _componentCount = 0;
        }

        private readonly record struct Entry(CosStream Stream, Matrix BaseMatrix, PdfColorSpace? Stencil, int ComponentStart, int ComponentCount, DisplayList? List);
    }
}
