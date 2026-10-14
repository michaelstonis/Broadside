namespace Broadside.Text;

/// <summary>What sets an extracted glyph apart from ordinary visible page text.</summary>
/// <remarks>ISO 32000-2 §9.3.6, §8.11.3.1, §14.8.2.2 and §14.9.4.</remarks>
[Flags]
public enum TextGlyphFlags
{
    /// <summary>Visible page text.</summary>
    None = 0,

    /// <summary>
    /// The glyph paints nothing: text rendering mode 3 (neither fill nor stroke) or 7 (clip only). Such text is often an OCR layer
    /// laid over a scanned image, so it is extracted.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.3.6, Table 104.</remarks>
    Invisible = 1 << 0,

    /// <summary>
    /// Optional content hides the glyph. Hidden glyphs are extracted only when
    /// <see cref="TextExtractionOptions.IncludeHiddenContent"/> asks for them.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.3.1.</remarks>
    Hidden = 1 << 1,

    /// <summary>
    /// The glyph is an artifact: pagination, layout or background content, not the document's real content. It is inside a
    /// marked-content sequence tagged <c>Artifact</c>, or, on a tagged page read in structure order, under an <c>Artifact</c>
    /// structure element or not in the structure tree. Artifacts are extracted only when
    /// <see cref="TextExtractionOptions.IncludeArtifacts"/> asks for them.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    Artifact = 1 << 2,

    /// <summary>
    /// The glyph is inside a marked-content sequence, or under a structure element, whose <c>ActualText</c> replaces its text: the
    /// first glyph of the sequence or element carries the replacement text and the others carry none, while every glyph keeps its
    /// own position. For an element whose content spans pages, that first glyph is its first in logical order, wherever its
    /// content starts; on its other pages every glyph of the element carries none.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.4.</remarks>
    ActualText = 1 << 3,

    /// <summary>
    /// The glyph is under a structure element whose alternate description (<c>Alt</c>) replaces its text, as a whole word or phrase
    /// (<see cref="TextExtractionOptions.ApplyAlternateDescriptions"/>): the element's first glyph carries the description and the
    /// others carry none, on later pages too when its content spans pages.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.3.</remarks>
    AlternateDescription = 1 << 4,

    /// <summary>
    /// The glyph is under a structure element whose expansion (<c>E</c>) replaces its text, as a whole word
    /// (<see cref="TextExtractionOptions.ApplyExpansions"/>): the element's first glyph carries the expansion and the others carry none,
    /// on later pages too when its content spans pages.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.5.</remarks>
    Expansion = 1 << 5,
}
