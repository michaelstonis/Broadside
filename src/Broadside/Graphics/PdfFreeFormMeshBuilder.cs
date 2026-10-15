namespace Broadside.Graphics;

/// <summary>Builds a free-form Gouraud-shaded triangle mesh (shading Type 4): separate triangles, strips and fans.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.5, Table 81. <see cref="AddTriangle"/> begins a triangle unconnected to the others (edge flag 0, its three
/// vertices written in full). <see cref="AddStripVertex"/> (edge flag 1) adds a triangle from the last two vertices of the previous
/// triangle and a new one; <see cref="AddFanVertex"/> (edge flag 2) one from the first and last vertices of the previous triangle and a
/// new one. Write it with <see cref="PdfDocument.CreateMeshShading(PdfFreeFormMeshBuilder, PdfShadingOptions?)"/>.
/// </remarks>
public sealed class PdfFreeFormMeshBuilder : PdfMeshBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PdfFreeFormMeshBuilder"/> class whose vertices carry colours.</summary>
    /// <param name="colorSpace">The mesh's colour space: any space except Pattern.</param>
    /// <exception cref="ArgumentException"><paramref name="colorSpace"/> is a Pattern space.</exception>
    public PdfFreeFormMeshBuilder(PdfColorSpace colorSpace)
        : base(colorSpace, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfFreeFormMeshBuilder"/> class whose vertices carry a parametric value t.</summary>
    /// <param name="colorSpace">The mesh's colour space: any space except Pattern and Indexed.</param>
    /// <param name="function">A function from t to the colour space's components.</param>
    /// <exception cref="ArgumentException">The space is a Pattern or Indexed space, or the function is unusable or does not take 1 input to the space's components.</exception>
    public PdfFreeFormMeshBuilder(PdfColorSpace colorSpace, PdfFunction function)
        : base(colorSpace, function ?? throw new ArgumentNullException(nameof(function)))
    {
    }

    /// <summary>Gets the number of triangles added.</summary>
    public int TriangleCount { get; private set; }

    /// <inheritdoc/>
    internal override PdfShadingType ShadingType => PdfShadingType.FreeFormTriangleMesh;

    /// <summary>Adds a triangle unconnected to the previous ones (edge flag 0).</summary>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second vertex.</param>
    /// <param name="c">The third vertex.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">A vertex does not fit the mesh (see <see cref="PdfMeshBuilder"/>).</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.5: f = 0 begins a new triangle.</remarks>
    public PdfFreeFormMeshBuilder AddTriangle(PdfMeshVertex a, PdfMeshVertex b, PdfMeshVertex c)
    {
        Check(a, nameof(a));
        Check(b, nameof(b));
        Check(c, nameof(c));
        AddVertex(0, a);
        AddVertex(0, b);
        AddVertex(0, c);
        TriangleCount++;
        return this;
    }

    /// <summary>Adds a triangle made of the previous triangle's last two vertices and <paramref name="vertex"/> (edge flag 1, a strip).</summary>
    /// <param name="vertex">The new vertex.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The vertex does not fit the mesh.</exception>
    /// <exception cref="InvalidOperationException">No triangle has been added yet.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.5: f = 1 forms the triangle v<sub>b</sub> v<sub>c</sub> v<sub>d</sub>.</remarks>
    public PdfFreeFormMeshBuilder AddStripVertex(PdfMeshVertex vertex) => Continue(1, vertex);

    /// <summary>Adds a triangle made of the previous triangle's first and last vertices and <paramref name="vertex"/> (edge flag 2, a fan).</summary>
    /// <param name="vertex">The new vertex.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The vertex does not fit the mesh.</exception>
    /// <exception cref="InvalidOperationException">No triangle has been added yet.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.5: f = 2 forms the triangle v<sub>a</sub> v<sub>c</sub> v<sub>d</sub>.</remarks>
    public PdfFreeFormMeshBuilder AddFanVertex(PdfMeshVertex vertex) => Continue(2, vertex);

    /// <inheritdoc/>
    internal override void ThrowIfIncomplete()
    {
        if (TriangleCount == 0)
        {
            throw new InvalidOperationException("The mesh has no triangle; add one with AddTriangle.");
        }
    }

    private PdfFreeFormMeshBuilder Continue(byte flag, in PdfMeshVertex vertex)
    {
        if (TriangleCount == 0)
        {
            throw new InvalidOperationException("A strip or fan vertex continues the previous triangle; add a triangle with AddTriangle first (ISO 32000-2 §8.7.4.5.5).");
        }

        Check(vertex, nameof(vertex));
        AddVertex(flag, vertex);
        TriangleCount++;
        return this;
    }
}
