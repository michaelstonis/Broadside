using Broadside.Graphics.Shadings;

namespace Broadside.Graphics;

/// <summary>A patch mesh shading: Coons patches (Type 6) or tensor-product patches (Type 7), colours given at the corners.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.7 (Tables 83 and 84) and §8.7.4.5.8 (Table 85). Every patch is stored as the 16 control points of a
/// tensor-product patch: p<sub>ij</sub> (column i along u, row j along v, both 0 to 3) at index 16 × patch + 4 × i + j of
/// <see cref="ControlPoints"/>. A Coons patch's four interior points are computed from its boundary with the equations of
/// §8.7.4.5.8, so both types draw the same way: S(u, v) = Σ p<sub>ij</sub> B<sub>i</sub>(u) B<sub>j</sub>(v). The four corner
/// colours are those of p<sub>00</sub>, p<sub>03</sub>, p<sub>33</sub> and p<sub>30</sub>, in that order.
/// </para>
/// <para>
/// Edge flags 1, 2 and 3 share an edge (four points) and two corner colours with the previous patch, as Tables 84 and 85 give; they
/// are filled in here, so every patch is complete. A patch with such a flag and no patch before it is dropped with a
/// <c>MeshEdgeFlagInvalid</c> diagnostic. Patches are read without padding between them.
/// </para>
/// </remarks>
public sealed class PdfPatchMeshShading : PdfMeshShading
{
    internal PdfPatchMeshShading(ShadingReader reader, PdfShadingType type)
        : base(reader, type)
    {
    }

    /// <summary>Gets a value indicating whether the patches are Coons patches (Type 6) rather than tensor-product patches (Type 7).</summary>
    public bool IsCoons => ShadingType == PdfShadingType.CoonsPatchMesh;

    /// <summary>Gets the number of decoded patches.</summary>
    public int PatchCount => Mesh.PatchCount;

    /// <summary>Gets 16 control points per patch, in the shading's target space: p<sub>ij</sub> at 16 × patch + 4 × i + j.</summary>
    public ReadOnlySpan<PathPoint> ControlPoints => Mesh.Points.AsSpan(0, Mesh.PatchCount * 16);

    /// <summary>Gets four corner colours per patch (at p<sub>00</sub>, p<sub>03</sub>, p<sub>33</sub>, p<sub>30</sub>), <see cref="PdfMeshShading.ColorStride"/> floats each.</summary>
    public ReadOnlySpan<float> CornerColors => Mesh.Colors.AsSpan(0, Mesh.PatchCount * 4 * ColorStride);

    /// <summary>Returns control point p<sub>ij</sub> of a patch.</summary>
    /// <param name="patch">The patch, from 0.</param>
    /// <param name="i">The column, along u: 0 to 3.</param>
    /// <param name="j">The row, along v: 0 to 3.</param>
    /// <returns>The point.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An index is out of range.</exception>
    public PathPoint GetControlPoint(int patch, int i, int j)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(patch, PatchCount);
        ArgumentOutOfRangeException.ThrowIfNegative(i);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(i, 3);
        ArgumentOutOfRangeException.ThrowIfNegative(j);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(j, 3);
        return Mesh.Points[(patch * 16) + (i * 4) + j];
    }
}
