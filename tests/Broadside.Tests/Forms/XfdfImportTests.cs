using System.Text;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>Importing XFDF files (Adobe XFDF 3.0, ISO 19444-1): field values, unknown names, wrong types, malformed markup and strict mode.</summary>
public sealed class XfdfImportTests
{
    [Fact]
    public void The_corpus_xfdf_file_in_the_shape_of_adobes_samples_sets_every_field_kind_and_adds_its_annotations()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Corpus.Bytes("form-data.xfdf"));

        Assert.Equal(["Ada Lovelace", "London", "Yes", "Blue", "ca", "1|2"], FormDataFixture.Values(document));
        Assert.Equal(FormDataFixture.ValueFieldNames, import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Equal(["unknown"], import.FieldsNotFound);
        Assert.Equal(["Text", "Popup", "Text", "Highlight", "Ink"], import.AnnotationsAdded.Select(annotation => annotation.Kind.ToString()));
        Assert.Empty(import.Diagnostics);
    }

    [Fact]
    public void Without_options_the_format_is_sniffed_and_with_options_only_what_they_include_is_imported()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Corpus.Bytes("form-data.xfdf"), new PdfFormDataOptions());

        Assert.Equal(6, import.FieldsSet.Count);
        Assert.Empty(import.AnnotationsAdded);
    }

    [Fact]
    public void Flat_dotted_names_are_read_like_nested_fields_and_a_byte_order_mark_is_accepted()
    {
        using PdfDocument document = FormDataFixture.Create();
        byte[] file = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><fields><field name=\"person.address.city\"><value>Zürich</value></field></fields></xfdf>")];

        PdfFormDataImport import = FormDataFixture.Import(document, file);

        Assert.Equal("Zürich", ((PdfTextField)Assert.Single(import.FieldsSet)).Value);
    }

    [Fact]
    public void A_wrong_typed_value_and_an_unknown_option_are_skipped_with_diagnostics()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Xfdf(
            "<field name=\"person\"><field name=\"name\"><value>a</value><value>b</value></field></field><field name=\"combo\"><value>fr</value></field><field name=\"check\"><value>Yes</value></field>"));

        Assert.Equal(["check"], import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Equal(["FormDataValueInvalid", "FormDataValueInvalid"], import.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Strict_mode_throws_at_the_first_value_that_cannot_be_applied()
    {
        using PdfDocument document = FormDataFixture.Create(new PdfEngine(new PdfOptions().UseStrict()));

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => FormDataFixture.Import(document, Xfdf("<field name=\"combo\"><value>fr</value></field>")));

        Assert.Equal("FormDataValueInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void Markup_that_is_not_well_formed_or_declares_a_document_type_cannot_be_imported()
    {
        using PdfDocument document = FormDataFixture.Create();

        DiagnosticException malformed = Assert.Throws<DiagnosticException>(() => FormDataFixture.Import(document, Encoding.UTF8.GetBytes("<xfdf><fields><field name=\"a\"></xfdf>")));
        DiagnosticException doctype = Assert.Throws<DiagnosticException>(() => FormDataFixture.Import(
            document, Encoding.UTF8.GetBytes("<!DOCTYPE xfdf [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><xfdf><fields><field name=\"check\"><value>&x;</value></field></fields></xfdf>")));

        Assert.Equal("FormDataInvalid", malformed.Diagnostic.Code);
        Assert.Equal("FormDataInvalid", doctype.Diagnostic.Code);
        Assert.False(((PdfCheckBoxField)document.AcroForm!.FindField("check")!).IsChecked);
    }

    [Fact]
    public void A_file_that_is_neither_fdf_nor_xfdf_is_refused_when_the_format_is_sniffed()
    {
        using PdfDocument document = FormDataFixture.Create();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => FormDataFixture.Import(document, "%PDF-1.7\n"));

        Assert.Equal("FormDataInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void A_root_outside_the_xfdf_namespace_is_read_with_a_warning()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Encoding.UTF8.GetBytes("<xfdf><fields><field name=\"check\"><value>Yes</value></field></fields></xfdf>"));

        Assert.Equal("check", Assert.Single(import.FieldsSet).FullyQualifiedName);
        Assert.Equal("FormDataInvalid", Assert.Single(import.Diagnostics).Code);
    }

    internal static byte[] Xfdf(string fields) =>
        Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><xfdf xmlns=\"http://ns.adobe.com/xfdf/\" xml:space=\"preserve\"><fields>{fields}</fields></xfdf>");
}
