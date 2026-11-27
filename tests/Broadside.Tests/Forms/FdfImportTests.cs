using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>Importing FDF files (ISO 32000-2 §12.7.8): field values and flags, unknown names, wrong types, repairs and strict mode.</summary>
public sealed class FdfImportTests
{
    private const string Header = "%FDF-1.2\n%âãÏÓ\n";

    [Fact]
    public void The_corpus_fdf_file_sets_every_field_kind_and_reports_the_field_the_form_lacks()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Corpus.Bytes("form-data.fdf"), new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf));

        Assert.Equal(["Ada Lovelace", "London", "Yes", "Blue", "ca", "1|2"], FormDataFixture.Values(document));
        Assert.Equal(FormDataFixture.ValueFieldNames, import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Equal(["unknown"], import.FieldsNotFound);
        Assert.Empty(import.AnnotationsAdded);
        Assert.Empty(import.Diagnostics);
    }

    [Fact]
    public void A_file_without_a_cross_reference_table_is_read_by_scanning_without_a_diagnostic_and_its_format_is_sniffed()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Fdf("<< /FDF << /Fields [ << /T (check) /V /Yes >> ] >> >>"));

        Assert.Equal("check", Assert.Single(import.FieldsSet).FullyQualifiedName);
        Assert.Empty(import.Diagnostics);
    }

    [Fact]
    public void A_wrong_startxref_is_repaired_by_scanning_and_recorded()
    {
        using PdfDocument document = FormDataFixture.Create();
        string file = Header + "1 0 obj\n<< /FDF << /Fields [ << /T (check) /V /Yes >> ] >> >>\nendobj\nxref\n0 2\n0000000000 65535 f \n0000000099 00000 n \ntrailer\n<< /Size 2 /Root 1 0 R >>\nstartxref\n9999\n%%EOF\n";

        PdfFormDataImport import = FormDataFixture.Import(document, file);

        Assert.True(((PdfCheckBoxField)document.AcroForm!.FindField("check")!).IsChecked);
        Assert.NotEmpty(import.Diagnostics);
        Assert.All(import.Diagnostics, diagnostic => Assert.NotEqual(DiagnosticSeverity.Information, diagnostic.Severity));
    }

    [Fact]
    public void Hierarchical_names_are_built_from_the_partial_names_of_direct_and_indirect_kids()
    {
        using PdfDocument document = FormDataFixture.Create();
        string file = Header
            + "1 0 obj\n<< /FDF << /Fields [ << /T (person) /Kids [ 2 0 R << /T (address) /Kids [ << /T (city) /V (Paris) >> ] >> ] >> ] >> >>\nendobj\n"
            + "2 0 obj\n<< /T (name) /V (Grace) >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";

        PdfFormDataImport import = FormDataFixture.Import(document, file);

        Assert.Equal(["person.name", "person.address.city"], import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Equal("Paris", ((PdfTextField)document.AcroForm!.FindField("person.address.city")!).Value);
    }

    [Fact]
    public void A_wrong_typed_value_or_an_unknown_option_is_skipped_with_a_diagnostic_and_the_rest_is_imported()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Fdf(
            "<< /FDF << /Fields [ << /T (person) /Kids [ << /T (name) /V 42 >> ] >> << /T (combo) /V (fr) >> << /T (list) /V [ (Olives) (Figs) ] >> << /T (check) /V /Maybe >> << /T (radio) /V /Red >> ] >> >>"));

        Assert.Equal(["radio"], import.FieldsSet.Select(field => field.FullyQualifiedName));
        Assert.Equal(["FormDataValueInvalid", "FormDataValueInvalid", "FormDataValueInvalid", "FormDataValueInvalid"], import.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Null(((PdfTextField)document.AcroForm!.FindField("person.name")!).Value);
        Assert.Equal("Red", ((PdfRadioButtonField)document.AcroForm.FindField("radio")!).Value.Value);
    }

    [Fact]
    public void Strict_mode_throws_at_the_first_wrong_typed_value()
    {
        using PdfDocument document = FormDataFixture.Create(new PdfEngine(new PdfOptions().UseStrict()));

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => FormDataFixture.Import(document, Fdf("<< /FDF << /Fields [ << /T (check) /V 42 >> ] >> >>")));

        Assert.Equal("FormDataValueInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void Field_and_widget_flags_are_replaced_set_and_cleared()
    {
        using PdfDocument document = FormDataFixture.Create();
        PdfAcroForm form = document.AcroForm!;

        FormDataFixture.Import(document, Fdf(
            "<< /FDF << /Fields [ << /T (check) /SetFf 3 /ClrFf 2 /SetF 32 >> << /T (combo) /Ff 131072 /SetFf 1 /F 4 /ClrF 4 >> ] >> >>"));

        Assert.True(form.FindField("check")!.IsReadOnly);
        Assert.False(form.FindField("check")!.IsRequired);
        Assert.True(((PdfTerminalField)form.FindField("check")!).Widgets[0].Flags.HasFlag(Broadside.Annotations.PdfAnnotationFlags.NoView));
        Assert.Equal(PdfFieldFlags.Combo, form.FindField("combo")!.Flags);
        Assert.Equal(Broadside.Annotations.PdfAnnotationFlags.Print, ((PdfTerminalField)form.FindField("combo")!).Widgets[0].Flags);
    }

    [Fact]
    public void The_field_names_option_ignores_entries_for_other_fields()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Corpus.Bytes("form-data.fdf"), new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf).WithFieldNames("combo"));

        Assert.Equal("combo", Assert.Single(import.FieldsSet).FullyQualifiedName);
        Assert.Empty(import.FieldsNotFound);
        Assert.Null(((PdfTextField)document.AcroForm!.FindField("person.name")!).Value);
    }

    [Fact]
    public void Every_field_of_a_name_several_fields_share_is_set()
    {
        using PdfDocument document = FormDataFixture.Create();
        PdfAcroForm form = document.AcroForm!;
        form.AddTextField("copy", document.Pages[0], new PdfRectangle(300, 300, 400, 320));
        // A second field dictionary with the same name, as a processor that merges duplicates by name reads it (§12.7.4.2).
        var second = (Broadside.Objects.CosDictionary)form.AddTextField("other", document.Pages[0], new PdfRectangle(300, 260, 400, 280)).Dictionary;
        second[new Broadside.Objects.CosName("T")] = new Broadside.Objects.CosString("copy"u8.ToArray());

        PdfFormDataImport import = FormDataFixture.Import(document, Fdf("<< /FDF << /Fields [ << /T (copy) /V (Both) >> ] >> >>"));

        Assert.Equal(2, import.FieldsSet.Count);
        Assert.All(form.FindFields("copy"), field => Assert.Equal("Both", ((PdfTextField)field).Value));
    }

    [Fact]
    public void An_encrypted_fdf_file_is_refused()
    {
        using PdfDocument document = FormDataFixture.Create();
        string file = Header + "1 0 obj\n<< /FDF << /Fields [] >> >>\nendobj\ntrailer\n<< /Root 1 0 R /Encrypt << /Filter /Standard >> >>\n%%EOF\n";

        Assert.Throws<NotSupportedException>(() => FormDataFixture.Import(document, file));
    }

    [Fact]
    public void Template_pages_and_scripts_are_preserved_but_not_applied_with_an_information()
    {
        using PdfDocument document = FormDataFixture.Create();

        PdfFormDataImport import = FormDataFixture.Import(document, Fdf("<< /FDF << /Pages [ << /Templates [] >> ] /JavaScript << /Before (app.alert(1)) >> >> >>"));

        Diagnostic diagnostic = Assert.Single(import.Diagnostics);
        Assert.Equal(("FormDataNotApplied", DiagnosticSeverity.Information), (diagnostic.Code, diagnostic.Severity));
        Assert.Empty(import.FieldsSet);
    }

    [Fact]
    public void A_file_with_no_fdf_catalog_cannot_be_imported()
    {
        using PdfDocument document = FormDataFixture.Create();

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => FormDataFixture.Import(document, Fdf("<< /Type /Catalog >>")));

        Assert.Equal("FormDataInvalid", exception.Diagnostic.Code);
    }

    [Fact]
    public void Utf8_encoded_strings_are_decoded_as_the_encoding_entry_says()
    {
        using PdfDocument document = FormDataFixture.Create();
        byte[] file = [.. System.Text.Encoding.Latin1.GetBytes(Header + "1 0 obj\n<< /FDF << /Encoding /utf_8 /Fields [ << /T (person) /Kids [ << /T (name) /V ("), .. "Zoë €"u8, .. System.Text.Encoding.Latin1.GetBytes(") >> ] >> ] >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n")];

        FormDataFixture.Import(document, file);

        Assert.Equal("Zoë €", ((PdfTextField)document.AcroForm!.FindField("person.name")!).Value);
    }

    [Fact]
    public void An_xfa_form_has_only_its_acroform_fields_set_with_the_xfa_information()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-xfa.pdf"));

        PdfFormDataImport import = FormDataFixture.Import(document, Fdf("<< /FDF << /Fields [ << /T (name) /V (Grace) >> ] >> >>"));

        Assert.Equal("Grace", ((PdfTextField)Assert.Single(import.FieldsSet)).Value);
        Assert.Equal("XfaFormPresent", Assert.Single(import.Diagnostics).Code);
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "XfaDataNotUpdated");
    }

    [Fact]
    public void An_annotation_entry_naming_an_object_whose_body_is_a_reference_is_skipped_with_a_diagnostic()
    {
        // Found by the fdf-import fuzz target: object 2's body is the reference 3 0 R, which no indirect object can be (§7.3.10).
        using PdfDocument document = FormDataFixture.Create();
        string file = Header
            + "1 0 obj\n<< /FDF << /Annots [ << /Type /Annot /Subtype /Text /Rect [0 0 10 10] /Page 0 /Contents (Note) /Popup 2 0 R /IRT 2 0 R >> ] >> >>\nendobj\n"
            + "2 0 obj\n3 0 R\nendobj\n3 0 obj\n<< /Type /Annot /Subtype /Popup /Rect [0 0 10 10] >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";

        PdfFormDataImport import = FormDataFixture.Import(document, file, new PdfFormDataOptions().WithFormat(PdfFormDataFormat.Fdf).WithFields(false).WithAnnotations());

        PdfAnnotation added = Assert.Single(import.AnnotationsAdded);
        Assert.Equal("Note", added.Contents);
        Assert.False(added.Dictionary.ContainsKey(new CosName("Popup")));
        Assert.False(added.Dictionary.ContainsKey(new CosName("IRT")));
        Assert.Equal(["FormDataInvalid"], import.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct());
    }

    internal static string Fdf(string catalog) => Header + "1 0 obj\n" + catalog + "\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
}
