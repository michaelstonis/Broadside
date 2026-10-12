using Broadside.Graphics.Shadings;

namespace Broadside.Graphics;

/// <summary>A triangle mesh shading: free-form (Type 4) or lattice-form (Type 5), colours given at the vertices.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.5 (Table 81) and §8.7.4.5.6 (Table 82). <see cref="Vertices"/> and <see cref="VertexColors"/> hold the
/// decoded vertices; <see cref="Triangles"/> three vertex indices per triangle, in the order they are painted. A colour inside a
/// triangle is the linear interpolation of its vertices' colours (of t, then the function, when the shading has one).
/// </para>
/// <para>
/// Type 4 edge flags: 0 starts a triangle from this vertex and the next two (whose flags are ignored); after a triangle (va, vb, vc),
/// 1 makes (vb, vc, vd) and 2 makes (va, vc, vd). A flag of 3, or 1 or 2 with no triangle before it, skips the vertex with a
/// <c>MeshEdgeFlagInvalid</c> diagnostic. Type 5: the vertices form rows of <see cref="VerticesPerRow"/>, each cell two triangles
/// (V<sub>i,j</sub>, V<sub>i,j+1</sub>, V<sub>i+1,j</sub>) and (V<sub>i,j+1</sub>, V<sub>i+1,j</sub>, V<sub>i+1,j+1</sub>); an
/// incomplete last row is dropped with <c>MeshLatticeIncomplete</c>.
/// </para>
/// </remarks>
public sealed class PdfTriangleMeshShading : PdfMeshShading
{
    internal PdfTriangleMeshShading(ShadingReader reader, PdfShadingType type)
        : base(reader, type)
    {
    }

    /// <summary>Gets the number of vertices per row of a lattice (Type 5); 0 for a free-form mesh.</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.6, Table 82 (<c>VerticesPerRow</c>).</remarks>
    public int VerticesPerRow => VerticesPerRowValue;

    /// <summary>Gets the number of decoded vertices that belong to triangles.</summary>
    public int VertexCount => Mesh.VertexCount;

    /// <summary>Gets the decoded vertices, in the shading's target space.</summary>
    public ReadOnlySpan<PathPoint> Vertices => Mesh.Points.AsSpan(0, Mesh.VertexCount);

    /// <summary>Gets the vertices' colours, <see cref="PdfMeshShading.ColorStride"/> floats per vertex.</summary>
    public ReadOnlySpan<float> VertexColors => Mesh.Colors.AsSpan(0, Mesh.VertexCount * ColorStride);

    /// <summary>Gets the number of triangles.</summary>
    public int TriangleCount => Mesh.TriangleCount;

    /// <summary>Gets three indices into <see cref="Vertices"/> per triangle.</summary>
    public ReadOnlySpan<int> Triangles => Mesh.Triangles.AsSpan(0, Mesh.TriangleCount * 3);
}
