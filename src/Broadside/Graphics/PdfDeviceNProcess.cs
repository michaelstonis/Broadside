using Broadside.Graphics.Colors;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>The <c>Process</c> dictionary of a DeviceN attributes dictionary: the process colour space and its component names.</summary>
/// <remarks>ISO 32000-2 §8.6.6.5, Table 71 (PDF 1.6). A live view.</remarks>
public sealed class PdfDeviceNProcess
{
    private readonly PdfColorSpace _owner;

    internal PdfDeviceNProcess(PdfColorSpace owner, CosDictionary dictionary)
    {
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the process dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the process colour space (a device or CIE-based space other than Lab), or <see langword="null"/> when missing.</summary>
    /// <remarks>ISO 32000-2 Table 71 (required).</remarks>
    public PdfColorSpace? ColorSpace =>
        Dictionary.TryGetValue(ColorSpaceNames.ColorSpace, out CosObject? value) ? _owner.Cache!.Find(value, _owner.DiagnosticReference, inline: false, out _) : null;

    /// <summary>Gets the names of the DeviceN components that correspond to the process space's components, in its order.</summary>
    /// <remarks>ISO 32000-2 Table 71 (required).</remarks>
    public IReadOnlyList<CosName> Components =>
        _owner.Cache!.Resolve(Dictionary.GetValueOrDefault(ColorSpaceNames.Components)) is CosArray array
            ? [.. array.Select(item => _owner.Cache.Resolve(item)).OfType<CosName>()]
            : [];
}
