using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;
using Broadside.Text;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Markdown;

/// <summary>
/// Markdown through the document builder (issue #645): each construct becomes the structure the builder writes for it, the text and
/// tables come back out of <see cref="PdfDocument.ExtractLayout()"/>, raw HTML and unresolved images are diagnostics, and a README-sized
/// document saves as PDF/UA-2 with nothing non-conformant.
/// </summary>
public sealed class MarkdownFlowTests
{
    private static readonly string Logo = Path.Combine(AppContext.BaseDirectory, "BuilderSamples", "logo.png");

    internal const string Readme = """
        # Broadside

        Broadside is a **fully managed** .NET PDF library: *read*, write, edit and `render`. See [the guide](#getting-started) or
        <https://github.com/michaelstonis/Broadside>, and www.example.com.

        ## Getting started

        Install the package:

        ```sh
        dotnet add package Broadside
        ```

        Then open a document:

            using PdfDocument document = PdfDocument.Open("in.pdf");
            	string text = document.Pages[0].ExtractText().PlainText;

        > Every repair is recorded as a diagnostic.
        > Strict mode throws instead.

        ## Features

        1. Reading and repairing
        2. Writing, with ~~no~~ native code
           - incremental updates
           - linearization
        3. Rendering

        - [x] PDF/UA-2 output
        - [ ] Footnotes

        | Feature | Status | Since |
        |:--------|:------:|------:|
        | Read    | done   | 1.0   |
        | Render  | done   | 1.0   |
        | Sign \| verify | partial | 1.1 |

        ---

        ![The Broadside logo](logo.png)

        Line one\
        line two.
        """;

    [Fact]
    public void Headings_become_H1_to_H6_with_slug_ids_and_the_first_level_1_heading_is_the_title()
    {
        using PdfDocument document = FromMarkdown("# Top\n\nText.\n\nSecond\n------\n\n### Third *level*\n\n## Second\n");

        Assert.Equal("Document(H1,P,H2,H3(Em),H2)", Shape(document.StructureTree!.Children));
        Assert.Equal("Top", document.Properties.Title);
        Assert.All(["top", "second", "third-level", "second-1"], id => Assert.NotNull(document.GetNamedDestination(id)));
    }

    [Fact]
    public void A_title_the_configuration_sets_is_kept()
    {
        using PdfDocument document = PdfDocument.CreateFromMarkdown("# Heading\n", configure: doc => doc.WithTitle("Set"));

        Assert.Equal("Set", document.Properties.Title);
    }

    [Fact]
    public void Emphasis_strong_code_and_strikethrough_become_Em_Strong_Code_and_spans()
    {
        using PdfDocument document = FromMarkdown("A *b* **c** `d` ~~e~~ ***f***.\n");

        Assert.Equal("Document(P(Em,Strong,Code,Strong))", Shape(document.StructureTree!.Children));
        Assert.Equal("A b c d e f.", document.Pages[0].ExtractText().PlainText.Trim());
    }

