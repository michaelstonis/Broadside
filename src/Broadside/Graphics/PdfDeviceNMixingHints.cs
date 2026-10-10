using Broadside.Graphics.Colors;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>The <c>MixingHints</c> dictionary of a DeviceN attributes dictionary: how the colourants combine on a printing press.</summary>
/// <remarks>ISO 32000-2 §8.6.6.5, Table 72 (PDF 1.6). A live view; exposed for output devices, not used to paint on a display.</remarks>
public sealed class PdfDeviceNMixingHints
{
    private readonly PdfColorSpace _owner;

    internal PdfDeviceNMixingHints(PdfColorSpace owner, CosDictionary dictionary)
    {
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the mixing hints dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets <c>Solidities</c>: for each colourant (and the key <c>Default</c>), its solidity from 0 to 1.</summary>
    /// <remarks>ISO 32000-2 Table 72. Values that are not numbers are skipped; values are clipped to 0..1.</remarks>
    public IReadOnlyDictionary<CosName, double> Solidities
    {
        get
        {
            var solidities = new Dictionary<CosName, double>();
            if (Cache.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.Solidities)) is CosDictionary entries)
            {
                foreach ((CosName name, CosObject value) in entries)
                {
                    if (ColorEntries.Number(Cache, value) is double solidity)
                    {
                        solidities[name] = Math.Clamp(solidity, 0, 1);
                    }
                }
            }

            return solidities;
        }
    }

    /// <summary>Gets <c>PrintingOrder</c>: the colourant names in the order they are printed.</summary>
    /// <remarks>ISO 32000-2 Table 72 (required when Solidities is present).</remarks>
    public IReadOnlyList<CosName> PrintingOrder => ColorEntries.Names(Cache, Dictionary.GetValueOrDefault(ColorSpaceNames.PrintingOrder));

    /// <summary>Gets <c>DotGain</c>: for each colourant (and <c>Default</c>), the function mapping tint to dot gain.</summary>
    /// <remarks>ISO 32000-2 Table 72 and §7.10. Entries that are not functions are skipped.</remarks>
    public IReadOnlyDictionary<CosName, PdfFunction> DotGain
    {
        get
        {
            var gains = new Dictionary<CosName, PdfFunction>();
            if (Cache.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.DotGain)) is CosDictionary entries)
            {
                foreach ((CosName name, CosObject value) in entries)
                {
                    if (TintSpaces.Function(Cache, value, 1, 1) is { } function)
                    {
                        gains[name] = function;
                    }
                }
            }

            return gains;
        }
    }

    private ColorSpaceCache Cache => _owner.Cache!;
}
