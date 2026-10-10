using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>Real-world deviations in Annots arrays and annotation dictionaries: lenient repairs with diagnostics, strict throws (ISO 32000-2 §12.5, ADR 0005).</summary>
public sealed class MalformedAnnotationTests
{
    [Fact]
    public void Annotations_malformed_pdf_reads_what_it_can_with_exactly_the_documented_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-malformed.pdf"));
        Assert.Empty(document.Diagnostics);

        IReadOnlyList<PdfAnnotation> first = document.Pages[0].Annotations;

        Assert.Equal(
            ["Square 10", "Square 10", "Unknown 11", "Circle 12", "Highlight 13", "Square 14", "Square 15", "Square 16", "Square -"],
            first.Select(annotation => $"{annotation.Kind} {annotation.Reference?.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}"));
        Assert.Same(first[0], first[1]);
        Assert.Equal(
            [
                "AnnotationEntryInvalid 3", // element 0 is null
                "AnnotationEntryInvalid 9", // element 1 refers to an integer
                "AnnotationDuplicate 10",
                "AnnotationSubtypeMissing 11",
                "AnnotationPageMismatch 16", // P names page 4
                "AnnotationNotIndirect 3",
            ],
            document.Diagnostics.Select(Describe));

        Assert.Equal(default, first[3].Rect);
        Assert.Empty(((PdfHighlightAnnotation)first[4]).QuadPoints);
        Assert.Null(first[5].GetAppearance());
        Assert.NotNull(first[6].GetAppearance());
        Assert.Null(first[6].GetAppearance()!.BoundingBox);
        Assert.Equal(new Broadside.Graphics.Matrix(1, 0, 0, 1, 90, 10), first[6].GetAppearanceMatrix(first[6].GetAppearance()!));

        IReadOnlyList<PdfAnnotation> second = document.Pages[1].Annotations;
        Assert.Same(first[7], second[0]);
        Assert.Same(document.Pages[0], second[0].Page);

        PdfTextAnnotation text = Assert.IsType<PdfTextAnnotation>(second[1]);
        Assert.Equal(PdfReplyType.Group, text.ReplyType);
        Assert.Same(second[2], text.Popup);
        Assert.Equal("Accepted", text.State);
        Assert.Equal((PdfAnnotationFlags)2048, text.Flags);
        Assert.Null(text.Color);
        Assert.Equal((0.0, PdfBorderSource.BorderArray), (text.Border.Width, text.Border.Source));
        Assert.Same(second[3], ((PdfPopupAnnotation)second[2]).Parent);
        PdfLinkAnnotation link = Assert.IsType<PdfLinkAnnotation>(second[3]);
        Assert.IsType<PdfUriAction>(link.Action);
        Assert.IsType<PdfExplicitDestination>(link.Destination);

        Assert.Equal(
            [
                "AnnotationFlagsInvalid 17",
                "AnnotationRectInvalid 12",
                "AnnotationSharedAcrossPages 16",
                "AppearanceBBoxMissing 15",
                "AppearanceStateMissing 14",
                "BorderArrayInvalid 17",
                "ColorArrayInvalid 17",
                "LinkActionAndDest 19",
                "PopupLinkMismatch 17",
                "PopupParentInvalid 18",
                "QuadPointsInvalid 13",
                "ReplyTypeWithoutInReplyTo 17",
                "StateModelMissing 17",
            ],
            document.Diagnostics.Skip(6).Select(Describe).Order(StringComparer.Ordinal));

