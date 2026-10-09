namespace Broadside.Fonts;

/// <summary>The stretch of a font, from narrowest to widest.</summary>
/// <remarks>ISO 32000-2 §9.8.1, Table 120, <c>FontStretch</c> (PDF 1.5).</remarks>
public enum PdfFontStretch
{
    /// <summary><c>UltraCondensed</c>.</summary>
    UltraCondensed = 0,

    /// <summary><c>ExtraCondensed</c>.</summary>
    ExtraCondensed = 1,

    /// <summary><c>Condensed</c>.</summary>
    Condensed = 2,

    /// <summary><c>SemiCondensed</c>.</summary>
    SemiCondensed = 3,

    /// <summary><c>Normal</c>.</summary>
    Normal = 4,

    /// <summary><c>SemiExpanded</c>.</summary>
    SemiExpanded = 5,

    /// <summary><c>Expanded</c>.</summary>
    Expanded = 6,

    /// <summary><c>ExtraExpanded</c>.</summary>
    ExtraExpanded = 7,

    /// <summary><c>UltraExpanded</c>.</summary>
    UltraExpanded = 8,
}
