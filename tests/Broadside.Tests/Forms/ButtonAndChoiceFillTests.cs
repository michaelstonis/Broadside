using System.Text;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Writing;
using Broadside.TestSupport;
using Broadside.Text;

namespace Broadside.Tests.Forms;

/// <summary>Setting check boxes, radio buttons and choice fields (ISO 32000-2 §12.7.5.2 and §12.7.5.4), read back after a save.</summary>
public sealed class ButtonAndChoiceFillTests
{
    private static readonly CosName Off = new("Off");
    private static readonly CosName Yes = new("Yes");
    private static readonly CosName Ff = new("Ff");
    private static readonly CosName MK = new("MK");
    private static readonly CosName CA = new("CA");
    private static readonly CosName AP = new("AP");
    private static readonly CosName AS = new("AS");
    private static readonly CosName V = new("V");

    [Fact]
    public void An_unchecked_check_box_re_reads_off_in_its_value_and_every_widgets_appearance_state()
    {
        byte[] saved = Fill<PdfCheckBoxField>("agree", field => field.SetChecked(false));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfCheckBoxField)reopened.AcroForm!.FindField("agree")!;
        Assert.False(field.IsChecked);
        Assert.Equal(Off, field.Value);
        Assert.All(field.Widgets, widget => Assert.Equal(Off, widget.AppearanceState));
        Assert.Empty(field.SelectedWidgets);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void A_check_box_checked_again_turns_on_both_of_its_duplicated_widgets()
    {
        byte[] saved = Fill<PdfCheckBoxField>("agree", field =>
        {
            field.SetChecked(false);
            field.SetChecked(true);
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfCheckBoxField)reopened.AcroForm!.FindField("agree")!;
        Assert.True(field.IsChecked);
        Assert.Equal(Yes, field.Value);
        Assert.All(field.Widgets, widget => Assert.Equal(Yes, widget.AppearanceState));
        Assert.Equal(2, field.SelectedWidgets.Count);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Selecting_a_radio_button_turns_the_others_off_and_re_reads_its_state_and_export_value()
    {
        byte[] saved = Fill<PdfRadioButtonField>("color", field => field.SetValue(new CosName("0")));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfRadioButtonField)reopened.AcroForm!.FindField("color")!;
        Assert.Equal(new CosName("0"), field.Value);
        Assert.Equal("Red", field.ExportValue);
        Assert.Equal([new CosName("0"), Off, Off], field.Widgets.Select(widget => widget.AppearanceState));
        Assert.Same(field.Widgets[0], Assert.Single(field.SelectedWidgets));
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Radio_buttons_with_the_same_export_value_turn_on_one_at_a_time_without_radios_in_unison()
    {
        byte[] saved = Fill<PdfRadioButtonField>("color", field =>
        {
            field.SetExportValue("Red");
            field.SetExportValue("Green");
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfRadioButtonField)reopened.AcroForm!.FindField("color")!;
        Assert.Equal([Off, new CosName("1"), Off], field.Widgets.Select(widget => widget.AppearanceState));
        Assert.Equal("Green", field.ExportValue);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void A_radio_group_in_unison_turns_on_every_widget_with_the_same_export_value()
    {
        byte[] saved = Fill<PdfRadioButtonField>("color", field =>
        {
            field.Dictionary[Ff] = new CosInteger((long)(PdfFieldFlags.Radio | PdfFieldFlags.NoToggleToOff | PdfFieldFlags.RadiosInUnison));
            field.SetValue(new CosName("0"));
            field.Select(field.Widgets[2]);
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfRadioButtonField)reopened.AcroForm!.FindField("color")!;
        Assert.True(field.IsRadiosInUnison);
        Assert.Equal([Off, new CosName("1"), new CosName("2")], field.Widgets.Select(widget => widget.AppearanceState));
        Assert.Equal(new CosName("2"), field.Value);
        Assert.Equal("Green", field.ExportValue);
        Assert.Equal(field.Widgets.Skip(1), field.SelectedWidgets);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Radio_buttons_with_no_toggle_to_off_refuse_to_turn_every_button_off_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var field = (PdfRadioButtonField)document.AcroForm!.FindField("color")!;

        Assert.Throws<InvalidOperationException>(() => field.SetValue(Off));
        Assert.Throws<ArgumentException>(() => field.SetValue(new CosName("Blue")));
        Assert.Throws<ArgumentException>(() => field.SetExportValue("Blue"));
        Assert.Equal(new CosName("1"), field.Value);
        Assert.Equal([Off, new CosName("1"), Off], field.Widgets.Select(widget => widget.AppearanceState));
    }

    [Fact]
    public void Generated_check_marks_show_the_mk_caption_character_in_zapf_dingbats()
    {
        byte[] saved = Fill<PdfCheckBoxField>("agree", field =>
        {
            ((CosDictionary)field.Widgets[0].Dictionary[MK])[CA] = new CosString("8"u8);
            field.GenerateAppearances();
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfCheckBoxField)reopened.AcroForm!.FindField("agree")!;
        Assert.True(field.IsChecked);

        // Widget 15 has the caption 8 (a cross), widget 25 none: a check box then shows a check mark, code 4 (§12.5.6.19, Annex D.6).
        foreach ((PdfWidgetAnnotation widget, char code) in field.Widgets.Zip("84"))
        {
            Assert.Equal(Yes, widget.AppearanceState);
            PdfAppearanceEntry normal = widget.AppearanceDictionary!.Normal!;
            Assert.Equal(["Off", "Yes"], normal.StateNames.Select(state => state.Value).Order());
            Assert.NotEqual(new CosReference(8, 0), normal.GetAppearance(Yes)!.Reference);

            TextGlyph glyph = Assert.Single(TextFieldFillTests.AppearanceText(widget).Glyphs);
            Assert.Equal("ZapfDingbats", glyph.Font.BaseFont);
            Assert.Equal(code, (char)glyph.CharacterCode);
            Assert.InRange(glyph.Quad.Bounds.Left, widget.Rect.Left, widget.Rect.Right);
            Assert.InRange(glyph.Quad.Bounds.Right, widget.Rect.Left, widget.Rect.Right);
            Assert.InRange(glyph.Origin.Y, widget.Rect.Bottom, widget.Rect.Top);

            // The off appearance shows nothing.
            Assert.DoesNotContain("Tj", Encoding.Latin1.GetString(normal.GetAppearance(Off)!.Decode().Span), StringComparison.Ordinal);
        }

        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Generated_radio_button_appearances_keep_their_on_states_and_show_a_dot_on_the_selected_button_in_poppler()
    {
        byte[] saved = Fill<PdfRadioButtonField>("color", field => field.GenerateAppearances());

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfRadioButtonField)reopened.AcroForm!.FindField("color")!;
        Assert.Equal(new CosName("1"), field.Value);
        Assert.Equal([Off, new CosName("1"), Off], field.Widgets.Select(widget => widget.AppearanceState));
        Assert.Equal([new CosName("0"), new CosName("1"), new CosName("2")], field.OnStateNames);
        Assert.Equal('l', (char)Assert.Single(TextFieldFillTests.AppearanceText(field.Widgets[1]).Glyphs).CharacterCode);

        Assert.Equal(0, TextFieldFillTests.DarkPixels(saved, field.Widgets[0].Rect));
        Assert.True(TextFieldFillTests.DarkPixels(saved, field.Widgets[1].Rect) > 20);
        Assert.Equal(0, TextFieldFillTests.DarkPixels(saved, field.Widgets[2].Rect));
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void A_check_box_without_appearance_states_gets_a_yes_state_from_generation_and_can_then_be_checked()
    {
        byte[] saved = Fill<PdfCheckBoxField>("agree", field =>
        {
            foreach (PdfWidgetAnnotation widget in field.Widgets)
            {
                widget.Dictionary.Remove(AP);
                widget.Dictionary.Remove(AS);
            }

            field.Dictionary[V] = Off;
            Assert.Throws<InvalidOperationException>(() => field.SetChecked(true));
            field.GenerateAppearances();
            field.SetChecked(true);
        });

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfCheckBoxField)reopened.AcroForm!.FindField("agree")!;
        Assert.Equal(Yes, field.Value);
        Assert.All(field.Widgets, widget => Assert.Equal(Yes, widget.AppearanceState));
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void A_combo_box_set_by_displayed_text_stores_the_export_value_and_shows_the_text()
    {
        byte[] saved = Fill<PdfComboBoxField>("country", field => field.SetValue("United States"));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfComboBoxField)reopened.AcroForm!.FindField("country")!;
        Assert.Equal(["us"], field.Values);
        Assert.Equal([0], field.SelectedIndices);
        Assert.Empty(field.StoredIndices);
        Assert.Equal("United States", TextFieldFillTests.AppearanceText(Assert.Single(field.Widgets)).Text);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void An_editable_combo_box_takes_text_that_is_no_option_and_a_plain_one_refuses_it()
    {
        byte[] saved = Fill<PdfComboBoxField>("country", field => field.SetValue("Mexico"));

        using (PdfDocument reopened = PdfDocument.Open(saved))
        {
            var field = (PdfComboBoxField)reopened.AcroForm!.FindField("country")!;
            Assert.Equal(["Mexico"], field.Values);
            Assert.Empty(field.SelectedIndices);
            Assert.Equal("Mexico", TextFieldFillTests.AppearanceText(Assert.Single(field.Widgets)).Text);
            AssertNoWarnings(reopened);
        }

        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var plain = (PdfComboBoxField)document.AcroForm!.FindField("country")!;
        plain.Dictionary[Ff] = new CosInteger((long)PdfFieldFlags.Combo);
        Assert.Throws<ArgumentException>(() => plain.SetValue("Mexico"));
        Assert.Equal(["ca"], plain.Values);
    }

    [Fact]
    public void A_multi_select_list_box_stores_values_and_indices_and_shows_its_options_from_the_top_index_with_the_selection_highlighted()
    {
        byte[] saved = Fill<PdfListBoxField>("toppings", field => field.SetSelectedIndices([2, 1]));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfListBoxField)reopened.AcroForm!.FindField("toppings")!;
        Assert.Equal(["Ham", "Olives"], field.Values);
        Assert.Equal([1, 2], field.StoredIndices);
        Assert.Equal([1, 2], field.SelectedIndices);
        Assert.Equal(1, field.TopIndex);

        PdfWidgetAnnotation widget = Assert.Single(field.Widgets);
        TextPage text = TextFieldFillTests.AppearanceText(widget);
        Assert.Equal(["Ham", "Olives"], Lines(text));
        Assert.All(text.Glyphs, glyph => Assert.Equal(10, glyph.FontSize));
        Assert.Contains("0.6 0.757 0.855 rg", Encoding.Latin1.GetString(widget.GetAppearance()!.Decode().Span), StringComparison.Ordinal);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Selecting_an_option_above_the_top_index_scrolls_the_list_box_to_it()
    {
        byte[] saved = Fill<PdfListBoxField>("toppings", field => field.SetSelectedIndices([0]));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfListBoxField)reopened.AcroForm!.FindField("toppings")!;
        Assert.Equal(["Cheese"], field.Values);
        Assert.Equal([0], field.SelectedIndices);
        Assert.Equal(0, field.TopIndex);
        Assert.False(field.Dictionary.ContainsKey(new CosName("TI")));
        Assert.Equal(["Cheese", "Ham", "Olives"], Lines(TextFieldFillTests.AppearanceText(field.Widgets[0])));
    }

    [Fact]
    public void Setting_the_top_index_keeps_the_selection_and_shows_the_list_from_there()
    {
        byte[] saved = Fill<PdfListBoxField>("toppings", field => field.SetTopIndex(2));

        using PdfDocument reopened = PdfDocument.Open(saved);
        var field = (PdfListBoxField)reopened.AcroForm!.FindField("toppings")!;
        Assert.Equal(2, field.TopIndex);
        Assert.Equal([0, 2], field.SelectedIndices);
        Assert.Equal(["Olives"], Lines(TextFieldFillTests.AppearanceText(field.Widgets[0])));
    }

    [Fact]
    public void A_list_box_cleared_stores_no_value_and_a_single_select_one_refuses_several_options()
    {
        byte[] saved = Fill<PdfListBoxField>("toppings", field => field.SetValue(null));

        using (PdfDocument reopened = PdfDocument.Open(saved))
        {
            var field = (PdfListBoxField)reopened.AcroForm!.FindField("toppings")!;
            Assert.Empty(field.Values);
            Assert.Empty(field.StoredIndices);
            Assert.Empty(field.SelectedIndices);
        }

        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        var single = (PdfListBoxField)document.AcroForm!.FindField("toppings")!;
        single.Dictionary.Remove(Ff);
        Assert.Throws<ArgumentException>(() => single.SetSelectedIndices([0, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => single.SetSelectedIndices([3]));
        Assert.Throws<ArgumentException>(() => single.SetValue("Pineapple"));
        Assert.Equal(["Cheese", "Olives"], single.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_button_and_choice_of_acroform_fields_pdf_set_and_saved_re_reads_its_value_states_and_indices(bool incremental)
    {
        byte[] original = File.ReadAllBytes(Corpus.Path("acroform-fields.pdf"));
        byte[] saved;
        using (PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf")))
        {
            PdfAcroForm form = document.AcroForm!;
            ((PdfCheckBoxField)form.FindField("agree")!).SetChecked(false);
            var color = (PdfRadioButtonField)form.FindField("color")!;
            color.Select(color.Widgets[2]);
            ((PdfComboBoxField)form.FindField("country")!).SetValue("us");
            ((PdfListBoxField)form.FindField("toppings")!).SetSelectedIndices([1]);
            saved = TextFieldFillTests.Save(document, new PdfSaveOptions().WithIncrementalUpdate(incremental));
        }

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfAcroForm reread = reopened.AcroForm!;
        var agree = (PdfCheckBoxField)reread.FindField("agree")!;
        Assert.Equal(Off, agree.Value);
        Assert.All(agree.Widgets, widget => Assert.Equal(Off, widget.AppearanceState));

        var colour = (PdfRadioButtonField)reread.FindField("color")!;
        Assert.Equal(new CosName("2"), colour.Value);
        Assert.Equal("Green", colour.ExportValue);
        Assert.Equal([Off, Off, new CosName("2")], colour.Widgets.Select(widget => widget.AppearanceState));

        var country = (PdfComboBoxField)reread.FindField("country")!;
        Assert.Equal(["us"], country.Values);
        Assert.Equal([0], country.SelectedIndices);

        var toppings = (PdfListBoxField)reread.FindField("toppings")!;
        Assert.Equal(["Ham"], toppings.Values);
        Assert.Equal([1], toppings.StoredIndices);
        Assert.Equal([1], toppings.SelectedIndices);

        Assert.True(TextFieldFillTests.DarkPixels(saved, country.Widgets[0].Rect) > 10);
        Assert.True(TextFieldFillTests.DarkPixels(saved, toppings.Widgets[0].Rect) > 10);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        AssertNoWarnings(reopened);
        if (incremental)
        {
            Assert.Equal(original, saved[..original.Length]);
            Assert.Equal(2, reopened.Revisions.Count);
        }
    }

    private static string[] Lines(TextPage text) =>
        [.. text.Glyphs.GroupBy(glyph => Math.Round(glyph.Origin.Y, 2)).OrderByDescending(line => line.Key)
            .Select(line => string.Concat(line.Select(glyph => text.GetGlyphText(glyph).ToString())))];

    private static void AssertNoWarnings(PdfDocument document) =>
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);

    private static byte[] Fill<T>(string name, Action<T> fill)
        where T : PdfField
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("acroform-fields.pdf"));
        fill((T)document.AcroForm!.FindField(name)!);
        return TextFieldFillTests.Save(document);
    }
}
