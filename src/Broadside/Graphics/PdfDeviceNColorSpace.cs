using System.Globalization;
using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>A DeviceN colour space: several colourants, each a tint from 0 to 1, with an alternate space and a tint transform.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.6.5, Tables 70 to 72: <c>[/DeviceN names alternateSpace tintTransform attributes]</c>, the attributes
/// dictionary optional (PDF 1.6). Names shall be unique except <c>None</c>, and <c>All</c> shall not appear; components named
/// <c>None</c> are never painted directly but still reach the tint transform; a space whose names are all <c>None</c> never paints.
/// A display always paints through the alternate. The initial colour is 1.0 for every component.
/// </para>
/// <para>
/// Repairs: elements of the names array that are not names are skipped; an empty names array makes the space DeviceGray; the
/// alternate and tint transform are repaired as for <see cref="PdfSeparationColorSpace"/>.
/// </para>
/// </remarks>
public sealed class PdfDeviceNColorSpace : PdfColorSpace
{
    internal PdfDeviceNColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.DeviceN;

    /// <inheritdoc/>
    public override PdfVersion MinimumVersion => Element(4) is CosDictionary ? new PdfVersion(1, 6) : new PdfVersion(1, 3);

    /// <summary>Gets the names of the colourants, one per component.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.5.</remarks>
    public IReadOnlyList<CosName> ColorantNames
    {
        get
        {
            if (Element(1) is not CosArray array)
            {
                return [];
            }

            var names = new List<CosName>(array.Count);
            foreach (CosObject item in array)
            {
                if (Cache!.Resolve(item) is CosName name)
                {
                    names.Add(name);
                }
            }

            return names;
        }
    }

    /// <inheritdoc/>
    public override int ComponentCount
    {
        get
        {
            if (Element(1) is not CosArray array)
            {
                return 0;
            }

            // Indexed, not foreach: the array's enumerator is boxed, and colour operators read this on every scn.
            int count = 0;
            for (int i = 0; i < array.Count; i++)
            {
                count += Cache!.Resolve(array[i]) is CosName ? 1 : 0;
            }

            return count;
        }
    }

    /// <summary>Gets a value indicating whether every colourant is <c>None</c>: the space never paints.</summary>
    public bool AreAllNone => ColorantNames is { Count: > 0 } names && names.All(static name => name.Equals(ColorSpaceNames.None));

    /// <summary>Gets the alternate space, a device or CIE-based space; DeviceGray when the entry is unusable.</summary>
    public PdfColorSpace Alternate => TintSpaces.Alternate(NestedSpace(2));

    /// <summary>Gets the tint transform, or <see langword="null"/> when the entry is not a function.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.5: n inputs, as many outputs as the alternate has components.</remarks>
    public PdfFunction? TintTransform => TintSpaces.Function(Cache!, RawElement(3), ComponentCount, Alternate.ComponentCount);

    /// <summary>Gets the attributes dictionary, or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 Table 70 (PDF 1.6).</remarks>
    public PdfDeviceNAttributes? Attributes => Element(4) is CosDictionary dictionary ? new PdfDeviceNAttributes(this, dictionary) : null;

    /// <summary>Gets a value indicating whether the attributes say NChannel, the subtype with Colorants, Process and MixingHints semantics.</summary>
    /// <remarks>ISO 32000-2 Table 70.</remarks>
    public bool IsNChannel => Attributes?.IsNChannel ?? false;

    /// <inheritdoc/>
    internal override bool Validate()
    {
        IReadOnlyList<CosName> names = ColorantNames;
        if (names.Count == 0)
        {
            Report(DiagnosticCodes.ColorSpaceInvalid, "A DeviceN colour space has no colourant names; DeviceGray is used.");
            return false;
        }

        if (Element(1) is CosArray array && array.Count != names.Count)
        {
            Report(DiagnosticCodes.DeviceNColorantsInvalid, "A DeviceN names array holds something that is not a name; it is skipped.");
        }

        var seen = new HashSet<CosName>();
        foreach (CosName name in names)
        {
            if (name.Equals(ColorSpaceNames.All) || (!name.Equals(ColorSpaceNames.None) && !seen.Add(name)))
            {
                Report(DiagnosticCodes.DeviceNColorantsInvalid, "A DeviceN names array repeats a colourant or names All, which it shall not.");
                break;
            }
        }

        if (names.Count > PdfColor.MaxComponents)
        {
            Report(
                DiagnosticCodes.ColorComponentLimitExceeded,
                string.Create(CultureInfo.InvariantCulture, $"A DeviceN colour space has {names.Count} colourants; colours set in it are painted through its alternate."),
                Diagnostics.DiagnosticSeverity.Information);
        }

        Attributes?.Validate();
        return TintSpaces.Check(this, RawElement(2), RawElement(3), names.Count, special: AreAllNone);
    }

    /// <inheritdoc/>
    internal override void AddDependencies(List<FunctionDependency> dependencies)
    {
        base.AddDependencies(dependencies);
        if (Element(1) is CosArray names)
        {
            dependencies.Add(new FunctionDependency(names, names.Version));
        }

        Alternate.AddDependencies(dependencies);
    }

    /// <inheritdoc/>
    private protected override double GetInitialComponent(int index) => 1;
}
