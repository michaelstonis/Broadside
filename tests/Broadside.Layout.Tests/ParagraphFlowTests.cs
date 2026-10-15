using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Layout.Tests;

/// <summary>
/// Paragraphs flowed by <see cref="FlowDocument"/> into a <see cref="PdfDocument"/>, saved, reopened and read back through text
/// extraction: lines stay inside the margins, read in order, justify to the right margin and break across pages. The text is
/// written with the canvas text operators, ISO 32000-2 §9.4.3 (<c>Tj</c>, <c>TJ</c>) and §9.4.4 (<c>Tw</c>).
/// </summary>
public sealed class ParagraphFlowTests
{
    /// <summary>The number of generated paragraphs that fills five pages in the first test's style.</summary>
    private const int ParagraphsForFivePages = 30;

    private static readonly PageSetup Page = PageSetup.Letter.WithMargins(new PageMargins(72, 72, 72, 72));

    [Fact]
    public void A_five_page_document_of_generated_paragraphs_re_reads_with_every_glyph_inside_the_margins_and_the_text_in_order()
    {
        using PdfDocument document = NewDocument();
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.TimesRoman), 11)
            .WithAlignment(TextAlignment.Justify)
            .WithSpaceAfter(6)
            .WithFirstLineIndent(18);
        var flow = new FlowDocument(Page);
        List<string> paragraphs = [];
        for (int index = 0; index < ParagraphsForFivePages; index++)
        {
            string text = Generated(index);
            paragraphs.Add(text);
            flow.Add(new Paragraph(text, style));
        }

        FlowLayoutResult result = flow.Render(document);

        Assert.Equal(5, result.Pages.Count);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal(5, reopened.Pages.Count);
        var words = new StringBuilder();
        foreach (PdfPage page in reopened.Pages)
        {
            TextPage text = page.ExtractText();
            Assert.NotEmpty(text.Glyphs);
            foreach (TextGlyph glyph in text.Glyphs)
            {
                PdfRectangle bounds = glyph.Quad.Bounds;
                Assert.InRange(bounds.Left, 72 - 0.001, 612 - 72 + 0.001);
                Assert.InRange(bounds.Right, 72 - 0.001, 612 - 72 + 0.001);
                Assert.InRange(bounds.Bottom, 72 - 0.001, 792 - 72 + 0.001);
                Assert.InRange(bounds.Top, 72 - 0.001, 792 - 72 + 0.001);
            }

            words.Append(text.PlainText).Append(' ');
        }

        Assert.Equal(Words(string.Join(' ', paragraphs)), Words(words.ToString()));
    }

    [Theory]
    [InlineData(TextAlignment.Justify, true)]
    [InlineData(TextAlignment.Justify, false)]
    public void Justified_lines_end_at_the_right_margin_except_the_last_line_of_each_paragraph(TextAlignment alignment, bool standard14)
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont font = standard14 ? document.GetStandard14Font(Standard14Font.Helvetica) : document.EmbedTrueTypeFont(LowercaseFont());
        var style = new ParagraphStyle(font, 10).WithAlignment(alignment).WithSpaceAfter(6);
        var flow = new FlowDocument(Page);
        for (int index = 0; index < 3; index++)
        {
            flow.Add(new Paragraph(Generated(index), style));
        }

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();
        List<(double Left, double Right, double Y)> lines = LineExtents(text);
        Assert.True(lines.Count > 6, $"Only {lines.Count} lines were laid out.");
        HashSet<double> paragraphEnds = ParagraphLastLines(lines);
        Assert.Equal(3, paragraphEnds.Count);
        foreach ((double left, double right, double y) in lines)
        {
            Assert.Equal(72, left, 3);
            if (paragraphEnds.Contains(y))
            {
                Assert.True(right < 540 - 1, $"The last line at {y} ends at {right}, at the margin.");
            }
            else
            {
                // Within the four decimals a content stream carries per word-spacing or TJ number.
                Assert.InRange(right, 540 - 0.005, 540 + 0.0001);
            }
        }
    }

    [Theory]
    [InlineData(TextAlignment.Left)]
    [InlineData(TextAlignment.Right)]
    [InlineData(TextAlignment.Center)]
    public void Ragged_alignments_place_each_line_against_its_margin_or_centred_between_them(TextAlignment alignment)
    {
        using PdfDocument document = NewDocument();
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Courier), 12).WithAlignment(alignment);
        var flow = new FlowDocument(Page);
        flow.Add(new Paragraph(Generated(1), style));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        List<(double Left, double Right, double Y)> lines = LineExtents(reopened.Pages[0].ExtractText());
        Assert.True(lines.Count > 2);
        foreach ((double left, double right, _) in lines)
        {
            Assert.InRange(right - left, 0, 468 + 0.001);
            switch (alignment)
            {
                case TextAlignment.Left:
                    Assert.Equal(72, left, 3);
                    break;
                case TextAlignment.Right:
                    Assert.Equal(540, right, 3);
                    break;
                default:
                    Assert.Equal(306, (left + right) / 2, 3);
                    break;
            }
        }
    }

    [Fact]
    public void Greedy_breaking_fills_each_line_with_as_many_words_as_fit_and_a_newline_forces_a_break()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont courier = document.GetStandard14Font(Standard14Font.Courier);
        // Courier advances 600/1000 em: at 10 points, 20 characters fill the 120-point measure exactly.
        var narrow = new PageSetup(264, 400, new PageMargins(72, 72, 72, 72));
        var flow = new FlowDocument(narrow);
        flow.Add(new Paragraph("aaaa bbbb cccc ddddd eeee ffff\ngg hh", new ParagraphStyle(courier, 10)));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();
        Assert.Equal(["aaaa bbbb cccc ddddd", "eeee ffff", "gg hh"], text.Lines.Select(line => text.GetLineText(line).ToString().Trim()).ToArray());
    }

    [Fact]
    public void Spacing_before_after_and_line_height_set_the_baselines()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
        var style = new ParagraphStyle(helvetica, 10).WithLineHeight(2).WithSpaceBefore(5).WithSpaceAfter(7);
        var flow = new FlowDocument(Page);
        flow.Add(new Paragraph("one", style)).Add(new Paragraph("two", style));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        TextPage text = reopened.Pages[0].ExtractText();
        double first = text.Glyphs[0].Origin.Y;
        double second = text.Glyphs[3].Origin.Y;
        // Line box 20 points; the glyph box (ascent 718, descent -207) is centred in it. Space before is dropped at the page top.
        double halfLeading = (20 - 9.25) / 2;
        Assert.Equal(720 - halfLeading - 7.18, first, 6);
        Assert.Equal(720 - 20 - 7 - 5 - halfLeading - 7.18, second, 6);
    }

    [Fact]
    public void Paragraphs_continue_on_a_new_page_when_the_next_line_would_cross_the_bottom_margin()
    {
        using PdfDocument document = NewDocument();
        var small = new PageSetup(300, 200, new PageMargins(20, 20, 20, 20));
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10).WithLineHeight(1.6);
        var flow = new FlowDocument(small);
        flow.Add(new Paragraph(string.Join(' ', Enumerable.Range(0, 500).Select(index => "w" + index)), style));

        FlowLayoutResult result = flow.Render(document);

        Assert.True(result.Pages.Count > 2);
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        var all = new StringBuilder();
        foreach (PdfPage page in reopened.Pages)
        {
            Assert.Equal(new PdfRectangle(0, 0, 300, 200), page.MediaBox);
            TextPage text = page.ExtractText();
            // 160 points of content at 16 points a line hold ten lines.
            Assert.Equal(page == reopened.Pages[^1] ? text.Lines.Count : 10, text.Lines.Count);
            all.Append(text.PlainText).Append(' ');
        }

        Assert.Equal(Enumerable.Range(0, 500).Select(index => "w" + index), Words(all.ToString()));
    }

    [Fact]
    public void Output_is_byte_identical_across_runs()
    {
        Assert.Equal(Build(), Build());

        static byte[] Build()
        {
            using PdfDocument document = NewDocument();
            var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.TimesRoman), 9.5)
                .WithAlignment(TextAlignment.Justify)
                .WithKerning(true);
            var flow = new FlowDocument(PageSetup.A4);
            for (int index = 0; index < 40; index++)
            {
                flow.Add(new Paragraph(Generated(index), style));
            }

            flow.Render(document);
            return Saved(document);
        }
    }

    [Fact]
    public void The_page_content_matches_the_recorded_hash_on_every_platform()
    {
        using PdfDocument document = NewDocument();
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.TimesRoman), 9.5)
            .WithAlignment(TextAlignment.Justify)
            .WithKerning(true)
            .WithFirstLineIndent(12);
        var flow = new FlowDocument(PageSetup.A4);
        for (int index = 0; index < 12; index++)
        {
            flow.Add(new Paragraph(Generated(index), style));
        }

        flow.Render(document);

        // The content streams are what the layout writes; the rest of the file belongs to the core writer.
        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        using var content = new MemoryStream();
        foreach (PdfPage page in reopened.Pages)
        {
            var stream = (CosStream)reopened.Resolve(page.Dictionary[new CosName("Contents")]);
            content.Write(reopened.DecodeStream(stream).Span);
        }

        Assert.Equal("8D1BAF38CB7D908D04589A86552A197FF49904CA5FD4789FBE53CBE93A441F29", Convert.ToHexString(SHA256.HashData(content.ToArray())));
    }

    [Fact]
    public void Kerned_paragraphs_measure_with_the_kerning_they_are_shown_with()
    {
        using PdfDocument document = NewDocument();
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 12)
            .WithAlignment(TextAlignment.Right)
            .WithKerning(true);
        var flow = new FlowDocument(Page);
        flow.Add(new Paragraph("AVAVAV To Yo", style));

        flow.Render(document);

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        List<(double Left, double Right, double Y)> lines = LineExtents(reopened.Pages[0].ExtractText());
        Assert.Equal(540, Assert.Single(lines).Right, 3);
    }

    [Fact]
    public void Each_block_carries_a_structure_type_and_language_for_tagging()
    {
        using PdfDocument document = NewDocument();
        var style = new ParagraphStyle(document.GetStandard14Font(Standard14Font.Helvetica), 10);

        var paragraph = new Paragraph("text", style);
        var heading = new Paragraph("Title", style) { StructureType = "H1", Language = "en-GB" };

        Assert.Equal("P", paragraph.StructureType);
        Assert.Null(paragraph.Language);
        Assert.Equal("H1", heading.StructureType);
        Assert.Equal("en-GB", heading.Language);
    }

    [Fact]
    public void Invalid_input_is_refused_before_anything_is_written()
    {
        using PdfDocument document = NewDocument();
        PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ParagraphStyle(helvetica, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParagraphStyle(helvetica, 10).WithLineHeight(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParagraphStyle(helvetica, 10).WithSpaceAfter(-1));
        Assert.Throws<ArgumentException>(() => new FlowDocument(new PageSetup(100, 100, new PageMargins(60, 10, 60, 10))));
        var flow = new FlowDocument(Page);
        flow.Add(new Paragraph("fine", new ParagraphStyle(helvetica, 10)));
        flow.Add(new Paragraph("not 中 encodable", new ParagraphStyle(helvetica, 10)));
        ArgumentException refused = Assert.Throws<ArgumentException>(() => flow.Render(document));
        Assert.Contains("paragraph 1", refused.Message, StringComparison.Ordinal);
        Assert.Empty(document.Pages);
    }

    [Fact]
    public void The_package_depends_only_on_the_core()
    {
        AssemblyName[] references = typeof(FlowDocument).Assembly.GetReferencedAssemblies();

        Assert.All(references, reference => Assert.True(
            reference.Name is "Broadside" or "System" or "netstandard" || reference.Name!.StartsWith("System.", StringComparison.Ordinal),
            $"Broadside.Layout references {reference.Name}."));
        Assert.Contains(references, static reference => reference.Name == "Broadside");
    }

    /// <summary>A new document without pages, its metadata dated by a fixed clock in UTC so saves are reproducible.</summary>
    private static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create(new PdfOptions().UseTimeProvider(FixedClock.Instance));
        document.Pages.RemoveAt(0);
        return document;
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static string[] Words(string text) => text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Deterministic pseudo-text: words of 1 to 11 lowercase letters, 40 to 120 words a paragraph.</summary>
    private static string Generated(int seed)
    {
        uint state = (uint)(seed * 2654435761u) | 1;
        uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        int count = 40 + (int)(Next() % 81);
        var builder = new StringBuilder();
        for (int word = 0; word < count; word++)
        {
            if (word > 0)
            {
                builder.Append(' ');
            }

            int length = 1 + (int)(Next() % 11);
            for (int letter = 0; letter < length; letter++)
            {
                builder.Append((char)('a' + (Next() % 26)));
            }
        }

        return builder.ToString();
    }

    /// <summary>Each line's left edge (first glyph origin), right edge (last non-space glyph end) and baseline.</summary>
    private static List<(double Left, double Right, double Y)> LineExtents(TextPage text)
    {
        List<(double, double, double)> lines = [];
        foreach (TextLine line in text.Lines)
        {
            double left = double.MaxValue;
            double right = double.MinValue;
            for (int index = line.GlyphStart; index < line.GlyphStart + line.GlyphCount; index++)
            {
                TextGlyph glyph = text.Glyphs[index];
                if (text.GetGlyphText(glyph).IsWhiteSpace())
                {
                    continue;
                }

                left = Math.Min(left, glyph.Origin.X);
                right = Math.Max(right, glyph.Origin.X + glyph.Advance.X);
            }

            lines.Add((left, right, text.Glyphs[line.GlyphStart].Origin.Y));
        }

        return lines;
    }

    /// <summary>The baselines of lines followed by a larger gap or by nothing: the last lines of paragraphs (laid out with space after).</summary>
    private static HashSet<double> ParagraphLastLines(List<(double Left, double Right, double Y)> lines)
    {
        HashSet<double> ends = [];
        for (int index = 0; index < lines.Count; index++)
        {
            if (index == lines.Count - 1 || lines[index].Y - lines[index + 1].Y > 12.5)
            {
                ends.Add(lines[index].Y);
            }
        }

        return ends;
    }

    /// <summary>A TrueType program mapping space and a–z to rectangles 500 units wide (space 250), with an <c>OS/2</c> table.</summary>
    private static byte[] LowercaseFont()
    {
        var builder = new TrueTypeBuilder
        {
            Cmap = TrueTypeBuilder.CmapTable((3, 1, TrueTypeBuilder.Format4((32, 32, -31), (97, 122, -95)))),
        };
        List<(ushort, short)> metrics = [(500, 0), (250, 0)];
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 400, 700));
        builder.Glyphs.Add(TrueTypeBuilder.Rectangle(0, 0, 0, 0));
        for (int letter = 0; letter < 26; letter++)
        {
            builder.Glyphs.Add(TrueTypeBuilder.Rectangle(20, 0, 380 + (letter * 4), 500));
            metrics.Add(((ushort)(420 + (letter * 6)), 20));
        }

        builder.Metrics = metrics;
        byte[] os2 = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(os2, 4);
        BinaryPrimitives.WriteUInt16BigEndian(os2.AsSpan(4), 400);
        builder.Overrides["OS/2"] = os2;
        return builder.Build();
    }

    private sealed class FixedClock : TimeProvider
    {
        public static FixedClock Instance { get; } = new();

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    }
}
