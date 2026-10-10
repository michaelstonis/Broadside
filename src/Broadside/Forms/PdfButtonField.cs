using System.Globalization;
using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>A button field: a push button, a check box or a set of radio buttons.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.2, Table 229; field type <c>Btn</c>. The kind comes from the inheritable flags: Pushbutton (bit 17) selects a
/// push button, else Radio (bit 16) a set of radio buttons, else a check box. When both are set the field is a push button (Radio
/// "may be set only if the Pushbutton flag is clear") and a <c>ButtonFlagsConflict</c> diagnostic is recorded.
/// </remarks>
public abstract class PdfButtonField : PdfTerminalField
{
    private protected PdfButtonField(FieldInfo info, PdfFieldKind kind, CosObject[] widgets)
        : base(info, kind, widgets)
    {
    }
}

/// <summary>A push button: a control that responds to the user at once and keeps no value.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.2.2: it "shall not use the V and DV entries". What it does is its widget's action
/// (<see cref="PdfWidgetAnnotation.Action"/>) and its caption the widget's appearance characteristics.
/// </remarks>
public sealed class PdfPushButtonField : PdfButtonField
{
    internal PdfPushButtonField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.PushButton, widgets)
    {
    }
}

/// <summary>A check box or a set of radio buttons: buttons with an on and an off state, whose value names the appearance state that is on.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.5.2.3, §12.7.5.2.4 and Table 230. Each widget's normal appearance has its on state, under any name, and
/// optionally <c>Off</c>. The field's value <c>V</c> names the on state that is selected, <c>Off</c> when none is. Widgets select
/// their appearance with <c>AS</c>, which "shall also be" that value; when they disagree, <c>AS</c> decides the appearance and
/// <c>V</c> remains the value, and a <c>ButtonValueStateMismatch</c> diagnostic is recorded.
/// </para>
/// <para>
/// With <c>Opt</c> (PDF 1.4), entry <em>i</em> is the export value of the <em>i</em>-th widget in <c>Kids</c>, and on states may
/// be named by that position (<c>/0</c>, <c>/1</c>), so widgets can share an export value and still be told apart.
/// </para>
/// </remarks>
public abstract class PdfToggleButtonField : PdfButtonField
{
    private protected PdfToggleButtonField(FieldInfo info, PdfFieldKind kind, CosObject[] widgets)
        : base(info, kind, widgets)
    {
    }

    /// <summary>Gets the selected on state (<c>V</c>, inheritable); <c>Off</c> when the value is absent or nothing is selected.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.3 and §12.7.5.2.4 (default <c>Off</c>). A string reads as the name it spells with a
    /// <c>FieldValueTypeInvalid</c> diagnostic; a name no widget has as its on state records <c>ButtonValueUnknownState</c> and is
    /// returned as stored.
    /// </remarks>
    public CosName Value
    {
        get
        {
            CosName value = ReadState(ValueObject, FormNames.V) ?? FormNames.Off;
            Check(value);
            return value;
        }
    }

    /// <summary>Gets the state the field reverts to on a reset-form action (<c>DV</c>, inheritable), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226.</remarks>
    public CosName? DefaultValue => ReadState(DefaultValueObject, FormNames.DV);

    /// <summary>Gets the export value of each widget in <c>Kids</c> order (<c>Opt</c>, inheritable, PDF 1.4); empty when absent.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.3, Table 230. An element that is not a text string reads as an empty string, and an array whose length
    /// differs from the number of widgets records <c>ButtonOptLengthMismatch</c>; both keep positions aligned with <c>Kids</c>.
    /// </remarks>
    public IReadOnlyList<string> Options
    {
        get
        {
            switch (Inherited(FormNames.Opt))
            {
                case null:
                    return [];
                case CosArray array:
                    var options = new string[array.Count];
                    for (int index = 0; index < array.Count; index++)
                    {
                        if (Document.Resolve(array[index]) is CosString text)
                        {
                            options[index] = text.DecodeText();
                        }
                        else
                        {
                            options[index] = string.Empty;
                            Report(DiagnosticCodes.FieldEntryInvalid, $"Element {index} of the button field's Opt array is not a text string; it reads as empty.");
                        }
                    }

                    if (options.Length != WidgetElements.Count)
                    {
                        Report(DiagnosticCodes.ButtonOptLengthMismatch, "The button field's Opt array shall have one entry per widget in Kids (Table 230); its length differs.");
                    }

                    return options;
                default:
                    return InvalidEntry<string[]>(FormNames.Opt, "an array of text strings") ?? [];
            }
        }
    }

