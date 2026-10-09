using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>Inherited entries, flags and decoded values of each field type (ISO 32000-2 §12.7.3 to §12.7.5).</summary>
public class FieldValueTests
{
    [Fact]
    public void The_interactive_form_dictionary_exposes_its_defaults_resources_flags_and_calculation_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;

        Assert.Equal(new CosReference(4, 0), form.Reference);
        Assert.False(form.NeedAppearances);
        Assert.Equal(PdfSignatureFlags.SignaturesExist, form.SignatureFlags);
        Assert.Equal("/Helv 0 Tf 0 g", form.DefaultAppearance);
        Assert.Equal(PdfTextJustification.Left, form.Quadding);
        Assert.Equal([new CosName("Helv"), new CosName("ZaDb")], ((CosDictionary)document.Resolve(form.DefaultResources![new CosName("Font")])).Keys);
        Assert.Same(form.FindField("name"), Assert.Single(form.CalculationOrder));
        Assert.Null(form.Xfa);
        Assert.Same(form, document.AcroForm);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Ft_ff_v_da_and_q_are_inherited_from_the_nearest_ancestor_and_da_and_q_then_from_the_form()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;

        var first = (PdfTextField)form.FindField("person.first")!;
        var last = (PdfTextField)form.FindField("person.last")!;
        var country = (PdfComboBoxField)form.FindField("country")!;

        Assert.Equal((PdfFieldType.Text, new CosName("Tx")), (first.FieldType, first.FieldTypeName));
        Assert.Equal((2u, true, false), (first.Flags, first.IsRequired, first.IsReadOnly));
        Assert.Equal((1u, false, true), (last.Flags, last.IsRequired, last.IsReadOnly));
        Assert.Equal(("/Helv 10 Tf 0 0 1 rg", PdfTextJustification.Centered), (first.DefaultAppearance, first.Quadding));
        Assert.Equal(("/Helv 0 Tf 0 g", PdfTextJustification.Left), (country.DefaultAppearance, country.Quadding));
        Assert.Equal("Grace", first.Value);
        Assert.Equal("Löv", last.Value);
        Assert.Null(first.MaxLength);
        Assert.Equal(PdfFieldType.Text, form.FindField("person")!.FieldType);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_text_field_reads_its_names_value_default_length_and_trigger_actions()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var name = (PdfTextField)document.AcroForm!.FindField("name")!;

        Assert.Equal(("name", "Your name", "full_name"), (name.PartialName, name.AlternateName, name.MappingName));
        Assert.Equal(("Ada", "Anonymous", 40), (name.Value, name.DefaultValue, name.MaxLength));
        Assert.Equal("/Helv 12 Tf 0 g", name.DefaultAppearance);
        Assert.False(name.IsMultiline || name.IsPassword || name.IsComb || name.IsRichText || name.IsFileSelect);
        Assert.IsType<PdfJavaScriptAction>(name.AdditionalActions!.Calculate);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
    }

    [Fact]
    public void A_check_box_reads_its_state_on_state_and_widget_states_across_pages()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var agree = (PdfCheckBoxField)document.AcroForm!.FindField("agree")!;

        Assert.Equal(new CosName("Yes"), agree.Value);
        Assert.Equal(new CosName("Off"), agree.DefaultValue);
        Assert.True(agree.IsChecked);
        Assert.Equal([new CosName("Yes")], agree.OnStateNames);
        Assert.Equal(agree.Widgets, agree.SelectedWidgets);
        Assert.Equal("Yes", agree.ExportValue);
        Assert.All(agree.Widgets, widget => Assert.Equal(new CosName("Yes"), agree.GetAppearanceState(widget)));
        Assert.Equal("4", agree.Widgets[0].AppearanceCharacteristics!.NormalCaption);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_radio_group_with_index_named_states_maps_its_value_to_the_export_value_of_the_selected_widget()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var color = (PdfRadioButtonField)document.AcroForm!.FindField("color")!;

        Assert.Equal((true, false), (color.IsNoToggleToOff, color.IsRadiosInUnison));
        Assert.Equal(new CosName("1"), color.Value);
        Assert.Equal(["Red", "Green", "Green"], color.Options);
        Assert.Equal([new CosName("0"), new CosName("1"), new CosName("2")], color.OnStateNames);
        Assert.Same(color.Widgets[1], Assert.Single(color.SelectedWidgets));
        Assert.Equal("Green", color.ExportValue);
        Assert.Equal(["Red", "Green", "Green"], color.Widgets.Select(color.GetWidgetExportValue));
        Assert.Equal(["Off", "1", "Off"], color.Widgets.Select(widget => color.GetAppearanceState(widget).Value));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_push_button_has_no_value_and_its_widget_action_names_fields_that_resolve_through_the_form()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        var submit = (PdfPushButtonField)form.FindField("submit")!;

        PdfResetFormAction reset = Assert.IsType<PdfResetFormAction>(Assert.Single(submit.Widgets).Action);

        Assert.Null(submit.ValueObject);
        Assert.Equal(
            [form.FindField("person"), form.FindField("agree")],
            reset.Fields!.SelectMany(form.FindFields));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Choice_fields_read_options_values_and_selected_indices()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        var country = (PdfComboBoxField)form.FindField("country")!;
        var toppings = (PdfListBoxField)form.FindField("toppings")!;

        Assert.Equal((true, false), (country.IsEditable, country.IsMultiSelect));
        Assert.Equal(["us United States", "ca Canada"], country.Options.Select(option => $"{option.ExportValue} {option.DisplayText}"));
        Assert.Equal(["ca"], country.Values);
        Assert.Equal([1], country.SelectedIndices);
        Assert.Empty(country.StoredIndices);

        Assert.True(toppings.IsMultiSelect);
        Assert.Equal(["Cheese", "Ham", "Olives"], toppings.Options.Select(option => option.DisplayText));
        Assert.Equal(["Cheese", "Olives"], toppings.Values);
        Assert.Equal([0, 2], toppings.SelectedIndices);
        Assert.Equal([0, 2], toppings.StoredIndices);
        Assert.Equal(1, toppings.TopIndex);
        Assert.Equal("/Helv 10 Tf 0 g", toppings.DefaultAppearance);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_signature_field_exposes_its_lock_and_seed_value_and_is_invisible_and_unsigned()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var sig = (PdfSignatureField)document.AcroForm!.FindField("sig")!;

        Assert.False(sig.IsSigned);
        Assert.Null(sig.SignatureDictionary);
        Assert.False(sig.IsVisible);
        PdfSignatureFieldLock fieldLock = sig.Lock!;
        Assert.Equal(PdfSignatureFieldLockAction.Include, fieldLock.Action);
        Assert.Equal(["name", "person.first"], fieldLock.FieldNames);
        Assert.Equal(2, fieldLock.Permissions);
        Assert.Equal(new CosName("Adobe.PPKLite"), sig.SeedValue![new CosName("Filter")]);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Reading_every_form_entry_records_nothing_and_writes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));

        int fields = FormWalker.Walk(document);

        Assert.Equal(10, fields);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity > DiagnosticSeverity.Information);
        Assert.All(document.AcroForm!.AllFields, field => Assert.False(field.Dictionary.IsDirty));
        Assert.False(document.AcroForm.Dictionary.IsDirty);
    }
}
