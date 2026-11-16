using Broadside.Annotations;
using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Layout.Flow;

/// <summary>The kind of interactive form field a <see cref="FieldBlock"/> places.</summary>
/// <remarks>ISO 32000-2 §12.7.5.</remarks>
public enum FormFieldType
{
    /// <summary>A text field (§12.7.5.3); multiline with <see cref="PdfFieldFlags.Multiline"/> in <see cref="FieldBlock.Options"/>.</summary>
    Text,

    /// <summary>A check box (§12.7.5.2.3), its label beside it.</summary>
    CheckBox,

    /// <summary>A set of radio buttons (§12.7.5.2.4), one per choice, each with the choice beside it.</summary>
    RadioButtons,

    /// <summary>A combo box (§12.7.5.4).</summary>
    ComboBox,

    /// <summary>A list box (§12.7.5.4).</summary>
    ListBox,

    /// <summary>An unsigned signature field (§12.7.5.5, PDF 1.3).</summary>
    Signature,
}

/// <summary>Where a <see cref="FieldBlock"/>'s label is drawn.</summary>
public enum FieldLabelPosition
{
    /// <summary>Above the field, across the content width.</summary>
    Above,

    /// <summary>Before the field on the same row (to its left), taking at most 40% of the content width.</summary>
    Before,
}

/// <summary>
/// An interactive form field placed by the flow: its label drawn as text (above or before it, beside a check box and each radio
/// button), then the field's widget where the layout puts it, added to the document's interactive form. The label is also the field's
/// alternate name (<c>TU</c>), which assistive technology reads.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7 (interactive forms, PDF 1.2), §12.7.4.1 (Table 226, <c>TU</c>), §12.5.6.19 (widget annotations). The field is made
/// through <see cref="PdfAcroForm"/> with an appearance for every widget; the form is created when the document has none. A label and
/// field keep together on one page.
/// </para>
/// <para>
/// ISO 32000-2 §14.8.4.7.3 (<c>Form</c>) and §12.5 (<c>Tabs</c>, PDF 1.5): tagged, the block is a <c>Form</c> element holding a <c>Lbl</c>
/// with the label's text and the widget's object reference; radio buttons are a <c>Div</c> holding the group's label (<c>P</c>) and a
/// <c>Form</c> per button, with its choice as <c>Lbl</c>. A tagged flow gives each page with a field the structure tab order
/// (<c>/Tabs /S</c>).
/// </para>
/// </remarks>
public sealed class FieldBlock : FlowBlock
{
    private static readonly CosName TabsKey = new("Tabs");
    private static readonly CosName StructureOrder = new("S");

