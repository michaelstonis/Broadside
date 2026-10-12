using Broadside.Graphics;
using Broadside.Security;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Every page of the fetched real-world corpora extracts its text leniently (ISO 32000-2 §9.4.4, §9.10, §14.9.4): no exception, the
/// glyphs' text ranges follow each other to the end of the page's text, and every origin, advance and quad corner is finite. Prints
/// how many glyphs each corpus has and how many are invisible, artifacts or replaced by <c>ActualText</c>. Skips when the corpora
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
                failure ??= Check(extractor, flagged) is { } problem ? $"{file} page {index + 1}: {problem}" : null;
            }
        }

        Assert.Null(failure);
        output.WriteLine($"{corpusId}: {glyphs} glyphs; invisible {flagged[0]}, hidden {flagged[1]}, artifact {flagged[2]}, ActualText {flagged[3]}.");
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

        return expectedStart == extractor.Text.Length ? null : $"glyph text ends at {expectedStart}, the text at {extractor.Text.Length}";
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
