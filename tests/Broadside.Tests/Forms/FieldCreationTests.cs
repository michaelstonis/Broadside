using System.Security.Cryptography.X509Certificates;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Signatures;
using Broadside.Tests.Document;
using Broadside.Tests.Writing;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>Creating form fields with widgets on pages (ISO 32000-2 §12.7.3 and §12.7.4), read back through the form reader after a save.</summary>
public sealed class FieldCreationTests
{
    private static readonly CosName Off = new("Off");

    [Fact]
    public void A_text_field_created_on_a_new_document_re_reads_its_type_flags_and_widget_on_the_page()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        PdfAcroForm form = document.CreateAcroForm();

        PdfTextField created = form.AddTextField("name", page, new PdfRectangle(72, 700, 272, 724), new PdfFieldOptions().WithFlags(PdfFieldFlags.Required | PdfFieldFlags.DoNotSpellCheck));
        Assert.Equal("name", created.FullyQualifiedName);

        byte[] saved = TextFieldFillTests.Save(document);

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfAcroForm reread = reopened.AcroForm!;
        PdfTextField field = Assert.IsType<PdfTextField>(Assert.Single(reread.Fields));
        Assert.Equal("name", field.FullyQualifiedName);
        Assert.Equal(PdfFieldFlags.Required | PdfFieldFlags.DoNotSpellCheck, field.Flags);
        PdfWidgetAnnotation widget = Assert.Single(field.Widgets);
        Assert.Equal(new PdfRectangle(72, 700, 272, 724), widget.Rect);
        Assert.Same(widget, Assert.Single(reopened.Pages[0].Annotations));
        Assert.NotNull(widget.GetAppearance());
        Assert.False(reread.NeedAppearances);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Dotted_names_create_the_missing_non_terminal_ancestors_once_and_reuse_existing_ones()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        PdfAcroForm form = document.CreateAcroForm();

        form.AddTextField("person.address.zip", page, new PdfRectangle(72, 700, 172, 720));
        form.AddTextField("person.address.city", page, new PdfRectangle(72, 670, 172, 690));
        form.AddCheckBox("person.subscribed", page, new PdfRectangle(72, 640, 86, 654));
        form.AddTextField("note", page, new PdfRectangle(72, 610, 172, 630));

