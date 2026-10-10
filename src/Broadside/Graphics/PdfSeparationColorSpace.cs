using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A Separation colour space: one colourant, a tint from 0 (none) to 1 (full), with an alternate space for devices without it.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.6.4: <c>[/Separation name alternateSpace tintTransform]</c>. The tint transform maps the tint to the alternate
/// space, a device or CIE-based space; a display, being additive, always paints through it. The colourant <c>All</c> marks every
/// colourant of the device (registration marks) and <c>None</c> paints nothing; for both the alternate and tint transform are
/// ignored. The initial colour is 1.0.
/// </para>
/// <para>
/// Repairs: an alternate that is not a device or CIE-based space reads as DeviceGray; a tint transform that is not a function is
/// replaced by the subtractive default (gray 1 − t, RGB (1 − t)×3, CMYK (0, 0, 0, t), else zeros). A colourant that is not a name
/// makes the space DeviceGray.
/// </para>
/// </remarks>
public sealed class PdfSeparationColorSpace : PdfColorSpace
{
    internal PdfSeparationColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.Separation;

    /// <inheritdoc/>
    public override int ComponentCount => 1;

    /// <summary>Gets the name of the colourant.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.4: Cyan, Magenta, Yellow and Black are reserved for the process colourants.</remarks>
    public CosName ColorantName => Element(1) as CosName ?? ColorSpaceNames.None;

    /// <summary>Gets a value indicating whether the colourant is <c>All</c>, every colourant of the device.</summary>
    public bool IsAll => ColorantName.Equals(ColorSpaceNames.All);

    /// <summary>Gets a value indicating whether the colourant is <c>None</c>, which paints nothing.</summary>
    public bool IsNone => ColorantName.Equals(ColorSpaceNames.None);

    /// <summary>Gets the alternate space, a device or CIE-based space; DeviceGray when the entry is unusable.</summary>
    public PdfColorSpace Alternate => TintSpaces.Alternate(NestedSpace(2));

    /// <summary>Gets the tint transform, or <see langword="null"/> when the entry is not a function.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.4 and §7.10: one input, as many outputs as the alternate has components.</remarks>
    public PdfFunction? TintTransform => TintSpaces.Function(Cache!, RawElement(3), 1, Alternate.ComponentCount);

    /// <inheritdoc/>
    internal override bool Validate()
    {
        if (Element(1) is not CosName)
        {
            Report(DiagnosticCodes.ColorSpaceInvalid, "A Separation colour space's colourant is not a name; DeviceGray is used.");
            return false;
        }

        return TintSpaces.Check(this, RawElement(2), RawElement(3), 1, special: IsAll || IsNone);
    }

    /// <inheritdoc/>
    internal override void AddDependencies(List<FunctionDependency> dependencies)
    {
        base.AddDependencies(dependencies);
        Alternate.AddDependencies(dependencies);
    }

    /// <inheritdoc/>
    private protected override double GetInitialComponent(int index) => 1;
}
