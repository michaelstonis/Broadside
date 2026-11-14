using Broadside.Fonts.Cmaps;

// The extension lives in the options' namespace so UsePredefinedCMaps is found next to the other Use* calls without another using.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Broadside;
#pragma warning restore IDE0130

/// <summary>Wires the predefined CMaps package into an engine's options.</summary>
/// <remarks>ISO 32000-2 §9.7.5.2, Table 116.</remarks>
public static class CmapsPdfOptionsExtensions
{
    /// <summary>
    /// Gives composite fonts that name a predefined CMap (such as <c>90ms-RKSJ-H</c> or <c>UniGB-UTF16-H</c>) its mappings, and
    /// makes the CID-to-Unicode tables of the Adobe CJK collections available: registers a <see cref="PredefinedCMapResolver"/> with
    /// <see cref="PdfOptions.UseFontResolver"/>.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>These options.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.7.5.2 (Table 116) and §9.10.2. Without it, a predefined CMap other than Identity-H and Identity-V is recorded
    /// as unavailable (with a diagnostic naming it), its codes are split by its codespace and each shows the glyph of CID 0.
    /// Resolvers registered earlier are asked first, so a resolver with its own CMap files registered before this call wins for the
    /// names it has.
    /// Each CMap and table is parsed the first time a document of the engine needs it and then shared, unchanged, by every document
    /// and thread of that engine (two engines share nothing). The parsed tables the engine keeps are bounded at 256 tables and 16 MiB of
    /// CMap text, about 50 MB of memory; all of this package parsed takes about 9 MB, so a viewer that opens documents in all five
    /// collections holds at most that. Past the bound a table is parsed for the document that needs it and not kept.
    /// </remarks>
    public static PdfOptions UsePredefinedCMaps(this PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseFontResolver(new PredefinedCMapResolver());
    }
}
