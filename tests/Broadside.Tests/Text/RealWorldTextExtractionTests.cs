using Broadside.Graphics;
using Broadside.Security;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Every page of the fetched real-world corpora extracts its text leniently (ISO 32000-2 §9.4.4, §9.10, §14.9.4): no exception, the
/// glyphs' text ranges follow each other to the end of the page's text, and every origin, advance and quad corner is finite. Prints
/// how many glyphs each corpus has and how many are invisible, artifacts or replaced by <c>ActualText</c>. Words and lines are
/// assembled on every page and checked for consistency with the glyphs and the plain text (§14.8.2.6.2). Skips when the corpora
/// are not fetched.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldTextExtractionTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusIds => new(Document.RealWorldCorpusTests.CorpusIds);

    [Theory]
    [MemberData(nameof(CorpusIds))]
    public void Every_page_of_a_real_world_corpus_extracts_consistent_positioned_text(string corpusId)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpusId);
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus '{corpusId}' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var extractor = new TextExtractor();
        long glyphs = 0;
        long words = 0;
        long lines = 0;
        long[] flagged = new long[4];
        string? failure = null;
        foreach (string file in files)
        {
            if (Document.RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            using PdfDocument? document = OpenOrNull(file);
            if (document is null)
            {
                continue;
            }

            for (int index = 0; index < document.Pages.Count; index++)
            {
                extractor.Clear();
                document.Pages[index].ProcessContent(extractor);
                glyphs += extractor.Glyphs.Length;
                words += extractor.Words.Length;
                lines += extractor.Lines.Length;
                failure ??= Check(extractor, flagged) is { } problem ? $"{file} page {index + 1}: {problem}" : null;
            }
        }

        Assert.Null(failure);
        output.WriteLine($"{corpusId}: {glyphs} glyphs, {words} words, {lines} lines; invisible {flagged[0]}, hidden {flagged[1]}, artifact {flagged[2]}, ActualText {flagged[3]}.");
    }

    private static string? Check(TextExtractor extractor, long[] flagged)
    {
        int expectedStart = 0;
        foreach (TextGlyph glyph in extractor.Glyphs)
        {
            for (int bit = 0; bit < flagged.Length; bit++)
            {
                if (((int)glyph.Flags & (1 << bit)) != 0)
                {
                    flagged[bit]++;
                }
            }

            if (glyph.TextStart != expectedStart || glyph.TextLength < 0)
            {
                return $"text range {glyph.TextStart}+{glyph.TextLength}, expected to start at {expectedStart}";
            }

            expectedStart += glyph.TextLength;
            Quad quad = glyph.Quad;
            if (!Finite(glyph.Origin) || !Finite(glyph.Advance) || !Finite(quad.BottomLeft) || !Finite(quad.BottomRight) || !Finite(quad.TopRight) || !Finite(quad.TopLeft))
            {
                return $"geometry not finite: origin {glyph.Origin}, advance {glyph.Advance}, quad {quad}";
            }
        }

        return expectedStart != extractor.Text.Length
            ? $"glyph text ends at {expectedStart}, the text at {extractor.Text.Length}"
            : CheckLayout(extractor);
    }

    /// <summary>
    /// Words follow each other through the glyphs and each has text; lines cover their words in order; the plain text is the lines'
    /// words joined by spaces and line feeds (§14.8.2.6.2).
    /// </summary>
    private static string? CheckLayout(TextExtractor extractor)
    {
        ReadOnlySpan<TextWord> words = extractor.Words;
        ReadOnlySpan<TextLine> lines = extractor.Lines;
        ReadOnlySpan<char> plain = extractor.PlainText;
        int nextGlyph = 0;
        foreach (TextWord word in words)
        {
            if (word.GlyphStart < nextGlyph || word.GlyphCount < 1 || word.TextLength < 1 || !Finite(word.Quad.BottomLeft) || !Finite(word.Quad.TopRight))
            {
                return $"word at glyph {word.GlyphStart}+{word.GlyphCount}, text {word.TextStart}+{word.TextLength}, after glyph {nextGlyph}";
            }

            nextGlyph = word.GlyphStart + word.GlyphCount;
        }

        int nextWord = 0;
        int plainStart = 0;
        foreach (TextLine line in lines)
        {
            if (line.WordStart != nextWord || line.WordCount < 1 || line.PlainTextStart != plainStart)
            {
                return $"line of words {line.WordStart}+{line.WordCount} at plain text {line.PlainTextStart}, expected word {nextWord} at {plainStart}";
            }

            var joined = new System.Text.StringBuilder();
            for (int index = line.WordStart; index < line.WordStart + line.WordCount; index++)
            {
                joined.Append(index == line.WordStart ? string.Empty : " ").Append(extractor.GetWordText(words[index]));
            }

            if (!extractor.GetLineText(line).SequenceEqual(joined.ToString()))
            {
                return $"line text '{extractor.GetLineText(line)}' is not its words '{joined}'";
            }

            nextWord += line.WordCount;
            plainStart += line.PlainTextLength + 1;
        }

        return nextWord != words.Length || Math.Max(0, plainStart - 1) != plain.Length
            ? $"lines cover {nextWord} of {words.Length} words and end at {plainStart - 1} of {plain.Length}"
            : null;
    }

    private static bool Finite(PathPoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static PdfDocument? OpenOrNull(string file)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)));
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
