namespace Broadside.Fonts;

/// <summary>One of the fourteen Standard 14 fonts, whose metrics every PDF processor provides.</summary>
/// <remarks>
/// ISO 32000-2 §9.6.2.2. The metrics are those of the Adobe Core 14 AFM files (Adobe Technical Note #5004), which ship in the core
/// package; the glyphs ship separately (ADR 0007).
/// </remarks>
public enum Standard14Font
{
    /// <summary>Courier.</summary>
    Courier = 0,

    /// <summary>Courier-Bold.</summary>
    CourierBold = 1,

    /// <summary>Courier-Oblique.</summary>
    CourierOblique = 2,

    /// <summary>Courier-BoldOblique.</summary>
    CourierBoldOblique = 3,

    /// <summary>Helvetica.</summary>
    Helvetica = 4,

    /// <summary>Helvetica-Bold.</summary>
    HelveticaBold = 5,

    /// <summary>Helvetica-Oblique.</summary>
    HelveticaOblique = 6,

    /// <summary>Helvetica-BoldOblique.</summary>
    HelveticaBoldOblique = 7,

    /// <summary>Times-Roman.</summary>
    TimesRoman = 8,

    /// <summary>Times-Bold.</summary>
    TimesBold = 9,

    /// <summary>Times-Italic.</summary>
    TimesItalic = 10,

    /// <summary>Times-BoldItalic.</summary>
    TimesBoldItalic = 11,

    /// <summary>Symbol, a symbolic font with its own built-in encoding (Annex D.5).</summary>
    Symbol = 12,

    /// <summary>ZapfDingbats, a symbolic font with its own built-in encoding (Annex D.6).</summary>
    ZapfDingbats = 13,
}