        using PdfDocument reopened = PdfDocument.Open(TextFieldFillTests.Save(document));
        PdfAcroForm reread = reopened.AcroForm!;
        Assert.Equal(["person", "note"], reread.Fields.Select(field => field.FullyQualifiedName));
        Assert.Equal(
            [
                "person PdfNonTerminalField", "person.address PdfNonTerminalField", "person.address.zip PdfTextField",
                "person.address.city PdfTextField", "person.subscribed PdfCheckBoxField", "note PdfTextField",
            ],
            reread.AllFields.Select(field => $"{field.FullyQualifiedName} {field.GetType().Name}"));
        PdfNonTerminalField address = Assert.IsType<PdfNonTerminalField>(reread.FindField("person.address"));
        Assert.Same(reread.FindField("person"), address.Parent);
        Assert.All(address.Children, child => Assert.Same(address, child.Parent));
        Assert.Equal(4, reopened.Pages[0].Annotations.Count);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Every_kind_of_field_created_re_reads_with_its_type_flags_widgets_and_appearances()
    {
        byte[] saved = CreateOneOfEach(withValues: false);

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfAcroForm form = reopened.AcroForm!;
        Assert.False(form.NeedAppearances);
        Assert.Equal(PdfSignatureFlags.SignaturesExist, form.SignatureFlags);
        Assert.Equal(
            ["text Text", "check CheckBox", "radio RadioButton", "combo ComboBox", "list ListBox", "button PushButton", "signature Signature"],
            form.Fields.Select(field => $"{field.FullyQualifiedName} {field.Kind}"));

        var text = (PdfTextField)form.FindField("text")!;
        Assert.Equal(PdfFieldFlags.Multiline, text.Flags);
        Assert.Equal(PdfTextJustification.Centered, text.Quadding);
        Assert.Equal("Your name", text.AlternateName);

        var check = (PdfCheckBoxField)form.FindField("check")!;
        Assert.False(check.IsChecked);
        Assert.Equal([new CosName("Yes")], check.OnStateNames);
        Assert.Equal(Off, Assert.Single(check.Widgets).AppearanceState);

        var radio = (PdfRadioButtonField)form.FindField("radio")!;
        Assert.Equal(PdfFieldFlags.Radio | PdfFieldFlags.NoToggleToOff, radio.Flags);
        Assert.Equal([new CosName("Red"), new CosName("Green"), new CosName("Blue")], radio.OnStateNames);
        Assert.Equal(Off, radio.Value);
        Assert.All(radio.Widgets, widget => Assert.Equal(Off, widget.AppearanceState));

        var combo = (PdfComboBoxField)form.FindField("combo")!;
        Assert.True(combo.IsEditable);
        Assert.Equal(["ca Canada", "us United States"], combo.Options.Select(option => $"{option.ExportValue} {option.DisplayText}"));
        Assert.Empty(combo.Values);

        var list = (PdfListBoxField)form.FindField("list")!;
        Assert.True(list.IsMultiSelect);
        Assert.Equal(["Cheese", "Olives", "Peppers"], list.Options.Select(option => option.DisplayText));

        var button = (PdfPushButtonField)form.FindField("button")!;
        Assert.Equal(PdfFieldFlags.Pushbutton, button.Flags);
        Assert.Equal("Send", Assert.Single(button.Widgets).AppearanceCharacteristics!.NormalCaption);

        var signature = (PdfSignatureField)form.FindField("signature")!;
        Assert.False(signature.IsSigned);
        Assert.True(signature.IsVisible);

        Assert.All(form.TerminalFields.SelectMany(field => field.Widgets), widget =>
        {
            Assert.NotNull(widget.GetAppearance());
            Assert.Equal(PdfAnnotationFlags.Print, widget.Flags);
            Assert.Same(reopened.Pages[0], widget.Page);
        });
        Assert.Equal(9, reopened.Pages[0].Annotations.Count);
        (int exitCode, string output) = ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Poppler_renders_every_created_widget_with_its_value_and_nothing_where_a_radio_button_is_off()
    {
        byte[] saved = CreateOneOfEach(withValues: true);

        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfAcroForm form = reopened.AcroForm!;
        Assert.Equal("Ada", ((PdfTextField)form.FindField("text")!).Value);
        Assert.True(((PdfCheckBoxField)form.FindField("check")!).IsChecked);
        Assert.Equal(new CosName("Green"), ((PdfRadioButtonField)form.FindField("radio")!).Value);
        Assert.Equal(["us"], ((PdfComboBoxField)form.FindField("combo")!).Values);
        Assert.Equal(["Cheese", "Peppers"], ((PdfListBoxField)form.FindField("list")!).Values);

        // No widget has a border here: the ink inside each rectangle is its value, caption or background.
        foreach (PdfWidgetAnnotation widget in form.TerminalFields.SelectMany(field => field.Widgets))
        {
            bool off = widget.Field is PdfRadioButtonField && Off.Equals(widget.AppearanceState);
            int ink = TextFieldFillTests.DarkPixels(saved, widget.Rect);
            Assert.True(off ? ink == 0 : ink > 10, $"{widget.Field!.FullyQualifiedName}: {ink} dark pixels");
        }

        AssertNoWarnings(reopened);
    }

    [Fact]
    public void Fields_added_to_an_existing_form_reuse_its_default_font_join_its_hierarchy_and_save_incrementally()
    {
        byte[] original = Corpus.Bytes("acroform-fields.pdf");
        using PdfDocument document = PdfDocument.Open(original);
        PdfAcroForm form = document.AcroForm!;
        int fonts = ((CosDictionary)document.Resolve(form.DefaultResources![new CosName("Font")])).Count;
        string? last = ((PdfTextField)form.FindField("person.last")!).Value;

        form.AddTextField("person.middle", document.Pages[0], new PdfRectangle(320, 300, 520, 324));
        form.AddCheckBox("extras.gift", document.Pages[1], new PdfRectangle(320, 300, 340, 320));
        Assert.Equal(fonts, ((CosDictionary)document.Resolve(form.DefaultResources![new CosName("Font")])).Count);

        byte[] saved = TextFieldFillTests.Save(document, new PdfSaveOptions().WithIncrementalUpdate(true));

        Assert.Equal(original, saved[..original.Length]);
        using PdfDocument reopened = PdfDocument.Open(saved);
        PdfAcroForm reread = reopened.AcroForm!;
        Assert.Equal(["first", "last", "middle"], ((PdfNonTerminalField)reread.FindField("person")!).Children.Select(child => child.PartialName));
        var middle = (PdfTextField)reread.FindField("person.middle")!;
        Assert.Equal("/Helv 0 Tf 0 g", middle.DefaultAppearance);
        Assert.Equal(PdfFieldFlags.Required, middle.Flags);
        Assert.Same(reopened.Pages[1], Assert.Single(((PdfCheckBoxField)reread.FindField("extras.gift")!).Widgets).Page);
        Assert.Equal(last, ((PdfTextField)reread.FindField("person.last")!).Value);
        Assert.Equal(11, reread.TerminalFields.Count);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void A_form_without_default_resources_gets_helvetica_zapf_dingbats_and_a_default_appearance()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>"));

        document.AcroForm!.AddCheckBox("agree", document.Pages[0], new PdfRectangle(72, 700, 92, 720));

        using PdfDocument reopened = PdfDocument.Open(TextFieldFillTests.Save(document));
        PdfAcroForm form = reopened.AcroForm!;
        var fonts = (CosDictionary)reopened.Resolve(form.DefaultResources![new CosName("Font")]);
        Assert.Equal(["Helvetica", "ZapfDingbats"], fonts.Select(entry => ((CosName)((CosDictionary)reopened.Resolve(entry.Value))[new CosName("BaseFont")]).Value));
        Assert.Equal("/Helv 0 Tf 0 g", form.DefaultAppearance);
        Assert.Equal("/ZaDb 0 Tf 0 g", form.FindField("agree")!.DefaultAppearance);
        AssertNoWarnings(reopened);
    }

    [Fact]
    public void A_name_already_taken_or_under_a_terminal_field_is_refused_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("acroform-fields.pdf"));
        PdfAcroForm form = document.AcroForm!;
        PdfPage page = document.Pages[0];
        string before = CosSnapshot.Of(document);

