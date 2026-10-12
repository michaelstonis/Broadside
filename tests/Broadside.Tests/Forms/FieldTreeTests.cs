using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>The field hierarchy of an interactive form and its links to widget annotations (ISO 32000-2 §12.7.1 to §12.7.4).</summary>
public sealed class FieldTreeTests
{
    [Fact]
    public void Acroform_fields_pdf_reads_the_field_hierarchy_with_fully_qualified_names_and_typed_fields()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));

        PdfAcroForm form = Assert.IsType<PdfAcroForm>(document.AcroForm);

        Assert.Equal(["name", "person", "agree", "color", "submit", "country", "toppings", "sig"], form.Fields.Select(field => field.FullyQualifiedName));
        Assert.Equal(
            [
                "name PdfTextField", "person PdfNonTerminalField", "person.first PdfTextField", "person.last PdfTextField",
                "agree PdfCheckBoxField", "color PdfRadioButtonField", "submit PdfPushButtonField", "country PdfComboBoxField",
                "toppings PdfListBoxField", "sig PdfSignatureField",
            ],
            form.AllFields.Select(field => $"{field.FullyQualifiedName} {field.GetType().Name}"));
        Assert.Equal(
            [
                "Text", "NonTerminal", "Text", "Text", "CheckBox", "RadioButton", "PushButton", "ComboBox", "ListBox", "Signature",
            ],
            form.AllFields.Select(field => field.Kind.ToString()));

        PdfNonTerminalField person = Assert.IsType<PdfNonTerminalField>(form.FindField("person"));
        Assert.Equal(["first", "last"], person.Children.Select(child => child.PartialName));
        Assert.All(person.Children, child => Assert.Same(person, child.Parent));
        Assert.Null(person.Parent);
        Assert.Same(person.Children[1], Assert.Single(form.FindFields("person.last")));
        Assert.Empty(form.FindFields("person.middle"));
        Assert.Equal(9, form.TerminalFields.Count);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Terminal_fields_and_the_widgets_on_the_pages_link_both_ways_to_the_same_instances()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;

        PdfWidgetAnnotation[] onPages = [.. document.Pages.SelectMany(page => page.Annotations).OfType<PdfWidgetAnnotation>()];

        Assert.Equal(12, onPages.Length);
        Assert.All(onPages, widget => Assert.Contains(widget, widget.Field!.Widgets));
        Assert.Equal(onPages.Length, form.TerminalFields.Sum(field => field.Widgets.Count));
        Assert.All(form.TerminalFields, field => Assert.All(field.Widgets, widget => Assert.Same(field, widget.Field)));

        var agree = (PdfCheckBoxField)form.FindField("agree")!;
        Assert.Equal([document.Pages[0], document.Pages[1]], agree.Widgets.Select(widget => widget.Page));
        Assert.Same(document.Pages[1].Annotations[0], agree.Widgets[1]);

        var name = (PdfTextField)form.FindField("name")!;
        PdfWidgetAnnotation merged = Assert.Single(name.Widgets);
        Assert.Same(name.Dictionary, merged.Dictionary);
        Assert.Same(form.FindField(name.Dictionary), name);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Concurrent_readers_get_the_same_field_and_widget_instances()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var results = new object?[64][];

        Parallel.For(0, results.Length, index =>
        {
            PdfAcroForm form = document.AcroForm!;
            PdfWidgetAnnotation widget = (PdfWidgetAnnotation)document.Pages[0].Annotations[0];
            results[index] = [form, .. form.AllFields, .. form.TerminalFields.SelectMany(field => field.Widgets), document.Pages[1].Annotations[0], widget.Field];
        });

        Assert.All(results, result => Assert.Equal(results[0], result, ReferenceEqualityComparer.Instance));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_mutation_of_the_field_tree_is_seen_on_the_next_read()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        PdfField name = form.FindField("name")!;
        var person = (PdfNonTerminalField)form.FindField("person")!;

        person.Children[0].Dictionary[new CosName("T")] = new CosString("given"u8);
        var kids = (CosArray)document.Resolve(person.Dictionary[new CosName("Kids")]);
        kids.RemoveAt(1);

        Assert.Equal(["person.given"], form.AllFields.Where(field => field.Parent is not null).Select(field => field.FullyQualifiedName));
        Assert.Empty(form.FindFields("person.last"));
        Assert.NotSame(name, form.FindField("name"));
        Assert.Same(form.FindField("name"), form.FindField("name"));
        Assert.Equal("Ada", ((PdfTextField)form.FindField("name")!).Value);
    }

    [Fact]
    public void A_document_without_an_acroform_has_no_form_and_its_widgets_no_field()
    {
        using PdfDocument plain = PdfDocument.Open(Corpus.Path("empty-page.pdf"));
        Assert.Null(plain.AcroForm);

        byte[] file = new Document.TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 42 >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 1 1] /T (w) /FT /Tx >>");
        using PdfDocument invalid = PdfDocument.Open(file);
        Assert.Null(((PdfWidgetAnnotation)invalid.Pages[0].Annotations[0]).Field);
        Assert.Equal("AcroFormInvalid", Assert.Single(invalid.Diagnostics).Code);

        using PdfDocument empty = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty, "/AcroForm << >>"));
        Assert.Empty(empty.AcroForm!.AllFields);
        Assert.Empty(empty.Diagnostics);
    }
}
