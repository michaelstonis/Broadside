namespace Broadside.Rendering.Icc;

/// <summary>The computational model by which an ICC profile takes device colours to the profile connection space.</summary>
/// <remarks>ICC.1:2022 §8.3 to §8.5 (profile types), §8.10 (precedence of tag usage) and Annex F (computational models).</remarks>
public enum IccProfileModel
{
    /// <summary>
    /// A three-component matrix-based profile: <c>rTRC</c>, <c>gTRC</c>, <c>bTRC</c> curves and the <c>rXYZ</c>, <c>gXYZ</c>,
    /// <c>bXYZ</c> matrix columns (ICC.1:2022 Annex F.3).
    /// </summary>
    MatrixTrc,

    /// <summary>A monochrome profile: the <c>kTRC</c> curve scales the PCS white (ICC.1:2022 Annex F.2).</summary>
    Monochrome,

    /// <summary>
    /// Any other profile: lookup-table based (<c>AToB0</c> and its kind), or of a data colour space other than gray and RGB. Its
    /// tags are not read beyond the tag table yet; such colours convert through the managed default.
    /// </summary>
    Other,
}
