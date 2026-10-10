using Broadside.Fonts.Standard14;

// The extension lives in the options' namespace so UseStandard14Fonts is found next to the other Use* calls without another using.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Broadside;
#pragma warning restore IDE0130

/// <summary>Wires the Standard 14 glyph package into an engine's options.</summary>
/// <remarks>ISO 32000-2 §9.6.2.2; ADR 0007.</remarks>
public static class Standard14PdfOptionsExtensions
{
    /// <summary>
    /// Draws non-embedded Standard 14 fonts with the package's fonts (Liberation Sans, Serif and Mono, Foxit Symbol and Dingbats),
    /// on every machine, ahead of operating-system fonts: registers a <see cref="Standard14FontResolver"/> with
    /// <see cref="PdfOptions.UseFontResolver"/>.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>These options.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.6.2.2. Resolvers registered earlier are asked first, so a company font store registered before this call
    /// still wins for the fonts it has. Fonts that are not Standard 14 fonts and that no resolver or system font matches are drawn
    /// with the package font most like them (§9.8), with a diagnostic.
    /// </remarks>
    public static PdfOptions UseStandard14Fonts(this PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseFontResolver(new Standard14FontResolver());
    }
}
