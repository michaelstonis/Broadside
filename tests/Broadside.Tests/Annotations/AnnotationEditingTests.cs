using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Graphics;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>Adding, editing and removing annotations through the page (issue #165). ISO 32000-2 §12.5.</summary>
public sealed class AnnotationEditingTests
{
    private static readonly PdfQuadrilateral Words = new(new PathPoint(100, 520), new PathPoint(220, 520), new PathPoint(100, 500), new PathPoint(220, 500));

    /// <summary>One creation per subtype the ticket lists, with the type it re-reads as.</summary>
    public static TheoryData<string> Kinds => [.. Creations.Keys];

    private static Dictionary<string, (Func<PdfPage, PdfAnnotation> Add, Type Type)> Creations { get; } = new()
    {
        ["Text"] = (page => page.AddTextAnnotation(new PdfRectangle(50, 700, 70, 720), "A note"), typeof(PdfTextAnnotation)),
        ["Link"] = (page => page.AddLinkAnnotation(new PdfRectangle(50, 650, 200, 670), new Uri("https://example.com/")), typeof(PdfLinkAnnotation)),
        ["FreeText"] = (page => page.AddFreeTextAnnotation(new PdfRectangle(300, 650, 500, 720), "Free text that wraps over more than one line"), typeof(PdfFreeTextAnnotation)),
        ["Line"] = (page => page.AddLineAnnotation(new PathPoint(50, 600), new PathPoint(250, 560)), typeof(PdfLineAnnotation)),
        ["Square"] = (page => page.AddSquareAnnotation(new PdfRectangle(300, 540, 400, 620)), typeof(PdfSquareAnnotation)),
        ["Circle"] = (page => page.AddCircleAnnotation(new PdfRectangle(420, 540, 540, 620)), typeof(PdfCircleAnnotation)),
        ["Polygon"] = (page => page.AddPolygonAnnotation([new PathPoint(50, 400), new PathPoint(150, 400), new PathPoint(100, 480)]), typeof(PdfPolygonAnnotation)),
        ["PolyLine"] = (page => page.AddPolyLineAnnotation([new PathPoint(200, 400), new PathPoint(250, 480), new PathPoint(300, 400)]), typeof(PdfPolyLineAnnotation)),
        ["Ink"] = (page => page.AddInkAnnotation([[new PathPoint(350, 400), new PathPoint(400, 450), new PathPoint(450, 400)], [new PathPoint(350, 470), new PathPoint(450, 470)]]), typeof(PdfInkAnnotation)),
        ["Highlight"] = (page => page.AddHighlightAnnotation([Words]), typeof(PdfHighlightAnnotation)),
        ["Underline"] = (page => page.AddUnderlineAnnotation([Shifted(Words, -40)]), typeof(PdfUnderlineAnnotation)),
        ["Squiggly"] = (page => page.AddSquigglyAnnotation([Shifted(Words, -80)]), typeof(PdfSquigglyAnnotation)),
        ["StrikeOut"] = (page => page.AddStrikeOutAnnotation([Shifted(Words, -120)]), typeof(PdfStrikeOutAnnotation)),
        ["Stamp"] = (page => page.AddStampAnnotation(new PdfRectangle(300, 250, 500, 300), "NotApproved"), typeof(PdfStampAnnotation)),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_created_subtype_rereads_as_its_typed_class_with_an_appearance(string kind)
    {
        (Func<PdfPage, PdfAnnotation> add, Type type) = Creations[kind];
        using PdfDocument document = PdfDocument.Create();
        PdfAnnotation created = add(document.Pages[0]);
        Assert.IsType(type, created);
        Assert.Same(created, Assert.Single(document.Pages[0].Annotations));

        using PdfDocument reopened = Strict.Open(CanvasTests.Saved(document));
        PdfAnnotation annotation = Assert.Single(reopened.Pages[0].Annotations);
        Assert.IsType(type, annotation);
        PdfFormXObject appearance = Assert.IsType<PdfFormXObject>(annotation.GetAppearance());
        Assert.NotNull(appearance.BoundingBox);
        Assert.NotNull(annotation.GetAppearanceMatrix(appearance));
        Assert.Same(reopened.Pages[0].Dictionary, annotation.PageObject);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Every_created_subtype_on_one_page_passes_qpdf()
    {
        using PdfDocument document = PdfDocument.Create();
        foreach ((Func<PdfPage, PdfAnnotation> add, _) in Creations.Values)
        {
            add(document.Pages[0]);
        }

        (int exitCode, string output) = ExternalTool.Run("qpdf", CanvasTests.Saved(document), "--check");
        Assert.True(exitCode == 0, output);
    }


    [Fact]
    public void Changing_a_squares_colour_and_regenerating_draws_the_new_colour()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfSquareAnnotation square = document.Pages[0].AddSquareAnnotation(new PdfRectangle(100, 100, 200, 200));
        CosStream before = square.GetAppearance()!.Stream;
        Assert.Contains("1 0 0 RG", Content(square));

        square.Color = new PdfDeviceColor(0, 0, 1);
        Assert.Same(before, square.GetAppearance()!.Stream);

        Assert.True(square.RegenerateAppearance());
        Assert.NotSame(before, square.GetAppearance()!.Stream);
        Assert.Contains("0 0 1 RG", Content(square));

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var read = Assert.IsType<PdfSquareAnnotation>(Assert.Single(reopened.Pages[0].Annotations));
        Assert.Equal(new PdfDeviceColor(0, 0, 1), read.Color);
        Assert.Contains("0 0 1 RG", Content(read));
    }

    [Fact]
    public void Editing_entries_keeps_the_appearance_of_an_annotation_from_a_file()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        var square = document.Pages[0].Annotations.OfType<PdfSquareAnnotation>().Single();
        CosStream appearance = square.GetAppearance()!.Stream;

        square.Contents = "Edited";
        square.Title = "Reviewer";
        square.Subject = "Checked";
        square.Color = new PdfDeviceColor(0.5);
        square.InteriorColor = null;
        square.Flags = PdfAnnotationFlags.Print | PdfAnnotationFlags.Locked;
        square.StrokingOpacity = 0.5;

        Assert.Same(appearance, square.GetAppearance()!.Stream);
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var read = reopened.Pages[0].Annotations.OfType<PdfSquareAnnotation>().Single();
        Assert.Equal(("Edited", "Reviewer", "Checked"), (read.Contents, read.Title, read.Subject));
        Assert.Equal(new PdfDeviceColor(0.5), read.Color);
        Assert.Null(read.InteriorColor);
        Assert.Equal(PdfAnnotationFlags.Print | PdfAnnotationFlags.Locked, read.Flags);
        Assert.Equal(0.5, read.StrokingOpacity);
        Assert.Equal(Content(square), Content(read));
    }

    [Fact]
    public void An_annotation_of_a_subtype_without_a_generator_is_not_regenerated()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        PdfAnnotation caret = document.Pages[0].Annotations.Single(annotation => annotation.Kind == PdfAnnotationKind.Caret);
        CosObject? before = caret.Dictionary.TryGetValue(new CosName("AP"), out CosObject? value) ? value : null;
        Assert.False(caret.RegenerateAppearance());
        Assert.Same(before, caret.Dictionary.TryGetValue(new CosName("AP"), out CosObject? after) ? after : null);
    }

    [Fact]
    public void Moving_an_ink_annotation_moves_its_points_and_rectangle_and_keeps_its_appearance()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfInkAnnotation ink = document.Pages[0].AddInkAnnotation([[new PathPoint(100, 100), new PathPoint(150, 120)]]);
        PdfRectangle rect = ink.Rect;
        CosStream appearance = ink.GetAppearance()!.Stream;

        ink.Move(10, -20);

        Assert.Equal(new PdfRectangle(rect.Left + 10, rect.Bottom - 20, rect.Right + 10, rect.Top - 20), ink.Rect);
        Assert.Equal([new PathPoint(110, 80), new PathPoint(160, 100)], ink.InkList[0]);
        Assert.Same(appearance, ink.GetAppearance()!.Stream);
        Assert.Equal(new Matrix(1, 0, 0, 1, 10, -20), ink.GetAppearanceMatrix(ink.GetAppearance()!));
    }

    [Fact]
    public void Removing_an_annotation_removes_its_popup_and_replies_and_leaves_the_rest_byte_identical_under_incremental_save()
    {
        byte[] original = Corpus.Bytes("annotations-subtypes.pdf");
        using PdfDocument document = PdfDocument.Open(original);
        PdfPage page = document.Pages[0];
        int count = page.Annotations.Count;
        var note = page.Annotations.OfType<PdfTextAnnotation>().First(text => text.Popup is not null);
        CosDictionary popup = note.Popup!.Dictionary;
        CosDictionary reply = page.Annotations.OfType<PdfMarkupAnnotation>().Single(markup => markup.InReplyTo?.Dictionary == note.Dictionary).Dictionary;

        page.RemoveAnnotation(note);

        Assert.Equal(count - 3, page.Annotations.Count);
        Assert.DoesNotContain(page.Annotations, annotation => annotation.Dictionary == note.Dictionary || annotation.Dictionary == popup || annotation.Dictionary == reply);
        byte[] saved = CanvasTests.Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));
        Assert.Equal(original, saved.AsSpan(0, original.Length).ToArray());
        string update = System.Text.Encoding.Latin1.GetString(saved, original.Length, saved.Length - original.Length);
        // The update rewrites the page (its direct Annots) and the save's own dating, but no annotation.
        int[] written = [.. System.Text.RegularExpressions.Regex.Matches(update, @"(\d+) 0 obj").Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Contains(page.Reference!.ObjectNumber, written);
        Assert.DoesNotContain(written, number => page.Annotations.Any(annotation => annotation.Reference!.ObjectNumber == number) || number == note.Reference!.ObjectNumber);

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(count - 3, reopened.Pages[0].Annotations.Count);
        Assert.Equal(
            page.Annotations.Select(annotation => annotation.Reference!.ObjectNumber),
            reopened.Pages[0].Annotations.Select(annotation => annotation.Reference!.ObjectNumber));
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
    }

    [Fact]
    public void Removing_a_popup_alone_removes_it_from_its_parent()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        PdfPage page = document.Pages[0];
        var popup = page.Annotations.OfType<PdfPopupAnnotation>().First();
        var parent = (PdfMarkupAnnotation)popup.Parent!;

        page.RemoveAnnotation(popup);

        Assert.Null(parent.Popup);
        Assert.Contains(page.Annotations, annotation => annotation.Dictionary == parent.Dictionary);
    }

    [Fact]
    public void Removing_a_tagged_annotation_removes_its_object_reference_and_parent_tree_entry()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        PdfSquareAnnotation square = page.AddSquareAnnotation(new PdfRectangle(10, 10, 50, 50));
        Broadside.Structure.PdfStructureTreeRoot tree = document.CreateStructureTree();
        Broadside.Structure.PdfStructureElement annot = tree.AddElement("Annot");
        annot.AddObjectReference(square.Reference!, page);
        Assert.NotNull(square.StructParent);

        page.RemoveAnnotation(square);

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        Broadside.Structure.PdfStructureElement element = Assert.Single(reopened.StructureTree!.Children.OfType<Broadside.Structure.PdfStructureElement>());
        Assert.Empty(element.Children);
        Assert.Empty(reopened.Pages[0].Annotations);
    }

    [Fact]
    public void Refusals_change_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfPage page = document.Pages[0];
        string before = CosSnapshot.Of(document);
        Assert.Throws<InvalidOperationException>(() => page.RemoveAnnotation(page.Annotations.First(annotation => annotation.Kind == PdfAnnotationKind.Widget)));
        Assert.Throws<ArgumentException>(() => page.AddPolygonAnnotation([new PathPoint(0, 0), new PathPoint(1, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.AddSquareAnnotation(new PdfRectangle(0, 0, double.NaN, 1)));
        Assert.Throws<InvalidOperationException>(() => page.AddSquareAnnotation(new PdfRectangle(5, 5, 5, 9)));
        Assert.Equal(before, CosSnapshot.Of(document));
    }

    private static string Content(PdfAnnotation annotation) => System.Text.Encoding.Latin1.GetString(annotation.GetAppearance()!.Decode().Span);

    private static readonly PdfEngine Strict = new(new PdfOptions().UseStrict());

    private static PdfQuadrilateral Shifted(PdfQuadrilateral quad, double dy) => new(
        new PathPoint(quad.Point1.X, quad.Point1.Y + dy),
        new PathPoint(quad.Point2.X, quad.Point2.Y + dy),
        new PathPoint(quad.Point3.X, quad.Point3.Y + dy),
        new PathPoint(quad.Point4.X, quad.Point4.Y + dy));
}
