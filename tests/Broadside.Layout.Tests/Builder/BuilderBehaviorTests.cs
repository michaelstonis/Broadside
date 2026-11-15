using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Flow.Units;
using Broadside.Structure;
using Broadside.TestSupport;
using Broadside.Text;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// The document builder's rules (issue #397): defaults, style inheritance (text properties inherit, box properties do not), accessibility
/// preconditions as diagnostics, programming errors thrown at the call, and handles that refuse to work outside their build.
/// </summary>
public sealed class BuilderBehaviorTests
{
    [Fact]
    public void The_hello_document_is_tagged_A4_titled_in_its_language_and_embeds_its_fonts()
    {
        using PdfDocument pdf = PdfDocument.Create(doc => doc
            .WithTitle("Hello")
            .WithLanguage("en-US")
            .Content(c =>
            {
                c.Heading(1, "Hello, Broadside");
                c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
            }));

        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        Assert.Equal("Document(H1,P)", Shape(reopened.StructureTree!.Children));
        Assert.Equal("en-US", reopened.Language);
        Assert.Equal("Hello", reopened.Properties.Title);
        Assert.True(reopened.ViewerPreferences!.DisplayDocTitle);
        Assert.True(reopened.MarkInfo.Marked);
        PdfRectangle box = reopened.Pages[0].MediaBox;
        Assert.Equal(595.276, box.Width, 3);
        Assert.Equal(841.89, box.Height, 3);
        string bytes = System.Text.Encoding.Latin1.GetString(Save(pdf));
        Assert.Equal(2, CountOf(bytes, "/FontFile2"));
        Assert.Empty(pdf.Diagnostics);
    }

    [Fact]
    public void Letter_comes_from_the_theme_and_never_from_the_machine()
    {
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithTheme(Theme.Letter).Content(c => c.Paragraph("x")));

