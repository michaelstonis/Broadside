using System.Globalization;

namespace Broadside.Graphics;

/// <summary>Builds a lattice-form Gouraud-shaded triangle mesh (shading Type 5): rows of vertices, each row as long as the others.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.6, Table 82. Neighbouring rows form a pseudo-rectangular lattice; each cell is cut into two triangles. A
/// lattice needs at least two rows of <see cref="VerticesPerRow"/> vertices. Write it with
/// <see cref="PdfDocument.CreateMeshShading(PdfLatticeMeshBuilder, PdfShadingOptions?)"/>.
/// </remarks>
public sealed class PdfLatticeMeshBuilder : PdfMeshBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PdfLatticeMeshBuilder"/> class whose vertices carry colours.</summary>
    /// <param name="colorSpace">The mesh's colour space: any space except Pattern.</param>
    /// <param name="verticesPerRow">The number of vertices in each row: at least 2.</param>
    /// <exception cref="ArgumentException"><paramref name="colorSpace"/> is a Pattern space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="verticesPerRow"/> is less than 2.</exception>
    public PdfLatticeMeshBuilder(PdfColorSpace colorSpace, int verticesPerRow)
        : base(colorSpace, null)
    {
        VerticesPerRow = CheckVerticesPerRow(verticesPerRow);
    }

    /// <summary>Initializes a new instance of the <see cref="PdfLatticeMeshBuilder"/> class whose vertices carry a parametric value t.</summary>
    /// <param name="colorSpace">The mesh's colour space: any space except Pattern and Indexed.</param>
    /// <param name="function">A function from t to the colour space's components.</param>
    /// <param name="verticesPerRow">The number of vertices in each row: at least 2.</param>
    /// <exception cref="ArgumentException">The space is a Pattern or Indexed space, or the function is unusable or does not take 1 input to the space's components.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="verticesPerRow"/> is less than 2.</exception>
    public PdfLatticeMeshBuilder(PdfColorSpace colorSpace, PdfFunction function, int verticesPerRow)
        : base(colorSpace, function ?? throw new ArgumentNullException(nameof(function)))
    {
        VerticesPerRow = CheckVerticesPerRow(verticesPerRow);
    }

    /// <summary>Gets the number of vertices in each row (<c>VerticesPerRow</c>).</summary>
    /// <remarks>ISO 32000-2 §8.7.4.5.6, Table 82.</remarks>
    public int VerticesPerRow { get; }

    /// <summary>Gets the number of rows added.</summary>
    public int RowCount { get; private set; }

    /// <inheritdoc/>
    internal override PdfShadingType ShadingType => PdfShadingType.LatticeFormTriangleMesh;

    /// <summary>Adds a row of the lattice.</summary>
    /// <param name="vertices">The row's vertices, left to right: exactly <see cref="VerticesPerRow"/> of them.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The row has another number of vertices, or a vertex does not fit the mesh.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.6.</remarks>
    public PdfLatticeMeshBuilder AddRow(params ReadOnlySpan<PdfMeshVertex> vertices)
    {
        if (vertices.Length != VerticesPerRow)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The row has {vertices.Length} vertices; every row of this lattice has {VerticesPerRow} (ISO 32000-2 §8.7.4.5.6)."),
                nameof(vertices));
        }

        foreach (PdfMeshVertex vertex in vertices)
        {
            Check(vertex, nameof(vertices));
        }

        foreach (PdfMeshVertex vertex in vertices)
        {
            AddVertex(0, vertex);
        }

        RowCount++;
        return this;
    }

    /// <inheritdoc/>
    internal override void ThrowIfIncomplete()
    {
        if (RowCount < 2)
        {
            throw new InvalidOperationException("A lattice needs at least two rows (ISO 32000-2 §8.7.4.5.6).");
        }
    }

    private static int CheckVerticesPerRow(int verticesPerRow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(verticesPerRow, 2);
        return verticesPerRow;
    }
}