    /// <summary>Creates a field block with structure type <c>Form</c> (<c>Div</c> for radio buttons).</summary>
    /// <param name="type">The kind of field.</param>
    /// <param name="name">The field's fully qualified name, unique in the document's form.</param>
    /// <param name="label">The label: shown with the field and written as its alternate name.</param>
    /// <param name="labelStyle">The style the label (and radio buttons' choices) are drawn in; its font size also sets the field's default size.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined value.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or empty, or <paramref name="label"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="labelStyle"/> is null.</exception>
    public FieldBlock(FormFieldType type, string name, string label, ParagraphStyle labelStyle)
        : base(type == FormFieldType.RadioButtons ? "Div" : "Form")
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown field type.");
        }

        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(labelStyle);
        Type = type;
        Name = name;
        Label = label;
        LabelStyle = labelStyle;
    }

    /// <summary>The kind of field.</summary>
    public FormFieldType Type { get; }

    /// <summary>The field's fully qualified name.</summary>
    public string Name { get; }

    /// <summary>The label.</summary>
    public string Label { get; }

    /// <summary>The style of the label.</summary>
    public ParagraphStyle LabelStyle { get; }

    /// <summary>Gets where the label is drawn; <see cref="FieldLabelPosition.Above"/> by default. A check box and radio buttons always have theirs beside.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined value.</exception>
    public FieldLabelPosition LabelPosition
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown label position.");
    }

    /// <summary>Gets the space between the label and the field, in points; 4 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double LabelGap
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    } = 4;

    /// <summary>
    /// Gets the field's width in points, or 0 (the default) for the rest of the content width; for a check box or radio button, the
    /// box's width, by default its height.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Width
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>
    /// Gets the field's height in points, or 0 (the default) for one line of the label's size with room for a border (four lines for a
    /// list box, three for a signature; a check box or radio button is as tall as the label's font size).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Height
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>Gets the choices of a combo box, list box or radio buttons, in the order shown; empty by default.</summary>
    /// <exception cref="ArgumentNullException">The value or one of its elements is null.</exception>
    public IReadOnlyList<string> Choices
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            string[] choices = [.. value];
            foreach (string choice in choices)
            {
                ArgumentNullException.ThrowIfNull(choice, nameof(value));
            }

            field = choices;
        }
    } = [];

    /// <summary>Gets the initial value: a text field's text, or the choice selected in a combo box, list box or radio buttons; null for none.</summary>
    public string? Value { get; init; }

    /// <summary>Gets whether a check box starts checked.</summary>
    public bool IsChecked { get; init; }

    /// <summary>
    /// Gets the field's flags (such as <see cref="PdfFieldFlags.Required"/> or <see cref="PdfFieldFlags.Multiline"/>), text appearance and
    /// widget colours, or null for the defaults. Its alternate name is replaced by <see cref="Label"/>; the options are copied, not changed.
    /// </summary>
    public PdfFieldOptions? Options { get; init; }

    /// <summary>Space above the block, dropped at the top of a page.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceBefore
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>Space below the block.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double SpaceAfter
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    internal override void Validate(int index)
    {
        new Paragraph(Label, LabelStyle).Validate(index);
        if (Type == FormFieldType.RadioButtons)
        {
            if (Choices.Count == 0)
            {
                throw new ArgumentException($"The radio buttons of block {index} have no choice.");
            }

            if (Choices.Distinct(StringComparer.Ordinal).Count() != Choices.Count || Choices.Any(static choice => choice.Length == 0 || choice == "Off" || choice.Contains('\0', StringComparison.Ordinal)))
            {
                throw new ArgumentException($"The radio buttons of block {index} need distinct choices, none empty or 'Off' (each is a button's on state, ISO 32000-2 §12.7.5.2.4).");
            }

            foreach (string choice in Choices)
            {
                new Paragraph(choice, LabelStyle).Validate(index);
            }
        }

        if (Value is { } value && Type is FormFieldType.RadioButtons or FormFieldType.ComboBox or FormFieldType.ListBox && !Choices.Contains(value, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The value '{value}' of field block {index} is not one of its choices.");
        }
    }

    internal override ContentWidths MeasureWidths(FlowServices services)
    {
        ContentWidths label = new Paragraph(Label, LabelStyle).MeasureWidths(services);
        double box = Box;
        return Type switch
        {
            FormFieldType.CheckBox or FormFieldType.RadioButtons => new(box + LabelGap + label.Min, box + LabelGap + label.Max),
            _ => new(Math.Max(label.Min, Width), Math.Max(label.Max, Width)),
        };
    }

    internal override void LayOut(FlowContext context)
    {
        // The content area is read before anything is placed: open the first page of the flow or section.
        context.EnsureRoom(0);
        context.AddSpace(SpaceBefore);
        switch (Type)
        {
            case FormFieldType.CheckBox:
                LayOutCheckBox(context);
                break;
            case FormFieldType.RadioButtons:
                LayOutRadioButtons(context);
                break;
            default:
                LayOutField(context);
                break;
        }

        context.AddSpace(SpaceAfter);
    }

    /// <summary>The side of a check box or radio button.</summary>
    private double Box => Width > 0 ? Width : Height > 0 ? Height : LabelStyle.FontSize;

    /// <summary>The field's height: its own, or the default for its type.</summary>
    private double FieldHeight
    {
        get
        {
            if (Height > 0)
            {
                return Height;
            }

            double line = LabelStyle.FontSize * 1.2;
            return Type switch
            {
                FormFieldType.ListBox => (4 * line) + 8,
                FormFieldType.Signature => 3 * line,
                _ => line + 8,
            };
        }
    }

    /// <summary>A text field, combo box, list box or signature field, its label above or before it.</summary>
    private void LayOutField(FlowContext context)
    {
        double height = FieldHeight;
        Paragraph label = LabelParagraph(Label, "Lbl");
        double fieldLeft;
        double labelRight;
        double labelHeight;
        double row;
        if (LabelPosition == FieldLabelPosition.Before)
        {
            labelRight = context.Left + Math.Min(context.Services.Shaper.Measure(Label, LabelStyle.TextStyle), context.Width * 0.4);
            fieldLeft = labelRight + LabelGap;
            labelHeight = Measure(context, label, context.Left, labelRight);
            row = Math.Max(labelHeight, height);
        }
        else
        {
            labelRight = context.Right;
            fieldLeft = context.Left;
            labelHeight = Measure(context, label, context.Left, labelRight);
            row = labelHeight + LabelGap + height;
        }

        context.EnsureRoom(row);
        context.PlaceMarkersAtTop();
        context.BeginBlock(this);
        double top = context.CursorY;
        Draw(context, label, context.Left, labelRight, top);
        double fieldTop = LabelPosition == FieldLabelPosition.Before ? top : top - labelHeight - LabelGap;
        double width = Width > 0 ? Math.Min(Width, context.Right - fieldLeft) : context.Right - fieldLeft;
        if (context.Drawing)
        {
            PdfPage page = context.Page;
            var rectangle = new PdfRectangle(fieldLeft, fieldTop - height, fieldLeft + Math.Max(width, 1), fieldTop);
            PdfAcroForm form = context.Document.AcroForm ?? context.Document.CreateAcroForm();
            PdfFieldOptions options = OptionsFor(Label);
            PdfTerminalField created = Type switch
            {
                FormFieldType.Text => form.AddTextField(Name, page, rectangle, options),
                FormFieldType.ComboBox => form.AddComboBox(Name, page, rectangle, Choices.Select(static choice => new PdfChoiceOption(choice)), options),
                FormFieldType.ListBox => form.AddListBox(Name, page, rectangle, Choices.Select(static choice => new PdfChoiceOption(choice)), options),
                _ => form.AddSignatureField(Name, page, rectangle, options),
            };
            switch (created)
            {
                case PdfTextField text when Value is not null:
                    text.SetValue(Value);
                    break;
                case PdfChoiceField choice when Value is not null:
                    choice.SetValue(Value);
                    break;
            }

            Tag(context, new FlowPart(StructureType, this, Language), created.Widgets, page);
        }

        context.Advance(row);
        context.EndBlock();
    }

    /// <summary>A check box with its label beside it.</summary>
    private void LayOutCheckBox(FlowContext context)
    {
        double box = Box;
        Paragraph label = LabelParagraph(Label, "Lbl");
        double labelLeft = context.Left + box + LabelGap;
        double labelHeight = Measure(context, label, labelLeft, context.Right);
        double row = Math.Max(labelHeight, box);
        context.EnsureRoom(row);
        context.PlaceMarkersAtTop();
        context.BeginBlock(this);
        double top = context.CursorY;
        if (context.Drawing)
        {
            double boxTop = top - Math.Max(0, (LabelStyle.LineAdvance - box) / 2);
            PdfAcroForm form = context.Document.AcroForm ?? context.Document.CreateAcroForm();
            PdfPage page = context.Page;
            PdfCheckBoxField field = form.AddCheckBox(Name, page, new PdfRectangle(context.Left, boxTop - box, context.Left + box, boxTop), OptionsFor(Label));
            if (IsChecked)
            {
                field.SetChecked(true);
            }

            Tag(context, new FlowPart(StructureType, this, Language), field.Widgets, page);
        }

        Draw(context, label, labelLeft, context.Right, top);
        context.Advance(row);
        context.EndBlock();
    }

    /// <summary>Radio buttons: the group's label, then a row per choice, each a button with the choice beside it.</summary>
    private void LayOutRadioButtons(FlowContext context)
    {
        double box = Box;
        Paragraph group = LabelParagraph(Label, "P");
        double groupHeight = Measure(context, group, context.Left, context.Right);
        double labelLeft = context.Left + box + LabelGap;
        var rows = new Paragraph[Choices.Count];
        var heights = new double[Choices.Count];
        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = LabelParagraph(Choices[index], "Lbl");
            heights[index] = Math.Max(Measure(context, rows[index], labelLeft, context.Right), box);
        }

        context.EnsureRoom(groupHeight + LabelGap + heights[0]);
        context.PlaceMarkersAtTop();
        context.BeginBlock(this);
        Draw(context, group, context.Left, context.Right, context.CursorY);
        context.Advance(groupHeight + LabelGap);
        var buttons = new List<PdfRadioButton>();
        var owners = new object[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            context.EnsureRoom(heights[index]);
            double top = context.CursorY;
            owners[index] = new object();
            context.BeginPart(new FlowPart("Form", owners[index], null));
            if (context.Drawing)
            {
                double boxTop = top - Math.Max(0, (LabelStyle.LineAdvance - box) / 2);
                buttons.Add(new PdfRadioButton(context.Page, new PdfRectangle(context.Left, boxTop - box, context.Left + box, boxTop), Choices[index]));
            }

            Draw(context, rows[index], labelLeft, context.Right, top);
            context.EndPart();
            context.Advance(heights[index]);
        }

        if (context.Drawing)
        {
            PdfAcroForm form = context.Document.AcroForm ?? context.Document.CreateAcroForm();
            PdfRadioButtonField field = form.AddRadioButtons(Name, buttons, OptionsFor(Label));
            if (Value is { } value)
            {
                field.SetValue(new CosName(value));
            }

            IReadOnlyList<PdfWidgetAnnotation> widgets = field.Widgets;
            for (int index = 0; index < widgets.Count && index < owners.Length; index++)
            {
                Tag(context, new FlowPart("Form", owners[index], null), [widgets[index]], buttons[index].Page);
            }
        }

        context.EndBlock();
    }

    private Paragraph LabelParagraph(string text, string structureType) => new(text, LabelStyle) { StructureType = structureType };

    /// <summary>The height <paramref name="label"/> takes between <paramref name="left"/> and <paramref name="right"/>, laid out without drawing.</summary>
    private static double Measure(FlowContext context, Paragraph label, double left, double right)
    {
        using var region = new FlowContext(context, left, Math.Max(left, right), 0, [double.MaxValue / 4], -1);
        label.LayOut(region);
        return region.Top - region.CursorY;
    }

    /// <summary>Draws <paramref name="label"/> between <paramref name="left"/> and <paramref name="right"/> from <paramref name="top"/> down.</summary>
    private static void Draw(FlowContext context, Paragraph label, double left, double right, double top)
    {
        using var region = new FlowContext(context, left, Math.Max(left, right), top, [double.MaxValue / 4], 0);
        label.LayOut(region);
    }

    /// <summary>A copy of the options with the label as the alternate name (<c>TU</c>).</summary>
    private PdfFieldOptions OptionsFor(string label)
    {
        var options = new PdfFieldOptions { AlternateName = label };
        if (Options is { } given)
        {
            options.Flags = given.Flags;
            options.FontSize = given.FontSize;
            options.TextColor = given.TextColor;
            options.Quadding = given.Quadding;
            options.BorderColor = given.BorderColor;
            options.BackgroundColor = given.BackgroundColor;
            options.BorderWidth = given.BorderWidth;
            options.MaxLength = given.MaxLength;
            options.Caption = given.Caption;
        }

        return options;
    }

    /// <summary>Ties each widget to the structure element of <paramref name="part"/> and, tagged, gives its page the structure tab order.</summary>
    private static void Tag(FlowContext context, FlowPart part, IReadOnlyList<PdfWidgetAnnotation> widgets, PdfPage page)
    {
        foreach (PdfWidgetAnnotation widget in widgets)
        {
            context.AddAnnotation(part, widget, page);
        }

        if (context.Services.Structure is not null)
        {
            page.Dictionary[TabsKey] = StructureOrder;
        }
    }
}
