using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>The Pattern colour space: colours are patterns, named by <c>scn</c>; for uncoloured patterns, with an underlying space.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.6.2 and §8.7.3.3: <c>/Pattern</c> alone for coloured tiling and shading patterns, <c>[/Pattern base]</c> for
/// uncoloured tiling patterns, whose colour is given in the underlying space <c>base</c> (which shall not be a Pattern space). The
/// initial colour is a pattern that paints nothing (Table 73). Images cannot use this space (Table 88).
/// </remarks>
public sealed class PdfPatternColorSpace : PdfColorSpace
{
    private PdfPatternColorSpace()
        : base(null, ColorSpaceNames.Pattern, null)
    {
    }

    internal PdfPatternColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.Pattern;

    /// <summary>Gets the underlying colour space of uncoloured patterns, or <see langword="null"/> for <c>/Pattern</c> alone.</summary>
    /// <remarks>ISO 32000-2 §8.7.3.3.</remarks>
    public PdfColorSpace? Underlying => Cache is null ? null : NestedSpace(1) is { Family: not PdfColorSpaceFamily.Pattern } space ? space : null;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §8.6.8: for an uncoloured pattern, <c>scn</c> takes the underlying space's components before the name.</remarks>
    public override int ComponentCount => Underlying?.ComponentCount ?? 0;

    /// <summary>Gets the space named <c>/Pattern</c> alone, for coloured patterns.</summary>
    internal static PdfPatternColorSpace Colored { get; } = new();

    /// <inheritdoc/>
    internal override bool Validate()
    {
        if (CosObject is CosArray { Count: > 1 } && Underlying is null)
        {
            PdfColorSpace? found = Cache!.Find(RawElement(1), DiagnosticReference, IsInline, out ColorSpaceFailure failure);
            if (found is null)
            {
                ColorSpaceCache.ReportFailure(this, failure, "a Pattern underlying space");
            }
            else
            {
                Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A Pattern colour space's underlying space is itself a Pattern space; it is ignored.");
            }
        }

        return true;
    }

    /// <inheritdoc/>
    internal override void AddDependencies(List<FunctionDependency> dependencies)
    {
        base.AddDependencies(dependencies);
        Underlying?.AddDependencies(dependencies);
    }

    /// <inheritdoc/>
    private protected override ComponentRange GetRangeCore(int index) => Underlying!.GetComponentRange(index);
}
