using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Text;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>form-fill</c> target (issue #167): opens the input as a whole file and fills up to 32 text fields with a Latin value (cut
/// to the field's <c>MaxLen</c>), which generates their appearances from whatever <c>DA</c>, <c>DR</c>, <c>MK</c>, <c>BS</c>,
/// <c>Rect</c>, <c>Q</c> and flags the file holds; then runs every generated appearance through a text extractor, saves, and
/// reopens. Invariants: filling never throws (the value is one Helvetica can always show, so no font program is needed; the system
/// font resolver is off so runs are deterministic); each widget's normal appearance is afterwards a form XObject whose text is
/// at most the value's length; the saved file opens with the same page count. Check boxes, radio buttons and choice fields are
/// then set too (issue #168, <see cref="FillButtonsAndChoices"/>). An encrypted input stops after filling (the <c>save</c> target
/// covers encrypted saves); one read as not encrypted, a damaged <c>Encrypt</c> entry included, must save (issue #283:
/// <see cref="PdfDocument.IsEncrypted"/> and the save agree), so no exception is tolerated there. Fields of every kind are then
/// created on the first page (issue #169, <see cref="CreateFields"/>) and must re-read after the save.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.3, §12.7.4, §12.7.4.3, §12.7.5.2, §12.7.5.3, §12.7.5.4, §12.5.5, §7.6.2.</remarks>
internal static class FormFillTarget
{
    private const string Value = "Fill 42 été\nzwei";

    private static readonly PdfEngine Engine = new(new PdfOptions().UseSystemFontResolver(null));

    public static void Target(ReadOnlySpan<byte> data)
    {
        using PdfDocument? document = FuzzTargets.OpenOrNull(data, Engine);
        if (document?.AcroForm is not { } form)
        {
            return;
        }

        int pages = document.Pages.Count;
        foreach (PdfTextField field in form.TerminalFields.OfType<PdfTextField>().Take(32))
        {
            string value = field.MaxLength is int length && length >= 0 && length < Value.Length ? Value[..length] : Value;
            field.SetValue(value);
            foreach (PdfWidgetAnnotation widget in field.Widgets)
            {
                if (widget.GetAppearance() is not { } appearance)
                {
                    throw new InvalidOperationException("A filled widget has no normal appearance.");
                }

                var extractor = new TextExtractor();
                if (widget.ProcessAppearance(appearance, extractor) && extractor.Text.Length > value.Length)
                {
                    throw new InvalidOperationException($"The appearance shows {extractor.Text.Length} characters for a value of {value.Length}.");
                }
            }
        }

        FillButtonsAndChoices(form);
        bool created = CreateFields(document, form);
        if (document.IsEncrypted)
        {
            return;
        }

        using var output = new MemoryStream();
        document.Save(output);
        using PdfDocument saved = PdfDocument.Open(output.ToArray());
        if (saved.Pages.Count != pages)
        {
            throw new InvalidOperationException($"The saved file has {saved.Pages.Count} pages; the input had {pages}.");
        }

        if (created && CreatedNames.Any(name => saved.AcroForm?.FindField(name) is not PdfTerminalField { Widgets.Count: > 0 }))
        {
            throw new InvalidOperationException("A created field does not re-read with its widget after the save.");
        }

        _ = saved.AcroForm?.TerminalFields.OfType<PdfTextField>().Select(field => field.Value).Count();
        _ = saved.AcroForm?.TerminalFields.OfType<PdfToggleButtonField>().Select(field => field.SelectedWidgets.Count).Count();
        _ = saved.AcroForm?.TerminalFields.OfType<PdfChoiceField>().Select(field => field.SelectedIndices.Count).Count();
    }

    /// <summary>The fields <see cref="CreateFields"/> adds.</summary>
    private static readonly string[] CreatedNames = ["BrsFuzz.text", "BrsFuzz.check", "BrsFuzz.radio", "BrsFuzz.list", "BrsFuzz.button", "BrsFuzz.sign"];

    /// <summary>
    /// Issue #169: creates one field of several kinds on the first page, under a non-terminal field <c>BrsFuzz</c>, whatever the form's
    /// <c>Fields</c>, <c>DR</c> and <c>DA</c> and the page's <c>Annots</c> hold. Invariants: each is found by its name afterwards
    /// with its kind and a widget with a normal appearance on the page. A name the file already uses refuses (the documented
    /// <see cref="InvalidOperationException"/>, nothing changed); so does a page the page tree does not hold as an indirect object.
    /// </summary>
    /// <returns>Whether the fields were created.</returns>
    private static bool CreateFields(PdfDocument document, PdfAcroForm form)
    {
        if (document.Pages.Count == 0 || document.Pages[0] is not { Reference: not null } page || form.FindFields("BrsFuzz").Count > 0)
        {
            return false;
        }

        var options = new PdfFieldOptions().WithBackgroundColor(new PdfDeviceColor(0.9));
        try
        {
            form.AddTextField(CreatedNames[0], page, new PdfRectangle(10, 10, 110, 30), options);
        }
        catch (ArgumentException)
        {
            // A page view the page collection does not hold (a damaged tree) is refused before anything changes.
            return false;
        }

        form.AddCheckBox(CreatedNames[1], page, new PdfRectangle(120, 10, 140, 30), options);
        form.AddRadioButtons(CreatedNames[2], [new PdfRadioButton(page, new PdfRectangle(150, 10, 170, 30), "A"), new PdfRadioButton(page, new PdfRectangle(180, 10, 200, 30), "B")], options);
        form.AddListBox(CreatedNames[3], page, new PdfRectangle(210, 10, 310, 50), [new PdfChoiceOption("a", "Fill"), new PdfChoiceOption("zwei")], options);
        form.AddPushButton(CreatedNames[4], page, new PdfRectangle(320, 10, 380, 30), new PdfFieldOptions().WithCaption("Go"));
        form.AddSignatureField(CreatedNames[5], page, new PdfRectangle(390, 10, 490, 50), options);
        foreach (string name in CreatedNames)
        {
            if (form.FindField(name) is not PdfTerminalField field || field.Widgets.Count == 0 || field.Widgets.Any(widget => widget.GetAppearance() is null))
            {
                throw new InvalidOperationException($"The created field {name} is not read back with its widgets and appearances.");
            }
        }

        return true;
    }

    /// <summary>
    /// Issue #168: generates the appearances of up to 32 check boxes and radio button fields and turns their last widget on, then
    /// generates up to 32 choice fields' appearances and selects their last option. Invariants: a selected widget shows its on state
    /// and is among the selected widgets; a selected option is the selection read back. Choice options are the file's text, so a
    /// character no font can show may refuse (the documented <see cref="InvalidOperationException"/>, with nothing changed).
    /// </summary>
    private static void FillButtonsAndChoices(PdfAcroForm form)
    {
        foreach (PdfToggleButtonField field in form.TerminalFields.OfType<PdfToggleButtonField>().Take(32))
        {
            field.GenerateAppearances();
            if (field.Widgets.LastOrDefault(widget => field.GetOnState(widget) is not null) is not { } widget)
            {
                // A field whose kids are no widget annotations has nothing to show; any widget it has gets an on state.
                if (field.Widgets.Count > 0)
                {
                    throw new InvalidOperationException("A button field has no on state after its appearances were generated.");
                }

                continue;
            }

            field.Select(widget);
            if (!Equals(widget.AppearanceState, field.GetOnState(widget)) || !field.SelectedWidgets.Any(selected => ReferenceEquals(selected.Dictionary, widget.Dictionary)))
            {
                throw new InvalidOperationException("A selected button widget does not show its on state.");
            }
        }

        foreach (PdfChoiceField field in form.TerminalFields.OfType<PdfChoiceField>().Take(32))
        {
            int count = field.Options.Count;
            try
            {
                field.GenerateAppearances();
                if (count > 0)
                {
                    field.SetSelectedIndices([count - 1]);
                }
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (count > 0 && !field.SelectedIndices.Contains(count - 1) && field.Options[count - 1].ExportValue.Length > 0 && !field.Options.Take(count - 1).Any(option => option.ExportValue == field.Options[count - 1].ExportValue))
            {
                throw new InvalidOperationException("A selected option is not the selection read back.");
            }
        }
    }
}
