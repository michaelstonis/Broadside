using Broadside.Graphics.Colors;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>The attributes dictionary of a DeviceN colour space: its subtype, spot colourants, process space and mixing hints.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.6.5, Table 70 (PDF 1.6). A live view; it is read and exposed, while blending by these attributes (NChannel
/// separations, mixing hints) is output-device work beyond a display's needs.
/// </remarks>
public sealed class PdfDeviceNAttributes
{
    private readonly PdfColorSpace _owner;

    internal PdfDeviceNAttributes(PdfColorSpace owner, CosDictionary dictionary)
    {
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the attributes dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the <c>Subtype</c>: <c>DeviceN</c> (the default) or <c>NChannel</c>.</summary>
    /// <remarks>ISO 32000-2 Table 70.</remarks>
    public CosName Subtype => Cache.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.Subtype)) as CosName ?? ColorSpaceNames.DeviceN;

    /// <summary>Gets a value indicating whether <see cref="Subtype"/> is <c>NChannel</c>.</summary>
    public bool IsNChannel => Subtype.Equals(ColorSpaceNames.NChannel);

    /// <summary>Gets the <c>Colorants</c> dictionary: for each spot colourant name, its Separation colour space.</summary>
    /// <remarks>ISO 32000-2 Table 70: required for an NChannel space with spot colourants. Entries that are not colour spaces are skipped.</remarks>
    public IReadOnlyDictionary<CosName, PdfColorSpace> Colorants
    {
        get
        {
            var colorants = new Dictionary<CosName, PdfColorSpace>();
            if (Cache.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.Colorants)) is CosDictionary entries)
            {
                foreach ((CosName name, CosObject value) in entries)
                {
                    if (Cache.Find(value, _owner.DiagnosticReference, inline: false, out _) is { } space)
                    {
                        colorants[name] = space;
                    }
                }
            }

            return colorants;
        }
    }

    /// <summary>Gets the <c>Process</c> dictionary: the process colour space and which components belong to it, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Tables 70 and 71.</remarks>
    public PdfDeviceNProcess? Process =>
        Cache.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.Process)) is CosDictionary process ? new PdfDeviceNProcess(_owner, process) : null;

    /// <summary>Gets the <c>MixingHints</c> dictionary, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Tables 70 and 72.</remarks>
    public PdfDeviceNMixingHints? MixingHints =>
        Cache.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.MixingHints)) is CosDictionary hints ? new PdfDeviceNMixingHints(_owner, hints) : null;

    private ColorSpaceCache Cache => _owner.Cache!;

    /// <summary>Records what is wrong with the attributes.</summary>
    internal void Validate()
    {
        if (Dictionary.TryGetValue(ColorSpaceNames.Colorants, out CosObject? colorants) && Cache.Resolve(colorants) is not CosDictionary)
        {
            _owner.Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A DeviceN attributes dictionary's Colorants is not a dictionary; it is ignored.");
        }

        if (Process is { } process && (process.ColorSpace is null || process.Components.Count == 0))
        {
            _owner.Report(DiagnosticCodes.ColorSpaceEntryInvalid, "A DeviceN Process dictionary lacks its ColorSpace or Components; it is ignored.");
        }
    }
}
