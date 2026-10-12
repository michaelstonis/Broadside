using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A CalGray colour space: one component A, 0 to 1, calibrated by a white point and a gamma.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.2, Table 62: <c>[/CalGray dictionary]</c>. A maps to X = XW·A^G, Y = YW·A^G, Z = ZW·A^G. A missing or
/// invalid white point reads as D65, a negative black point as [0 0 0] and a gamma that is not positive as 1, each recorded once.
/// </remarks>
public sealed class PdfCalGrayColorSpace : PdfColorSpace
{
    internal PdfCalGrayColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.CalGray;

    /// <inheritdoc/>
    public override int ComponentCount => 1;

    /// <summary>Gets the CalGray dictionary; empty when the array has none.</summary>
    public CosDictionary Dictionary => Element(1) as CosDictionary ?? [];

    /// <summary>Gets the diffuse white point, <c>WhitePoint</c>; D65 when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 62 (required).</remarks>
    public CieXyz WhitePoint => ColorEntries.WhitePoint(Cache!, Dictionary) ?? CieXyz.D65;

    /// <summary>Gets the diffuse black point, <c>BlackPoint</c>; [0 0 0] when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 62.</remarks>
    public CieXyz BlackPoint => ColorEntries.BlackPoint(Cache!, Dictionary, out _) ?? default;

    /// <summary>Gets the gamma G, <c>Gamma</c>; 1 when missing or not positive.</summary>
    /// <remarks>ISO 32000-2 Table 62.</remarks>
    public double Gamma => ColorEntries.Number(Cache!, Dictionary, ColorSpaceNames.Gamma) is > 0 and double gamma ? gamma : 1;

    /// <inheritdoc/>
    internal override bool Validate()
    {
        if (CieChecks.Dictionary(this, Element(1), "CalGray") is not { } dictionary)
        {
            return true;
        }

        CieChecks.WhiteAndBlack(this, Cache!, dictionary, "CalGray");
        if (dictionary.ContainsKey(ColorSpaceNames.Gamma) && ColorEntries.Number(Cache!, dictionary, ColorSpaceNames.Gamma) is not > 0)
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A CalGray Gamma is not a positive number; 1 is used.");
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
