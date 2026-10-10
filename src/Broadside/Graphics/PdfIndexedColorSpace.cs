using System.Globalization;
using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>An Indexed colour space: one component, an index from 0 to <see cref="HighValue"/> into a colour table in a base space.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.6.3: <c>[/Indexed base hival lookup]</c> (<c>/I</c> in an inline image). The base is any device, CIE-based,
/// Separation or DeviceN space; the lookup table is a string or stream of m × (hival + 1) bytes, m being the base's component
/// count; byte values 0 to 255 map linearly onto each base component's range. An index is rounded to the nearest integer and clipped
/// to 0..hival.
/// </para>
/// <para>
/// Repairs: hival outside 0..255 is clipped; a short table reads as zeros past its end, a long one is cut. A space without a usable
/// base or lookup table is read as DeviceGray.
/// </para>
/// </remarks>
public sealed class PdfIndexedColorSpace : PdfColorSpace
{
    internal PdfIndexedColorSpace(ColorSpaceCache cache, CosArray array, CosReference? reference)
        : base(cache, array, reference)
    {
    }

    /// <inheritdoc/>
    public override PdfColorSpaceFamily Family => PdfColorSpaceFamily.Indexed;

    /// <inheritdoc/>
    public override int ComponentCount => 1;

    /// <inheritdoc/>
    public override PdfVersion MinimumVersion => Element(3) is CosString ? new PdfVersion(1, 2) : new PdfVersion(1, 1);

    /// <summary>Gets the base colour space, the space of the table's colours.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.3.</remarks>
    public PdfColorSpace Base => NestedSpace(1) ?? PdfDeviceGrayColorSpace.Instance;

    /// <summary>Gets hival, the largest index, 0 to 255.</summary>
    /// <remarks>ISO 32000-2 §8.6.6.3: an integer "whose value shall not be greater than 255".</remarks>
    public int HighValue => ColorEntries.Number(Cache!, Element(2)) is double value ? (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255) : 0;

    /// <summary>Returns the lookup table as stored: a string's bytes, or a stream's decoded data.</summary>
    /// <returns>The table; empty when there is none.</returns>
    /// <remarks>ISO 32000-2 §8.6.6.3: m × (hival + 1) bytes, component by component for each colour in turn.</remarks>
    public ReadOnlyMemory<byte> GetLookup() => Element(3) switch
    {
        CosString text => text.Bytes.ToArray(),
        CosStream stream => Cache!.Document.DecodeStream(stream),
        _ => ReadOnlyMemory<byte>.Empty,
    };

    /// <inheritdoc/>
    internal override bool Validate()
    {
        PdfColorSpace? baseSpace = Cache!.Find(RawElement(1), DiagnosticReference, IsInline, out ColorSpaceFailure failure);
        if (baseSpace is null)
        {
            ColorSpaceCache.ReportFailure(this, failure, "an Indexed base");
            return false;
        }

        if (baseSpace.Family is PdfColorSpaceFamily.Pattern or PdfColorSpaceFamily.Indexed)
        {
            Report(DiagnosticCodes.ColorSpaceInvalid, "An Indexed colour space's base is a Pattern or Indexed space, which it shall not be; DeviceGray is used.");
            return false;
        }

        if (ColorEntries.Number(Cache!, Element(2)) is not double hival)
        {
            Report(DiagnosticCodes.ColorSpaceInvalid, "An Indexed colour space's hival is not a number; DeviceGray is used.");
            return false;
        }

        if (hival is < 0 or > 255 || hival != Math.Floor(hival))
        {
            Report(DiagnosticCodes.ColorSpaceEntryInvalid, string.Create(CultureInfo.InvariantCulture, $"An Indexed colour space's hival {hival} is not an integer from 0 to 255; {HighValue} is used."));
        }

        if (Element(3) is not (CosString or CosStream))
        {
            Report(DiagnosticCodes.IndexedLookupInvalid, "An Indexed colour space has no lookup string or stream; DeviceGray is used.");
            return false;
        }

        int expected = baseSpace.ComponentCount * (HighValue + 1);
        int length = GetLookup().Length;
        if (length < expected)
        {
            Report(DiagnosticCodes.IndexedLookupInvalid, string.Create(CultureInfo.InvariantCulture, $"An Indexed lookup table has {length} bytes where {expected} are needed; the missing entries read as zeros."));
        }
        else if (length > expected)
        {
            Report(DiagnosticCodes.IndexedLookupInvalid, string.Create(CultureInfo.InvariantCulture, $"An Indexed lookup table has {length} bytes where {expected} are needed; the rest are ignored."));
        }

        return true;
    }

    /// <inheritdoc/>
    internal override void AddDependencies(List<FunctionDependency> dependencies)
    {
        base.AddDependencies(dependencies);
        Base.AddDependencies(dependencies);
        if (Element(3) is CosStream stream)
        {
            dependencies.Add(new FunctionDependency(stream, stream.Version));
        }
    }

    /// <inheritdoc/>
    private protected override ComponentRange GetRangeCore(int index) => new(0, HighValue);
}
