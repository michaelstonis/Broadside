using Broadside.Annotations;
using Broadside.Forms;

namespace Broadside.TestSupport;

/// <summary>
/// The field set every viewer shell fills the same way (#543): <c>acroform-filling.pdf</c> with a text field, a comb field, a read-only
/// field, a check box, a radio group, a list box, a combo box, an editable combo box and a multi-select list box (#560). Each shell
/// types and picks the <c>*Typed</c> inputs through its own input surface (a native text box, a native picker or list, keys) and must
/// end with the document the form API gives for the same final values.
/// </summary>
public static class FormFillingScenario
{
    /// <summary>The corpus file.</summary>
    public const string FileName = "acroform-filling.pdf";

    /// <summary>Typed into <c>name</c> (MaxLen 20).</summary>
    public const string NameTyped = "Grace Brewster Murray Hopper";

    /// <summary>What <c>name</c> holds: the typed text cut at MaxLen.</summary>
    public const string Name = "Grace Brewster Murra";

    /// <summary>Typed into the comb field <c>code</c> (MaxLen 5).</summary>
    public const string CodeTyped = "123456789";

    /// <summary>What <c>code</c> holds: one character per comb.</summary>
    public const string Code = "12345";

    /// <summary>The <c>size</c> radio button turned on (its third widget, state <c>/L</c>).</summary>
    public const int SizeWidget = 2;

    /// <summary>The <c>fruit</c> list box option picked (Cherry).</summary>
    public const int Fruit = 2;

    /// <summary>The <c>country</c> combo box option picked (Canada).</summary>
    public const int Country = 1;

    /// <summary>Typed into the editable <c>city</c> combo box: a value that is not an option.</summary>
    public const string City = "Nice";

    /// <summary>
    /// The <c>colors</c> multi-select list box options toggled, in order, from its value Red (§12.7.5.4, MultiSelect): Blue on (Red and
    /// Blue picked), then Red off. A pick that replaced the selection instead would end on Red.
    /// </summary>
    public static IReadOnlyList<int> ColorsToggled { get; } = [2, 0];

    /// <summary>The <c>colors</c> options picked after each toggle of <see cref="ColorsToggled"/>.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> ColorsAfterEachToggle { get; } = [[0, 2], [2]];

    /// <summary>The <c>colors</c> options picked at the end (Blue).</summary>
    public static IReadOnlyList<int> Colors => ColorsAfterEachToggle[^1];

    /// <summary>Fills a document through the form API with the scenario's final values, in the order the shells fill them.</summary>
    /// <param name="form">The form of an open copy of <see cref="FileName"/>.</param>
    public static void FillThroughApi(PdfAcroForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        ((PdfTextField)form.FindField("name")!).SetValue(Name);
        ((PdfTextField)form.FindField("code")!).SetValue(Code);
        ((PdfCheckBoxField)form.FindField("agree")!).SetChecked(true);
        var size = (PdfRadioButtonField)form.FindField("size")!;
        size.Select(size.Widgets[SizeWidget]);
        ((PdfChoiceField)form.FindField("fruit")!).SetSelectedIndices([Fruit]);
        ((PdfChoiceField)form.FindField("country")!).SetSelectedIndices([Country]);
        ((PdfChoiceField)form.FindField("city")!).SetValue(City);
        ((PdfChoiceField)form.FindField("colors")!).SetSelectedIndices(Colors);
    }

    /// <summary>
    /// Describes every terminal field's value, one line each (text, picked options and typed value, on state), for comparing what a
    /// shell filled with <see cref="ExpectedAsync"/>.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <returns>The description.</returns>
    public static string Describe(PdfAcroForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return string.Join('\n', form.TerminalFields.Select(field => field.FullyQualifiedName + " = " + field switch
        {
            PdfTextField text => text.Value ?? "(null)",
            PdfChoiceField choice => $"[{string.Join(", ", choice.SelectedIndices)}] {string.Join("|", choice.Values)}",
            PdfToggleButtonField toggle => toggle.Value.Value,
            _ => "-",
        }));
    }

    /// <summary>The description of <see cref="FileName"/> filled through the form API (<see cref="FillThroughApi"/>).</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>What <see cref="Describe"/> gives for the filled form.</returns>
    public static async Task<string> ExpectedAsync(CancellationToken cancellationToken = default)
    {
        using PdfDocument document = await PdfDocument.OpenAsync(Corpus.Bytes(FileName), cancellationToken).ConfigureAwait(false);
        FillThroughApi(document.AcroForm!);
        return Describe(document.AcroForm!);
    }

    /// <summary>Finds the widget of a field (its <paramref name="widget"/>th) among the targets a layer has loaded.</summary>
    /// <param name="form">The form.</param>
    /// <param name="name">The field's fully qualified name.</param>
    /// <param name="targets">The loaded targets and their annotations.</param>
    /// <param name="widget">Which of the field's widgets.</param>
    /// <returns>The target of the widget.</returns>
    public static T FindWidget<T>(PdfAcroForm form, string name, IEnumerable<(T Target, PdfAnnotation Annotation)> targets, int widget = 0)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(targets);
        var field = (PdfTerminalField)form.FindField(name)!;
        var dictionary = field.Widgets[widget].Dictionary;
        foreach ((T target, PdfAnnotation annotation) in targets)
        {
            if (ReferenceEquals(annotation.Dictionary, dictionary))
            {
                return target;
            }
        }

        throw new InvalidOperationException($"No widget of {name} is loaded.");
    }
}
