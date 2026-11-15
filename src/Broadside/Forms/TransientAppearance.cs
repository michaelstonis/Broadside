using Broadside.Annotations;
using Broadside.Graphics;

namespace Broadside.Forms;

/// <summary>
/// Display-only appearances for widgets that lack one: built from the field's current value as the form-filling API would build it,
/// drawn by one display list, and never placed on the widget, so rendering leaves the document unchanged.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.4.3 (variable text), §12.7.5.2 (buttons), §12.7.5.3 (text fields), §12.7.5.4 (choice fields) and §12.7.3,
/// Table 224 (<c>NeedAppearances</c>, which asks a processor to construct the appearances of the form's widgets). No font is embedded
/// (that would add objects to the document): a value neither the default appearance font nor Helvetica can show gets no appearance.
/// </remarks>
internal static class TransientAppearance
{
    /// <summary>Builds the appearance <paramref name="widget"/> would get from its field's value, or <see langword="null"/>.</summary>
    /// <param name="widget">The widget.</param>
    /// <returns>A form XObject that is no object of the document, or <see langword="null"/> when none can be built.</returns>
    public static PdfFormXObject? Create(PdfWidgetAnnotation widget)
    {
        if (widget.Field is not { } field)
        {
            return null;
        }

        try
        {
            Objects.CosStream? stream = field switch
            {
                PdfTextField text => TextFieldAppearance.Build(text, text.Value, transient: true).StreamFor(widget),
                PdfChoiceField choice => BuildChoice(choice).StreamFor(widget),
                PdfToggleButtonField toggle => ToggleButtonAppearance.Build(toggle).StreamFor(widget),
                PdfPushButtonField push => TextFieldAppearance.BuildCaption(push, widget.AppearanceCharacteristics?.NormalCaption ?? string.Empty, transient: true).StreamFor(widget),
                _ => null,
            };
            return stream is null ? null : new PdfFormXObject(widget.Document, stream, reference: null);
        }
        catch (InvalidOperationException)
        {
            // A character no usable font has: the widget stays without an appearance, as it was.
            return null;
        }
    }

    /// <summary>Whether a widget whose appearance exists still gets a transient one: a variable-text field when the form asks for new appearances.</summary>
    /// <param name="widget">The widget.</param>
    /// <returns>Whether to replace its appearance for display.</returns>
    public static bool Replaces(PdfWidgetAnnotation widget) =>
        widget.Document.AcroForm is { NeedAppearances: true } && widget.Field is PdfTextField or PdfChoiceField;

    private static TextFieldAppearance BuildChoice(PdfChoiceField choice)
    {
        IReadOnlyList<int> selected = choice.SelectedIndices;
        string? text = selected.Count == 0 && choice is PdfComboBoxField { IsEditable: true } && choice.Values is [var typed, ..] ? typed : null;
        return TextFieldAppearance.BuildChoice(choice, selected, text, topIndex: null, scrollToSelection: false, transient: true);
    }
}
