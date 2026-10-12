namespace Broadside.Text;

/// <summary>
/// The text extracted from a page: its glyphs in the order the content shows them, each with its position, and the Unicode text
/// they stand for as one string. Immutable; safe to share between threads.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4 (text objects), §9.10 (extraction of text content) and §14.9.4 (replacement text). <see cref="Text"/> is the
/// glyphs' texts concatenated in content order, with nothing added between them. <see cref="Words"/> and <see cref="Lines"/> group
/// the glyphs (§14.8.2.6.2), and <see cref="PlainText"/> joins the words with spaces and the lines with line feeds, in content order;
/// they are assembled on first use. Reading order across lines and columns is built on top of them.
/// </remarks>
public sealed class TextPage
{
    private readonly TextGlyph[] _glyphs;
    private Assembled? _assembled;

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

    /// <summary>Gets the words, in the order of their glyphs.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2 and §14.9.4; <see cref="TextWord"/> gives the rules.</remarks>
    public IReadOnlyList<TextWord> Words => Assemble().Words;

    /// <summary>Gets the lines, in the order of their glyphs.</summary>
    /// <remarks>ISO 32000-2 §9.4.4 and §9.7.4.3; <see cref="TextLine"/> gives the rules.</remarks>
    public IReadOnlyList<TextLine> Lines => Assemble().Lines;

    /// <summary>
    /// Gets the page's plain text in content order: the words of each line separated by one space, the lines by one line feed
    /// (U+000A), with no line feed after the last. White space shown by the content becomes those separators.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2.</remarks>
    public string PlainText => Assemble().PlainText;

    /// <summary>Gets the text of one word: the texts of its glyphs.</summary>
    /// <param name="word">A word of this page.</param>
    /// <returns>Its UTF-16 units in <see cref="Text"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The word's range is not within <see cref="Text"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2.</remarks>
    public ReadOnlySpan<char> GetWordText(in TextWord word) => Text.AsSpan(word.TextStart, word.TextLength);

    /// <summary>Gets the text of one line: its words separated by single spaces.</summary>
    /// <param name="line">A line of this page.</param>
    /// <returns>Its UTF-16 units in <see cref="PlainText"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The line's range is not within <see cref="PlainText"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public ReadOnlySpan<char> GetLineText(in TextLine line) => PlainText.AsSpan(line.PlainTextStart, line.PlainTextLength);

    /// <summary>Returns <see cref="Text"/>.</summary>
    /// <returns>The page's text.</returns>
    public override string ToString() => Text;

    private Assembled Assemble()
    {
        if (Volatile.Read(ref _assembled) is { } assembled)
        {
            return assembled;
        }

        var layout = new TextLayout();
        layout.Build(_glyphs, Text);
        assembled = new Assembled(layout.Words.ToArray(), layout.Lines.ToArray(), new string(layout.PlainText));

        // Immutable once built: a thread losing the race returns the first one published, an equal copy.
        return Interlocked.CompareExchange(ref _assembled, assembled, null) ?? assembled;
    }

    private sealed record Assembled(TextWord[] Words, TextLine[] Lines, string PlainText);
}
