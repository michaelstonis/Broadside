using Broadside.Annotations;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.Structure;
using Broadside.Tests.Forms;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Tagged content, artifacts and optional content written through <see cref="PdfCanvas"/>, saved, reopened and read back through
/// the structure tree, text extraction, the display list and poppler. ISO 32000-2 §14.6, §14.7.5, §14.8.2.2 and §8.11.
/// </summary>
public sealed class CanvasTaggingTests
{
    [Fact]
    public void A_tagged_paragraph_drawn_on_two_pages_re_reads_as_one_element_with_two_mcid_kids()
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        PdfStructureTreeRoot root = document.CreateStructureTree();
        PdfStructureElement body = root.AddElement("Document");
        PdfStructureElement paragraph = body.AddElement("P");
        int[] mcids = new int[2];
        for (int index = 0; index < 2; index++)
        {
            using PdfCanvas canvas = document.Pages[index].CreateCanvas();
            canvas.BeginArtifact(PdfArtifactType.Pagination, "Header", PdfArtifactEdges.Top, new PdfRectangle(72, 740, 300, 760));
            Show(canvas, font, $"Running head {index + 1}", 745);
            canvas.EndMarkedContent();
            PdfMarkedContentReference item = canvas.BeginTaggedContent(paragraph);
            mcids[index] = item.Mcid;
            Show(canvas, font, index == 0 ? "First half" : "second half", 600);
            canvas.EndMarkedContent();
        }

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfStructureTreeRoot tree = reopened.StructureTree!;
        PdfStructureElement p = Assert.IsType<PdfStructureElement>(Assert.Single(tree.Children[0].Children));
        Assert.Equal("P", p.StructureType!.Value);
        PdfMarkedContentReference[] kids = [.. p.Children.OfType<PdfMarkedContentReference>()];
        Assert.Equal(2, kids.Length);
        Assert.Equal(mcids, kids.Select(kid => kid.Mcid));
        Assert.Equal([reopened.Pages[0].Reference, reopened.Pages[1].Reference], kids.Select(kid => kid.Page!.Reference));
        for (int index = 0; index < 2; index++)
        {
            PdfStructureElement owner = reopened.StructureTree!.GetMarkedContentElements(reopened.Pages[index])[mcids[index]];
            Assert.Equal("P", owner.StructureType!.Value);
        }

