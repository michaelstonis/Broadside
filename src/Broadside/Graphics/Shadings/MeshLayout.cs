namespace Broadside.Graphics.Shadings;

/// <summary>How the data of a mesh shading (Types 4 to 7) is laid out: field widths, Decode ranges and what each vertex carries.</summary>
/// <remarks>ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8 (Tables 81 to 85) and §8.9.5.2 (the Decode formula).</remarks>
internal sealed class MeshLayout
{
    /// <summary>The default limit on vertices per shading (a patch counts as 16), guarding against decompression bombs.</summary>
    public const int DefaultMaxVertices = 1 << 22;

    /// <summary>Gets the shading type, 4 to 7.</summary>
    public required int ShadingType { get; init; }

    /// <summary>Gets the width of a coordinate, 1 to 32.</summary>
    public required int BitsPerCoordinate { get; init; }

    /// <summary>Gets the width of a colour component or t, 1 to 32.</summary>
    public required int BitsPerComponent { get; init; }

    /// <summary>Gets the width of an edge flag, 1 to 32; 0 for Type 5, which has none.</summary>
    public required int BitsPerFlag { get; init; }

    /// <summary>Gets the Decode array: x, y, then one pair per value of a vertex.</summary>
    public required double[] Decode { get; init; }

    /// <summary>Gets the number of colour values per vertex in the data: 1 (t) with a function, else the colour space's components.</summary>
    public required int ValueCount { get; init; }

    /// <summary>Gets the number of vertices per row of a lattice (Type 5).</summary>
    public int VerticesPerRow { get; init; }

    /// <summary>Gets the Indexed colour space's lookup, when colours are indices to convert to the base space.</summary>
    public MeshPalette? Palette { get; init; }

    /// <summary>Gets the most vertices to decode (a patch counts as 16); the rest is dropped with <see cref="MeshIssues.LimitExceeded"/>.</summary>
    public int MaxVertices { get; init; } = DefaultMaxVertices;

    /// <summary>Gets the number of floats stored per vertex colour.</summary>
    public int ColorStride => Palette?.BaseComponentCount ?? ValueCount;

    /// <summary>Gets a value indicating whether the mesh is made of patches (Types 6 and 7).</summary>
    public bool IsPatchMesh => ShadingType >= 6;
}

/// <summary>An Indexed colour space's lookup table, converting mesh colour indices to base colour components (§8.6.6.3).</summary>
internal sealed class MeshPalette
{
    private readonly byte[] _lookup;
    private readonly ComponentRange[] _ranges;

    public MeshPalette(int highValue, ReadOnlySpan<byte> lookup, ComponentRange[] baseRanges)
    {
        HighValue = highValue;
        _lookup = lookup.ToArray();
        _ranges = baseRanges;
    }

    /// <summary>Gets hival, the largest valid index.</summary>
    public int HighValue { get; }

    /// <summary>Gets m, the number of components of the base colour space.</summary>
    public int BaseComponentCount => _ranges.Length;

    /// <summary>Writes the base colour of a decoded index: rounded half up, clipped to 0 to hival, looked up and scaled to the ranges.</summary>
    public void Convert(double value, Span<float> destination)
    {
        int index = (int)Math.Clamp(Math.Floor(value + 0.5), 0, HighValue);
        int m = _ranges.Length;
        for (int k = 0; k < m; k++)
        {
            int at = (index * m) + k;
            double sample = at < _lookup.Length ? _lookup[at] : 0;
            ComponentRange range = _ranges[k];
            destination[k] = (float)(range.Minimum + (sample * (range.Maximum - range.Minimum) / 255));
        }
    }
}

/// <summary>What went wrong decoding mesh data; each kind is recorded once.</summary>
[Flags]
internal enum MeshIssues
{
    None = 0,

    /// <summary>The data ends inside a vertex, triangle or patch, which is dropped.</summary>
    Truncated = 1,

    /// <summary>An edge flag is not allowed, or continues a triangle or patch that does not exist; the data it covers is dropped.</summary>
    FlagInvalid = 2,

    /// <summary>A lattice's last row is incomplete, or it has fewer than two rows.</summary>
    LatticeIncomplete = 4,

    /// <summary>The mesh has more vertices than the limit; the rest is dropped.</summary>
    LimitExceeded = 8,

    /// <summary>Patches were written back to back, without the padding to whole bytes; they are read as one bit stream.</summary>
    PatchesUnpadded = 16,
}

/// <summary>Decoded mesh geometry and colours, one array each.</summary>
internal sealed class MeshData
{
    public static readonly MeshData Empty = new([], [], [], 0, 0, 0, MeshIssues.None);

    public MeshData(PathPoint[] points, float[] colors, int[] triangles, int vertexCount, int triangleCount, int patchCount, MeshIssues issues)
    {
        Points = points;
        Colors = colors;
        Triangles = triangles;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
        PatchCount = patchCount;
        Issues = issues;
    }

    /// <summary>Gets the vertices (Types 4 and 5) or the 16 control points of each patch (Types 6 and 7).</summary>
    public PathPoint[] Points { get; }

    /// <summary>Gets the vertex colours, or the four corner colours of each patch, stride <see cref="MeshLayout.ColorStride"/>.</summary>
    public float[] Colors { get; }

    /// <summary>Gets three vertex indices per triangle.</summary>
    public int[] Triangles { get; }

    public int VertexCount { get; }

    public int TriangleCount { get; }

    public int PatchCount { get; }

    public MeshIssues Issues { get; }

    /// <summary>Returns the same mesh with <paramref name="issue"/> added to <see cref="Issues"/>.</summary>
    public MeshData With(MeshIssues issue) => new(Points, Colors, Triangles, VertexCount, TriangleCount, PatchCount, Issues | issue);
}
