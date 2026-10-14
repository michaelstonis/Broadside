using System.Globalization;

namespace Broadside.Graphics;

/// <summary>Builds a patch mesh shading: Coons patches (Type 6) or tensor-product patches (Type 7), colours given at the corners.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.7 (Tables 83 and 84) and §8.7.4.5.8 (Table 85). Each patch is given by its control points in the order the
/// data stream lists them, which is the order of Figure 45: p<sub>00</sub> p<sub>01</sub> p<sub>02</sub> p<sub>03</sub>
/// p<sub>13</sub> p<sub>23</sub> p<sub>33</sub> p<sub>32</sub> p<sub>31</sub> p<sub>30</sub> p<sub>20</sub> p<sub>10</sub>, the
/// boundary (12 points for a Coons patch), followed for a tensor-product patch by the interior points p<sub>11</sub>
/// p<sub>12</sub> p<sub>22</sub> p<sub>21</sub> (16 points). The four corner colours are those of p<sub>00</sub>, p<sub>03</sub>,
/// p<sub>33</sub> and p<sub>30</sub>, in that order.
/// </para>
/// <para>
/// Every patch is written in full with edge flag 0; patches that share an edge repeat its points. Write the mesh with
/// <see cref="PdfDocument.CreateMeshShading(PdfPatchMeshBuilder, PdfShadingOptions?)"/>.
/// </para>
/// </remarks>
public sealed class PdfPatchMeshBuilder : PdfMeshBuilder
{
    /// <summary>Initializes a new instance of the <see cref="PdfPatchMeshBuilder"/> class whose corners carry colours.</summary>
    /// <param name="colorSpace">The mesh's colour space: any space except Pattern.</param>
    /// <param name="tensorProduct"><see langword="true"/> for tensor-product patches (16 control points, Type 7); <see langword="false"/> for Coons patches (12, Type 6).</param>
    /// <exception cref="ArgumentException"><paramref name="colorSpace"/> is a Pattern space.</exception>
    public PdfPatchMeshBuilder(PdfColorSpace colorSpace, bool tensorProduct = false)
        : base(colorSpace, null)
    {
        IsTensorProduct = tensorProduct;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPatchMeshBuilder"/> class whose corners carry a parametric value t.</summary>
    /// <param name="colorSpace">The mesh's colour space: any space except Pattern and Indexed.</param>
    /// <param name="function">A function from t to the colour space's components.</param>
    /// <param name="tensorProduct"><see langword="true"/> for tensor-product patches (Type 7); <see langword="false"/> for Coons patches (Type 6).</param>
    /// <exception cref="ArgumentException">The space is a Pattern or Indexed space, or the function is unusable or does not take 1 input to the space's components.</exception>
    public PdfPatchMeshBuilder(PdfColorSpace colorSpace, PdfFunction function, bool tensorProduct = false)
        : base(colorSpace, function ?? throw new ArgumentNullException(nameof(function)))
    {
        IsTensorProduct = tensorProduct;
    }

    /// <summary>Gets a value indicating whether the patches are tensor-product patches (Type 7) rather than Coons patches (Type 6).</summary>
    public bool IsTensorProduct { get; }

    /// <summary>Gets the number of patches added.</summary>
    public int PatchCount { get; private set; }

    /// <inheritdoc/>
    internal override PdfShadingType ShadingType => IsTensorProduct ? PdfShadingType.TensorProductPatchMesh : PdfShadingType.CoonsPatchMesh;

    private int PointCount => IsTensorProduct ? 16 : 12;

    /// <summary>Adds a patch whose corners carry colours.</summary>
    /// <param name="controlPoints">The control points, in data-stream order: 12 for a Coons patch, 16 for a tensor-product patch.</param>
    /// <param name="cornerColors">The colours at p<sub>00</sub>, p<sub>03</sub>, p<sub>33</sub> and p<sub>30</sub>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The counts are wrong, a point is not finite, or a colour does not fit the mesh.</exception>
    /// <remarks>ISO 32000-2 Table 84 (f = 0).</remarks>
    public PdfPatchMeshBuilder AddPatch(ReadOnlySpan<PathPoint> controlPoints, ReadOnlySpan<PdfColor> cornerColors)
    {
        CheckPoints(controlPoints);
        CheckCorners(cornerColors.Length, nameof(cornerColors));
        foreach (PdfColor color in cornerColors)
        {
            CheckColor(color, nameof(cornerColors));
        }

        AddPatch(controlPoints, cornerColors, []);
        PatchCount++;
        return this;
    }

    /// <summary>Adds a patch whose corners carry a parametric value t.</summary>
    /// <param name="controlPoints">The control points, in data-stream order: 12 for a Coons patch, 16 for a tensor-product patch.</param>
    /// <param name="cornerParameters">The values of t at p<sub>00</sub>, p<sub>03</sub>, p<sub>33</sub> and p<sub>30</sub>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The counts are wrong, a point is not finite, or a value does not fit the mesh.</exception>
    /// <remarks>ISO 32000-2 Table 84 (f = 0) and Table 83 (<c>Function</c>).</remarks>
    public PdfPatchMeshBuilder AddPatch(ReadOnlySpan<PathPoint> controlPoints, ReadOnlySpan<double> cornerParameters)
    {
        CheckPoints(controlPoints);
        CheckCorners(cornerParameters.Length, nameof(cornerParameters));
        foreach (double t in cornerParameters)
        {
            CheckParameter(t, nameof(cornerParameters));
        }

        AddPatch(controlPoints, [], cornerParameters);
        PatchCount++;
        return this;
    }

    /// <inheritdoc/>
    internal override void ThrowIfIncomplete()
    {
        if (PatchCount == 0)
        {
            throw new InvalidOperationException("The mesh has no patch; add one with AddPatch.");
        }
    }

    private static void CheckCorners(int count, string parameter)
    {
        if (count != 4)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"A patch has 4 corner colours; {count} were given (ISO 32000-2 §8.7.4.5.7)."),
                parameter);
        }
    }

    private void CheckPoints(ReadOnlySpan<PathPoint> controlPoints)
    {
        if (controlPoints.Length != PointCount)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {(IsTensorProduct ? "tensor-product" : "Coons")} patch has {PointCount} control points; {controlPoints.Length} were given (ISO 32000-2 §8.7.4.5.{(IsTensorProduct ? 8 : 7)})."),
                nameof(controlPoints));
        }

        foreach (PathPoint point in controlPoints)
        {
            CheckPoint(point, nameof(controlPoints));
        }
    }
}
