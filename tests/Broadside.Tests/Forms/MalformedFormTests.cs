using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Document;

namespace Broadside.Tests.Forms;

/// <summary>Real-world deviations in field trees and field entries: lenient repairs with diagnostics, strict throws (ISO 32000-2 §12.7, ADR 0005).</summary>
public sealed class MalformedFormTests
{
    private const int FieldTreeDepth = 256;


    /// <summary>
    /// Fields [42 (an integer), 4 a (Kids [5]), 6 noft (merged widget, no FT), 7 parentmissing (kid 8 has no Parent), 9 mismatch
    /// (kid 10's Parent is 7), 12 dup (Tx), 14 dup (Ch), 16 sig (two widgets), 17 a.b (Comb without MaxLen)]; 5 b's Kids lists 4
    /// again (a cycle); widget 11 is on the page but in no field's Kids.
    /// </summary>
    private static byte[] MalformedTree() => new TestPdf().Build(
        "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [42 4 0 R 6 0 R 7 0 R 9 0 R 12 0 R 14 0 R 16 0 R 17 0 R] /DA (/Helv 0 Tf 0 g) >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [6 0 R 8 0 R 10 0 R 11 0 R 12 0 R 18 0 R 19 0 R] >>",
        "<< /T (a) /FT /Tx /Kids [5 0 R] >>",
        "<< /T (b) /Parent 4 0 R /Kids [4 0 R] >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /T (noft) >>",
        "<< /T (parentmissing) /FT /Btn /Kids [8 0 R] >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /AS /Off /AP << /N << /On 20 0 R /Off 20 0 R >> >> >>",
        "<< /T (mismatch) /FT /Btn /Ff 49152 /V /On /Kids [10 0 R] >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 7 0 R /AS /On /AP << /N << /On 20 0 R /Off 20 0 R >> >> >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /T (orphan) /FT /Tx /V (lost) >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /T (dup) /FT /Tx >>",
        "null",
        "<< /T (dup) /FT /Ch >>",
        "null",
        "<< /T (sig) /FT /Sig /Kids [18 0 R 19 0 R] >>",
        "<< /T (a.b) /FT /Tx /Ff 16777216 >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 16 0 R >>",
        "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 16 0 R >>",
        "<< /Length 0 >>\nstream\n\nendstream");

    [Fact]
    public void A_field_found_outside_the_tree_follows_changes_to_its_dictionaries()
    {
        using PdfDocument document = PdfDocument.Open(MalformedTree());
        var orphan = (PdfWidgetAnnotation)document.Pages[0].Annotations[3];
        Assert.Equal("orphan", orphan.Field!.FullyQualifiedName);

        orphan.Dictionary[new CosName("T")] = new CosString("renamed"u8);

        Assert.Equal("renamed", orphan.Field!.FullyQualifiedName);
    }

    [Fact]
    public void A_malformed_field_tree_reads_what_it_can_with_exactly_the_documented_diagnostics()
    {
        using PdfDocument document = PdfDocument.Open(MalformedTree());
        PdfAcroForm form = document.AcroForm!;
        Assert.Empty(document.Diagnostics);

        Assert.Equal(
            [
                "a PdfNonTerminalField", "a.b PdfTextField", "noft PdfUnknownField", "parentmissing PdfCheckBoxField",
                "mismatch PdfRadioButtonField", "dup PdfTextField", "dup PdfListBoxField", "sig PdfSignatureField", "a.b PdfTextField",
            ],
            form.AllFields.Select(field => $"{field.FullyQualifiedName} {field.GetType().Name}"));
        Assert.Equal(
            [
                "FieldReferenceInvalid 1", // Fields element 0 is an integer; the AcroForm is direct, so the catalog
                "FieldTreeCycle 5", // b's Kids lists a again
                "FieldTypeMissing 6",
                "FieldParentMissing 8",
                "FieldParentMismatch 10",
                "SignatureFieldMultipleWidgets 16",
                "FieldNameHasPeriod 17",
                "FieldNameDuplicateInconsistent 14", // FT Tx and Ch under one name; also the two a.b fields have equal entries
            ],
            document.Diagnostics.Select(Describe));

        Assert.Equal(2, form.FindFields("dup").Count);
        Assert.Equal(2, form.FindFields("a.b").Count);
        Assert.Equal(2, ((PdfSignatureField)form.FindField("sig")!).Widgets.Count);
        Assert.Same(form.FindField("mismatch"), ((PdfWidgetAnnotation)document.Pages[0].Annotations[2]).Field);

        int count = document.Diagnostics.Count;
        PdfWidgetAnnotation orphan = (PdfWidgetAnnotation)document.Pages[0].Annotations[3];
        PdfTextField outside = Assert.IsType<PdfTextField>(orphan.Field);
        Assert.Equal(("orphan", "lost"), (outside.FullyQualifiedName, outside.Value));
        Assert.Same(outside, orphan.Field);
        Assert.Same(orphan, Assert.Single(outside.Widgets));
        Assert.Empty(form.FindFields("orphan"));
        Assert.True(((PdfTextField)form.FindFields("a.b")[1]).IsComb);
        Assert.Equal(["WidgetNotInFieldTree 11", "TextCombInvalid 17"], document.Diagnostics.Skip(count).Select(Describe));
    }