        int count = document.Diagnostics.Count;
        AnnotationWalker.Walk(document);
        Assert.Equal(count, document.Diagnostics.Count);
    }

    [Fact]
    public void Strict_mode_throws_from_the_annotation_list_not_from_open()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-malformed.pdf"), new PdfOptions().UseStrict());
        PdfPage page = document.Pages[0];

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => page.Annotations);

        Assert.Equal("AnnotationEntryInvalid", error.Diagnostic.Code);
    }

    [Fact]
    public void Strict_mode_throws_from_the_property_that_reads_a_malformed_entry()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Square /Rect [0 0 10] >>");
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict());
        PdfAnnotation square = Assert.Single(document.Pages[0].Annotations);

        Assert.Equal("AnnotationRectInvalid", Assert.Throws<DiagnosticException>(() => square.Rect).Diagnostic.Code);
    }

    public static TheoryData<string, string, string> ArrayAndTypeDeviations => new()
    {
        { "/Annots 5 0 R", "AnnotsInvalid", "/Type /Annot /Subtype /Square" },
        { "/Annots [4 0 R]", "AnnotationSubtypeNotName", "/Type /Annot /Subtype (Square)" },
        { "/Annots [4 0 R]", "AnnotationTypeInvalid", "/Type /Annotation /Subtype /Square" },
        { "/Annots [4 0 R]", "AnnotationSubtypeMissing", "/Type /Annot /Subtype <00>" },
    };

    [Theory]
    [MemberData(nameof(ArrayAndTypeDeviations))]
    public void A_deviation_in_the_annots_array_or_the_type_entries_is_recorded_once(string pageEntries, string code, string annotationEntries)
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> {pageEntries} >>",
            $"<< {annotationEntries} /Rect [0 0 10 10] >>",
            "42");
        using PdfDocument document = PdfDocument.Open(file);

        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;
        _ = document.Pages[0].Annotations;

        Assert.Equal(code, Assert.Single(document.Diagnostics).Code);
        Assert.Equal(code == "AnnotsInvalid" ? 0 : 1, annotations.Count);
    }

    [Fact]
    public void A_trap_network_that_is_not_the_last_annotation_is_recorded()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R 5 0 R] >>",
            "<< /Type /Annot /Subtype /TrapNet /Rect [0 0 10 10] /Version [] /AnnotStates [] >>",
            "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] >>");
        using PdfDocument document = PdfDocument.Open(file);

        _ = document.Pages[0].Annotations;

        Assert.Equal("TrapNetPlacementInvalid 4", Describe(Assert.Single(document.Diagnostics)));
    }

    [Fact]
    public void Annots_on_a_page_tree_node_is_used_with_a_diagnostic()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /Annots [4 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.IsType<PdfLinkAnnotation>(Assert.Single(document.Pages[0].Annotations));
        Assert.Equal("AnnotsInherited 3", Describe(Assert.Single(document.Diagnostics)));
    }

    [Fact]
    public void An_appearance_is_required_only_in_a_pdf_2_0_file()
    {
        const string annotation = "<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] >>";
        byte[] legacy = Build("%PDF-1.7", annotation);
        byte[] modern = Build("%PDF-2.0", annotation);

        using PdfDocument before = PdfDocument.Open(legacy);
        using PdfDocument after = PdfDocument.Open(modern);
        Assert.Null(Assert.Single(before.Pages[0].Annotations).AppearanceDictionary);
        Assert.Null(Assert.Single(after.Pages[0].Annotations).AppearanceDictionary);

        Assert.Empty(before.Diagnostics);
        Assert.Contains("AppearanceMissing 4", after.Diagnostics.Select(Describe));

        static byte[] Build(string header, string annotation) => new TestPdf { Header = header, TrailerEntries = "/ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]" }.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
            annotation);
    }

    [Fact]
    public void A_mutation_that_changes_the_annots_array_or_the_subtype_is_seen_on_the_next_read()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-link.pdf"));
        PdfPage page = document.Pages[0];
        PdfAnnotation link = Assert.Single(page.Annotations);

        var annots = (CosArray)document.Resolve(page.Dictionary[new CosName("Annots")]);
        annots.Add(annots[0]);
        Assert.Equal(2, page.Annotations.Count);
        Assert.Same(link, page.Annotations[1]);

        link.Dictionary[new CosName("Subtype")] = new CosName("Square");
        Assert.IsType<PdfSquareAnnotation>(page.Annotations[0]);
    }

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
}
