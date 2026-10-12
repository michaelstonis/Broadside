using System.Diagnostics.CodeAnalysis;

namespace Broadside.Graphics;

/// <summary>The seven kinds of shading, the <c>ShadingType</c> entry of a shading dictionary.</summary>
/// <remarks>ISO 32000-2 §8.7.4.5.1, Table 77.</remarks>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The values are the numbers the PDF entry holds; 0 is not one of them.")]
public enum PdfShadingType
{
    /// <summary>Type 1: colour as a function of two variables over a domain (§8.7.4.5.2, Table 78).</summary>
    FunctionBased = 1,

    /// <summary>Type 2: colour varying along an axis (§8.7.4.5.3, Table 79).</summary>
    Axial = 2,

    /// <summary>Type 3: colour varying between two circles (§8.7.4.5.4, Table 80).</summary>
    Radial = 3,

    /// <summary>Type 4: a free-form mesh of triangles with colours at their vertices (§8.7.4.5.5, Table 81).</summary>
    FreeFormTriangleMesh = 4,

    /// <summary>Type 5: a lattice of triangles with colours at their vertices (§8.7.4.5.6, Table 82).</summary>
    LatticeFormTriangleMesh = 5,

    /// <summary>Type 6: a mesh of Coons patches, bounded by four cubic Bézier curves (§8.7.4.5.7, Table 83).</summary>
    CoonsPatchMesh = 6,

    /// <summary>Type 7: a mesh of tensor-product patches, sixteen control points each (§8.7.4.5.8).</summary>
    TensorProductPatchMesh = 7,
}
