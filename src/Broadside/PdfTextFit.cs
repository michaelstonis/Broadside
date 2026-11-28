namespace Broadside;

/// <summary>Where the glyphs after a replaced text go when the replacement is wider or narrower than the text it replaces.</summary>
/// <remarks>ISO 32000-2 §9.4.3 (Table 107, <c>TJ</c>), §9.3.4 (<c>Tz</c>) and §9.4.4.</remarks>
public enum PdfTextFit
{
    /// <summary>The glyphs after the replacement keep their positions; a longer replacement may overlap them, a shorter one leaves a gap.</summary>
    KeepPosition,

    /// <summary>
    /// A replacement wider than the text it replaces is squeezed with horizontal scaling (<c>Tz</c>), down to 50 % of its width, to
    /// fit; past that it overlaps. The glyphs after it keep their positions.
    /// </summary>
    Compress,

    /// <summary>
    /// The glyphs after the replacement on the same line, in the same text object, move by the difference in width, so the line closes
    /// up or opens out; a vertical line moves along y.
    /// </summary>
    ShiftLine,
}
