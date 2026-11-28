namespace Broadside.Text;

/// <summary>A line of words an OCR engine recognized, in logical (reading) order.</summary>
/// <param name="Words">The words, in logical order: for a right-to-left line the first word is the rightmost.</param>
/// <param name="BaselineAngle">The line's rotation in degrees, counterclockwise as the image is seen (90 for text that runs upward); 0 by default.</param>
/// <param name="Language">The line's language as a BCP 47 tag (<c>en</c>, <c>de-CH</c>), or <see langword="null"/> when unknown.</param>
/// <remarks>
/// ISO 32000-2 §9.4.2 (text positioning) and §14.9.2 (natural language). Each line becomes one text object; with
/// <see cref="TextLayerOptions.Tag"/> it also becomes one <c>P</c> structure element carrying <paramref name="Language"/>.
/// </remarks>
public sealed record OcrLine(IReadOnlyList<OcrWord> Words, double BaselineAngle = 0, string? Language = null);
