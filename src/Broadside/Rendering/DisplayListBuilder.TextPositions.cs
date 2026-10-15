using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Text positions for hit-testing: every character code shown on the page (or in a form or annotation appearance it paints), in
/// content order, with its box in page space and its Unicode text, whatever font it is in and whether or not it paints.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private DisplayTextGlyph[] _textGlyphs = new DisplayTextGlyph[64];
    private int _textGlyphCount;
    private char[] _text = new char[256];
    private int _textLength;

    // The extent across the baseline of each font met in this build: descent and ascent, in text space units.
    private readonly Dictionary<PdfFont, (double Descent, double Ascent)> _fontExtents = new(ReferenceEqualityComparer.Instance);

    /// <summary>Records where a shown character code lies and the text it stands for.</summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §9.4.4: the box runs from the glyph's origin to its horizontal displacement w0 (§9.2.4) along the baseline and from
    /// the font's descent to its ascent across it (§9.8.1, Table 120), mapped by the text rendering matrix and the CTM into page space
    /// (the default user space of the list, §8.3.2.3): the box <see cref="Text.TextGlyph.Quad"/> gives. The text is the code's
    /// Unicode mapping (§9.10.2). Every rendering mode is recorded, invisible text (mode 3) included, as are Type 3 glyphs (§9.6.4),
    /// which the list does not paint yet.
    /// </para>
    /// <para>
    /// Only text shown by page content, forms, groups and annotation appearances is recorded: text inside a Type 3 glyph's
    /// description, a tiling pattern's cell or a soft mask is part of a paint, not text on the page. Hidden optional content (§8.11)
    /// never reaches the builder.
    /// </para>
    /// </remarks>
    private void RecordText(in GlyphEvent glyph, ContentContext context)
    {
        if (glyph.Font is not { } font
            || glyph.IsHidden
            || context.RunKind is not (ContentRunKind.Page or ContentRunKind.Form or ContentRunKind.Group or ContentRunKind.Appearance))
        {
            return;
        }

        if (_text.Length - _textLength < FontUnicode.MaxLength)
        {
            Array.Resize(ref _text, Math.Max(_text.Length * 2, _textLength + FontUnicode.MaxLength));
        }

        int written = glyph.MapUnicode(_text.AsSpan(_textLength), out _);
        if (!_fontExtents.TryGetValue(font, out (double Descent, double Ascent) extent))
        {
            extent = Text.TextExtractor.ReadExtent(font);
            _fontExtents[font] = extent;
        }

        Quad quad = Quad.FromRectangle(glyph.TextMatrix * glyph.Ctm, 0, extent.Descent, glyph.HorizontalDisplacement, extent.Ascent);
        Append(
            ref _textGlyphs,
            ref _textGlyphCount,
            new DisplayTextGlyph(quad, _textLength, written, glyph.CharacterCode, glyph.CodeLength, glyph.RenderingMode, glyph.IsVertical));
        _textLength += written;
    }

    /// <summary>Forgets the recorded text, keeping the tables' capacity and no reference into a document.</summary>
    private void ResetTextPositions()
    {
        _textGlyphCount = 0;
        _textLength = 0;
        _fontExtents.Clear();
    }
}
