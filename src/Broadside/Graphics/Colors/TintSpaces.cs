using System.Globalization;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Colors;

/// <summary>What Separation and DeviceN spaces share: an alternate space and a tint transform (ISO 32000-2 §8.6.6.4, §8.6.6.5).</summary>
internal static class TintSpaces
{
    /// <summary>Returns whether <paramref name="space"/> may be the alternate of a Separation or DeviceN space: a device or CIE-based space.</summary>
    /// <param name="space">The space.</param>
    /// <returns><see langword="true"/> for DeviceGray, DeviceRGB, DeviceCMYK, CalGray, CalRGB, Lab and ICCBased.</returns>
    public static bool IsAlternateFamily(PdfColorSpace space) => space.Family <= PdfColorSpaceFamily.IccBased;

    /// <summary>Returns the alternate to use: the given one when it may be an alternate, else DeviceGray.</summary>
    /// <param name="space">The space the entry reads as, or <see langword="null"/>.</param>
    /// <returns>The alternate.</returns>
    public static PdfColorSpace Alternate(PdfColorSpace? space) =>
        space is not null && IsAlternateFamily(space) ? space : PdfDeviceGrayColorSpace.Instance;

    /// <summary>Returns the tint transform's view, without recording anything when the entry is not a function.</summary>
    /// <param name="cache">The document's colour spaces.</param>
    /// <param name="value">The entry as stored.</param>
    /// <param name="inputs">The number of colourants.</param>
    /// <param name="outputs">The number of alternate components.</param>
    /// <returns>The function, or <see langword="null"/>.</returns>
    public static PdfFunction? Function(ColorSpaceCache cache, CosObject value, int inputs, int outputs) =>
        cache.Resolve(value) is CosDictionary or CosStream ? cache.Document.Functions.Get(value, inputs, outputs) : null;

    /// <summary>Records what is wrong with the alternate and the tint transform.</summary>
    /// <param name="space">The Separation or DeviceN space.</param>
    /// <param name="alternate">The alternate entry as stored.</param>
    /// <param name="tint">The tint transform entry as stored.</param>
    /// <param name="inputs">The number of colourants.</param>
    /// <param name="special">Whether the colourants are All or None, for which both are ignored (but shall still be valid).</param>
    /// <returns><see langword="true"/>: the space stays usable.</returns>
    public static bool Check(PdfColorSpace space, CosObject alternate, CosObject tint, int inputs, bool special)
    {
        ColorSpaceCache cache = space.Cache!;
        PdfColorSpace? found = cache.Find(alternate, space.DiagnosticReference, space.IsInline, out ColorSpaceFailure failure);
        if (found is null)
        {
            ColorSpaceCache.ReportFailure(space, failure, "an alternate colour space");
        }
        else if (!IsAlternateFamily(found))
        {
            space.Report(DiagnosticCodes.ColorSpaceEntryInvalid, "An alternate colour space shall be a device or CIE-based space; DeviceGray is used.");
        }

        int outputs = Alternate(found).ComponentCount;
        PdfFunction? function = Function(cache, tint, inputs, outputs);
        if (function is not { IsValid: true })
        {
            space.Report(
                DiagnosticCodes.TintTransformInvalid,
                special
                    ? "The tint transform is not a usable function; it is not needed for the colourants All and None."
                    : "The tint transform is not a usable function; tints are converted as subtractive ink on the alternate space instead.");
        }
        else if (function.InputCount != inputs || function.OutputCount < outputs)
        {
            space.Report(
                DiagnosticCodes.TintTransformInvalid,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The tint transform takes {function.InputCount} inputs and gives {function.OutputCount} outputs where {inputs} and {outputs} are needed; missing outputs read as 0."));
        }

        return true;
    }
}