        Assert.Throws<InvalidOperationException>(() => form.AddTextField("name", page, new PdfRectangle(0, 0, 10, 10)));
        Assert.Throws<InvalidOperationException>(() => form.AddTextField("person.first", page, new PdfRectangle(0, 0, 10, 10)));
        Assert.Throws<InvalidOperationException>(() => form.AddTextField("name.child", page, new PdfRectangle(0, 0, 10, 10)));
        Assert.Throws<InvalidOperationException>(() => document.CreateAcroForm());

        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void Options_that_do_not_apply_to_the_field_are_refused_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        PdfAcroForm form = document.CreateAcroForm();
        using PdfDocument other = PdfDocument.Create();
        var rect = new PdfRectangle(72, 700, 272, 724);
        string before = CosSnapshot.Of(document);

        Assert.Throws<ArgumentException>(() => form.AddCheckBox("a", page, rect, new PdfFieldOptions().WithFlags(PdfFieldFlags.Multiline)));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a", page, rect, new PdfFieldOptions().WithFlags(PdfFieldFlags.Comb)));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a", page, rect, new PdfFieldOptions().WithFlags(PdfFieldFlags.Comb | PdfFieldFlags.Multiline).WithMaxLength(4)));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a", page, rect, new PdfFieldOptions().WithCaption("x")));
        Assert.Throws<ArgumentException>(() => form.AddComboBox("a", page, rect, [], new PdfFieldOptions().WithMaxLength(3)));
        Assert.Throws<ArgumentException>(() => form.AddComboBox("a", page, rect, [], new PdfFieldOptions().WithFlags(PdfFieldFlags.MultiSelect)));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a..b", page, rect));
        Assert.Throws<ArgumentException>(() => form.AddTextField(string.Empty, page, rect));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a", other.Pages[0], rect));
        Assert.Throws<ArgumentException>(() => form.AddTextField("a", page, new PdfRectangle(0, 0, double.NaN, 10)));
        Assert.Throws<ArgumentException>(() => form.AddRadioButtons("a", []));
        Assert.Throws<ArgumentException>(() => new PdfRadioButton(page, rect, "Off"));

        Assert.Equal(before, CosSnapshot.Of(document));
    }

    [Fact]
    public void A_field_whose_options_no_font_can_show_is_refused_and_nothing_changes()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseSystemFontResolver(null));
        PdfAcroForm form = document.CreateAcroForm();
        string before = CosSnapshot.Of(document);

        Assert.Throws<InvalidOperationException>(() =>
            form.AddListBox("city.name", document.Pages[0], new PdfRectangle(72, 600, 272, 700), [new PdfChoiceOption("Москва")]));

        Assert.Equal(before, CosSnapshot.Of(document));
        Assert.Empty(form.Fields);
    }

    [Fact]
    public void A_created_signature_field_is_signed_in_place_by_name()
    {
        using X509Certificate2 certificate = Signatures.TestCertificates.SelfSigned("CN=Broadside signer");
        using PdfDocument document = PdfDocument.Create();
        document.CreateAcroForm().AddSignatureField("approval", document.Pages[0], new PdfRectangle(72, 72, 272, 132));
        using var output = new MemoryStream();

        document.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithFieldName("approval"));

        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        var field = Assert.IsType<PdfSignatureField>(Assert.Single(reopened.AcroForm!.TerminalFields));
        Assert.True(field.IsSigned);
        Assert.True(field.IsVisible);
        Assert.Equal(1, Signatures.Pdfsig.ValidSignatures(Signatures.Pdfsig.Run(output.ToArray())));
    }

    /// <summary>A document with one field of each kind on its page, filled and without borders when <paramref name="withValues"/>; the saved bytes.</summary>
    private static byte[] CreateOneOfEach(bool withValues)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfPage page = document.Pages[0];
        PdfAcroForm form = document.CreateAcroForm();
        PdfFieldOptions Options() => withValues ? new PdfFieldOptions().WithBorderColor(null) : new PdfFieldOptions();

        PdfTextField text = form.AddTextField("text", page, new PdfRectangle(72, 700, 272, 730), Options()
            .WithFlags(PdfFieldFlags.Multiline).WithQuadding(PdfTextJustification.Centered).WithAlternateName("Your name").WithFontSize(14));
        PdfCheckBoxField check = form.AddCheckBox("check", page, new PdfRectangle(72, 660, 92, 680), Options());
        PdfRadioButtonField radio = form.AddRadioButtons(
            "radio",
            [
                new PdfRadioButton(page, new PdfRectangle(72, 620, 92, 640), "Red"),
                new PdfRadioButton(page, new PdfRectangle(102, 620, 122, 640), "Green"),
                new PdfRadioButton(page, new PdfRectangle(132, 620, 152, 640), "Blue"),
            ],
            Options().WithFlags(PdfFieldFlags.NoToggleToOff));
        PdfComboBoxField combo = form.AddComboBox(
            "combo", page, new PdfRectangle(72, 570, 272, 594), [new PdfChoiceOption("ca", "Canada"), new PdfChoiceOption("us", "United States")], Options().WithFlags(PdfFieldFlags.Edit));
        PdfListBoxField list = form.AddListBox(
            "list", page, new PdfRectangle(72, 480, 272, 550), [new PdfChoiceOption("Cheese"), new PdfChoiceOption("Olives"), new PdfChoiceOption("Peppers")], Options().WithFlags(PdfFieldFlags.MultiSelect));
        form.AddPushButton("button", page, new PdfRectangle(72, 430, 172, 460), Options().WithCaption("Send").WithBackgroundColor(new PdfDeviceColor(0.85)));
        form.AddSignatureField("signature", page, new PdfRectangle(72, 330, 272, 410), Options().WithBackgroundColor(new PdfDeviceColor(0.2)));

        if (withValues)
        {
            text.SetValue("Ada");
            check.SetChecked(true);
            radio.SetValue(new CosName("Green"));
            combo.SetValue("us");
            list.SetSelectedIndices([0, 2]);
        }

        return TextFieldFillTests.Save(document);
    }

    private static void AssertNoWarnings(PdfDocument document) =>
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Severity != DiagnosticSeverity.Information);
}
