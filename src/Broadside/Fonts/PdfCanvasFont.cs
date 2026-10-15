using System.Globalization;
using System.Text;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Fonts;

/// <summary>
/// A font of a document that a <see cref="PdfCanvas"/> shows text with: how text encodes in it, how wide it is, and its vertical
/// metrics. Get one with <see cref="PdfDocument.GetStandard14Font"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.2.4 (glyph positioning and metrics: widths in thousandths of text space), §9.3 (text state) and §9.4.3 (text
/// space displacement). A canvas font owns one font dictionary of its document (<see cref="Font"/> is the read view over it); the
/// canvas names it in the <c>Font</c> resources and writes each character of shown text as the font's character code.
/// </para>
/// <para>
/// Metrics are in glyph space units, thousandths of text space: multiply by the font size and divide by 1000 for text space units.
/// <see cref="MeasureWidth"/> gives the advance of text without character spacing, word spacing or horizontal scaling, which the
/// canvas's text state adds when the text is shown (§9.4.4: <c>tx = ((w0 − Tj/1000) × Tfs + Tc + Tw) × Th</c>).
/// </para>
/// </remarks>
public abstract class PdfCanvasFont
{
    private protected PdfCanvasFont(PdfDocument document, CosReference reference)
    {
        Document = document;
        Reference = reference;
    }

    /// <summary>Gets the document-model view of the font dictionary the canvas names (§9.5).</summary>
    /// <exception cref="InvalidOperationException">The font dictionary was removed from the document.</exception>
    public PdfFont Font => Document.GetFont(Reference) ?? throw new InvalidOperationException("The font dictionary was removed from the document.");

    /// <summary>Gets the font's PostScript name, the <c>BaseFont</c> it is written with (without a subset tag).</summary>
    public abstract string Name { get; }

    /// <summary>Gets the maximum height above the baseline of the font's accented capitals, in glyph space units (§9.8.1, <c>Ascent</c>).</summary>
    public abstract double Ascent { get; }

    /// <summary>Gets the maximum depth below the baseline of the font's descenders, a negative number in glyph space units (§9.8.1, <c>Descent</c>).</summary>
    public abstract double Descent { get; }

    /// <summary>Gets the top of flat capital letters above the baseline, in glyph space units (§9.8.1, <c>CapHeight</c>).</summary>
    public abstract double CapHeight { get; }

    /// <summary>Gets the top of flat lowercase letters above the baseline, in glyph space units, or 0 when unknown (§9.8.1, <c>XHeight</c>).</summary>
    public abstract double XHeight { get; }

    /// <summary>Gets the smallest rectangle enclosing every glyph of the font drawn at the origin, in glyph space units (§9.8.1, <c>FontBBox</c>).</summary>
    public abstract PdfRectangle FontBBox { get; }

    /// <summary>
    /// Gets a value indicating whether word spacing (<see cref="PdfCanvas.SetWordSpacing"/>) widens this font's spaces: true for
    /// one-byte encodings, where the space character is code 32 (§9.3.3).
    /// </summary>
    public bool AppliesWordSpacing => CodeLength == 1;

    /// <summary>Gets the document the font belongs to.</summary>
    internal PdfDocument Document { get; }

    /// <summary>Gets the indirect reference of the font dictionary.</summary>
    internal CosReference Reference { get; }

    /// <summary>Gets the number of bytes of each character code: 1 for a simple font, 2 for an <c>Identity-H</c> composite font.</summary>
    internal abstract int CodeLength { get; }

    /// <summary>Gets a value indicating whether every character of <paramref name="text"/> has a character code and a glyph in this font.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="false"/> when <see cref="PdfCanvas.ShowText(ReadOnlySpan{char}, bool)"/> would refuse it.</returns>
    public bool CanEncode(ReadOnlySpan<char> text) => FindUnencodable(text) < 0;

