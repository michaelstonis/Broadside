namespace Broadside.Text;

/// <summary>
/// The text extracted from a page: its glyphs in the order the content shows them, each with its position, and the Unicode text
/// they stand for as one string. Immutable; safe to share between threads.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4 (text objects), §9.10 (extraction of text content) and §14.9.4 (replacement text). <see cref="Text"/> is the
/// glyphs' texts concatenated in content order, with nothing added between them: words, lines and reading order are built on top
/// of the glyphs.
/// </remarks>
public sealed class TextPage
{
    private readonly TextGlyph[] _glyphs;

    internal TextPage(TextGlyph[] glyphs, string text)
    {
        _glyphs = glyphs;
        Text = text;
    }

    /// <summary>Gets the glyphs, in content order.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public IReadOnlyList<TextGlyph> Glyphs => _glyphs;

    /// <summary>Gets the text of every glyph, in content order; <see cref="TextGlyph.TextStart"/> and <see cref="TextGlyph.TextLength"/> index it.</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public string Text { get; }

    /// <summary>Gets the text of one glyph.</summary>
    /// <param name="glyph">A glyph of this page.</param>
    /// <returns>Its UTF-16 units in <see cref="Text"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The glyph's range is not within <see cref="Text"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10.2 and §14.9.4.</remarks>
    public ReadOnlySpan<char> GetGlyphText(in TextGlyph glyph) => Text.AsSpan(glyph.TextStart, glyph.TextLength);

    /// <summary>Returns <see cref="Text"/>.</summary>
    /// <returns>The page's text.</returns>
    public override string ToString() => Text;
}
