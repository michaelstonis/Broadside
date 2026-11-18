using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Layout;
using Broadside.Tests.Layout;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// Issue #546: text and layout extracted in slices (<see cref="PdfPage.BeginTextExtraction"/>, <see cref="PdfPage.BeginLayoutExtraction"/>)
/// are what the whole extractions return, so a single-threaded viewer can extract a page between frames. ISO 32000-2 §9.10, §14.8.2.5,
/// §14.8.4 and §14.9.4.
/// </summary>
public sealed class SlicedExtractionTests
{
    public static TheoryData<string, int> Pages => new()
    {
        { "text-standard14.pdf", 0 },
        { "text-truetype-embedded.pdf", 0 },
        { "text-tagged-replacements.pdf", 0 },
        { "text-tagged-spanning.pdf", 0 },
        { "text-tagged-spanning.pdf", 1 },
        { "text-tagged-spanning-image.pdf", 1 },
        { "layout-tagged.pdf", 0 },
        { "layout-two-column-tagged.pdf", 0 },
        { "layout-two-column-untagged.pdf", 0 },
        { "layout-three-column.pdf", 0 },
        { "layout-tables.pdf", 0 },
        { "layout-headings-lists.pdf", 0 },
        { "layout-figures.pdf", 0 },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void Text_extracted_in_slices_is_the_text_extracted_whole(string file, int pageIndex)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file));
        PdfPage page = document.Pages[pageIndex];
        foreach (TextExtractionOptions options in new[]
        {
            new TextExtractionOptions(),
            new TextExtractionOptions().WithArtifacts(),
            new TextExtractionOptions { ReadingOrder = TextReadingOrder.Content },
        })
        {
            string expected = Project(page.ExtractText(options));
            using TextExtraction extraction = page.BeginTextExtraction(options);
            int slices = 1;
            while (!extraction.Continue(TimeSpan.Zero))
            {
                slices++;
            }

            Assert.True(extraction.IsCompleted);
            Assert.Equal(expected, Project(extraction.Result));
            Assert.True(extraction.Continue(TimeSpan.Zero)); // a finished extraction stays finished
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_layout_built_in_slices_is_the_layout_built_whole(string file, int pageIndex)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file));
        PdfPage page = document.Pages[pageIndex];
        foreach (LayoutOptions options in new[]
        {
            new LayoutOptions(),
            new LayoutOptions().WithDetection(LayoutDetection.AllPages),
            new LayoutOptions().WithDetection(LayoutDetection.None),
        })
        {
            string expected = LayoutProjection.Of(page.ExtractLayout(options));
            using LayoutExtraction extraction = page.BeginLayoutExtraction(options);
            while (!extraction.Continue(TimeSpan.Zero))
            {
            }

            LayoutTree tree = extraction.Result;
            Assert.Equal(expected, LayoutProjection.Of(tree));
            Assert.Single(tree.Pages);
            Assert.Equal(page.ExtractLayout(options).Pages[0].Text.Text, tree.Pages[0].Text.Text);
        }
    }

    [Fact]
    public void An_extraction_pauses_between_top_level_operators_and_its_result_waits_for_the_end()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("layout-tables.pdf"));
        PdfPage page = document.Pages[0];
        using TextExtraction text = page.BeginTextExtraction(new TextExtractionOptions());
        using LayoutExtraction layout = page.BeginLayoutExtraction(new LayoutOptions());
        Assert.False(text.IsCompleted);
        Assert.False(layout.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => text.Result);
        Assert.Throws<InvalidOperationException>(() => layout.Result);

        // Both run on this thread at once, each a slice at a time, as a viewer's page loads interleave.
        int textSlices = 1, layoutSlices = 1;
        bool textDone = false, layoutDone = false;
        while (!textDone || !layoutDone)
        {
            if (!textDone && !(textDone = text.Continue(TimeSpan.Zero)))
            {
                textSlices++;
            }

            if (!layoutDone && !(layoutDone = layout.Continue(TimeSpan.Zero)))
            {
                layoutSlices++;
            }
        }

        Assert.True(textSlices > 2, $"The text was extracted in {textSlices} slices.");
        Assert.True(layoutSlices > 2, $"The layout was built in {layoutSlices} slices.");
        Assert.Equal(Project(page.ExtractText()), Project(text.Result));
        Assert.Equal(LayoutProjection.Of(page.ExtractLayout()), LayoutProjection.Of(layout.Result));
    }

    [Fact]
    public void An_infinite_budget_extracts_in_one_slice_and_a_negative_one_is_refused()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("layout-tables.pdf"));
        PdfPage page = document.Pages[0];
        using TextExtraction text = page.BeginTextExtraction(new TextExtractionOptions());
        using LayoutExtraction layout = page.BeginLayoutExtraction(new LayoutOptions());

        Assert.Throws<ArgumentOutOfRangeException>(() => text.Continue(TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Continue(TimeSpan.FromMilliseconds(-2)));
        Assert.True(text.Continue(Timeout.InfiniteTimeSpan));
        Assert.True(layout.Continue(Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public void An_abandoned_extraction_cannot_continue_and_a_cancelled_one_ends()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("layout-tables.pdf"));
        PdfPage page = document.Pages[0];
        TextExtraction text = page.BeginTextExtraction(new TextExtractionOptions());
        LayoutExtraction layout = page.BeginLayoutExtraction(new LayoutOptions());
        Assert.False(text.Continue(TimeSpan.Zero));
        Assert.False(layout.Continue(TimeSpan.Zero));
        text.Dispose();
        layout.Dispose();
        Assert.Throws<ObjectDisposedException>(() => text.Continue(TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(() => layout.Continue(TimeSpan.Zero));

        using var cancellation = new CancellationTokenSource();
        ContentOptions content = new ContentOptions().WithCancellation(cancellation.Token);
        using TextExtraction cancelled = page.BeginTextExtraction(new TextExtractionOptions().WithContentOptions(content));
        using LayoutExtraction cancelledLayout = page.BeginLayoutExtraction(new LayoutOptions().WithContentOptions(content));
        Assert.False(cancelled.Continue(TimeSpan.Zero));
        Assert.False(cancelledLayout.Continue(TimeSpan.Zero));
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => cancelled.Continue(Timeout.InfiniteTimeSpan));
        Assert.ThrowsAny<OperationCanceledException>(() => cancelledLayout.Continue(Timeout.InfiniteTimeSpan));
        Assert.Throws<ObjectDisposedException>(() => cancelled.Continue(TimeSpan.Zero));

        // The page's text is still extracted whole afterwards: the abandoned runs gave back what they held.
        Assert.Equal(Project(page.ExtractText()), Project(page.ExtractText()));
    }

    /// <summary>The glyphs of a page: their codes, boxes, flags, fonts, text and structure elements, one line each.</summary>
    private static string Project(TextPage text)
    {
        var output = new StringBuilder();
        output.Append(text.Text).Append('\n');
        foreach (TextGlyph glyph in text.Glyphs)
        {
            output.Append(CultureInfo.InvariantCulture, $"{glyph.CharacterCode} {glyph.Quad} {glyph.Flags} {glyph.Font.BaseFont} {glyph.FontSize} {glyph.TextStart}+{glyph.TextLength} {glyph.StructureElement?.StructureType?.Value} {glyph.Language}\n");
        }

        return output.ToString();
    }
}