    /// <summary>Gets the names of the widgets' on states, each once, in <c>Kids</c> order.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.3: every state of a widget's normal appearance other than <c>Off</c>.</remarks>
    public IReadOnlyList<CosName> OnStateNames
    {
        get
        {
            var names = new List<CosName>();
            foreach (PdfWidgetAnnotation widget in Widgets)
            {
                if (GetOnState(widget) is { } state && !names.Contains(state))
                {
                    names.Add(state);
                }
            }

            return names;
        }
    }

    /// <summary>Gets the widgets whose on state is the field's value: none when it is <c>Off</c>; several for duplicated check boxes or radio buttons in unison.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.3 (example 2) and §12.7.5.2.4, Table 229 (RadiosInUnison).</remarks>
    public IReadOnlyList<PdfWidgetAnnotation> SelectedWidgets
    {
        get
        {
            CosName value = Value;
            return FormNames.Off.Equals(value) ? [] : [.. Widgets.Where(widget => value.Equals(GetOnState(widget)))];
        }
    }

    /// <summary>
    /// Gets the export value of the selected state: the <c>Opt</c> entry of the first selected widget when there is an <c>Opt</c>, else
    /// the state's name; <see langword="null"/> when the value is <c>Off</c>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.3, Table 230.</remarks>
    public string? ExportValue
    {
        get
        {
            CosName value = Value;
            if (FormNames.Off.Equals(value))
            {
                return null;
            }

            IReadOnlyList<string> options = Options;
            if (options.Count > 0)
            {
                foreach (PdfWidgetAnnotation widget in Widgets)
                {
                    if (value.Equals(GetOnState(widget)) && IndexOfWidget(widget.Dictionary) is var index && index >= 0 && index < options.Count)
                    {
                        return options[index];
                    }
                }

                if (int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int position) && position < options.Count)
                {
                    return options[position];
                }
            }

            return value.Value;
        }
    }

    /// <summary>Returns the on state of <paramref name="widget"/>: the state of its normal appearance other than <c>Off</c>.</summary>
    /// <param name="widget">One of the field's widgets.</param>
    /// <returns>
    /// The state name; <see langword="null"/> when the widget has no appearance states. A normal appearance with states but none other
    /// than <c>Off</c> records <c>ButtonOnStateMissing</c>; with several, the one equal to the value is used, else the first, with
    /// <c>ButtonStatesAmbiguous</c>.
    /// </returns>
    /// <remarks>ISO 32000-2 §12.7.5.2.3 and §12.5.5.</remarks>
    public CosName? GetOnState(PdfWidgetAnnotation widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (widget.AppearanceDictionary?.Normal is not { HasStates: true } normal)
        {
            return null;
        }

        CosName[] on = [.. normal.StateNames.Where(state => !FormNames.Off.Equals(state))];
        switch (on.Length)
        {
            case 0:
                Report(DiagnosticCodes.ButtonOnStateMissing, "A widget of the button field has appearance states but none other than Off, so it has no on state.");
                return null;
            case 1:
                return on[0];
            default:
                Report(DiagnosticCodes.ButtonStatesAmbiguous, "A widget of the button field has more than one on state in its normal appearance; the one equal to the value, else the first, is used.");
                CosName? value = ReadState(ValueObject, FormNames.V);
                return on.FirstOrDefault(state => state.Equals(value)) ?? on[0];
        }
    }

    /// <summary>Returns the export value of <paramref name="widget"/>: its <c>Opt</c> entry when there is an <c>Opt</c>, else its on state's name.</summary>
    /// <param name="widget">One of the field's widgets.</param>
    /// <returns>The export value, or <see langword="null"/> when the widget has no on state and no <c>Opt</c> entry.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.2.3, Table 230.</remarks>
    public string? GetWidgetExportValue(PdfWidgetAnnotation widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        IReadOnlyList<string> options = Options;
        int index = IndexOfWidget(widget.Dictionary);
        return index >= 0 && index < options.Count ? options[index] : GetOnState(widget)?.Value;
    }

    /// <summary>
    /// Returns the appearance state <paramref name="widget"/> shows: its <c>AS</c>; without one, the field's value when the widget
    /// has it as its on state, else <c>Off</c>.
    /// </summary>
    /// <param name="widget">One of the field's widgets.</param>
    /// <returns>The state name.</returns>
    /// <remarks>ISO 32000-2 §12.7.5.2.3: "the value of the AS key shall be used instead of the V key to determine which appearance to use".</remarks>
    public CosName GetAppearanceState(PdfWidgetAnnotation widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (widget.AppearanceState is { } state)
        {
            return state;
        }

        CosName value = ReadState(ValueObject, FormNames.V) ?? FormNames.Off;
        return value.Equals(GetOnState(widget)) ? value : FormNames.Off;
    }

    /// <summary>Reads a state value: a name, or a string read as the name it spells with a diagnostic.</summary>
    private CosName? ReadState(CosObject? value, CosName key)
    {
        switch (value)
        {
            case null:
                return null;
            case CosName name:
                return name;
            case CosString text:
                ReportValueType(key, "a name; it is a string, read as the name it spells");
                return new CosName(text.Bytes);
            default:
                ReportValueType(key, "a name; it reads as Off");
                return null;
        }
    }

    /// <summary>Checks the value against the widgets' states: a value no widget has, and widgets whose AS disagrees with it.</summary>
    private void Check(CosName value)
    {
        bool known = FormNames.Off.Equals(value);
        bool anyStates = false;
        foreach (PdfWidgetAnnotation widget in Widgets)
        {
            CosName? on = GetOnState(widget);
            anyStates |= on is not null;
            known |= value.Equals(on);
            if (on is not null && widget.AppearanceState is { } state && !state.Equals(value.Equals(on) ? value : FormNames.Off))
            {
                Report(DiagnosticCodes.ButtonValueStateMismatch, "A widget's AS entry differs from the button field's value, which it shall equal (§12.7.5.2.3); AS decides the appearance.");
            }
        }

        if (!known && anyStates)
        {
            Report(DiagnosticCodes.ButtonValueUnknownState, $"The button field's value {value.Value} is not the on state of any of its widgets; nothing is shown selected.");
        }
    }
}

