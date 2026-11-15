using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Forms;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using Broadside.Tests.Writing;

namespace Broadside.Layout.Tests;

/// <summary>
/// Form fields placed by the flow (issue #397, E3): a <see cref="FieldBlock"/> draws its label and adds its field where the layout puts
/// it (ISO 32000-2 §12.7), with the label as the alternate name (<c>TU</c>), tagged <c>Form</c> with the widget's object reference
/// (§14.8.4.7.3) and the page's tab order set to the structure order (§12.5, <c>/Tabs /S</c>).
/// </summary>
public sealed class FieldFlowTests
{
    [Fact]
    public void A_text_field_is_placed_below_its_label_with_the_label_as_its_alternate_name()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph("Complete every field.", body));
        flow.Add(new FieldBlock(FormFieldType.Text, "name", "Full name", body)
        {
            Width = 200,
            Value = "Ada",
            Options = new PdfFieldOptions().WithFlags(PdfFieldFlags.Required),
        });

        FlowLayoutResult result = flow.Render(document);

        Assert.Empty(result.Diagnostics);
        PdfTextField field = Assert.IsType<PdfTextField>(document.AcroForm!.FindField("name"));
        Assert.Equal("Full name", field.AlternateName);
        Assert.Equal("Ada", field.Value);
        Assert.True(field.IsRequired);
        PdfWidgetAnnotation widget = Assert.Single(field.Widgets);
        PdfRectangle rect = widget.Rect;
        Assert.Equal(PageSetup.Letter.Margins.Left, rect.Left, 6);
        Assert.Equal(200, rect.Right - rect.Left, 6);
        Assert.Equal(20, rect.Top - rect.Bottom, 6);
        double labelBottom = LabelBottom(result.Pages[0], "Full name");
        Assert.True(rect.Top < labelBottom, $"the field ({rect.Top}) is below its label ({labelBottom})");
        Assert.Same(result.Pages[0].Dictionary, Assert.Single(result.Pages[0].Annotations.OfType<PdfWidgetAnnotation>()).PageObject);
        Assert.False(result.Pages[0].Dictionary.ContainsKey(new CosName("Tabs")), "an untagged flow leaves the tab order alone");
    }

    [Fact]
    public void A_label_placed_before_its_field_shares_its_row()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new FieldBlock(FormFieldType.Text, "email", "Email", body) { LabelPosition = FieldLabelPosition.Before });

        FlowLayoutResult result = flow.Render(document);

        PdfRectangle rect = Assert.Single(Assert.IsType<PdfTextField>(document.AcroForm!.FindField("email")).Widgets).Rect;
        double right = PageSetup.Letter.Width - PageSetup.Letter.Margins.Right;
        Assert.Equal(right, rect.Right, 6);
        Assert.Equal(PageSetup.Letter.Height - PageSetup.Letter.Margins.Top, rect.Top, 6);
        Assert.True(rect.Left > PageSetup.Letter.Margins.Left + 20, "the label comes first on the row");
    }

    [Fact]
    public void Each_field_type_makes_its_field_with_its_initial_value()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        Form(document, tagging: null).Render(document);

        using PdfDocument reopened = PdfDocument.Open(AnchorFlowTests.Saved(document));
        PdfAcroForm form = reopened.AcroForm!;
        Assert.Equal("Ada", Assert.IsType<PdfTextField>(form.FindField("name")).Value);
        Assert.True(Assert.IsType<PdfCheckBoxField>(form.FindField("terms")).IsChecked);
        PdfRadioButtonField collection = Assert.IsType<PdfRadioButtonField>(form.FindField("collection"));
        Assert.Equal(2, collection.Widgets.Count);
        Assert.Equal(new CosName("Deliver"), collection.Value);
        Assert.Equal("Collection", collection.AlternateName);
        Assert.Equal([1], Assert.IsType<PdfComboBoxField>(form.FindField("site")).SelectedIndices);
        Assert.IsType<PdfListBoxField>(form.FindField("items"));
        Assert.IsType<PdfSignatureField>(form.FindField("signature"));
        Assert.All(form.Fields, field => Assert.False(string.IsNullOrEmpty(field.AlternateName)));
    }

    [Fact]
    public void A_tagged_field_is_a_Form_element_holding_its_label_and_its_widget_and_its_page_tabs_in_structure_order()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        FlowDocument flow = Form(document, FlowTagging.Default.WithLanguage("en"));
        flow.Render(document);

        byte[] saved = AnchorFlowTests.Saved(document);
        AnchorFlowTests.AssertQpdfPasses(saved);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfStructureTreeRoot root = reopened.StructureTree!;
        Assert.Equal(
            "Document(P,Form(Lbl),Form(Lbl),Div(P,Form(Lbl),Form(Lbl)),Form(Lbl),Form(Lbl),Form(Lbl))",
            Shape(root.Children));
        foreach (PdfStructureElement element in root.Elements.Where(element => element.StructureType?.Value == "Form"))
        {
            PdfObjectReference widget = Assert.Single(element.Children.OfType<PdfObjectReference>());
            Assert.Equal("Widget", ((CosName)((CosDictionary)widget.ReferencedObject)[new CosName("Subtype")]).Value);
            Assert.Same(element.Dictionary, root.FindElementForObject(widget.ReferencedObject)?.Dictionary);
        }

        Assert.Equal(new CosName("S"), reopened.Pages[0].Dictionary[new CosName("Tabs")]);
        FlowContentRecorder recorder = FlowContentRecorder.Record(reopened.Pages[0]);
        Assert.Equal(0, recorder.Untagged);
        Assert.Equal(0, recorder.Mixed);
    }

    [Fact]
    public void Every_field_type_is_made_without_a_diagnostic_so_a_strict_document_accepts_them()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());
        document.Pages.RemoveAt(0);

        FlowLayoutResult result = Form(document, FlowTagging.Default.WithLanguage("en")).Render(document);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(document.Diagnostics);
        Assert.Equal(6, document.AcroForm!.Fields.Count);
    }

    [Fact]
    public void A_field_that_does_not_fit_moves_to_the_next_page_with_its_label()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new Paragraph(AnchorFlowTests.Filler, body));
        flow.Add(new FieldBlock(FormFieldType.Text, "late", "A field at the end", body) { Height = 500 });

        FlowLayoutResult result = flow.Render(document);

        PdfPage last = result.Pages[^1];
        Assert.Contains("A field at the end", last.ExtractText().PlainText, StringComparison.Ordinal);
        Assert.Single(last.Annotations.OfType<PdfWidgetAnnotation>());
        double top = PageSetup.Letter.Height - PageSetup.Letter.Margins.Top;
        Assert.InRange(LabelBounds(last, "A field at the end").Top, top - 12, top);
    }

    [Fact]
    public void A_field_name_used_twice_or_already_in_the_form_is_refused_before_anything_is_added()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var twice = new FlowDocument(PageSetup.Letter);
        twice.Add(new FieldBlock(FormFieldType.Text, "name", "Name", body));
        twice.Add(new FieldBlock(FormFieldType.CheckBox, "name", "Again", body));

        Assert.Throws<ArgumentException>(() => twice.Render(document));
        Assert.Empty(document.Pages);
        Assert.Null(document.AcroForm);

        var once = new FlowDocument(PageSetup.Letter);
        once.Add(new FieldBlock(FormFieldType.Text, "name", "Name", body));
        once.Render(document);
        int pages = document.Pages.Count;
        Assert.Throws<ArgumentException>(() => once.Render(document));
        Assert.Equal(pages, document.Pages.Count);
    }

    [Fact]
    public void A_field_block_checks_its_arguments()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        ParagraphStyle body = AnchorFlowTests.Body(document);

        Assert.Throws<ArgumentException>(() => new FieldBlock(FormFieldType.Text, "name", " ", body));
        Assert.Throws<ArgumentException>(() => new FieldBlock(FormFieldType.Text, string.Empty, "Name", body));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldBlock((FormFieldType)42, "name", "Name", body));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FieldBlock(FormFieldType.Text, "name", "Name", body) { Width = -1 });
        var flow = new FlowDocument(PageSetup.Letter);
        flow.Add(new FieldBlock(FormFieldType.ComboBox, "site", "Site", body) { Choices = ["Leeds"], Value = "York" });
        Assert.Throws<ArgumentException>(() => flow.Render(document));
        var radios = new FlowDocument(PageSetup.Letter);
        radios.Add(new FieldBlock(FormFieldType.RadioButtons, "pick", "Pick", body));
        Assert.Throws<ArgumentException>(() => radios.Render(document));
    }

    [Fact]
    [Trait("Category", "VeraPdf")]
    public void A_tagged_form_passes_verapdf_PDF_UA_1()
    {
        using PdfDocument document = AnchorFlowTests.NewDocument();
        PdfCanvasFont font = document.EmbedTrueTypeFont(Standard14FontResolver.GetProgram(Standard14Font.Helvetica));
        var body = new ParagraphStyle(font, 10);
        var flow = new FlowDocument(PageSetup.Letter)
        {
            Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Loan request").WithNamespace(FlowStructureNamespace.Pdf17),
        };
        flow.Add(new Paragraph("Equipment loan request", body) { StructureType = "H1" });
        flow.Add(new FieldBlock(FormFieldType.Text, "name", "Full name", body));
        flow.Add(new FieldBlock(FormFieldType.ComboBox, "site", "Site", body) { Choices = ["Leeds", "York"], Value = "Leeds" });
        flow.Add(new FieldBlock(FormFieldType.RadioButtons, "collection", "Collection", body) { Choices = ["Pick up", "Deliver"] });
        flow.Add(new FieldBlock(FormFieldType.CheckBox, "terms", "I will return the equipment in working order", body));
        flow.Render(document);

        byte[] saved = PdfUAFlowTests.Saved(document, new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));

        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        Assert.Empty(VeraPdf.FailedRules(saved, "ua1"));
    }

    private static FlowDocument Form(PdfDocument document, FlowTagging? tagging)
    {
        ParagraphStyle body = AnchorFlowTests.Body(document);
        var flow = new FlowDocument(PageSetup.Letter) { Tagging = tagging };
        flow.Add(new Paragraph("Complete every field.", body));
        flow.Add(new FieldBlock(FormFieldType.Text, "name", "Full name", body) { Value = "Ada" });
        flow.Add(new FieldBlock(FormFieldType.CheckBox, "terms", "I will return the equipment", body) { IsChecked = true });
        flow.Add(new FieldBlock(FormFieldType.RadioButtons, "collection", "Collection", body) { Choices = ["Pick up", "Deliver"], Value = "Deliver" });
        flow.Add(new FieldBlock(FormFieldType.ComboBox, "site", "Site", body) { Choices = ["Leeds", "Manchester", "York"], Value = "Manchester" });
        flow.Add(new FieldBlock(FormFieldType.ListBox, "items", "Items", body) { Choices = ["Laptop", "Monitor"] });
        flow.Add(new FieldBlock(FormFieldType.Signature, "signature", "Signature", body) { Width = 200 });
        return flow;
    }

    private static double LabelBottom(PdfPage page, string label) => LabelBounds(page, label).Bottom;

    private static PdfRectangle LabelBounds(PdfPage page, string label)
    {
        Text.TextPage text = page.ExtractText();
        string first = label.Split(' ')[0];
        Text.TextWord word = text.Words.First(word => text.GetWordText(word).SequenceEqual(first));
        return word.Quad.Bounds;
    }

    private static string Shape(IEnumerable<PdfStructureItem> items) => string.Join(',', items.OfType<PdfStructureElement>().Select(element =>
    {
        string children = Shape(element.Children);
        return children.Length == 0 ? element.StructureType!.Value : $"{element.StructureType!.Value}({children})";
    }));
}
