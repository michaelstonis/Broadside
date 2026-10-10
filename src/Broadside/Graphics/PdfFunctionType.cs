namespace Broadside.Graphics;

/// <summary>The kind of a function object, its <c>FunctionType</c> entry.</summary>
/// <remarks>ISO 32000-2 §7.10.1, Table 38. Type 1 is not defined.</remarks>
public enum PdfFunctionType
{
    /// <summary>A sampled function (PDF 1.2): a table of sample values with interpolation (§7.10.2).</summary>
    Sampled = 0,

    /// <summary>An exponential interpolation function (PDF 1.3) between two sets of coefficients (§7.10.3).</summary>
    Exponential = 2,

    /// <summary>A stitching function (PDF 1.3): 1-input functions applied over subdomains of one domain (§7.10.4).</summary>
    Stitching = 3,

    /// <summary>A PostScript calculator function (PDF 1.3): a program in a subset of the PostScript language (§7.10.5).</summary>
    PostScriptCalculator = 4,
}