        Assert.Equal(612, pdf.Pages[0].MediaBox.Width);
        Assert.Equal(792, pdf.Pages[0].MediaBox.Height);
    }

    [Fact]
    public void Text_properties_inherit_into_a_group_and_box_properties_do_not()
    {
        PdfColor red = PdfColor.FromRgb(0.8f, 0.1f, 0.1f);
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
            c.Group(g =>
            {
                g.Paragraph("first");
                g.Paragraph("second").FontSize(9);
            }).Style(new Style { Color = red, FontSize = 14, SpaceAfter = 40 })));

        FlowDocument flow = Assemble(doc => doc.Content(c =>
            c.Group(g =>
            {
                g.Paragraph("first");
                g.Paragraph("second").FontSize(9);
            }).Style(new Style { Color = red, FontSize = 14, SpaceAfter = 40 })));

        var group = (BlockGroup)Assert.Single(flow.Blocks);
        Assert.False(group.IsTagged);
        var first = (Paragraph)group.Blocks[0];
        var second = (Paragraph)group.Blocks[1];
        Assert.Equal(14, first.Style.FontSize);
        Assert.Equal(9, second.Style.FontSize);
        Assert.Equal(red, ((TextRun)Assert.Single(first.Content)).Style!.Color);
        Assert.Equal(6, first.Style.SpaceAfter);
        Assert.Equal(6, second.Style.SpaceAfter);
    }

    [Fact]
    public void Ten_thousand_paragraphs_with_three_looks_share_three_resolved_styles()
    {
        FlowDocument flow = Assemble(doc => doc.Content(c =>
        {
            for (int index = 0; index < 10_000; index++)
            {
                switch (index % 3)
                {
                    case 0:
                        c.Paragraph("plain");
                        break;
                    case 1:
                        c.Paragraph("bold").Bold();
                        break;
                    default:
                        c.Paragraph("small").FontSize(8);
                        break;
                }
            }
        }));

        Assert.Equal(3, flow.Blocks.Select(block => ((Paragraph)block).Style).Distinct().Count());
    }

    [Fact]
    public void A_theme_shared_and_derived_is_never_changed()
    {
        Theme derived = Theme.Default.WithBaseSize(10.5) with { Headings = Theme.Default.Headings with { H1 = new Style { FontSize = 22 } } };

        Assert.Equal(11, Theme.Default.BaseSize);
        Assert.Equal(10.5, derived.BaseSize);
        Assert.Null(Theme.Default.Headings.H1.FontSize);
        FlowDocument flow = Assemble(doc => doc.WithTheme(derived).Content(c =>
        {
            c.Heading(1, "Big");
            c.Heading(2, "Scaled");
        }));
        Assert.Equal(22, ((Paragraph)flow.Blocks[0]).Style.FontSize);
        Assert.Equal(10.5 * 1.6, ((Paragraph)flow.Blocks[1]).Style.FontSize, 6);
    }

    [Fact]
    public void Lang_is_written_only_where_it_changes()
    {
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
        {
            c.Paragraph("English.");
            c.Paragraph("Also English.").Language("en");
            c.Paragraph("Français.").Language("fr");
            c.Paragraph(t =>
            {
                t.Text("Mixed: ");
                t.Text("hola").Language("es");
            });
        }));

        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        PdfStructureElement[] paragraphs = [.. reopened.StructureTree!.Elements.Where(element => element.StructureType?.Value == "P")];
        Assert.Equal([null, null, "fr", null], paragraphs.Select(paragraph => paragraph.Language));
        Assert.Equal("es", reopened.StructureTree.Elements.Single(element => element.StructureType?.Value == "Span").Language);
    }

    [Fact]
    public void A_skipped_heading_level_is_a_diagnostic()
    {
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
        {
            c.Heading(1, "Top");
            c.Heading(3, "Too deep");
            c.Heading(2, "Back up");
        }));

        Diagnostic diagnostic = Assert.Single(pdf.Diagnostics);
        Assert.Equal(LayoutDiagnostics.HeadingLevelSkipped, diagnostic.Kind);
        Assert.Contains("Too deep", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void No_language_and_no_title_on_a_tagged_document_are_diagnostics()
    {
        using PdfDocument pdf = PdfDocument.Create(doc => doc.Content(c => c.Paragraph("x")));

        Assert.Equal(["DocumentLanguageMissing", "DocumentTitleMissing"], pdf.Diagnostics.Select(diagnostic => diagnostic.Code).Order());
    }

    [Fact]
    public void Strict_mode_throws_the_first_diagnostic()
    {
        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => PdfDocument.Create(new PdfOptions().UseStrict(), doc => doc.WithTitle("t").Content(c => c.Paragraph("x"))));

        Assert.Equal("DocumentLanguageMissing", exception.Diagnostic.Code);
    }

    [Fact]
    public void Untagged_output_still_writes_the_title_and_language()
    {
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("Plain").WithLanguage("de").WithTagging(false).Content(c => c.Paragraph("x")));

        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        Assert.Null(reopened.StructureTree);
        Assert.Equal("Plain", reopened.Properties.Title);
        Assert.Equal("de", reopened.Language);
        Assert.Empty(pdf.Diagnostics);
    }

    [Fact]
    public void Images_need_alternate_text_and_text_that_looks_like_a_file_name_is_a_diagnostic()
    {
        ImageSource image = ImageSource.FromBytes(PngSamples.Rgb(8, 4, alpha: false, interlaced: false));

        Assert.Throws<ArgumentException>(() => PdfDocument.Create(doc => doc.Content(c => c.Image(image, "  "))));
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
        {
            c.Image(image, "IMG_0423.jpg").Width(40.Mm);
            c.Image(image, "A gradient").Width(20.Mm).Caption("Figure 1. A gradient");
            c.DecorativeImage(image).Width(60.Mm).AlignCenter();
        }));

        Assert.Equal(["AlternateTextLooksLikeFileName"], pdf.Diagnostics.Select(diagnostic => diagnostic.Code + (diagnostic.Code == "AlternateTextLooksLikeFileName" ? string.Empty : ": " + diagnostic.Message)));
        using PdfDocument reopened = PdfDocument.Open(Save(pdf));
        Assert.Equal("Document(Figure,Figure(Caption))", Shape(reopened.StructureTree!.Children));
        Assert.Equal(1, CountOf(System.Text.Encoding.Latin1.GetString(Save(pdf)), "/Subtype /Image"));
    }

    [Fact]
    public void Programming_errors_throw_at_the_call()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Content(c => c.Heading(7, "x"))));
        Assert.Throws<ArgumentNullException>(() => PdfDocument.Create(doc => doc.Content(c => c.Paragraph((string)null!))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Content(c => c.Paragraph("x").FontSize(-1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Content(c => c.Table(t =>
        {
            t.Columns(ColumnWidth.Fixed(100), ColumnWidth.Fixed(100));
            t.Row(r => r.Cell("too wide").ColumnSpan(3));
        }))));
        Assert.Throws<InvalidOperationException>(() => PdfDocument.Create(doc => doc.Content(c => c.Table(t =>
        {
            t.Columns(ColumnWidth.Fixed(100));
            t.Row(r =>
            {
                r.Cell("one");
                r.Cell("two");
            });
        }))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Section(s => s.Margins(-1))));
    }

    [Fact]
    public void A_default_builder_handle_throws()
    {
        Assert.Throws<InvalidOperationException>(() => default(ContainerBuilder).Paragraph("x"));
        Assert.Throws<InvalidOperationException>(() => default(DocumentBuilder).WithTitle("x"));
        Assert.Throws<InvalidOperationException>(() => default(TableBuilder).Row("a"));
        Assert.Throws<InvalidOperationException>(() => default(ParagraphElement).Bold());
    }

    [Fact]
    public void A_literal_brace_field_in_a_header_prints_as_written()
    {
        string data = "{page} of {pages}";
        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").Section(s => s
            .Header(h => h.Left(data).Right($"{data} | {PageField.Number}/{PageField.Count} {PageField.Label}"))
            .Content(c => c.Paragraph("Body."))));

        string text = pdf.Pages[0].ExtractText(new TextExtractionOptions().WithArtifacts()).PlainText;
        Assert.Contains("{page} of {pages}", text, StringComparison.Ordinal);
        Assert.Contains("{page} of {pages} | 1/1 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_face_falls_back_to_the_regular_one_with_a_diagnostic()
    {
        ReadOnlyMemory<byte> regular = Broadside.Fonts.Standard14.Standard14FontResolver.GetProgram(Standard14Font.Helvetica);
        FontFamily family = FontFamily.FromPrograms(regular);

        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithTheme(Theme.Default.WithBodyFont(family)).Content(c =>
        {
            c.Paragraph("regular");
            c.Paragraph("bold").Bold();
            c.Paragraph("bold again").Bold();
        }));

        Diagnostic diagnostic = Assert.Single(pdf.Diagnostics);
        Assert.Equal(LayoutDiagnostics.FontFaceMissing, diagnostic.Kind);
    }

    [Fact]
    public void A_family_with_a_fallback_draws_what_its_faces_cannot_encode_from_the_fallback()
    {
        // Liberation Sans has no CJK; nothing here encodes it, so the character is replaced and reported, never thrown.
        FontFamily body = FontFamily.Standard(Standard14Family.Helvetica).WithFallback(FontFamily.Standard(Standard14Family.Times));

        using PdfDocument pdf = PdfDocument.Create(doc => doc.WithTitle("t").WithLanguage("en").WithTheme(Theme.Default.WithBodyFont(body)).Content(c =>
            c.Paragraph("Price: 10 €, name: 山田")));

        // Recorded once per code, like every diagnostic of the document.
        Assert.Equal(LayoutDiagnostics.CharacterNotEncodable, Assert.Single(pdf.Diagnostics).Kind);
        Assert.Contains("Price: 10 €, name: ??", pdf.Pages[0].ExtractText().PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void Units_convert_to_points()
    {
        Assert.Equal(72, 1.In.ToPoints(), 9);
        Assert.Equal(56.6929, 20.Mm.ToPoints(), 4);
        Assert.Equal(70.8661, 2.5.Cm.ToPoints(), 4);
        Assert.Equal(12, 12.Pt.ToPoints());
        Assert.Equal(Length.Points(84), 1.In + 12);
        Assert.True(20.Mm > 1.Cm);
        Assert.Equal("56.6929pt", 20.Mm.ToString());
        Assert.Equal(ColumnWidth.Fixed(72), (ColumnWidth)1.In);
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The flow a builder run assembles, before layout.</summary>
    private static FlowDocument Assemble(Action<DocumentBuilder> build)
    {
        using PdfDocument document = PdfDocument.Create();
        var context = new BuildContext(document, appending: false);
        build(new DocumentBuilder(context));
        context.Closed = true;
        return new DocumentAssembler(context).Assemble();
    }
}
