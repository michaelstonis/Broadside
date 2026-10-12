using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Colors;

/// <summary>The checks the CIE dictionaries share (ISO 32000-2 Tables 62 to 64).</summary>
internal static class CieChecks
{
    /// <summary>Records a missing or invalid <c>WhitePoint</c> and an invalid <c>BlackPoint</c>.</summary>
    /// <param name="space">The space reporting.</param>
    /// <param name="cache">The document's colour spaces.</param>
    /// <param name="dictionary">The CIE dictionary.</param>
    /// <param name="family">The family name, for the message.</param>
    public static void WhiteAndBlack(PdfColorSpace space, ColorSpaceCache cache, CosDictionary dictionary, string family)
    {
        if (ColorEntries.WhitePoint(cache, dictionary) is null)
        {
            space.Report(
                DiagnosticCodes.ColorSpaceEntryInvalid,
                $"A {family} WhitePoint is missing or invalid (it shall be three numbers with X and Z positive and Y 1.0); D65 is used.");
        }

        if (ColorEntries.BlackPoint(cache, dictionary, out bool present) is null && present)
        {
            space.Report(DiagnosticCodes.ColorSpaceEntryInvalid, $"A {family} BlackPoint is not three non-negative numbers; [0 0 0] is used.");
        }
    }

    /// <summary>Returns the dictionary at element 1 of a CIE space array, reporting when it is missing.</summary>
    /// <param name="space">The space.</param>
    /// <param name="value">The resolved element 1.</param>
    /// <param name="family">The family name, for the message.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public static CosDictionary? Dictionary(PdfColorSpace space, CosObject value, string family)
    {
        if (value is CosDictionary dictionary)
        {
            return dictionary;
        }

        space.Report(DiagnosticCodes.ColorSpaceEntryInvalid, $"A {family} colour space has no dictionary; its entries take their defaults and D65 is used as the white point.");
        return null;
    }
}