    /// <summary>Measures the advance of <paramref name="text"/> shown at <paramref name="fontSize"/>: the sum of its glyph widths, and of the font's kerning between neighbouring glyphs when <paramref name="kerning"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="fontSize">The font size (<c>Tf</c>'s size operand).</param>
    /// <param name="kerning">Whether to add the font's kerning, as <see cref="PdfCanvas.ShowText(ReadOnlySpan{char}, bool)"/> with kerning shows it.</param>
    /// <returns>The advance in unscaled text space units, without character spacing, word spacing or horizontal scaling.</returns>
    /// <exception cref="ArgumentException">A character of <paramref name="text"/> has no code in this font (the message names it).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not finite.</exception>
    /// <remarks>ISO 32000-2 §9.2.4 and §9.4.4 (with <c>Tc</c> = <c>Tw</c> = 0 and <c>Th</c> = 1); the kerning is the font's own (for a Standard 14 font, the AFM <c>KPX</c> pairs).</remarks>
    public double MeasureWidth(ReadOnlySpan<char> text, double fontSize, bool kerning = false)
    {
        if (!double.IsFinite(fontSize))
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), fontSize, "The font size shall be a finite number.");
        }

        double units = 0;
        CanvasGlyph previous = default;
        bool first = true;
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            CanvasGlyph glyph = GlyphOrThrow(rune, index, nameof(text));
            if (kerning && !first)
            {
                units += GetKerning(previous, glyph);
            }

            units += glyph.Width;
            previous = glyph;
            first = false;
            index += rune.Utf16SequenceLength;
        }

        return units * fontSize / 1000;
    }

    /// <summary>Finds the glyph that shows a Unicode scalar value: its character code and width.</summary>
    /// <param name="scalar">The Unicode scalar value.</param>
    /// <param name="glyph">The glyph.</param>
    /// <returns><see langword="false"/> when the font has no code for it.</returns>
    internal abstract bool TryGetGlyph(int scalar, out CanvasGlyph glyph);

    /// <summary>The kerning between two glyphs, in glyph space units (negative brings them closer); 0 by default.</summary>
    internal virtual int GetKerning(in CanvasGlyph left, in CanvasGlyph right) => 0;

    /// <summary>Called for every glyph a canvas writes, after the whole string encoded; an embedding font records the glyphs its subset needs.</summary>
    internal virtual void OnShown(in CanvasGlyph glyph)
    {
    }

    /// <summary>The message of the exception for a character the font cannot show, naming it and the alternative.</summary>
    internal abstract string DescribeUnencodable(Rune character, int index);

    /// <summary>The glyph of <paramref name="rune"/>, or an <see cref="ArgumentException"/> naming it.</summary>
    internal CanvasGlyph GlyphOrThrow(Rune rune, int index, string parameterName) =>
        TryGetGlyph(rune.Value, out CanvasGlyph glyph) ? glyph : throw new ArgumentException(DescribeUnencodable(rune, index), parameterName);

    /// <summary>The UTF-16 index of the first character the font cannot show, or -1.</summary>
    internal int FindUnencodable(ReadOnlySpan<char> text)
    {
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!TryGetGlyph(rune.Value, out _))
            {
                return index;
            }

            index += rune.Utf16SequenceLength;
        }

        return -1;
    }

    /// <summary>A character as a message shows it: <c>'Ж' (U+0416)</c>, or only the code point for a control or invisible character.</summary>
    private protected static string Describe(Rune character)
    {
        string code = string.Create(CultureInfo.InvariantCulture, $"U+{character.Value:X4}");
        UnicodeCategory category = Rune.GetUnicodeCategory(character);
        bool printable = category is not (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.SpaceSeparator
            or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
        return printable ? $"'{character}' ({code})" : code;
    }
}

/// <summary>One glyph of a canvas font: the character code that shows it, its width, and a font-specific key for kerning lookups.</summary>
/// <param name="Code">The character code, written in <see cref="PdfCanvasFont.CodeLength"/> bytes, high byte first.</param>
/// <param name="Width">The glyph's horizontal displacement in glyph space units (§9.2.4).</param>
/// <param name="Key">What the font identifies the glyph by (a Standard 14 font: its glyph name's index; an embedded font: its glyph id).</param>
internal readonly record struct CanvasGlyph(uint Code, double Width, int Key);
