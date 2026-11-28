namespace Broadside.Diagnostics;

/// <summary>The codes of the diagnostics replacing or deleting text in existing page content records.</summary>
/// <remarks>
/// ISO 32000-2 §9.4.3, §9.9.2, §9.10.3 and §14.9.4. Each code is reported under <see cref="DiagnosticArea.TextEdit"/>; see
/// <see cref="DiagnosticCatalog"/> for every area. They are recorded on the document (<see cref="PdfDocument.Diagnostics"/>) and in
/// <see cref="PdfTextEditResult.Diagnostics"/>. In strict mode the first one throws, and the edit is undone before the exception
/// leaves.
/// </remarks>
public static class TextEditDiagnostics
{
    /// <summary>Gets the <c>TextEditFallbackFont</c> code: characters of a replacement the original font cannot show are shown in a fallback font.</summary>
    /// <remarks>ISO 32000-2 §9.9.2</remarks>
    public static DiagnosticCode TextEditFallbackFont { get; } = new(nameof(TextEditFallbackFont), DiagnosticArea.TextEdit, DiagnosticSeverity.Warning, "ISO 32000-2 §9.9.2",
        "Characters of a replacement have no usable glyph in the font the replaced text is shown in (a subset font usually lacks them). They are shown in a fallback font, switched to inside the same text object.");

    /// <summary>Gets the <c>TextEditReplacementTextRemoved</c> code: an <c>ActualText</c>, <c>Alt</c> or <c>E</c> entry that did not hold the replaced text was removed.</summary>
    /// <remarks>ISO 32000-2 §14.9.4</remarks>
    public static DiagnosticCode TextEditReplacementTextRemoved { get; } = new(nameof(TextEditReplacementTextRemoved), DiagnosticArea.TextEdit, DiagnosticSeverity.Warning, "ISO 32000-2 §14.9.4",
        "Replacement text (ActualText, Alt or E) of content whose text was replaced does not hold the old text, so it cannot be updated. The entry is removed, so it no longer contradicts the content.");

    /// <summary>Gets the <c>TextEditToUnicodeExtended</c> code: a font's ToUnicode CMap gained entries, or was created, so the new codes read back as the new text.</summary>
    /// <remarks>ISO 32000-2 §9.10.3</remarks>
    public static DiagnosticCode TextEditToUnicodeExtended { get; } = new(nameof(TextEditToUnicodeExtended), DiagnosticArea.TextEdit, DiagnosticSeverity.Warning, "ISO 32000-2 §9.10.3",
        "A replacement uses codes of a font that its ToUnicode CMap does not map. The CMap is extended (or created) for them; other pages that use the font gain the entries, which change nothing they show.");

    /// <summary>Gets the <c>TextEditMatchNotLocated</c> code: the glyphs of a match could not all be found again in the content, so the match was left as it was.</summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode TextEditMatchNotLocated { get; } = new(nameof(TextEditMatchNotLocated), DiagnosticArea.TextEdit, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3",
        "The glyphs of a match could not all be found again in the page's content (the match came from other text, or the content changed since). The match is left as it was.");

    /// <summary>Gets the <c>TextEditNotEncodable</c> code: a replacement has a character no font tried can show, so the match was left as it was.</summary>
    /// <remarks>ISO 32000-2 §9.4.3</remarks>
    public static DiagnosticCode TextEditNotEncodable { get; } = new(nameof(TextEditNotEncodable), DiagnosticArea.TextEdit, DiagnosticSeverity.Warning, "ISO 32000-2 §9.4.3",
        "A replacement has a character that neither the original font nor any fallback font can show, or it would be shown in vertical writing through a fallback font. The match is left as it was.");

    /// <summary>Gets every code of this area, in declaration order.</summary>
    internal static DiagnosticCode[] Codes { get; } =
    [
        TextEditFallbackFont,
        TextEditReplacementTextRemoved,
        TextEditToUnicodeExtended,
        TextEditMatchNotLocated,
        TextEditNotEncodable,
    ];
}
