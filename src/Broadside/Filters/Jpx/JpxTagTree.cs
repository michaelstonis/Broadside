namespace Broadside.Filters.Jpx;

/// <summary>
/// A tag tree over the code-blocks of one precinct sub-band: inclusion (first layer of each code-block) or the number of missing most
/// significant bit-planes. Nodes are decoded lazily, only as far as a threshold needs.
/// </summary>
/// <remarks>ITU-T T.800 B.10.2 (Figures B.12 and B.13). One tree per precinct sub-band per tile; reset with the tile (B.10.2).</remarks>
internal sealed class JpxTagTree
{
    private const int Unknown = int.MaxValue;

    private readonly int[] _value;
    private readonly int[] _low;
    private readonly int[] _levelStart;
    private readonly int[] _levelWidth;
    private readonly int _levels;

    /// <summary>Initializes a new instance of the <see cref="JpxTagTree"/> class with <paramref name="width"/> x <paramref name="height"/> leaves.</summary>
    public JpxTagTree(int width, int height)
    {
        int levels = 1;
        int nodes = width * height;
        for (int w = width, h = height; w > 1 || h > 1; levels++)
        {
            w = (w + 1) >> 1;
            h = (h + 1) >> 1;
            nodes += w * h;
        }

        _levels = levels;
        _levelStart = new int[levels];
        _levelWidth = new int[levels];
        _value = new int[nodes];
        _low = new int[nodes];
        Array.Fill(_value, Unknown);
        for (int level = 0, start = 0, w = width, h = height; level < levels; level++)
        {
            _levelStart[level] = start;
            _levelWidth[level] = w;
            start += w * h;
            w = (w + 1) >> 1;
            h = (h + 1) >> 1;
        }
    }

    /// <summary>
    /// Decodes leaf (<paramref name="x"/>, <paramref name="y"/>) up to <paramref name="threshold"/> and reports whether its value is
    /// below it, reading only the bits that question needs.
    /// </summary>
    /// <returns><see langword="true"/> when the leaf's value is less than <paramref name="threshold"/>.</returns>
    public bool Decode(ref JpxPacketHeaderReader reader, int x, int y, int threshold)
    {
        Span<int> path = stackalloc int[32];
        for (int level = 0, px = x, py = y; level < _levels; level++, px >>= 1, py >>= 1)
        {
            path[level] = _levelStart[level] + (py * _levelWidth[level]) + px;
        }

        int low = 0;
        for (int level = _levels - 1; level >= 0; level--)
        {
            int node = path[level];
            if (low > _low[node])
            {
                _low[node] = low;
            }
            else
            {
                low = _low[node];
            }

            while (low < threshold && low < _value[node])
            {
                if (reader.ReadBit() == 1)
                {
                    _value[node] = low;
                }
                else
                {
                    low++;
                }
            }

            _low[node] = low;
        }

        return _value[path[0]] < threshold;
    }

    /// <summary>Decodes the full value of leaf (<paramref name="x"/>, <paramref name="y"/>).</summary>
    /// <param name="reader">The packet header being read.</param>
    /// <param name="x">The leaf's column.</param>
    /// <param name="y">The leaf's row.</param>
    /// <param name="limit">A value past which decoding stops (a damaged header would otherwise read zero bits forever).</param>
    /// <returns>The value, at most <paramref name="limit"/>.</returns>
    public int DecodeValue(ref JpxPacketHeaderReader reader, int x, int y, int limit)
    {
        int threshold = 1;
        while (!Decode(ref reader, x, y, threshold) && threshold <= limit)
        {
            threshold++;
        }

        return threshold - 1;
    }
}
