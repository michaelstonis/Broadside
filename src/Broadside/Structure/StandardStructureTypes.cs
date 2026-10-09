namespace Broadside.Structure;

/// <summary>The standard structure types of the PDF 1.7 and PDF 2.0 standard structure namespaces.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.4 (Tables 364-375, the PDF 2.0 namespace), §14.8.6.1 and Annex M (the differences); ISO/TS 32005 §5.3-§5.5 and
/// Tables 2-3 (the unique PDF 1.7 types). <c>Hn</c> is any <c>H</c> followed by a decimal number of at least 1 in PDF 2.0; the PDF 1.7
/// namespace has only <c>H1</c> to <c>H6</c>.
/// </remarks>
internal static class StandardStructureTypes
{
    /// <summary>Types defined in both namespaces (ISO/TS 32005 §5.3), headings <c>H1</c>-<c>H6</c> included.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "Document", "Part", "Sect", "Div", "NonStruct",
        "P", "H", "H1", "H2", "H3", "H4", "H5", "H6",
        "Lbl", "Span", "Link", "Annot", "Form", "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP",
        "L", "LI", "LBody",
        "Table", "TR", "TH", "TD", "THead", "TBody", "TFoot",
        "Caption", "Figure", "Formula",
    };

    /// <summary>Types defined solely in the PDF 1.7 namespace (Annex M; ISO/TS 32005 Tables 2 and 3).</summary>
    private static readonly HashSet<string> Pdf17Only = new(StringComparer.Ordinal)
    {
        "Art", "BlockQuote", "TOC", "TOCI", "Index", "Private", "Quote", "Note", "Reference", "BibEntry", "Code",
    };

    /// <summary>Types defined solely in the PDF 2.0 namespace (Annex M), except <c>Hn</c> with n above 6.</summary>
    private static readonly HashSet<string> Pdf20Only = new(StringComparer.Ordinal)
    {
        "DocumentFragment", "Aside", "Title", "FENote", "Sub", "Em", "Strong", "Artifact",
    };

    /// <summary>Whether <paramref name="type"/> is a standard type of the PDF 1.7 namespace.</summary>
    public static bool IsPdf17(string type) => Common.Contains(type) || Pdf17Only.Contains(type);

    /// <summary>Whether <paramref name="type"/> is a standard type of the PDF 2.0 namespace.</summary>
    public static bool IsPdf20(string type) => Common.Contains(type) || Pdf20Only.Contains(type) || IsHn(type);

    /// <summary>Whether <paramref name="type"/> is defined in both namespaces (ISO/TS 32005 §5.3).</summary>
    public static bool IsCommon(string type) => Common.Contains(type);

    /// <summary>Whether <paramref name="type"/> is a heading <c>Hn</c>: <c>H</c> followed by a decimal number of at least 1 (§14.8.4.5).</summary>
    public static bool IsHn(string type)
    {
        if (type.Length < 2 || type[0] != 'H' || type[1] == '0')
        {
            return false;
        }

        for (int index = 1; index < type.Length; index++)
        {
            if (!char.IsAsciiDigit(type[index]))
            {
                return false;
            }
        }

        return true;
    }
}
