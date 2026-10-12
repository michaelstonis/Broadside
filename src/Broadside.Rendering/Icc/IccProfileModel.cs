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
    /// A profile whose data colour space the engine does not convert by its tags (such as a Lab colour space profile, which
    /// converts as Lab), or that has neither usable lookup tables nor matrix/TRC tags.
    /// </summary>
    Other,

    /// <summary>
    /// A profile with lookup tables (<c>AToB0</c>, <c>AToB1</c>, <c>AToB2</c>: lut8Type, lut16Type or lutAToBType), which take
    /// precedence over matrix/TRC tags (ICC.1:2022 §8.10.2, §10.10 to §10.12).
    /// </summary>
    LookupTable,
}
