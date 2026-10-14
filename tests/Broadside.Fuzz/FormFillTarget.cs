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
/// <see cref="PdfDocument.IsEncrypted"/> and the save agree), so no exception is tolerated there.
/// </summary>
/// <remarks>ISO 32000-2 §12.7.4.3, §12.7.5.2, §12.7.5.3, §12.7.5.4, §12.5.5, §7.6.2.</remarks>
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

        _ = saved.AcroForm?.TerminalFields.OfType<PdfTextField>().Select(field => field.Value).Count();
        _ = saved.AcroForm?.TerminalFields.OfType<PdfToggleButtonField>().Select(field => field.SelectedWidgets.Count).Count();
        _ = saved.AcroForm?.TerminalFields.OfType<PdfChoiceField>().Select(field => field.SelectedIndices.Count).Count();
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
