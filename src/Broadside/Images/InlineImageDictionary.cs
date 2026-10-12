using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Images;

/// <summary>Expands the abbreviated keys and values of an inline image dictionary into the full names of an image XObject's.</summary>
/// <remarks>
/// ISO 32000-2 §8.9.7, Tables 91 and 92 (the table is <see cref="InlineImageAbbreviations"/>). Keys: <c>BPC</c>, <c>CS</c>, <c>D</c>, <c>DP</c>, <c>F</c>, <c>H</c>, <c>IM</c>, <c>I</c>
/// (Interpolate), <c>L</c> (Length, PDF 2.0), <c>W</c>; when a key is written both ways the abbreviation wins, as other readers do.
/// Values: the colour space names <c>G</c>, <c>RGB</c>, <c>CMYK</c> and <c>I</c> (Indexed, as an array's first element, whose base
/// may be abbreviated too), and the filter names <c>AHx</c>, <c>A85</c>, <c>LZW</c>, <c>Fl</c>, <c>RL</c>, <c>CCF</c>, <c>DCT</c>.
/// Other keys are kept as written. JPXDecode, JBIG2Decode and Crypt shall not be used in an inline image; they are decoded anyway,
/// with a diagnostic.
/// </remarks>
internal static class InlineImageDictionary
{
    /// <summary>Returns a new dictionary with full keys and values.</summary>
    /// <param name="written">The dictionary as written between <c>BI</c> and <c>ID</c>.</param>
    /// <param name="diagnostics">Where to report a filter not allowed in inline images.</param>
    /// <param name="owner">The content stream, for diagnostics.</param>
    /// <returns>The expanded dictionary.</returns>
    public static CosDictionary Expand(CosDictionary written, DiagnosticSink diagnostics, CosReference? owner)
    {
        var expanded = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in written)
        {
            CosName key = InlineImageAbbreviations.ExpandKey(entry.Key);
            if (key.Equals(entry.Key) && InlineImageAbbreviations.AbbreviationOfKey(key) is { } abbreviation && written.ContainsKey(abbreviation))
            {
                continue;
            }

            expanded[key] = ExpandValue(key, entry.Value, diagnostics, owner);
        }

        return expanded;
    }

    private static CosObject ExpandValue(CosName key, CosObject value, DiagnosticSink diagnostics, CosReference? owner)
    {
        if (key.Equals(ImageNames.Filter))
        {
            return value switch
            {
                CosName name => ExpandFilter(name, diagnostics, owner),
                CosArray names => new CosArray(names.Select(element => element is CosName name ? ExpandFilter(name, diagnostics, owner) : element)),
                _ => value,
            };
        }

        if (key.Equals(ImageNames.ColorSpace))
        {
            switch (value)
            {
                case CosName name:
                    return InlineImageAbbreviations.ExpandColorSpace(name);
                case CosArray { Count: > 0 } array when array[0] is CosName head && InlineImageAbbreviations.ExpandColorSpace(head).Equals(ImageNames.Indexed):
                    var indexed = new CosArray(array) { [0] = ImageNames.Indexed };
                    if (indexed.Count > 1 && indexed[1] is CosName baseName)
                    {
                        indexed[1] = InlineImageAbbreviations.ExpandColorSpace(baseName);
                    }

                    return indexed;
            }
        }

        return value;
    }

    private static CosName ExpandFilter(CosName name, DiagnosticSink diagnostics, CosReference? owner)
    {
        CosName full = InlineImageAbbreviations.TryExpandFilter(name, out CosName? expanded) ? expanded : name;
        if (full.Equals(FilterNames.JpxDecode) || full.Equals(FilterNames.Jbig2Decode) || full.Equals(FilterNames.Crypt))
        {
            diagnostics.Report(
                DiagnosticCodes.InlineImageFilterNotAllowed,
                DiagnosticSeverity.Warning,
                $"The filter /{full.Value} shall not be used in an inline image; it is applied anyway.",
                offset: null,
                owner);
        }

        return full;
    }
}
