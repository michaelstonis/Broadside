namespace Broadside.Fonts;

/// <summary>One glyph of a string shown in a composite font: the code read, the CID it selects, the glyph id and the glyph's metrics.</summary>
/// <param name="Code">The character code and how many bytes of the string it takes.</param>
/// <param name="Cid">
/// The CID the code maps to (§9.7.6.2), which gives the metrics even when its glyph is missing; 0 for an invalid or unmapped code.
/// </param>
/// <param name="GlyphId">
/// The glyph id in the CIDFont's program: the CID's glyph, else the glyph of the code's notdef mapping, else the glyph of CID 0
/// (§9.7.6.3).
/// </param>
/// <param name="Width">The horizontal displacement w0 of the CID, in glyph space units (thousandths of text space; §9.7.4.3).</param>
/// <param name="VerticalMetrics">The vertical displacement and position vector of the CID (§9.7.4.3), used in vertical writing.</param>
/// <remarks>ISO 32000-2 §9.7.6.2, §9.7.6.3 and §9.7.4.3; see <see cref="PdfType0Font.ReadGlyph"/>.</remarks>
public readonly record struct CidGlyph(CharacterCode Code, int Cid, int GlyphId, double Width, CidVerticalMetrics VerticalMetrics)
{
    /// <summary>Gets a value indicating whether word spacing (<c>Tw</c>) applies to the glyph: its code is the single byte 32.</summary>
    /// <remarks>ISO 32000-2 §9.3.3: never for the byte 32 inside a multi-byte code.</remarks>
    public bool AppliesWordSpacing => Code.Length == 1 && Code.Value == 0x20;
}
