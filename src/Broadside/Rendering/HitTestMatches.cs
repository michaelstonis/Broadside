using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The matches of a <see cref="HitTestIndex"/> query, enumerated without allocating: by kind (glyphs, images, paths, links) and,
/// within a kind, in content order. Use it in a <c>foreach</c>; it is its own enumerator, so enumerate it once.
/// </summary>
/// <remarks>Viewer capability 4. Valid while the index it came from is reachable; the default value has no matches.</remarks>
public ref struct HitTestMatches
{
    private readonly HitTestIndex? _index;
    private readonly HitTestKinds _kinds;
    private readonly PathPoint _point;
    private readonly PdfRectangle _area;
    private readonly bool _isArea;
    private readonly ReadOnlySpan<int> _cell;
    private readonly ReadOnlySpan<int> _overflow;
    private readonly bool _scan;
    private int _cellPosition;
    private int _overflowPosition;
    private int _next;

    internal HitTestMatches(
        HitTestIndex index,
        HitTestKinds kinds,
        PathPoint point,
        PdfRectangle area,
        bool isArea,
        ReadOnlySpan<int> cell,
        ReadOnlySpan<int> overflow,
        bool scan)
    {
        _index = index;
        _kinds = kinds;
        _point = point;
        _area = area;
        _isArea = isArea;
        _cell = cell;
        _overflow = overflow;
        _scan = scan;
    }

    /// <summary>Gets the current match.</summary>
    public HitTestItem Current { get; private set; }

    /// <summary>Returns this value as its own enumerator.</summary>
    /// <returns>This value.</returns>
    public readonly HitTestMatches GetEnumerator() => this;

    /// <summary>Moves to the next match.</summary>
    /// <returns><see langword="false"/> when there is no further match.</returns>
    public bool MoveNext()
    {
        if (_index is not { } index)
        {
            return false;
        }

        while (TryNext(index, out int id))
        {
            HitTestItem item = index.Identify(id);
            if ((item.Kind & _kinds) == 0)
            {
                if (_scan)
                {
                    _next = index.EndOfKind(id);
                }

                continue;
            }

            if (_isArea ? index.Intersects(id, _area) : index.Contains(id, _point))
            {
                Current = item;
                return true;
            }
        }

        return false;
    }

    /// <summary>The next candidate: every item in order when scanning, else the cell's and the overflow's merged in order.</summary>
    private bool TryNext(HitTestIndex index, out int id)
    {
        if (_scan)
        {
            id = _next;
            if (id >= index.Count)
            {
                return false;
            }

            _next++;
            return true;
        }

        bool inCell = _cellPosition < _cell.Length;
        bool inOverflow = _overflowPosition < _overflow.Length;
        if (inCell && (!inOverflow || _cell[_cellPosition] < _overflow[_overflowPosition]))
        {
            id = _cell[_cellPosition++];
            return true;
        }

        if (inOverflow)
        {
            id = _overflow[_overflowPosition++];
            return true;
        }

        id = 0;
        return false;
    }
}