    [Fact]
    public void Lists_keep_their_kind_start_number_and_nesting()
    {
        using PdfDocument document = FromMarkdown("3. three\n4. four\n   - nested\n\n- bullet\n");

        Assert.Equal("Document(L(LI(Lbl,LBody(P)),LI(Lbl,LBody(P,L(LI(Lbl,LBody(P)))))),L(LI(Lbl,LBody(P))))", Shape(document.StructureTree!.Children));
        PdfStructureElement ordered = document.StructureTree.Children[0].Children.OfType<PdfStructureElement>().First();
        Assert.Equal(new CosName("Decimal"), ordered.GetAttributeValue(new CosName("List"), new CosName("ListNumbering")));
        string text = document.Pages[0].ExtractText().PlainText;
        Assert.Contains("3.", text, StringComparison.Ordinal);
        Assert.Contains("4.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_list_item_shows_a_ballot_box_with_its_state_as_the_label_text()
    {
        using PdfDocument document = FromMarkdown("- [x] done\n- [ ] open\n");

        PdfStructureElement[] labels = [.. Descendants(document.StructureTree!.Children[0]).Where(element => element.StructureType?.Value == "Lbl")];
        Assert.Equal(["☑", "☐"], labels.Select(label => label.ActualText));
        Assert.Equal("☑ done\n☐ open", document.Pages[0].ExtractText().PlainText.Trim().ReplaceLineEndings("\n"));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind == LayoutDiagnostics.CharacterNotEncodable);
    }

    [Fact]
    public void A_code_block_keeps_its_whitespace_in_the_monospace_font_with_tabs_expanded()
    {
        using PdfDocument document = FromMarkdown("```\nif (x)\n\treturn;\n```\n");

        Assert.Equal("Document(P)", Shape(document.StructureTree!.Children));
        TextPage page = document.Pages[0].ExtractText();
        TextWord[] words = [.. page.Words];
        TextWord first = Array.Find(words, word => page.Text.Substring(word.TextStart, word.TextLength) == "if")!;
        TextWord indented = Array.Find(words, word => page.Text.Substring(word.TextStart, word.TextLength) == "return;")!;
        TextGlyph glyph = page.Glyphs[first.GlyphStart];
        Assert.Contains("Mono", glyph.Font.BaseFont, StringComparison.Ordinal);
        Assert.Equal(4 * 0.6 * glyph.FontSize, indented.Quad.BottomLeft.X - first.Quad.BottomLeft.X, 0.5);
    }

    [Fact]
    public void A_block_quote_is_an_indented_Div()
    {
        using PdfDocument document = FromMarkdown("Before.\n\n> Quoted\n> > nested\n");

        Assert.Equal("Document(P,Div(P,Div(P)))", Shape(document.StructureTree!.Children));
    }

    [Fact]
    public void A_table_has_a_header_row_body_rows_and_its_column_alignment()
    {
        using PdfDocument document = FromMarkdown("| a | b |\n|---|--:|\n| 1 | 2 |\n| 3 |\n");

        Assert.Equal("Document(Table(THead(TR(TH(P),TH(P))),TBody(TR(TD(P),TD(P)),TR(TD(P),TD(P)))))", Shape(document.StructureTree!.Children));
        string markdown = document.ExtractMarkdown();
        Assert.Contains("| **a** | **b** |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 1 | 2 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Links_go_to_their_URI_or_to_the_heading_an_anchor_names_and_other_destinations_stay_text()
    {
        using PdfDocument document = FromMarkdown("# Top\n\n[web](https://example.com/a b) [back](#top) [file](docs/a.md) <https://example.org> www.example.net\n");

        PdfLinkAnnotation[] links = [.. document.Pages[0].Annotations.OfType<PdfLinkAnnotation>()];
        Assert.Equal(4, links.Length);
        Assert.Equal(4, Descendants(document.StructureTree!.Children[0]).Count(element => element.StructureType?.Value == "Link"));
        Assert.Contains("file", document.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind == LayoutDiagnostics.AnchorNotFound);
    }

    [Fact]
    public void An_image_from_the_base_directory_is_a_Figure_with_its_alternate_text()
    {
        using PdfDocument document = FromMarkdown("Text ![A blue square](logo.png) after.\n", new MarkdownOptions().WithBaseDirectory(Path.GetDirectoryName(Logo)));

        Assert.Equal("Document(P,Figure,P)", Shape(document.StructureTree!.Children));
        Assert.Equal("A blue square", document.StructureTree.Children[0].Children.OfType<PdfStructureElement>().ElementAt(1).AlternateDescription);
    }

    [Fact]
    public void The_image_resolver_is_asked_first_with_the_destination_as_written()
    {
        var asked = new List<string>();
        MarkdownOptions options = new MarkdownOptions().WithImageResolver(destination =>
        {
            asked.Add(destination);
            return ImageSource.FromFile(Logo);
        });

        using PdfDocument document = FromMarkdown("![Logo](my%20logo.png)\n", options);

        Assert.Equal(["my logo.png"], asked);
        Assert.Equal("Document(Figure)", Shape(document.StructureTree!.Children));
    }

    [Fact]
    public void An_image_without_alternate_text_is_a_Figure_tagging_reports()
    {
        using PdfDocument document = FromMarkdown("![](logo.png)\n", new MarkdownOptions().WithBaseDirectory(Path.GetDirectoryName(Logo)));

        Assert.Equal("Document(Figure)", Shape(document.StructureTree!.Children));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Kind == StructureDiagnostics.FigureWithoutAlternateText);
    }

    [Theory]
    [InlineData("https://example.com/logo.png", "remote images are not fetched")]
    [InlineData("../logo.png", "the path leaves the base directory")]
    [InlineData("/etc/passwd", "the path leaves the base directory")]
    [InlineData("missing.png", "no such file")]
    public void An_image_that_does_not_resolve_is_its_alternate_text_in_italics(string destination, string reason)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Logo)!, "markdown-base");
        Directory.CreateDirectory(directory);

        using PdfDocument document = FromMarkdown($"See ![the logo]({destination}).\n", new MarkdownOptions().WithBaseDirectory(directory));

