using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Text;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Marked-content sequences written through <see cref="PdfCanvas"/>: tags, inline property lists, nesting with text objects, and
/// artifacts left out of text extraction. ISO 32000-2 §14.6.1 (Table 352), §14.6.2 and §14.8.2.2.2 (Table 363).
/// </summary>
public sealed class CanvasMarkedContentTests
{
    [Fact]
    public void An_artifact_with_a_property_list_reads_back_typed_and_its_text_is_left_out_of_extraction()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginMarkedContent("Artifact", new CosDictionary
            {
                [new CosName("Type")] = new CosName("Pagination"),
                [new CosName("Subtype")] = new CosName("Footer"),
            });
            Assert.Equal(1, canvas.MarkedContentDepth);
            Show(canvas, font, "Page 1", 40);
            canvas.EndMarkedContent();
            canvas.BeginMarkedContent("Span");
            Show(canvas, font, "Body", 400);
            canvas.EndMarkedContent();
            Assert.Equal(0, canvas.MarkedContentDepth);
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        var recorder = new Recorder(reopened);
        reopened.Pages[0].ProcessContent(recorder);
        Assert.Equal(["Artifact Pagination Footer", "Span"], recorder.Sequences);
        Assert.Equal("Body", reopened.Pages[0].ExtractText().PlainText.Trim());
        Assert.Contains("Page 1", reopened.Pages[0].ExtractText(new TextExtractionOptions().WithArtifacts()).PlainText);
    }

    [Fact]
    public void A_sequence_inside_a_text_object_shall_end_inside_it_and_one_outside_shall_not_end_inside_one()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        canvas.BeginText();
        canvas.BeginMarkedContent("Span");
        Assert.Throws<InvalidOperationException>(canvas.EndText);
        canvas.EndMarkedContent();
        canvas.EndText();

        canvas.BeginMarkedContent("P");
        canvas.BeginText();
        Assert.Throws<InvalidOperationException>(canvas.EndMarkedContent);
        canvas.EndText();
        canvas.EndMarkedContent();
        Assert.Throws<InvalidOperationException>(canvas.EndMarkedContent);
    }

    [Fact]
    public void A_property_list_holding_an_indirect_reference_or_an_empty_tag_is_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        var properties = new CosDictionary { [new CosName("Ref")] = document.AddObject(new CosInteger(1)) };

        Assert.Throws<ArgumentException>(() => canvas.BeginMarkedContent("Artifact", properties));
        Assert.Throws<ArgumentException>(() => canvas.BeginMarkedContent(string.Empty));
        Assert.Equal(0, canvas.MarkedContentDepth);
    }

    [Fact]
    public void Disposing_closes_open_sequences_inside_the_text_object_before_ending_it()
    {
        using PdfDocument document = PdfDocument.Create();
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.BeginMarkedContent("Artifact");
            canvas.BeginText();
            canvas.BeginMarkedContent("Span");
        }

        using PdfDocument reopened = PdfDocument.Open(Saved(document));
        var recorder = new Recorder(reopened);
        reopened.Pages[0].ProcessContent(recorder);
        Assert.Equal(["Artifact", "Span"], recorder.Sequences);
        Assert.Equal("BMC BT BMC EMC ET EMC", string.Join(' ', recorder.Operators));
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

    private sealed class Recorder(PdfDocument document) : ContentProcessor
    {
        public List<string> Sequences { get; } = [];

        public List<string> Operators { get; } = [];

        public override ContentEvents Events => ContentEvents.MarkedContent | ContentEvents.Operators;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            var tag = new CosName(System.Text.Encoding.ASCII.GetString(mc.Tag));
            CosDictionary? properties = mc.Properties ?? (mc.InlineProperties.Kind == ContentOperandKind.Dictionary ? mc.InlineProperties.ToCosObject() as CosDictionary : null);
            var list = new PdfPropertyList(tag, properties, document);
            Sequences.Add(string.Join(' ', new[] { tag.Value, list.ArtifactType, list.ArtifactSubtype }.Where(part => part is not null)));
        }

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            string name = System.Text.Encoding.ASCII.GetString(op.Keyword);
            if (name is "BMC" or "BDC" or "EMC" or "BT" or "ET")
            {
                Operators.Add(name);
            }
        }
    }
}