/// <summary>A check box: one or more widgets that toggle together between an on and an off state.</summary>
/// <remarks>ISO 32000-2 §12.7.5.2.3; Pushbutton and Radio flags clear.</remarks>
public sealed class PdfCheckBoxField : PdfToggleButtonField
{
    internal PdfCheckBoxField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.CheckBox, widgets)
    {
    }

    /// <summary>Gets a value indicating whether the box is checked: its value is a state other than <c>Off</c>.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.3.</remarks>
    public bool IsChecked => !FormNames.Off.Equals(Value);
}

/// <summary>A set of radio buttons: widgets of which at most one is on, unless several share an on state and turn on in unison.</summary>
/// <remarks>ISO 32000-2 §12.7.5.2.4; Radio flag set, Pushbutton clear.</remarks>
public sealed class PdfRadioButtonField : PdfToggleButtonField
{
    internal PdfRadioButtonField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.RadioButton, widgets)
    {
    }

    /// <summary>Gets a value indicating whether exactly one button shall be on at all times (<c>Ff</c> bit 15, NoToggleToOff).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.4, Table 229.</remarks>
    public bool IsNoToggleToOff => HasFlag(PdfFieldFlags.NoToggleToOff);

    /// <summary>Gets a value indicating whether buttons with the same on state turn on and off together (<c>Ff</c> bit 26, RadiosInUnison, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.4, Table 229.</remarks>
    public bool IsRadiosInUnison => HasFlag(PdfFieldFlags.RadiosInUnison);
}