        Assert.Equal("Document(P)", Shape(document.StructureTree!.Children));
        Assert.Contains("See the logo.", document.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Kind == LayoutDiagnostics.MarkdownImageNotFound);
        Assert.Contains(reason, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Raw_HTML_is_dropped_and_reported_once()
    {
        using PdfDocument document = FromMarkdown("<div>\nblock\n</div>\n\nText <b>bold</b> <br>.\n");

        Assert.Equal("Document(P)", Shape(document.StructureTree!.Children));
        Assert.Equal("Text bold .", document.Pages[0].ExtractText().PlainText.Trim());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Kind == LayoutDiagnostics.MarkdownHtmlIgnored);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void A_thematic_break_is_an_artifact_rule_and_a_hard_break_ends_the_line()
    {
        using PdfDocument document = FromMarkdown("one  \ntwo\n\n***\n\nthree\n");

        Assert.Equal("Document(P,P)", Shape(document.StructureTree!.Children));
        Assert.Contains("one\ntwo", document.Pages[0].ExtractText().PlainText.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_extracted_layout_gives_back_the_headings_lists_and_tables()
    {
        using PdfDocument document = FromMarkdown(Readme, new MarkdownOptions().WithBaseDirectory(Path.GetDirectoryName(Logo)));

        string markdown = document.ExtractMarkdown();
        Assert.Contains("# Broadside", markdown, StringComparison.Ordinal);
        Assert.Contains("## Getting started", markdown, StringComparison.Ordinal);
        Assert.Contains("## Features", markdown, StringComparison.Ordinal);
        Assert.Contains("Reading and repairing", markdown, StringComparison.Ordinal);
        Assert.Contains("| **Feature** | **Status** | **Since** |", markdown, StringComparison.Ordinal);
        Assert.Contains("| Sign \\| verify | partial | 1.1 |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_nested_deeper_than_the_limit_is_laid_out_at_the_limit()
    {
        string markdown = new string('>', 200) + " deep\n\n" + string.Concat(Enumerable.Range(0, 40).Select(depth => new string(' ', depth * 2) + "- item\n"));

        using PdfDocument document = FromMarkdown(markdown);

        Assert.Contains("deep", document.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Turning_the_GitHub_extensions_off_leaves_tables_and_strikethrough_as_text()
    {
        using PdfDocument document = FromMarkdown("| a |\n|---|\n\n~~x~~ www.example.com\n", new MarkdownOptions().WithGitHubExtensions(false));

        Assert.Equal("Document(P,P)", Shape(document.StructureTree!.Children));
        Assert.Empty(document.Pages[0].Annotations);
        Assert.Contains("~~x~~", document.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_README_saves_as_PDF_UA_2_with_nothing_non_conformant()
    {
        using PdfDocument document = Ua2Readme();

        byte[] saved = Save(document);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind == SavingDiagnostics.PdfUANonConformant);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(2, reopened.Metadata!.Packet!.PdfUAPart);
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_README_passes_verapdf_PDF_UA_2()
    {
        using PdfDocument document = Ua2Readme();

        byte[] saved = Save(document);

        Assert.Empty(VeraPdf.FailedRules(saved, "ua2"));
    }

    [Fact]
    public async Task The_README_sample_matches_its_snapshot()
    {
        using PdfDocument document = Ua2Readme();
        using PdfDocument reopened = await PdfDocument.OpenAsync(Save(document), TestContext.Current.CancellationToken);
        await VerifyXunit.Verifier.Verify($"""
            == structure ==
            {StructureText(reopened)}
            == text ==
            {PagesText(reopened)}
            == extracted Markdown ==
            {reopened.ExtractMarkdown()}
            """);
    }

    internal static PdfDocument Ua2Readme() =>
        PdfDocument.Create(Options(), doc => doc
            .WithLanguage("en-US")
            .WithPdfUAConformance(PdfUAConformance.PdfUA2)
            .Content(c => c.AddMarkdown(Readme, new MarkdownOptions().WithBaseDirectory(Path.GetDirectoryName(Logo)), titleFromHeading: true)));

    private static PdfDocument FromMarkdown(string markdown, MarkdownOptions? options = null) =>
        PdfDocument.CreateFromMarkdown(markdown, options, doc => doc.WithLanguage("en"));

    private static IEnumerable<PdfStructureElement> Descendants(PdfStructureElement element)
    {
        foreach (PdfStructureElement child in element.Children.OfType<PdfStructureElement>())
        {
            yield return child;
            foreach (PdfStructureElement descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
