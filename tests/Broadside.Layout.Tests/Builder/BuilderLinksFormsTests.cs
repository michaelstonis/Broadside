using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Layout.Flow.Units;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests.Builder;

/// <summary>
/// The document builder's links, outline, table of contents, page references (B6) and form fields (B7) of issue #397: each writes the
/// same bytes as the engine blocks it fronts, the outline follows the headings to depth 3 by default, and every field carries its label.
/// </summary>
public sealed class BuilderLinksFormsTests
{
    [Fact]
    public void A_table_of_contents_links_and_page_references_write_the_same_bytes_as_the_hand_built_flow()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Guide")
            .WithLanguage("en-US")
            .Content(c =>
            {
                c.Heading(1, "Guide");
                c.TableOfContents().Title("Contents").Depth(2);
                c.PageBreak();
                c.Heading(2, "Installing").Id("install");
                c.Paragraph("Add the package.");
                c.Heading(2, "Reading");
                c.Paragraph(t =>
                {
                    t.Text("See ");
                    t.Link("Installing", to: "install");
                    t.Text(" on page ");
                    t.PageOf("install");
                    t.Text(".");
                });
            }));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont bold = Embed(twin, Standard14Font.HelveticaBold);
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var body = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var h2 = new ParagraphStyle(bold, 11 * 1.6).WithSpaceBefore(12).WithSpaceAfter(6);
        var accent = PdfColor.FromHex("#0B5FFF");
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Guide"), OutlineDepth = 3 };
        flow.Add(new Paragraph("Guide", new ParagraphStyle(bold, 22).WithSpaceBefore(12).WithSpaceAfter(6)) { StructureType = "H1" });
        flow.Add(new BlockGroup(new Paragraph("Contents", h2)) { KeepWithNext = true });
        flow.Add(new TableOfContents(body) { Depth = 2 });
        flow.Add(new PageBreak());
        flow.Add(new Paragraph("Installing", h2) { StructureType = "H2", Id = "install" });
        flow.Add(new Paragraph("Add the package.", body));
        flow.Add(new Paragraph("Reading", h2) { StructureType = "H2" });
        flow.Add(new Paragraph(
            [
                new TextRun("See "),
                new TextRun("Installing", new TextStyle(regular, 11).WithColor(accent).WithUnderline(true)) { Destination = "install" },
                new TextRun(" on page "),
                new PageReference("install"),
                new TextRun("."),
            ],
            body));
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void The_outline_lists_headings_to_depth_3_by_default_and_none_at_depth_0()
    {
        static void Headings(ContainerBuilder c)
        {
            c.Heading(1, "One");
            c.Heading(2, "One point one");
            c.Heading(3, "Deep");
            c.Heading(4, "Too deep");
        }

        using PdfDocument outlined = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").Content(Headings));
        using PdfDocument flat = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").WithOutlineDepth(0).Content(Headings));

        using PdfDocument reopened = PdfDocument.Open(Save(outlined));
        PdfOutlineItem one = Assert.Single(reopened.Outline!.Items);
        Assert.Equal("One", one.Title);
        PdfOutlineItem two = Assert.Single(one.Children);
        Assert.Equal("Deep", Assert.Single(two.Children).Title);
        Assert.Empty(Assert.Single(two.Children).Children);
        using PdfDocument flatCopy = PdfDocument.Open(Save(flat));
        Assert.True(flatCopy.Outline is null || flatCopy.Outline.Items.Count == 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.WithOutlineDepth(7)));
    }

    [Fact]
    public void A_link_to_an_unknown_id_is_a_diagnostic_and_stays_text()
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc.WithTitle("t").WithLanguage("en").Content(c =>
            c.Paragraph(t => t.Link("nowhere", to: "missing"))));

        Assert.Contains(pdf.Diagnostics, diagnostic => diagnostic.Code == LayoutDiagnostics.AnchorNotFound.Name);
        Assert.Empty(pdf.Pages[0].Annotations);
    }

    [Fact]
    public void Fields_write_the_same_bytes_as_the_hand_built_field_blocks()
    {
        using PdfDocument built = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Form")
            .WithLanguage("en-GB")
            .Content(c =>
            {
                c.TextField("name", "Full name").Width(90.Mm).Required();
                c.TextField("items", "Items requested").Multiline(lines: 4);
                c.ComboBox("site", "Site", ["Leeds", "York"]).Value("Leeds");
                c.CheckBox("terms", "I agree").Checked();
            }));

        using PdfDocument twin = NewTwin();
        PdfCanvasFont regular = Embed(twin, Standard14Font.Helvetica);
        var label = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle("Form"), OutlineDepth = 3 };
        flow.Add(new FieldBlock(FormFieldType.Text, "name", "Full name", label)
        {
            Width = Length.Millimeters(90).ToPoints(),
            Options = new PdfFieldOptions().WithFlags(PdfFieldFlags.Required),
            SpaceAfter = 6,
        });
        flow.Add(new FieldBlock(FormFieldType.Text, "items", "Items requested", label)
        {
            Height = (4 * 11 * Theme.Default.LineHeight) + 4,
            Options = new PdfFieldOptions().WithFlags(PdfFieldFlags.Multiline),
            SpaceAfter = 6,
        });
        flow.Add(new FieldBlock(FormFieldType.ComboBox, "site", "Site", label) { Choices = ["Leeds", "York"], Value = "Leeds", SpaceAfter = 6 });
        flow.Add(new FieldBlock(FormFieldType.CheckBox, "terms", "I agree", label) { IsChecked = true, SpaceAfter = 6 });
        flow.Render(twin);

        Assert.Equal(Save(twin), Save(built));
    }

    [Fact]
    public void Every_field_type_is_tagged_as_a_form_with_its_label_as_alternate_name_and_passes_qpdf()
    {
        using PdfDocument pdf = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Equipment loan request")
            .WithLanguage("en-GB")
            .WithPdfUAConformance(PdfUAConformance.PdfUA1)
            .Content(c =>
            {
                c.Heading(1, "Equipment loan request");
                c.TextField("email", "Email address").Width(90.Mm).Required();
                c.ListBox("kit", "Kit", ["Laptop", "Monitor", "Headset"]);
                c.RadioGroup("collection", "Collection", ["Pick up", "Deliver to my desk"]).Value("Pick up");
                c.SignatureField("signature", "Signature").Size(70.Mm, 20.Mm);
            }));

        byte[] bytes = Save(pdf);
        Assert.DoesNotContain(pdf.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
        FlowBuildingBlockTests.AssertQpdfPasses(bytes);
        using PdfDocument reopened = PdfDocument.Open(bytes);
        PdfAcroForm form = reopened.AcroForm!;
        Assert.Equal("Email address", form.FindField("email")!.AlternateName);
        Assert.Equal("Signature", form.FindField("signature")!.AlternateName);
        Assert.Contains("Form", StructureText(reopened), StringComparison.Ordinal);
    }

    [Fact]
    public void Fields_refuse_bad_arguments_at_the_call()
    {
        Assert.Throws<ArgumentException>(() => PdfDocument.Create(doc => doc.Content(c => c.TextField("name", " "))));
        Assert.Throws<ArgumentException>(() => PdfDocument.Create(doc => doc.Content(c => c.ComboBox("site", "Site", []))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(doc => doc.Content(c => c.TextField("x", "X").Multiline(0))));
        Assert.Throws<ArgumentException>(() => PdfDocument.Create(doc => doc.Content(c =>
        {
            c.TextField("same", "One");
            c.TextField("same", "Two");
        })));
    }
}
