using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Structure;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// Auto-tagged documents checked against PDF/UA-1 (issue #206, spec story 38): an untagged layout corpus file, auto-tagged and given
/// alternate text (plus the embedded fonts and Unicode mappings it lacks, <see cref="SuppliedFonts"/>), saves as PDF/UA-1 and passes
/// veraPDF's PDF/UA-1 profile; what is still missing is reported under its veraPDF rule identifier, never claimed.
/// </summary>
public sealed class AutoTaggingPdfUATests
{
    private const string NonConformant = "PdfUANonConformant";
    private const string NotIdentified = "PdfUANotIdentified";

    public static TheoryData<string> LayoutFiles =>
        ["layout-two-column-untagged.pdf", "layout-three-column.pdf", "layout-headings-lists.pdf", "layout-tables.pdf", "layout-figures.pdf"];

    [Theory]
    [Trait("Category", "VeraPdf")]
    [MemberData(nameof(LayoutFiles))]
    public void An_untagged_layout_file_auto_tagged_with_supplied_alternate_text_passes_verapdf_PDF_UA_1(string name)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(name));
        byte[] saved = Accessible(document);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(1, reopened.Metadata!.Packet!.PdfUAPart);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
    }

    [Theory]
    [Trait("Category", "VeraPdf")]
    [InlineData("layout-two-column-untagged.pdf")]
    [InlineData("layout-tables.pdf")]
    public void An_untagged_layout_file_auto_tagged_and_converted_to_PDF_A_2u_passes_verapdf_PDF_UA_1_and_PDF_A_2u(string name)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(name));
        document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));
        document.Properties.Title = "Layout sample";
        PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2u);

        byte[] saved = PdfUAFlowTests.Saved(document, conversion.CreateSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.True(conversion.IsConformant, string.Join("; ", conversion.NotConverted));
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
        Assert.Empty(VeraPdf.FailedRules(saved, "2u"));
    }

    [Theory]
    [MemberData(nameof(LayoutFiles))]
    public void An_untagged_layout_file_auto_tagged_with_supplied_alternate_text_conforms_by_the_library_check(string name)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(name));
        byte[] saved = Accessible(document);

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code is NonConformant or NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(1, reopened.Metadata!.Packet!.PdfUAPart);
    }

    public static TheoryData<string, string[]> Limitations => new()
    {
        { "layout-two-column-untagged.pdf", ["7.2-33", "7.21.4.1-1"] },
        { "layout-figures.pdf", ["7.2-33", "7.21.4.1-1", "7.21.7-1", "7.3-1"] },
    };

    [Theory]
    [MemberData(nameof(Limitations))]
    public void Auto_tagging_alone_reports_each_missing_requirement_and_the_save_claims_no_conformance(string name, string[] rules)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path(name));
        document.AutoTag();
        document.Properties.Title = "Layout sample";

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        string[] reported = [.. document.Diagnostics.Where(diagnostic => diagnostic.Code == NonConformant)
            .Select(diagnostic => diagnostic.Message[(diagnostic.Message.IndexOf("veraPDF rule ", StringComparison.Ordinal) + 13)..])
            .Select(rest => rest[..rest.IndexOf(',', StringComparison.Ordinal)]).Distinct().Order(StringComparer.Ordinal)];
        Assert.Equal(rules, reported);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == NotIdentified);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Null(reopened.Metadata?.Packet?.PdfUAPart);
    }

    [Fact]
    public void A_figure_left_with_the_placeholder_alternate_text_is_reported_and_refused_in_strict_mode()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-figures.pdf"), new PdfOptions().UseStrict());
        AutoTaggingResult result = document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));
        SuppliedFonts.Supply(document);
        document.Properties.Title = "Figures";
        result.Figures[0].AlternateDescription = "A bar chart of four quarters";

        DiagnosticException refused = Assert.Throws<DiagnosticException>(() => PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1)));

        Assert.Equal(NonConformant, refused.Diagnostic.Code);
        Assert.Contains("veraPDF rule 7.3-1,", refused.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("placeholder", refused.Diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_is_tagged_as_a_Link_holding_its_text_and_an_object_reference_and_described_by_its_text()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-two-column-untagged.pdf"));
        PdfPage page = document.Pages[0];
        PdfRectangle word = page.ExtractText().Glyphs.Take(4).Select(static glyph => glyph.Quad.Bounds)
            .Aggregate(static (a, b) => new PdfRectangle(Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top)));
        page.AddLinkAnnotation(word, new Uri("https://example.org/"));
        document.AutoTag();

        PdfAnnotation link = page.Annotations.Single(annotation => annotation.Kind == PdfAnnotationKind.Link);
        PdfStructureElement element = document.StructureTree!.FindElementForObject(link.Reference!)!;
        Assert.Equal("Link", element.StructureType!.Value);
        Assert.Contains(element.Children, kid => kid is PdfObjectReference);
        Assert.Contains(element.Children, kid => kid is PdfMarkedContentReference);
        Assert.Equal(string.Concat(page.ExtractText().Glyphs.Take(4).Select(glyph => page.ExtractText().GetGlyphText(glyph).ToString())).Trim(), link.Contents);
    }

    [Fact]
    public void Every_other_annotation_is_tagged_as_an_Annot_or_a_Form_after_the_page_content()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        document.AutoTag();

        foreach (PdfPage page in document.Pages)
        {
            foreach (PdfAnnotation annotation in page.Annotations)
            {
                string expected = annotation.Kind switch
                {
                    PdfAnnotationKind.Widget => "Form",
                    PdfAnnotationKind.Link => "Link",
                    _ => "Annot",
                };
                Assert.Equal(expected, document.StructureTree!.FindElementForObject(annotation.Reference!)?.StructureType?.Value);
            }
        }
    }

    [Fact]
    public void Headings_start_at_H1_and_never_skip_a_level()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("layout-headings-lists.pdf"));
        document.AutoTag();

        List<int> levels = [];
        Walk(document.StructureTree!.Children);
        Assert.NotEmpty(levels);
        Assert.Equal(1, levels[0]);
        Assert.All(levels.Zip(levels.Skip(1)), pair => Assert.True(pair.Second <= pair.First + 1, $"H{pair.First} then H{pair.Second}"));

        void Walk(IEnumerable<PdfStructureItem> items)
        {
            foreach (PdfStructureElement element in items.OfType<PdfStructureElement>())
            {
                if (element.StructureType?.Value is ['H', >= '1' and <= '6'] type)
                {
                    levels.Add(type[1] - '0');
                }

                Walk(element.Children);
            }
        }
    }

    [Fact]
    public void Text_hidden_by_optional_content_is_tagged_too()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("optional-content.pdf"));
        document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));

        PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Code == NonConformant && diagnostic.Message.Contains("rule 7.1-3,", StringComparison.Ordinal));
    }

    /// <summary>Auto-tags a layout corpus file, describes its figures, supplies fonts, Unicode and a title, and saves it as PDF/UA-1.</summary>
    private static byte[] Accessible(PdfDocument document)
    {
        AutoTaggingResult result = document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));
        for (int index = 0; index < result.Figures.Count; index++)
        {
            result.Figures[index].AlternateDescription = $"Figure {index + 1} of the sample document";
        }

        SuppliedFonts.Supply(document);
        document.Properties.Title = "Layout sample";
        return PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
    }
}
