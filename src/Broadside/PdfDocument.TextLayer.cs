namespace Broadside;

/// <content>OCR text layers (issue #649): the document's one glyphless font.</content>
public sealed partial class PdfDocument
{
    /// <summary>Gets or sets the glyphless font OCR text layers show their text in, added on first use.</summary>
    internal Text.GlyphlessFont? GlyphlessFont { get; set; }
}
