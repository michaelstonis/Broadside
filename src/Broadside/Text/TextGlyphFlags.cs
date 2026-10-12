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

    /// <summary>The glyph is inside a marked-content sequence tagged <c>Artifact</c>: pagination, layout or background content, not the document's real content.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.</remarks>
    Artifact = 1 << 2,

    /// <summary>
    /// The glyph is inside a marked-content sequence whose property list has <c>ActualText</c>: the sequence's first glyph carries the
    /// replacement text and the others carry none, while every glyph keeps its own position.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.4.</remarks>
    ActualText = 1 << 3,
}
