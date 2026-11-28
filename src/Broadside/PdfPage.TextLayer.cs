using Broadside.Text;

namespace Broadside;

/// <content>OCR text layers (issue #649): ISO 32000-2 §9.3.6, §9.4.2, §9.7.6, §14.8.2.4 and §14.7.5.</content>
public sealed partial class PdfPage
{
    /// <summary>
    /// Adds an invisible text layer from external OCR output: each word is written as invisible text exactly over its box in the
    /// scanned image, so search, selection and copy work on the page while it renders as before.
    /// </summary>
    /// <param name="ocr">The OCR output for this page, read with <see cref="OcrPage.FromHocr"/> or <see cref="OcrPage.FromAlto"/> or built in code.</param>
    /// <param name="options">The minimum confidence, the area the image covers and tagging; <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ocr"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.3.6 (text rendering mode 3: neither filled nor stroked), §9.3.4 (horizontal scaling), §9.4.2 (text
    /// positioning), §9.7.6 and §9.7.5.2 (a Type 0 font with <c>Identity-H</c>), §9.10.3 (<c>ToUnicode</c>), §14.8.2.4 (invisible
    /// content) and §14.7.5 (structure content). OCR coordinates (top-left origin, image units) map onto
    /// <see cref="TextLayerOptions.Target"/> (default: the crop box) as the page is displayed, so the page's <see cref="Rotation"/> is
    /// honored. Each line is one <c>BT</c> … <c>ET</c> in rendering mode 3; each word starts at its box's baseline (the box's bottom
    /// plus the font's descent, a fifth of the box height), at a font size equal to the box height, stretched with <c>Tz</c> so its
    /// advance equals the box width; a real space follows each word but the last, stretched over the gap to the next word, so
    /// extraction splits the words. A rotated line or word gets a rotated text matrix.
    /// </para>
    /// <para>
    /// The text is shown in a glyphless font written once per document: a minimal embedded TrueType program with one blank glyph,
    /// as a Type 0 font whose codes are UTF-16 code units, which covers every script in a few hundred bytes (PDF 1.2; the
    /// document's version is raised on save). The layer is appended after the page's existing content in its own <c>q</c> …
    /// <c>Q</c>, so what the page shows does not change. Words that are empty or whitespace, have a box of no width or height, or
    /// fall below <see cref="TextLayerOptions.MinConfidence"/> are skipped; a right-to-left line is written in logical order with
    /// each word over its box, which is what extraction expects. With <see cref="TextLayerOptions.Tag"/>, a structure tree is created
    /// when the document has none and each line becomes a <c>P</c> element with its language. A mutation: no read may run
    /// concurrently with it. Replacing an existing text layer is not done: a second call adds a second layer.
    /// </para>
    /// </remarks>
    public void AddTextLayer(OcrPage ocr, TextLayerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        TextLayerWriter.Write(_document, this, ocr, options ?? new TextLayerOptions());
    }
}
