namespace Broadside.Text;

/// <summary>A word an OCR engine recognized: its text, its box in the source image and how sure the engine is of it.</summary>
/// <param name="Text">The word's characters, in logical order (a right-to-left word is not reversed).</param>
/// <param name="Bounds">
/// The word's box in the units of the source image (pixels), whose origin is the image's top-left corner and whose y axis grows
/// downward: <see cref="PdfRectangle.Left"/> and <see cref="PdfRectangle.Right"/> are the box's left and right edges,
/// <see cref="PdfRectangle.Bottom"/> its top edge (the smaller y) and <see cref="PdfRectangle.Top"/> its bottom edge (the larger y).
/// For rotated text it is the axis-aligned box around the word.
/// </param>
/// <param name="Confidence">How sure the engine is of the word, from 0 to 1; 1 by default.</param>
/// <param name="Angle">The word's own rotation in degrees, counterclockwise as the image is seen, added to its line's <see cref="OcrLine.BaselineAngle"/>; 0 by default.</param>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.3.6 and §14.8.2.4. OCR output is not part of ISO 32000-2; <see cref="PdfPage.AddTextLayer(OcrPage, TextLayerOptions?)"/>
/// writes the word as invisible text (rendering mode 3) over the box. Read words from hOCR with <see cref="OcrPage.FromHocr(System.IO.Stream)"/>, from
/// ALTO with <see cref="OcrPage.FromAlto(System.IO.Stream)"/>, or build them from any engine's word boxes.
/// </para>
/// </remarks>
public sealed record OcrWord(string Text, PdfRectangle Bounds, double Confidence = 1, double Angle = 0);
