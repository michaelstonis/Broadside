using System.Globalization;
using Broadside.Forms;
using Microsoft.Maui.Controls;

namespace Broadside.Viewer.Maui;

/// <summary>
/// The form of a <see cref="PdfView"/>'s document as a list of native editors, one per widget: text (single line, multiline, password),
/// check boxes, radio buttons, combo boxes and list boxes (single or multiple selection), read-only fields shown disabled. Focusing an
/// editor scrolls its widget into view; every value goes through the view's <see cref="PdfView.Annotations"/> layer, which regenerates
/// the field's appearance and redraws only its overlay tiles (calculated fields that depend on it follow).
/// </summary>
/// <remarks>ISO 32000-2 §12.7 (interactive forms): §12.7.5.2 (buttons), §12.7.5.3 (text fields), §12.7.5.4 (choice fields), §12.7.4.3 (variable text). Spec #33 viewer capability 12.</remarks>
public sealed class PdfFormPanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfFormPanel), null, propertyChanged: (b, _, n) => ((PdfFormPanel)b)._follower.Attach((PdfView?)n));

    private const int MaxPages = 100;
    private readonly ViewerFollower _follower;
    private readonly Label _status = PanelKit.Hint(string.Empty, "form-status");
    private readonly VerticalStackLayout _list = new() { Spacing = 10, AutomationId = "form-fields" };
    private IReadOnlyList<FormFieldItem> _fields = [];
    private int _generation;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view whose form it fills.</summary>
    public PdfFormPanel()
    {
        _follower = new ViewerFollower(() => _ = RefreshAsync());
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    _status,
                    _list,
                    PanelKit.Hint("Fill a field here or tap it in the page. Values go through the viewer's annotation layer: the field's appearance is regenerated and only its tiles redraw."),
                },
            },
        };
    }

    /// <summary>Raised after the panel changed a field's value (the document now differs from its file).</summary>
    public event EventHandler? Edited;

    /// <summary>Gets or sets the view whose form this panel fills.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    /// <summary>Gets the number of widgets the panel lists.</summary>
    public int FieldCount => _fields.Count;

    /// <summary>Reads the form again and shows every field's current value.</summary>
    /// <returns>A task that completes when the list shows them.</returns>
    public async Task RefreshAsync()
    {
        int generation = ++_generation;
        if (_follower.View is not { Annotations: { } layer, CurrentSession: { } session })
        {
            _fields = [];
            _list.Children.Clear();
            _status.Text = "No document.";
            return;
        }

        if (_fields.Count == 0)
        {
            _status.Text = "Reading the form…";
        }

        IReadOnlyList<FormFieldItem> fields;
        try
        {
            fields = await FormFields.ListAsync(layer, session.PageCount, MaxPages, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return;
        }
#pragma warning disable CA1031 // shown in the panel
        catch (Exception exception)
#pragma warning restore CA1031
        {
            if (generation == _generation)
            {
                _status.Text = "The form could not be read: " + exception.Message;
            }

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _fields = fields;
        _status.Text = fields.Count == 0
            ? "This document has no form fields."
            : string.Create(CultureInfo.InvariantCulture, $"{fields.Count} field(s){(session.PageCount > MaxPages ? $" on the first {MaxPages} pages" : string.Empty)}");
        _list.Children.Clear();
        foreach (FormFieldItem item in fields)
        {
            _list.Children.Add(Row(layer, item));
        }
    }

    private VerticalStackLayout Row(Broadside.Viewer.AnnotationLayer layer, FormFieldItem item)
    {
        var label = new Label
        {
            Text = item.Label + string.Create(CultureInfo.InvariantCulture, $"  · p. {item.Target.PageIndex + 1}"),
            FontSize = 13,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        View editor = item.Field switch
        {
            PdfTextField text => TextEditor(layer, item, text),
            PdfCheckBoxField check => CheckEditor(layer, item, check),
            PdfRadioButtonField => PanelKit.Button("Choose this option", "field:" + item.Name, () => Edit(layer, item, l => l.ToggleField())),
            PdfChoiceField { IsMultiSelect: true } list => ListEditor(layer, item, list),
            PdfChoiceField choice => ChoiceEditor(layer, item, choice),
            _ => PanelKit.Hint(item.Field.Kind + " (shown, not editable here)"),
        };
        editor.IsEnabled = !item.Field.IsReadOnly;
        SemanticProperties.SetDescription(editor, item.Label);
        return new VerticalStackLayout { Spacing = 2, Children = { label, editor } };
    }

    private InputView TextEditor(Broadside.Viewer.AnnotationLayer layer, FormFieldItem item, PdfTextField field)
    {
        InputView input = field.IsMultiline
            ? new Editor { Text = field.Value, HeightRequest = 80, AutoSize = EditorAutoSizeOption.Disabled }
            : new Entry { Text = field.Value, IsPassword = field.IsPassword, ReturnType = ReturnType.Done };
        input.AutomationId = "field:" + item.Name;
        input.MaxLength = field.MaxLength is int max && max > 0 ? max : int.MaxValue;
        string? committed = field.Value;
        void Commit()
        {
            if (input.Text != committed)
            {
                committed = input.Text;
                Edit(layer, item, l => l.SetFieldText(input.Text));
            }
        }

        input.Focused += (_, _) => Reveal(item);
        input.Unfocused += (_, _) => Commit();
        if (input is Entry entry)
        {
            entry.Completed += (_, _) => Commit();
        }

        return input;
    }

    private CheckBox CheckEditor(Broadside.Viewer.AnnotationLayer layer, FormFieldItem item, PdfCheckBoxField field)
    {
        var box = new CheckBox { IsChecked = field.IsChecked, AutomationId = "field:" + item.Name };
        box.CheckedChanged += (_, e) =>
        {
            if (e.Value != field.IsChecked)
            {
                Edit(layer, item, l => l.ToggleField());
            }
        };
        return box;
    }

    private Picker ChoiceEditor(Broadside.Viewer.AnnotationLayer layer, FormFieldItem item, PdfChoiceField field)
    {
        List<string> options = ["—", .. field.Options.Select(o => o.DisplayText)];
        int selected = field.SelectedIndices.Count > 0 ? field.SelectedIndices[0] + 1 : 0;
        var picker = new Picker { ItemsSource = options, SelectedIndex = selected, AutomationId = "field:" + item.Name };
        picker.Focused += (_, _) => Reveal(item);
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex != selected)
            {
                selected = picker.SelectedIndex;
                string? value = selected == 0 ? null : field.Options[selected - 1].ExportValue;
                Edit(layer, item, l => l.SetFieldChoice(value));
            }
        };
        return picker;
    }

    private VerticalStackLayout ListEditor(Broadside.Viewer.AnnotationLayer layer, FormFieldItem item, PdfChoiceField field)
    {
        var stack = new VerticalStackLayout { AutomationId = "field:" + item.Name };
        var boxes = new List<CheckBox>();
        for (int i = 0; i < field.Options.Count; i++)
        {
            var box = new CheckBox { IsChecked = field.SelectedIndices.Contains(i) };
            box.CheckedChanged += (_, _) =>
            {
                int[] indices = [.. boxes.Select((b, index) => (b, index)).Where(p => p.b.IsChecked).Select(p => p.index)];
                if (!indices.SequenceEqual(field.SelectedIndices))
                {
                    Edit(layer, item, l => l.SetFieldSelection(indices));
                }
            };
            boxes.Add(box);
            stack.Children.Add(new HorizontalStackLayout { Children = { box, new Label { Text = field.Options[i].DisplayText, VerticalTextAlignment = TextAlignment.Center } } });
        }

        return stack;
    }

    // Only scrolls: focusing the widget in the layer would open the view's own editor over it and take the keyboard from this one.
    private void Reveal(FormFieldItem item) => _follower.View?.Reveal(item.Target.PageIndex, item.Rectangle);

    private void Edit(Broadside.Viewer.AnnotationLayer layer, FormFieldItem item, Func<Broadside.Viewer.AnnotationLayer, bool> edit)
    {
        // The layer sets the focused field's value; the focus is given back at once, so the view's own editor never opens.
        layer.Focus(item.Target);
        bool changed = edit(layer);
        layer.Focus(null);
        if (changed)
        {
            Edited?.Invoke(this, EventArgs.Empty);

            // Calculated fields and the other widgets of a radio group change with it: show every value again.
            Dispatcher?.Dispatch(() => _ = RefreshAsync());
        }
    }
}
