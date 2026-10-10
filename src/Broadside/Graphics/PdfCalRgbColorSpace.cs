using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A CalRGB colour space: components A, B and C, each 0 to 1, calibrated by a white point, gammas and a matrix.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.3, Table 63: <c>[/CalRGB dictionary]</c>. X = XA·A^GR + XB·B^GG + XC·C^GB, and likewise Y and Z, with
/// <c>Matrix</c> = [XA YA ZA XB YB ZB XC YC ZC]. Missing or invalid entries read as their defaults (a white point as D65), each
/// recorded once.
/// </remarks>
public sealed class PdfCalRgbColorSpace : PdfColorSpace
{
    private static readonly double[] IdentityMatrix = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    internal PdfCalRgbColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.CalRgb;

    /// <inheritdoc/>
    public override int ComponentCount => 3;

    /// <summary>Gets the CalRGB dictionary; empty when the array has none.</summary>
    public CosDictionary Dictionary => Element(1) as CosDictionary ?? [];

    /// <summary>Gets the diffuse white point, <c>WhitePoint</c>; D65 when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 63 (required).</remarks>
    public CieXyz WhitePoint => ColorEntries.WhitePoint(Cache!, Dictionary) ?? CieXyz.D65;

    /// <summary>Gets the diffuse black point, <c>BlackPoint</c>; [0 0 0] when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 63.</remarks>
    public CieXyz BlackPoint => ColorEntries.BlackPoint(Cache!, Dictionary, out _) ?? default;

    /// <summary>Gets the gammas [GR GG GB], <c>Gamma</c>; [1 1 1] when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 63.</remarks>
    public IReadOnlyList<double> Gamma =>
        ColorEntries.Numbers(Cache!, Dictionary, ColorSpaceNames.Gamma) is [> 0, > 0, > 0] gamma ? gamma : [1.0, 1.0, 1.0];

    /// <summary>Gets the linear interpretation of the decoded components, <c>Matrix</c>; the identity when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 63: [XA YA ZA XB YB ZB XC YC ZC].</remarks>
    public IReadOnlyList<double> Matrix =>
        ColorEntries.Numbers(Cache!, Dictionary, ColorSpaceNames.Matrix) is { Length: 9 } matrix ? matrix : IdentityMatrix;

    /// <inheritdoc/>
    internal override bool Validate()
    {
        if (CieChecks.Dictionary(this, Element(1), "CalRGB") is not { } dictionary)
        {
            return true;
        }

        CieChecks.WhiteAndBlack(this, Cache!, dictionary, "CalRGB");
        if (dictionary.ContainsKey(ColorSpaceNames.Gamma) && ColorEntries.Numbers(Cache!, dictionary, ColorSpaceNames.Gamma) is not [> 0, > 0, > 0])
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A CalRGB Gamma is not three positive numbers; [1 1 1] is used.");
        }

        if (dictionary.ContainsKey(ColorSpaceNames.Matrix) && ColorEntries.Numbers(Cache!, dictionary, ColorSpaceNames.Matrix) is not { Length: 9 })
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A CalRGB Matrix is not nine numbers; the identity is used.");
        }

        return true;
    }

    /// <inheritdoc/>
    internal override void AddDependencies(List<FunctionDependency> dependencies)
    {
        base.AddDependencies(dependencies);
        if (Element(1) is CosDictionary dictionary)
        {
            dependencies.Add(new FunctionDependency(dictionary, dictionary.Version));
        }
    }
}