    [Fact]
    public void Strict_mode_throws_from_the_field_tree_not_from_open()
    {
        using PdfDocument document = PdfDocument.Open(MalformedTree(), new PdfOptions().UseStrict());
        PdfAcroForm form = document.AcroForm!;

        Assert.Equal("FieldReferenceInvalid", Assert.Throws<DiagnosticException>(() => form.Fields).Diagnostic.Code);
    }

    public static TheoryData<string, string, string> TreeDeviations => new()
    {
        // Fields entry, extra objects 5.., expected diagnostic
        { "/Fields [5 0 R]", "<< /T (kid) /FT /Tx /DA (x) /Parent 6 0 R >>|<< /T (top) /Kids [5 0 R] >>", "FieldNotRoot 5" },
        { "/Fields [5 0 R]", "<< /T (mixed) /FT /Tx /DA (x) /Kids [6 0 R 7 0 R] >>|<< /T (child) /Parent 5 0 R >>|<< /Type /Annot /Subtype /Widget /Rect [0 0 1 1] /Parent 5 0 R >>", "FieldKidsMixed 5" },
        { "/Fields [5 0 R]", "<< /T (both) /FT /Btn /Ff 98304 >>", "ButtonFlagsConflict 5" },
        { "/Fields [5 0 R]", "<< /T (named) /FT /Tx /DA (x) /Kids [6 0 R] >>|<< /Parent 5 0 R /Kids [7 0 R] >>|<< /T (deep) /Parent 6 0 R >>", "FieldNameMissing 6" },
        { "/Fields [<< /T (direct) /FT /Tx /DA (x) >>]", string.Empty, "FieldNotIndirect 1" },
        { "/Fields 5 0 R", "<< /T (notarray) >>", "AcroFormFieldsMissing 1" },
        { "/NeedAppearances false", string.Empty, "AcroFormFieldsMissing 1" },
        { "/Fields [5 0 R]", "<< /T (x) /FT /Tx /DA (x) /Kids 6 0 R >>|<< /Type /Annot >>", "FieldEntryInvalid 5" },
        { "/Fields [5 0 R]", "<< /FT /Tx /DA (x) >>", "FieldNameMissing 5" },
    };

    [Theory]
    [MemberData(nameof(TreeDeviations))]
    public void A_deviation_in_the_field_tree_is_recorded_once_when_the_tree_is_read(string acroForm, string objects, string expected)
    {
        string[] extra = objects.Length == 0 ? [] : objects.Split('|');
        byte[] file = new TestPdf().Build(
            [
                $"<< /Type /Catalog /Pages 2 0 R /AcroForm << {acroForm} >> >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
                "null",
                .. extra,
            ]);
        using PdfDocument document = PdfDocument.Open(file);

        _ = document.AcroForm!.AllFields;
        _ = document.AcroForm.AllFields;

        Assert.Equal(expected, Describe(Assert.Single(document.Diagnostics)));
    }

