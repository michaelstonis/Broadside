using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A Lab colour space: components L* (0 to 100), a* and b* (within <see cref="Range"/>), relative to a white point.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.5.4, Table 64: <c>[/Lab dictionary]</c>. M = (L* + 16) / 116, L = M + a* / 500, N = M − b* / 200;
/// X = XW·g(L), Y = YW·g(M), Z = ZW·g(N), with g(x) = x³ for x ≥ 6/29, else 108/841·(x − 4/29). A missing or invalid
/// <c>Range</c> reads as [−100 100 −100 100]; the initial colour is 0 0 0 clipped into the ranges.
/// </remarks>
public sealed class PdfLabColorSpace : PdfColorSpace
{
    private static readonly double[] DefaultRange = [-100, 100, -100, 100];

    internal PdfLabColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.Lab;

    /// <inheritdoc/>
    public override int ComponentCount => 3;

    /// <summary>Gets the Lab dictionary; empty when the array has none.</summary>
    public CosDictionary Dictionary => Element(1) as CosDictionary ?? [];

    /// <summary>Gets the diffuse white point, <c>WhitePoint</c>; D65 when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 64 (required).</remarks>
    public CieXyz WhitePoint => ColorEntries.WhitePoint(Cache!, Dictionary) ?? CieXyz.D65;

    /// <summary>Gets the diffuse black point, <c>BlackPoint</c>; [0 0 0] when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 64.</remarks>
    public CieXyz BlackPoint => ColorEntries.BlackPoint(Cache!, Dictionary, out _) ?? default;

    /// <summary>Gets the ranges of a* and b*, [amin amax bmin bmax]; [−100 100 −100 100] when missing or invalid.</summary>
    /// <remarks>ISO 32000-2 Table 64.</remarks>
    public IReadOnlyList<double> Range => ReadRange() ?? DefaultRange;

    /// <inheritdoc/>
    internal override bool Validate()
    {
        if (CieChecks.Dictionary(this, Element(1), "Lab") is not { } dictionary)
        {
            return true;
        }

        CieChecks.WhiteAndBlack(this, Cache!, dictionary, "Lab");
        if (dictionary.ContainsKey(ColorSpaceNames.Range) && ReadRange() is null)
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A Lab Range is not four numbers [amin amax bmin bmax] with each minimum below its maximum; [-100 100 -100 100] is used.");
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

    /// <inheritdoc/>
    private protected override ComponentRange GetRangeCore(int index)
    {
        if (index == 0)
        {
            return new ComponentRange(0, 100);
        }

        IReadOnlyList<double> range = Range;
        return new ComponentRange(range[2 * (index - 1)], range[(2 * (index - 1)) + 1]);
    }

    private double[]? ReadRange() =>
        ColorEntries.Numbers(Cache!, Element(1) as CosDictionary, ColorSpaceNames.Range) is [double amin, double amax, double bmin, double bmax] range
            && amin <= amax && bmin <= bmax
            ? range
            : null;
}
