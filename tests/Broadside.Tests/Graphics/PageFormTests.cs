using System.Text;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Pages made into form XObjects (issue #628): ISO 32000-2 §8.10.1 and §8.10.2 (Table 93), §8.10.3 (groups), §7.7.3.3 and §7.7.3.4
/// (page attributes and inheritance), §14.11.2 (page boundaries) and §8.11.2 (optional content groups).
/// </summary>
public sealed class PageFormTests
{
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName Font = new("Font");
    private static readonly CosName XObject = new("XObject");

    [Fact]
    public void A_page_of_the_same_document_shares_its_resources_and_draws_its_content()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        DrawText(document, page, "Letterhead");

        PdfFormXObject form = document.CreateFormXObject(page);

        Assert.True(form.IsForm);
        Assert.Same(page.Resources, form.Resources);
        Assert.Equal(page.CropBox, form.BoundingBox);
        Assert.Equal(Matrix.Identity, form.Matrix);
        Assert.Contains("(Letterhead) Tj", Encoding.ASCII.GetString(form.Decode().Span), StringComparison.Ordinal);
        Assert.Equal(new CosName("FlateDecode"), form.Dictionary[new CosName("Filter")]);
    }

    [Fact]
    public void A_page_of_another_document_copies_its_resources_and_extracts_the_same_text_after_a_save()
    {
        using PdfDocument source = PdfDocument.Create();
        DrawText(source, source.Pages[0], "Imported text");
        using PdfDocument target = PdfDocument.Create();

        PdfFormXObject form = target.CreateFormXObject(source.Pages[0]);
        using (PdfCanvas canvas = target.Pages[0].CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        Assert.NotSame(source.Pages[0].Resources, form.Resources);
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(target));
        Assert.Contains("Imported text", reopened.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Inherited_resources_are_the_ones_the_form_refers_to()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        DrawText(document, page, "Inherited");
        CosObject resources = page.Dictionary[Resources];
        page.Dictionary.Remove(Resources);
        page.Ancestors!.Dictionary[Resources] = resources;
        Assert.NotNull(page.Resources);

        PdfFormXObject form = document.CreateFormXObject(page);

        Assert.Same(page.Resources, form.Resources);
        Assert.Same(resources, form.Dictionary[Resources]);
    }

    [Theory]
    [InlineData(0, 200, 300)]
    [InlineData(90, 300, 200)]
    [InlineData(180, 200, 300)]
    [InlineData(270, 300, 200)]
    public void The_matrix_shows_the_box_as_a_viewer_does_with_its_lower_left_corner_at_the_origin(int rotation, double width, double height)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(50, 60, 250, 360));
        page.Rotation = rotation;

        PdfFormXObject form = document.CreateFormXObject(page);

        Assert.Equal(new PdfRectangle(0, 0, width, height), Displayed(form));

        // The page's top-left corner is where a viewer shows it after rotating clockwise.
        PathPoint topLeft = form.Matrix.Transform(50, 360);
        PathPoint expected = rotation switch
        {
            0 => new PathPoint(0, height),
            90 => new PathPoint(width, height),
            180 => new PathPoint(width, 0),
            _ => new PathPoint(0, 0),
        };
        Assert.Equal(expected, topLeft);
    }

    [Fact]
    public void Without_rotation_the_form_keeps_the_page_upright_as_stored()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 200, 300));
        page.Rotation = 90;

        PdfFormXObject form = document.CreateFormXObject(page, new PdfPageFormOptions().WithRotation(false));

        Assert.Equal(Matrix.Identity, form.Matrix);
        Assert.Equal(new PdfRectangle(0, 0, 200, 300), Displayed(form));
    }

    [Theory]
    [InlineData(PdfPageBoundary.MediaBox, 0, 0, 600, 800)]
    [InlineData(PdfPageBoundary.CropBox, 10, 10, 590, 790)]
    [InlineData(PdfPageBoundary.BleedBox, 20, 20, 580, 780)]
    [InlineData(PdfPageBoundary.TrimBox, 30, 30, 570, 770)]
    [InlineData(PdfPageBoundary.ArtBox, 40, 40, 560, 760)]
    public void Each_boundary_becomes_the_bounding_box(PdfPageBoundary boundary, double left, double bottom, double right, double top)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 600, 800));
        page.CropBox = new PdfRectangle(10, 10, 590, 790);
        page.BleedBox = new PdfRectangle(20, 20, 580, 780);
        page.TrimBox = new PdfRectangle(30, 30, 570, 770);
        page.ArtBox = new PdfRectangle(40, 40, 560, 760);

        PdfFormXObject form = document.CreateFormXObject(page, new PdfPageFormOptions().WithBoundary(boundary));

        Assert.Equal(new PdfRectangle(left, bottom, right, top), form.BoundingBox);
        Assert.Equal(new PdfRectangle(0, 0, right - left, top - bottom), Displayed(form));
    }

    [Fact]
    public void A_box_beyond_the_media_box_is_intersected_with_it()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 600, 800));
        page.CropBox = new PdfRectangle(-100, 100, 700, 900);

        PdfFormXObject form = document.CreateFormXObject(page);

        Assert.Equal(new PdfRectangle(0, 100, 600, 800), form.BoundingBox);
    }

    [Fact]
    public void The_user_unit_scales_the_form()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages.Add(new PdfRectangle(0, 0, 100, 50));
        page.Dictionary[new CosName("UserUnit")] = new CosReal(2.5);

        PdfFormXObject form = document.CreateFormXObject(page);

        Assert.Equal(new PdfRectangle(0, 0, 250, 125), Displayed(form));
    }

    [Fact]
    public void The_page_group_becomes_the_form_group_in_both_documents()
    {
        using PdfDocument source = PdfDocument.Create();
        PdfTransparencyGroup group = source.Pages[0].CreateTransparencyGroup();
        group.IsIsolated = true;
        using PdfDocument target = PdfDocument.Create();

        PdfFormXObject same = source.CreateFormXObject(source.Pages[0]);
        PdfFormXObject other = target.CreateFormXObject(source.Pages[0]);

        Assert.True(same.TransparencyGroup?.IsIsolated);
        Assert.True(other.TransparencyGroup?.IsIsolated);
        Assert.NotSame(source.Pages[0].Dictionary[new CosName("Group")], other.Dictionary[new CosName("Group")]);
    }

    [Fact]
    public void The_same_page_and_options_give_the_same_form()
    {
        using PdfDocument source = PdfDocument.Create();
        DrawText(source, source.Pages[0], "Once");
        using PdfDocument target = PdfDocument.Create();

        PdfFormXObject first = target.CreateFormXObject(source.Pages[0]);
        PdfFormXObject second = target.CreateFormXObject(source.Pages[0], new PdfPageFormOptions());
        PdfFormXObject media = target.CreateFormXObject(source.Pages[0], new PdfPageFormOptions().WithBoundary(PdfPageBoundary.MediaBox));

        Assert.Same(first, second);
        Assert.NotSame(first, media);
        Assert.Same(first, target.CreateFormXObject(source.Pages[0]));
    }

    [Fact]
    public void Pages_of_one_source_that_share_a_font_copy_it_once()
    {
        using PdfDocument source = PdfDocument.Create();
        source.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        DrawText(source, source.Pages[0], "One");
        DrawText(source, source.Pages[1], "Two");
        Assert.Equal(FontReference(source, source.Pages[0].Resources!), FontReference(source, source.Pages[1].Resources!));
        using PdfDocument target = PdfDocument.Create();

        PdfFormXObject first = target.CreateFormXObject(source.Pages[0]);
        PdfFormXObject second = target.CreateFormXObject(source.Pages[1]);

        Assert.Equal(FontReference(target, first.Resources!), FontReference(target, second.Resources!));
    }

    [Fact]
    public void Printable_annotations_are_drawn_into_the_form_and_others_are_not()
    {
        using PdfDocument source = PdfDocument.Create();
        PdfPage page = source.Pages[0];
        PdfFreeTextAnnotation printed = page.AddFreeTextAnnotation(new PdfRectangle(100, 100, 300, 150), "Printed");
        printed.Flags = PdfAnnotationFlags.Print;
        PdfFreeTextAnnotation screenOnly = page.AddFreeTextAnnotation(new PdfRectangle(100, 200, 300, 250), "Screen");
        screenOnly.Flags = PdfAnnotationFlags.None;
        using PdfDocument target = PdfDocument.Create();

        PdfFormXObject without = target.CreateFormXObject(page);
        PdfFormXObject with = target.CreateFormXObject(page, new PdfPageFormOptions().WithAnnotations());

        Assert.DoesNotContain("Do", Encoding.ASCII.GetString(without.Decode().Span), StringComparison.Ordinal);
        string content = Encoding.ASCII.GetString(with.Decode().Span);
        Assert.Single(content.Split("Do").Skip(1));
        CosDictionary forms = Assert.IsType<CosDictionary>(target.Resolve(with.Resources![XObject]));
        Assert.Single(forms);
        Assert.Equal(2, page.Annotations.Count);
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(Drawn(target, with)));
        Assert.Contains("Printed", reopened.Pages[0].ExtractText().Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Screen", reopened.Pages[0].ExtractText().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Optional_content_groups_are_copied_and_listed_in_the_target()
    {
        using PdfDocument source = PdfDocument.Create();
        PdfOptionalContentGroup shown = source.CreateOptionalContentGroup("Shown");
        PdfOptionalContentGroup hidden = source.CreateOptionalContentGroup("Hidden", visible: false);
        using (PdfCanvas canvas = source.Pages[0].CreateCanvas())
        {
            foreach (PdfOptionalContentGroup group in new[] { shown, hidden })
            {
                canvas.BeginOptionalContent(group);
                canvas.Rectangle(10, 10, 10, 10);
                canvas.Fill();
                canvas.EndMarkedContent();
            }
        }

        using PdfDocument target = PdfDocument.Create();
        target.CreateFormXObject(source.Pages[0]);

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(target));
        PdfOptionalContentProperties content = Assert.IsType<PdfOptionalContentProperties>(reopened.OptionalContent);
        Assert.Equal(["Shown", "Hidden"], content.Groups.Select(group => group.Name));
        PdfOptionalContentState state = content.GetDefaultStates();
        Assert.True(state.IsOn(content.Groups[0]));
        Assert.False(state.IsOn(content.Groups[1]));
    }

    [Fact]
    public void Structure_ties_are_left_out_by_default_and_kept_on_request()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        page.Dictionary[new CosName("StructParents")] = new CosInteger(4);
        SetContent(document, page, "/P <</MCID 0 /Lang (en)>> BDC 0 0 1 1 re f EMC");

        PdfFormXObject plain = document.CreateFormXObject(page);
        PdfFormXObject kept = document.CreateFormXObject(page, new PdfPageFormOptions().WithStructure());

        Assert.Null(plain.StructParents);
        string content = Encoding.ASCII.GetString(plain.Decode().Span);
        Assert.DoesNotContain("MCID", content, StringComparison.Ordinal);
        Assert.Contains("/Lang", content, StringComparison.Ordinal);
        Assert.Equal(4, kept.StructParents);
        Assert.Contains("/MCID 0", Encoding.ASCII.GetString(kept.Decode().Span), StringComparison.Ordinal);
    }

    [Fact]
    public void Content_streams_are_joined_and_unbalanced_state_is_closed()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        CosReference first = document.AddObject(new CosStream(new CosDictionary(), "Q q q 1 0 0 1 5 5 cm"u8.ToArray()));
        CosReference second = document.AddObject(new CosStream(new CosDictionary(), "BT /F1 1 Tf"u8.ToArray()));
        page.Dictionary[KnownNames.Contents] = new CosArray { first, second };

        string content = Encoding.ASCII.GetString(document.CreateFormXObject(page).Decode().Span);

        Assert.Equal("q\nQ q q 1 0 0 1 5 5 cm\nBT /F1 1 Tf\nET\nQ\nQ\n", content);
    }

    [Fact]
    public void An_empty_page_gives_a_form_with_no_content()
    {
        using PdfDocument document = PdfDocument.Create();

        PdfFormXObject form = document.CreateFormXObject(document.Pages[0]);

        Assert.Equal(0, form.Decode().Length);
        Assert.False(form.Dictionary.ContainsKey(new CosName("Filter")));
    }

    [Fact]
    public void A_content_stream_with_a_filter_the_engine_does_not_decode_throws_and_changes_nothing()
    {
        using PdfDocument source = PdfDocument.Create();
        source.Pages[0].Dictionary[KnownNames.Contents] = source.AddObject(
            new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("NoSuchDecode") }, "x"u8.ToArray()));
        using PdfDocument target = PdfDocument.Create();
        int count = target.EnumerateObjectReferences().Count();

        Assert.Throws<NotSupportedException>(() => target.CreateFormXObject(source.Pages[0]));
        Assert.Equal(count, target.EnumerateObjectReferences().Count());
    }

    [Fact]
    public void A_page_drawn_through_its_form_on_itself_shows_the_snapshot()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        DrawText(document, page, "Before");
        PdfFormXObject form = document.CreateFormXObject(page);
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.Transform(new Matrix(0.5, 0, 0, 0.5, 0, 0));
            canvas.DrawForm(form);
        }

        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        string text = reopened.Pages[0].ExtractText().Text;
        Assert.Equal(2, text.Split("Before").Length - 1);
        Assert.Empty(reopened.Diagnostics);
    }

    [Theory]
    [InlineData("page-rotation.pdf")]
    [InlineData("transparency-groups.pdf")]
    [InlineData("pattern-in-form.pdf")]
    [InlineData("optional-content.pdf")]
    public void Imported_corpus_pages_round_trip_with_no_new_diagnostics(string fileName)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Bytes(fileName));
        int before = source.Diagnostics.Count;
        using PdfDocument target = PdfDocument.Create();
        foreach (PdfPage page in source.Pages)
        {
            PdfFormXObject form = target.CreateFormXObject(page);
            PdfRectangle box = Displayed(form);
            using PdfCanvas canvas = target.Pages.Add(box).CreateCanvas();
            canvas.DrawForm(form);
        }

        Assert.Equal(before, source.Diagnostics.Count);
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(target));
        foreach (PdfPage page in reopened.Pages)
        {
            page.ProcessContent(new NothingProcessor());
        }

        Assert.Empty(reopened.Diagnostics);
    }

    private sealed class NothingProcessor : ContentProcessor
    {
        public override ContentEvents Events => ContentEvents.All;
    }

    /// <summary>The form's bounding box through its matrix: where it lands in the user space that draws it.</summary>
    internal static PdfRectangle Displayed(PdfFormXObject form)
    {
        PdfRectangle box = form.BoundingBox!.Value;
        PathPoint a = form.Matrix.Transform(box.Left, box.Bottom);
        PathPoint b = form.Matrix.Transform(box.Right, box.Top);
        return new PdfRectangle(Math.Round(a.X, 9), Math.Round(a.Y, 9), Math.Round(b.X, 9), Math.Round(b.Y, 9));
    }

    private static PdfDocument Drawn(PdfDocument target, PdfFormXObject form)
    {
        using PdfCanvas canvas = target.Pages[0].CreateCanvas();
        canvas.DrawForm(form);
        return target;
    }

    private static void DrawText(PdfDocument document, PdfPage page, string text)
    {
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.SetFillColor(PdfColor.FromRgb(1, 0, 0));
        canvas.Rectangle(50, 50, 100, 100);
        canvas.Fill();
        canvas.BeginText();
        canvas.SetFont(document.GetStandard14Font(Standard14Font.Helvetica), 12);
        canvas.MoveText(72, 700);
        canvas.ShowText(text);
        canvas.EndText();
    }

    private static void SetContent(PdfDocument document, PdfPage page, string content) =>
        page.Dictionary[KnownNames.Contents] = document.AddObject(new CosStream(new CosDictionary(), Encoding.ASCII.GetBytes(content)));

    private static CosObject FontReference(PdfDocument document, CosDictionary resources) =>
        Assert.Single(Assert.IsType<CosDictionary>(document.Resolve(resources[Font]))).Value;
}