        Assert.Equal("First half", reopened.Pages[0].ExtractText().PlainText.Trim());
        Assert.Equal("second half", reopened.Pages[1].ExtractText().PlainText.Trim());
    }

    [Fact]
    public void Artifacts_re_read_as_artifacts_with_their_type_subtype_edges_and_box()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginArtifact(PdfArtifactType.Pagination, "Footer", PdfArtifactEdges.Bottom, new PdfRectangle(72, 30, 540, 50));
            Show(canvas, font, "Page 1", 36);
            canvas.EndMarkedContent();
            canvas.BeginArtifact(PdfArtifactType.Layout);
            canvas.Rectangle(72, 100, 400, 1);
            canvas.Fill();
            canvas.EndMarkedContent();
            canvas.BeginArtifact();
            canvas.EndMarkedContent();
            canvas.BeginArtifact(PdfArtifactType.Background, null, PdfArtifactEdges.Top | PdfArtifactEdges.Bottom | PdfArtifactEdges.Left | PdfArtifactEdges.Right, null);
            canvas.EndMarkedContent();
        }

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        var recorder = new PropertyListRecorder(reopened);
        reopened.Pages[0].ProcessContent(recorder);
        Assert.Equal(4, recorder.Lists.Count);
        Assert.All(recorder.Lists, list => Assert.True(list.IsArtifact));
        Assert.Equal(["Pagination", "Layout", null, "Background"], recorder.Lists.Select(list => list.ArtifactType));
        Assert.Equal("Footer", recorder.Lists[0].ArtifactSubtype);
        Assert.Equal(["Bottom"], recorder.Lists[0].ArtifactAttachments);
        Assert.Equal(new PdfRectangle(72, 30, 540, 50), recorder.Lists[0].ArtifactBoundingBox);
        Assert.Equal(["Top", "Bottom", "Left", "Right"], recorder.Lists[3].ArtifactAttachments);
        Assert.Equal(string.Empty, reopened.Pages[0].ExtractText().PlainText.Trim());
    }

    [Fact]
    public void Attached_edges_on_a_layout_artifact_or_an_empty_subtype_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        Assert.Throws<ArgumentException>(() => canvas.BeginArtifact(PdfArtifactType.Layout, null, PdfArtifactEdges.Top, null));
        Assert.Throws<ArgumentException>(() => canvas.BeginArtifact(PdfArtifactType.Pagination, string.Empty, PdfArtifactEdges.None, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.BeginArtifact((PdfArtifactType)42));
        Assert.Equal(0, canvas.MarkedContentDepth);
    }

    [Fact]
    public void A_pdf_2_0_artifact_subtype_raises_the_document_to_pdf_2_0()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginArtifact(PdfArtifactType.Pagination, "PageNum", PdfArtifactEdges.Bottom, null);
            canvas.Rectangle(0, 0, 1, 1);
            canvas.Fill();
            canvas.EndMarkedContent();
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        Assert.Equal(new PdfVersion(2, 0), reopened.Version);
    }

    [Fact]
    public void A_written_optional_content_group_hides_its_content_under_the_off_configuration()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.4"));
        PdfOptionalContentGroup shown = document.CreateOptionalContentGroup("Shown");
        PdfOptionalContentGroup hidden = document.CreateOptionalContentGroup("Hidden", visible: false);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginOptionalContent(shown);
            canvas.Rectangle(100, 600, 100, 100);
            canvas.Fill();
            canvas.EndMarkedContent();
            canvas.BeginOptionalContent(hidden);
            canvas.Rectangle(300, 600, 100, 100);
            canvas.Fill();
            canvas.EndMarkedContent();
        }

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        Assert.Equal(10000, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(100, 600, 200, 700)));
        Assert.Equal(0, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(300, 600, 400, 700)));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(new PdfVersion(1, 5), reopened.Version);
        PdfOptionalContentProperties properties = reopened.OptionalContent!;
        Assert.Equal(["Shown", "Hidden"], properties.Groups.Select(group => group.Name));
        Assert.Equal(["Hidden"], properties.DefaultConfiguration.Off.Select(group => group.Name));
        Assert.Single(Fills(reopened.Pages[0].CreateDisplayList()));
        PdfOptionalContentState allOn = properties.GetDefaultStates().Apply(new CosArray { new CosName("ON"), properties.Groups[1].Reference! });
        Assert.Equal(2, Fills(reopened.Pages[0].CreateDisplayList(new ContentOptions().WithOptionalContentState(allOn))).Count);
    }

    [Fact]
    public void Membership_dictionaries_by_policy_and_by_visibility_expression_decide_visibility()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfOptionalContentGroup on = document.CreateOptionalContentGroup("On");
        PdfOptionalContentGroup off = document.CreateOptionalContentGroup("Off", visible: false);
        PdfOptionalContentMembership anyOn = document.CreateOptionalContentMembership([on, off], PdfVisibilityPolicy.AnyOn);
        PdfOptionalContentMembership allOn = document.CreateOptionalContentMembership([on, off], PdfVisibilityPolicy.AllOn);
        PdfOptionalContentMembership notOff = document.CreateOptionalContentMembership(
            PdfVisibilityExpression.And(PdfVisibilityExpression.Of(on), PdfVisibilityExpression.Not(PdfVisibilityExpression.Of(off))));
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            double x = 50;
            foreach (PdfOptionalContentMembership membership in new[] { anyOn, allOn, notOff })
            {
                canvas.BeginOptionalContent(membership);
                canvas.Rectangle(x, 600, 50, 50);
                canvas.Fill();
                canvas.EndMarkedContent();
                x += 100;
            }
        }

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        Assert.Equal(2500, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(50, 600, 100, 650)));
        Assert.Equal(0, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(150, 600, 200, 650)));
        Assert.Equal(2500, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(250, 600, 300, 650)));

        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal(2, Fills(reopened.Pages[0].CreateDisplayList()).Count);
        PdfOptionalContentMembership expression = reopened.OptionalContent!.FindMembership(((CosDictionary)reopened.Resolve(reopened.Pages[0].Resources![new CosName("Properties")])).Values.Last())!;
        Assert.Equal("And(On, Not(Off))", expression.VisibilityExpression!.ToString());
    }

    [Fact]
    public void Forms_images_and_annotations_take_an_optional_content_entry()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfOptionalContentGroup off = document.CreateOptionalContentGroup("Off", visible: false);
        PdfOptionalContentGroup on = document.CreateOptionalContentGroup("On");
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 50, 50));
        using (PdfCanvas inside = form.CreateCanvas())
        {
            inside.Rectangle(0, 0, 50, 50);
            inside.Fill();
        }

        form.SetOptionalContent(off);
        PdfImage image = document.CreateImage([0], new PdfImageLayout(1, 1, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);
        image.SetOptionalContent(document.CreateOptionalContentMembership([off], PdfVisibilityPolicy.AllOn));
        PdfAnnotation note = document.Pages[0].AddTextAnnotation(new PdfRectangle(400, 400, 420, 420), "note");
        note.SetOptionalContent(off);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SaveState();
            canvas.Transform(new Matrix(1, 0, 0, 1, 100, 600));
            canvas.DrawForm(form);
            canvas.RestoreState();
            canvas.DrawImage(image, 300, 600, 50, 50);
        }

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        Assert.Equal(0, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(100, 600, 150, 650)));
        Assert.Equal(0, TextFieldFillTests.DarkPixels(saved, new PdfRectangle(300, 600, 350, 650)));

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfOptionalContentProperties properties = reopened.OptionalContent!;
        PdfOptionalContentState states = properties.GetDefaultStates();
        Assert.False(reopened.Pages[0].Annotations[0].IsVisible(states));
        Assert.Equal("Off", properties.FindGroup(reopened.Pages[0].Annotations[0].OptionalContent)!.Name);

        form.SetOptionalContent(on);
        Assert.Same(on.Dictionary, form.OptionalContent);
        form.RemoveOptionalContent();
        Assert.Null(form.OptionalContent);
    }

    [Fact]
    public void A_form_drawn_on_a_page_takes_its_own_structural_parent_key()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfStructureElement figure = document.CreateStructureTree().AddElement("Figure");
        PdfFormXObject form = document.CreateFormXObject(new PdfRectangle(0, 0, 50, 50));
        using (PdfCanvas inside = form.CreateCanvas())
        {
            Assert.Throws<InvalidOperationException>(() => inside.BeginTaggedContent(figure));
            PdfMarkedContentReference item = inside.BeginTaggedContent(figure, document.Pages[0], null);
            Assert.Equal(0, item.Mcid);
            inside.Rectangle(0, 0, 50, 50);
            inside.Fill();
            inside.EndMarkedContent();
        }

        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        byte[] saved = Saved(document);
        AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfStructureElement read = Assert.IsType<PdfStructureElement>(Assert.Single(reopened.StructureTree!.Children));
        PdfMarkedContentReference kid = Assert.IsType<PdfMarkedContentReference>(Assert.Single(read.Children));
        Assert.NotNull(kid.ContentStream);
        Assert.Equal("Figure", reopened.StructureTree.GetMarkedContentElements(kid.ContentStream!)[0].StructureType!.Value);
    }

    [Fact]
    public void Tagged_content_carries_extra_properties_and_refuses_an_mcid_or_another_documents_element()
    {
        using PdfDocument other = PdfDocument.Create();
        PdfStructureElement foreign = other.CreateStructureTree().AddElement("P");
        using PdfDocument document = PdfDocument.Create();
        PdfStructureElement span = document.CreateStructureTree().AddElement("Span");
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            Assert.Throws<ArgumentException>(() => canvas.BeginTaggedContent(foreign));
            Assert.Throws<ArgumentException>(() => canvas.BeginTaggedContent(span, new CosDictionary { [new CosName("MCID")] = new CosInteger(9) }));
            Assert.Throws<ArgumentException>(() => canvas.BeginTaggedContent(span, document.Pages.Add(new PdfRectangle(0, 0, 10, 10)), null));
            Assert.Throws<ArgumentException>(() => canvas.BeginMarkedContent("Span", new CosDictionary { [new CosName("MCID")] = new CosInteger(0) }));
            canvas.BeginTaggedContent(span, new CosDictionary { [new CosName("ActualText")] = new CosString("Hi"u8.ToArray()) });
            canvas.EndMarkedContent();
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        var recorder = new PropertyListRecorder(reopened);
        reopened.Pages[0].ProcessContent(recorder);
        PdfPropertyList list = Assert.Single(recorder.Lists);
        Assert.Equal("Hi", list.ActualText);
        Assert.Equal(0, list.Mcid);
    }

    [Fact]
    public void Optional_content_of_another_document_is_refused()
    {
        using PdfDocument other = PdfDocument.Create();
        PdfOptionalContentGroup foreign = other.CreateOptionalContentGroup("Foreign");
        using PdfDocument document = PdfDocument.Create();
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        Assert.Throws<ArgumentException>(() => canvas.BeginOptionalContent(foreign));
        Assert.Throws<ArgumentException>(() => document.CreateOptionalContentMembership([foreign], PdfVisibilityPolicy.AnyOn));
        Assert.Throws<ArgumentException>(() => document.CreateFormXObject(new PdfRectangle(0, 0, 1, 1)).SetOptionalContent(foreign));
    }

    private static List<string> Fills(DisplayList list)
    {
        var recorder = new RecordingBackend();
        list.Replay(recorder);
        return [.. recorder.Operations.Where(operation => operation.StartsWith("fill", StringComparison.Ordinal))];
    }

    private static void Show(PdfCanvas canvas, PdfCanvasFont font, string text, double y)
    {
        canvas.BeginText();
        canvas.SetFont(font, 10);
        canvas.SetTextMatrix(new Matrix(1, 0, 0, 1, 72, y));
        canvas.ShowText(text);
        canvas.EndText();
    }

    private static byte[] Saved(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static void AssertQpdfPasses(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("qpdf", file, "--check");
        Assert.True(exitCode == 0, output);
    }

    private sealed class PropertyListRecorder(PdfDocument document) : ContentProcessor
    {
        public List<PdfPropertyList> Lists { get; } = [];

        public override ContentEvents Events => ContentEvents.MarkedContent;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            var tag = new CosName(System.Text.Encoding.ASCII.GetString(mc.Tag));
            CosDictionary? properties = mc.Properties ?? (mc.InlineProperties.Kind == ContentOperandKind.Dictionary ? mc.InlineProperties.ToCosObject() as CosDictionary : null);
            Lists.Add(new PdfPropertyList(tag, properties, document));
        }
    }
}