    [Fact]
    public void An_unnamed_level_is_transparent_for_names_but_its_entries_are_inherited()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /T (named) /FT /Tx /Kids [5 0 R] >>",
            "<< /Parent 4 0 R /DA (/Helv 9 Tf 0 g) /Ff 4096 /Kids [6 0 R] >>",
            "<< /T (deep) /Parent 5 0 R >>");
        using PdfDocument document = PdfDocument.Open(file);

        var deep = (PdfTextField)Assert.Single(document.AcroForm!.TerminalFields);

        Assert.Equal("named.deep", deep.FullyQualifiedName);
        Assert.Same(document.AcroForm.FindField("named"), deep.Parent);
        Assert.Equal(("/Helv 9 Tf 0 g", true), (deep.DefaultAppearance, deep.IsMultiline));
        Assert.Equal("FieldNameMissing 5", Describe(Assert.Single(document.Diagnostics)));
    }

    [Fact]
    public void Nested_unnamed_levels_pass_on_the_entries_of_the_nearest_one()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /T (named) /Kids [5 0 R] >>",
            "<< /Parent 4 0 R /FT /Ch /DA (/Helv 9 Tf 0 g) /Kids [6 0 R] >>",
            "<< /Parent 5 0 R /FT /Tx /DA (/Cour 12 Tf 0 g) /Kids [7 0 R] >>",
            "<< /T (deep) /Parent 6 0 R >>");
        using PdfDocument document = PdfDocument.Open(file);

        var deep = Assert.IsType<PdfTextField>(Assert.Single(document.AcroForm!.TerminalFields));

        Assert.Equal(("named.deep", PdfFieldType.Text, "/Cour 12 Tf 0 g"), (deep.FullyQualifiedName, deep.FieldType, deep.DefaultAppearance));
    }

    public static TheoryData<string, string, string> ValueDeviations => new()
    {
        // field entries, property, expected diagnostic
        { "/FT /Tx /DA (x) /V /Name", "Value", "FieldValueTypeInvalid 4" },
        { "/FT /Tx /DA (x) /Ff -1", "Flags", "FieldFlagsInvalid 4" },
        { "/FT /Tx /DA (x) /Ff 8", "Flags", "FieldFlagsInvalid 4" },
        { "/FT /Tx /DA (x) /Ff 2.5", "Flags", "FieldFlagsInvalid 4" },
        { "/FT /Tx", "DefaultAppearance", "DefaultAppearanceMissing 4" },
        { "/FT /Tx /DA (x) /Q 7", "Quadding", "FieldEntryInvalid 4" },
        { "/FT /Tx /DA (x) /MaxLen (ten)", "MaxLength", "FieldEntryInvalid 4" },
        { "/FT /Ch /DA (x) /Opt [(a) [(b)] 3]", "Options", "ChoiceOptionInvalid 4" },
        { "/FT /Ch /DA (x) /Opt [(a) (b)] /V (c)", "SelectedIndices", "ChoiceValueNotInOptions 4" },
        { "/FT /Ch /DA (x) /Opt [(a) (b)] /I [1 0 5]", "StoredIndices", "ChoiceIndicesInvalid 4" },
        { "/FT /Btn /Opt [(a) (b)] /Kids [5 0 R]", "Options", "ButtonOptLengthMismatch 4" },
        { "/FT /Btn /V /Maybe /Kids [5 0 R]", "Value", "ButtonValueUnknownState 4" },
        { "/FT /Btn /V (On\\000) /Kids [5 0 R]", "Value", "FieldValueTypeInvalid 4" },
        { "/FT /Btn /V /Off /Kids [6 0 R]", "Value", "ButtonValueStateMismatch 4" },
        { "/FT /Btn /V /Yes /Kids [7 0 R]", "OnStateNames", "ButtonStatesAmbiguous 4" },
        { "/FT /Btn /V /Off /Kids [8 0 R]", "OnStateNames", "ButtonOnStateMissing 4" },
        { "/FT /Sig /Lock << /Action /All >>", "Lock", "SignatureFieldDictNotIndirect 4" },
        { "/FT /Sig /V 42", "SignatureDictionary", "FieldValueTypeInvalid 4" },
        { "/FT /Tx /DA (x) /TU 42", "AlternateName", "FieldEntryInvalid 4" },
    };

    [Theory]
    [MemberData(nameof(ValueDeviations))]
    public void A_malformed_field_entry_reads_as_its_default_with_one_diagnostic_when_read(string entries, string property, string expected)
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [5 0 R 6 0 R 7 0 R 8 0 R] >>",
            $"<< /T (f) {entries} >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 4 0 R /AS /Off /AP << /N << /Yes 9 0 R /Off 9 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 4 0 R /AS /Yes /AP << /N << /Yes 9 0 R /Off 9 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 4 0 R /AS /Yes /AP << /N << /Yes 9 0 R /Oui 9 0 R >> >> >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /Parent 4 0 R /AS /Off /AP << /N << /Off 9 0 R >> >> >>",
            "<< /Length 0 >>\nstream\n\nendstream");
        using PdfDocument document = PdfDocument.Open(file);
        PdfField field = Assert.Single(document.AcroForm!.Fields);
        Assert.Empty(document.Diagnostics);

        object? value = field.GetType().GetProperty(property)!.GetValue(field);
        if (value is System.Collections.IEnumerable sequence and not string)
        {
            _ = sequence.Cast<object>().Count();
        }

        Assert.Equal(expected, Describe(Assert.Single(document.Diagnostics)));
        using PdfDocument strict = PdfDocument.Open(file, new PdfOptions().UseStrict());
        PdfField strictField = Assert.Single(strict.AcroForm!.Fields);
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => strictField.GetType().GetProperty(property)!.GetValue(strictField));
        Assert.Equal(expected.Split(' ')[0], Assert.IsType<DiagnosticException>(error.InnerException).Diagnostic.Code);
    }

    [Fact]
    public void A_choice_option_list_on_an_ancestor_is_used_with_a_diagnostic()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (x) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /T (group) /FT /Ch /Opt [(a) (b)] /Kids [5 0 R] >>",
            "<< /T (list) /Parent 4 0 R /V (b) >>");
        using PdfDocument document = PdfDocument.Open(file);
        var list = (PdfListBoxField)document.AcroForm!.FindField("group.list")!;

        Assert.Equal([1], list.SelectedIndices);
        Assert.Equal("ChoiceOptionsInherited 5", Describe(Assert.Single(document.Diagnostics)));
    }

    [Fact]
    public void A_choice_value_given_as_the_display_text_matches_and_i_tells_duplicates_apart()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R] /DA (x) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
            "<< /T (display) /FT /Ch /Opt [[(1) (One)] [(2) (Two)]] /V (Two) >>",
            "<< /T (twins) /FT /Ch /Ff 2097152 /Opt [(x) (y) (x)] /V [(x)] /I [2] >>");
        using PdfDocument document = PdfDocument.Open(file);

        Assert.Equal([1], ((PdfChoiceField)document.AcroForm!.FindField("display")!).SelectedIndices);
        Assert.Equal([2], ((PdfChoiceField)document.AcroForm.FindField("twins")!).SelectedIndices);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_field_tree_nested_deeper_than_the_limit_is_cut_with_a_diagnostic()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (x) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
        };
        for (int level = 0; level < 300; level++)
        {
            int number = level + 4;
            objects.Add(level == 299
                ? $"<< /T (f{level}) /FT /Tx /Parent {number - 1} 0 R >>"
                : $"<< /T (f{level}) {(level == 0 ? string.Empty : $"/Parent {number - 1} 0 R ")}/Kids [{number + 1} 0 R] >>");
        }

        using PdfDocument document = PdfDocument.Open(new TestPdf().Build([.. objects]));

        Assert.Equal(FieldTreeDepth, document.AcroForm!.AllFields.Count);
        Assert.Equal("FieldTreeTooDeep", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void A_field_in_a_cycle_through_parent_entries_resolves_from_its_widget_without_looping()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 1 1] /Parent 5 0 R >>",
            "<< /T (loop) /FT /Tx /DA (x) /Parent 6 0 R /Kids [4 0 R] >>",
            "<< /T (back) /Parent 5 0 R /Kids [5 0 R] >>");
        using PdfDocument document = PdfDocument.Open(file);

        PdfTerminalField? field = ((PdfWidgetAnnotation)document.Pages[0].Annotations[0]).Field;

        Assert.Equal("loop", field!.FullyQualifiedName);
        Assert.Contains("WidgetNotInFieldTree 4", document.Diagnostics.Select(Describe));
    }

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Code} {diagnostic.ObjectReference?.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
}
