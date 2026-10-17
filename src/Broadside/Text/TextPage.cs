namespace Broadside.Text;

/// <summary>
/// The text extracted from a page: its glyphs in reading order, each with its position, and the Unicode text they stand for as one
/// string. Immutable; safe to share between threads.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4 (text objects), §9.10 (extraction of text content), §14.8.2.5 (page content order and logical content order)
/// and §14.9 (replacement text). The glyphs are in logical content order when the page is tagged and was read in
/// <see cref="TextReadingOrder.Structure"/> order, else in the order the content shows them (<see cref="TextExtractionOptions.ReadingOrder"/>).
/// <see cref="Text"/> is the glyphs' texts concatenated in that order, with nothing added between them. <see cref="Words"/> and
/// <see cref="Lines"/> group the glyphs (§14.8.2.6.2), and <see cref="PlainText"/> joins the words with spaces and the lines with line
/// feeds, in the same order; they are assembled on first use. Reading order across columns of untagged pages is built on top of them.
/// </remarks>
public sealed class TextPage
{
    private readonly TextGlyph[] _glyphs;
    private Assembled? _assembled;

    internal TextPage(TextGlyph[] glyphs, string text, int[]? contentOrders = null)
    {
        _glyphs = glyphs;
        Text = text;
        ContentOrders = contentOrders;
    }

    /// <summary>Gets the glyphs, in reading order: logical content order for a tagged page read in structure order, else content order.</summary>
    /// <remarks>ISO 32000-2 §9.4.3 and §14.8.2.5.1.</remarks>
    public IReadOnlyList<TextGlyph> Glyphs => _glyphs;

    /// <summary>The glyphs as a span, for assembling layout over a slice of them (issue #186).</summary>
    internal ReadOnlySpan<TextGlyph> GlyphSpan => _glyphs;

    /// <summary>
    /// For a page read in structure order, each glyph's content item position in logical content order (ascending; -1 for an
    /// artifact outside the tree), so content that shows no glyph can be placed among the glyphs; <see langword="null"/> otherwise.
    /// </summary>
    internal int[]? ContentOrders { get; }

    /// <summary>Gets the text of every glyph, in the order of <see cref="Glyphs"/>; <see cref="TextGlyph.TextStart"/> and <see cref="TextGlyph.TextLength"/> index it.</summary>
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
    /// Gets the page's plain text in the order of <see cref="Glyphs"/>: the words of each line separated by one space, the lines by one line feed
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

        // Issue #284: the thread's layout is reused, so only the words, lines and plain text kept here are allocated.
        TextLayout layout = TextLayout.Rent();
        try
        {
            layout.Build(_glyphs, Text);
            assembled = new Assembled(layout.Words.ToArray(), layout.Lines.ToArray(), new string(layout.PlainText));
        }
        finally
        {
            TextLayout.Return(layout);
        }

        // Immutable once built: a thread losing the race returns the first one published, an equal copy.
        return Interlocked.CompareExchange(ref _assembled, assembled, null) ?? assembled;
    }

    private sealed record Assembled(TextWord[] Words, TextLine[] Lines, string PlainText);
}
