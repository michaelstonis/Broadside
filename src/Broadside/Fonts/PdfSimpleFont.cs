using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>A simple font: one byte per character code, a 256-entry encoding from codes to glyph names, and one width per code.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.1. <see cref="GetGlyphName"/> applies the font's encoding (§9.6.5): the <c>Encoding</c> entry, a predefined
/// encoding or an encoding dictionary with <c>BaseEncoding</c> and <c>Differences</c>, over the font's built-in encoding.
/// <see cref="GetWidth"/> applies <c>FirstChar</c>, <c>LastChar</c>, <c>Widths</c> and the descriptor's <c>MissingWidth</c>
/// (§9.6.2.1, Table 109), and the Standard 14 metrics for a non-embedded Standard 14 font that does not give them (§9.6.2.2).
/// </para>
/// <para>
/// The 256 names and widths are computed on first use and kept until the font dictionary, its <c>Widths</c> array, its encoding
/// dictionary, the <c>Differences</c> array or its font descriptor changes; looking one up then allocates nothing. Deviations
/// found while computing them are recorded once, on the font's object, when first used: in strict mode that call throws.
/// </para>
/// </remarks>
public abstract class PdfSimpleFont : PdfFont
{
    private volatile SimpleFontMetrics? _metrics;

    private protected PdfSimpleFont(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfFontType fontType)
        : base(document, dictionary, reference, fontType)
    {
    }

    /// <summary>
    /// Gets the Standard 14 font whose metrics this font uses, or <see langword="null"/>. A Type 1 or TrueType font that is not
    /// embedded uses them when its <c>BaseFont</c> is one of the fourteen names, or another name producers use for one of them
    /// (such as <c>Arial,Bold</c> for Helvetica-Bold, recorded as an information diagnostic).
    /// </summary>
    /// <remarks>ISO 32000-2 §9.6.2.2.</remarks>
    public Standard14Font? Standard14 => Metrics.Standard14;

    /// <summary>Gets the glyph name the font's encoding gives a character code; <c>.notdef</c> when it gives none.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>The glyph name.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.6.5. The built-in encoding is the Symbol or ZapfDingbats encoding for those two Standard 14 fonts (Annex D.5,
    /// D.6) and StandardEncoding for the other twelve and for any other font that is not embedded (Annex D.2); a predefined encoding
    /// named on a non-embedded Symbol or ZapfDingbats font is ignored, as pdf.js does. For a Type 3 font the encoding is entirely
    /// defined by the <c>Encoding</c> entry (§9.6.5.3).
    /// </remarks>
    public string GetGlyphName(byte code) => Metrics.Names[code];

    /// <summary>Gets the width of a character code's glyph, in glyph space units: thousandths of text space, except for Type 3 fonts.</summary>
    /// <param name="code">The character code.</param>
    /// <returns>The width.</returns>
    /// <remarks>
    /// ISO 32000-2 §9.6.2.1, Table 109 (<c>Widths</c>), §9.8.1 (<c>MissingWidth</c>) and §9.2.4. A code from <c>FirstChar</c> to
    /// <c>LastChar</c> has its <c>Widths</c> element; other codes have the descriptor's <c>MissingWidth</c>. A non-embedded Standard
    /// 14 font without <c>Widths</c> takes the width of the code's glyph from its metrics; <c>.notdef</c> is 250 (600 in Courier),
    /// as in Acrobat. A Type 3 font's widths are in its glyph space, which its <c>FontMatrix</c> maps to text space.
    /// </remarks>
    public double GetWidth(byte code) => Metrics.Widths[code];

    /// <summary>Gets the names and widths of all 256 codes, rebuilt when an object they come from has changed.</summary>
    internal SimpleFontMetrics Metrics
    {
        get
        {
            SimpleFontMetrics? metrics = _metrics;
            if (metrics is null || !metrics.IsCurrent)
            {
                metrics = SimpleFontMetrics.Build(this);
                _metrics = metrics;
            }

            return metrics;
        }
    }

    /// <summary>Gets a value indicating whether the font can take Standard 14 metrics when not embedded: Type 1 and TrueType fonts.</summary>
    internal virtual bool CanUseStandard14 => true;

    /// <inheritdoc/>
    private protected override PdfFontDescriptor? SynthesizedDescriptor => Metrics.SynthesizedDescriptor;

    /// <summary>
    /// Gets the built-in encoding of the embedded font program as glyph names per code, or <see langword="null"/> when the program
    /// is not read (the font program parsers supply it). Without it, an embedded font's default base encoding is StandardEncoding.
    /// </summary>
    internal virtual string?[]? GetProgramEncoding() => null;
}
