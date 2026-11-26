using System.Text;
using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Tests.Forms;

/// <summary>Exporting field values as XFDF (Adobe XFDF 3.0, ISO 19444-1): the markup and the round trip of every field kind.</summary>
public sealed class XfdfExportTests
{
    [Fact]
    public void The_default_export_is_xfdf_with_nested_fields_and_a_value_per_selected_option()
    {
        using PdfDocument document = FormDataFixture.CreateFilled();

        string xml = Encoding.UTF8.GetString(FormDataFixture.Export(document, new PdfFormDataOptions().WithSourceFile("form.pdf")));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<xfdf xmlns=\"http://ns.adobe.com/xfdf/\" xml:space=\"preserve\"><f href=\"form.pdf\"/><ids original=\"", xml, StringComparison.Ordinal);
        Assert.Contains(
            "<fields><field name=\"person\"><field name=\"name\"><value>Ada Lovelace</value></field><field name=\"address\"><field name=\"city\">"
            + "<value>London (UK) &amp; &lt;Europe&gt; été €</value></field></field></field>",
            xml,
            StringComparison.Ordinal);
        Assert.Contains("<field name=\"check\"><value>Yes</value></field>", xml, StringComparison.Ordinal);
        Assert.Contains("<field name=\"list\"><value>Cheese</value><value>Peppers</value></field>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("button", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("signature", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<annots", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_field_kind_round_trips_through_xfdf_into_an_empty_copy_of_the_form()
    {
        using PdfDocument source = FormDataFixture.CreateFilled();
        using PdfDocument target = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(target, FormDataFixture.Export(source));

        Assert.Equal(FormDataFixture.Values(source), FormDataFixture.Values(target));
        Assert.Equal(FormDataFixture.ValueFieldNames, import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Empty(import.Diagnostics);
    }

    [Fact]
    public void A_rich_text_value_is_written_as_value_richtext_and_read_back_as_rv()
    {
        using PdfDocument source = FormDataFixture.CreateFilled();
        var field = (PdfTextField)source.AcroForm!.FindField("person.name")!;
        field.Dictionary[new CosName("RV")] = new CosString(Encoding.ASCII.GetBytes("<?xml version=\"1.0\"?><body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Ada <b>Lovelace</b></p></body>"));
        using PdfDocument target = FormDataFixture.Create();

        byte[] file = FormDataFixture.Export(source);
        FormDataFixture.Import(target, file);

        Assert.Contains("<value-richtext><body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Ada <b>Lovelace</b></p></body></value-richtext>", Encoding.UTF8.GetString(file), StringComparison.Ordinal);
        var imported = (PdfTextField)target.AcroForm!.FindField("person.name")!;
        Assert.Equal("Ada Lovelace", imported.Value);
        Assert.Equal("<body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Ada <b>Lovelace</b></p></body>", imported.RichValue);
    }

    [Fact]
    public void A_rich_text_value_that_is_not_well_formed_is_left_out_so_the_file_stays_well_formed()
    {
        using PdfDocument source = FormDataFixture.CreateFilled();
        ((PdfTextField)source.AcroForm!.FindField("person.name")!).Dictionary[new CosName("RV")] = new CosString("<body><p>unclosed</body>"u8.ToArray());
        using PdfDocument target = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(target, FormDataFixture.Export(source));

        Assert.Empty(import.Diagnostics);
        Assert.Null(target.AcroForm!.FindField("person.name")!.RichValue);
    }

    [Fact]
    public void A_document_without_a_form_exports_an_empty_xfdf_file()
    {
        using PdfDocument document = PdfDocument.Create();

        string xml = Encoding.UTF8.GetString(FormDataFixture.Export(document));

        Assert.DoesNotContain("<fields", xml, StringComparison.Ordinal);
        Assert.Contains("<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"", xml, StringComparison.Ordinal);
    }
}
