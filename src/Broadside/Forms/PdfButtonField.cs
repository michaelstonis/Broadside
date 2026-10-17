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

    /// <summary>
    /// Gets the widgets whose on state is the field's value: none when it is <c>Off</c>; several for duplicated check boxes or radio
    /// buttons in unison (which with <c>Opt</c> also includes widgets with the same export value).
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.3 (example 2) and §12.7.5.2.4, Table 229 (RadiosInUnison). Of radio buttons that share the on state without
    /// RadiosInUnison, at most one is on: those whose <c>AS</c> shows it, when any does.
    /// </remarks>
    public IReadOnlyList<PdfWidgetAnnotation> SelectedWidgets
    {
        get
        {
            CosName value = Value;
            if (FormNames.Off.Equals(value))
            {
                return [];
            }

            string? export = UnisonByExportValue ? ExportOf(value) : null;
            List<PdfWidgetAnnotation> selected = [.. Widgets.Where(widget => IsOn(widget, GetOnState(widget), value, export))];
            if (IsExclusiveRadio && selected.Count > 1)
            {
                // Radio buttons that share an on state without RadiosInUnison: at most one is on (§12.7.5.2.4); AS tells which.
                List<PdfWidgetAnnotation> shown = [.. selected.Where(widget => value.Equals(widget.AppearanceState))];
                return shown.Count > 0 ? shown : selected;
            }

            return selected;
        }
    }

    /// <summary>
    /// Gets the export value of the selected state: the <c>Opt</c> entry of the first selected widget when there is an <c>Opt</c>, else
    /// the state's name; <see langword="null"/> when the value is <c>Off</c>.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.3, Table 230.</remarks>
    public string? ExportValue => ExportOf(Value);

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
        CosName? on = GetOnState(widget);
        return on is not null && !FormNames.Off.Equals(value) && IsOn(widget, on, value, UnisonByExportValue ? ExportOf(value) : null) ? on : FormNames.Off;
    }

    /// <summary>
    /// Selects the on state <paramref name="value"/>, or nothing with <c>Off</c>: sets the field's value (<c>V</c>) and the appearance
    /// state (<c>AS</c>) of every widget that has appearance states.
    /// </summary>
    /// <param name="value">An on state of one of the widgets (<see cref="OnStateNames"/>), or <c>Off</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not the on state of any widget.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="value"/> is <c>Off</c> for radio buttons with NoToggleToOff set.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.5.2.3, §12.7.5.2.4 and Table 229. <c>V</c> is stored as a name in the field's own dictionary and each
    /// widget's <c>AS</c> is set to its on state when it turns on, else to <c>Off</c>, so that both keys agree (the clauses' "shall
    /// also be"). Every check box widget with the on state turns on (duplicated boxes toggle together, §12.7.5.2.3 example 2). Of radio
    /// buttons the first widget with the on state turns on; with RadiosInUnison every widget with that on state, or with the same
    /// <c>Opt</c> export value, turns on with it. The existing appearances are kept: <see cref="GenerateAppearances"/> makes new ones.
    /// </para>
    /// <para>
    /// Read-only fields are set too: the flag restricts the user, not the program. When the form also has an XFA resource (Annex K),
    /// only the AcroForm value changes and an <c>XfaDataNotUpdated</c> information diagnostic is recorded.
    /// </para>
    /// </remarks>
    public void SetValue(CosName value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (FormNames.Off.Equals(value))
        {
            Apply(FormNames.Off, null);
            return;
        }

        PdfWidgetAnnotation widget = Widgets.FirstOrDefault(candidate => value.Equals(GetOnState(candidate)))
            ?? throw new ArgumentException($"{value.Value} is not the on state of any widget of the field {FullyQualifiedName} (ISO 32000-2 §12.7.5.2.3).", nameof(value));
        Apply(value, widget);
    }

    /// <summary>Turns <paramref name="widget"/> on: the field's value becomes its on state, as when the user clicks it.</summary>
    /// <param name="widget">One of the field's widgets (<see cref="PdfTerminalField.Widgets"/>) that has an on state.</param>
    /// <exception cref="ArgumentException"><paramref name="widget"/> is not a widget of the field or has no on state.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.2.3, §12.7.5.2.4 and Table 229, as <see cref="SetValue(CosName)"/> describes, except that of radio buttons
    /// without RadiosInUnison exactly this widget turns on, even when another has the same on state.
    /// </remarks>
    public void Select(PdfWidgetAnnotation widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        if (IndexOfWidget(widget.Dictionary) < 0)
        {
            throw new ArgumentException($"The widget is not a widget of the field {FullyQualifiedName}.", nameof(widget));
        }

        CosName on = GetOnState(widget)
            ?? throw new ArgumentException($"The widget has no on state in its normal appearance (ISO 32000-2 §12.7.5.2.3); call {nameof(GenerateAppearances)} first.", nameof(widget));
        Apply(on, widget);
    }

    /// <summary>Turns on the first widget whose export value is <paramref name="exportValue"/>, or turns every widget off with <see langword="null"/>.</summary>
    /// <param name="exportValue">An export value (<see cref="GetWidgetExportValue"/>), or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">No widget has <paramref name="exportValue"/> as its export value.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="exportValue"/> is <see langword="null"/> for radio buttons with NoToggleToOff set.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.2.3, Table 230 (<c>Opt</c>): as <see cref="Select"/> does for that widget.</remarks>
    public void SetExportValue(string? exportValue)
    {
        if (exportValue is null)
        {
            Apply(FormNames.Off, null);
            return;
        }

        PdfWidgetAnnotation widget = Widgets.FirstOrDefault(candidate => GetOnState(candidate) is not null && GetWidgetExportValue(candidate) == exportValue)
            ?? throw new ArgumentException($"No widget of the field {FullyQualifiedName} exports {exportValue} (ISO 32000-2 §12.7.5.2.3, Table 230).", nameof(exportValue));
        Select(widget);
    }

    /// <summary>
    /// Generates a normal appearance for each widget: an on and an <c>Off</c> appearance stream, the on one showing the widget's caption
    /// (<c>MK</c> <c>CA</c>) in ZapfDingbats; each widget's appearance state is set to match the value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.5.2.3, §12.7.5.2.4 and §12.5.6.19 (Table 192: <c>CA</c>, <c>BG</c>, <c>BC</c>, <c>R</c>). Each widget's
    /// <c>AP</c> becomes a normal appearance subdictionary with the on state and <c>Off</c>, two new form XObjects (down and rollover
    /// appearances are dropped). Both draw the <c>MK</c> background and border in the widget's border style; the on appearance then
    /// shows the caption's character codes in ZapfDingbats (§9.6.2.2, Annex D.6), centred, in the default appearance's size (an auto
    /// size fits the widget) and colour. Without a caption a check box shows a check mark (<c>4</c>) and a radio button a dot
    /// (<c>l</c>). The default appearance font is used when it is ZapfDingbats; otherwise a ZapfDingbats font is added to the
    /// appearance's resources.
    /// </para>
    /// <para>
    /// The on state keeps its name; a widget without one gets <c>Yes</c> (check box) or its position in <c>Kids</c> (radio button,
    /// Table 230). Nothing changes when building an appearance fails.
    /// </para>
    /// </remarks>
    public void GenerateAppearances() => ToggleButtonAppearance.Build(this).Apply();

    /// <summary>Gets the caption a generated appearance shows when the widget has none: a check mark or a dot in ZapfDingbats.</summary>
    internal abstract string DefaultCaption { get; }

    /// <summary>Returns the name a generated appearance gives the on state of the widget at <paramref name="index"/> when it has none.</summary>
    internal abstract CosName DefaultOnState(int index);

    /// <summary>Gets a value indicating whether the field is radio buttons of which at most one is on (RadiosInUnison clear).</summary>
    private bool IsExclusiveRadio => this is PdfRadioButtonField radio && !radio.IsRadiosInUnison;

    /// <summary>Gets a value indicating whether radio buttons in unison with <c>Opt</c> turn on by export value as well as by on state.</summary>
    private bool UnisonByExportValue => this is PdfRadioButtonField { IsRadiosInUnison: true } && Inherited(FormNames.Opt) is not null;

    /// <summary>The export value of the state <paramref name="value"/>, as <see cref="ExportValue"/> describes.</summary>
    private string? ExportOf(CosName value)
    {
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

    /// <summary>Whether <paramref name="widget"/> shows its on state <paramref name="on"/> for the field value <paramref name="value"/>.</summary>
    private bool IsOn(PdfWidgetAnnotation widget, CosName? on, CosName value, string? export) =>
        on is not null && (value.Equals(on) || (export is not null && export == GetWidgetExportValue(widget)));

    /// <summary>Stores <paramref name="value"/> and every widget's appearance state, <paramref name="chosen"/> being the widget turned on.</summary>
    private void Apply(CosName value, PdfWidgetAnnotation? chosen)
    {
        if (chosen is null && this is PdfRadioButtonField { IsNoToggleToOff: true })
        {
            throw new InvalidOperationException($"The radio button field {FullyQualifiedName} has NoToggleToOff set: exactly one button shall be on at all times (ISO 32000-2 §12.7.5.2.4, Table 229).");
        }

        bool exclusive = IsExclusiveRadio;
        string? export = chosen is not null && UnisonByExportValue ? GetWidgetExportValue(chosen) : null;
        var states = new List<(PdfWidgetAnnotation Widget, CosName State)>();
        foreach (PdfWidgetAnnotation widget in Widgets)
        {
            if (GetOnState(widget) is not { } on)
            {
                continue;
            }

            bool turnsOn = chosen is not null && (ReferenceEquals(widget.Dictionary, chosen.Dictionary) || (!exclusive && IsOn(widget, on, value, export)));
            states.Add((widget, turnsOn ? on : FormNames.Off));
        }

        Dictionary[FormNames.V] = value;
        foreach ((PdfWidgetAnnotation widget, CosName state) in states)
        {
            widget.Dictionary[AnnotationNames.AS] = state;
        }

        Form.ReportXfaNotUpdated(FullyQualifiedName);
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
            case CosString text when text.Bytes.Contains((byte)0):
                ReportValueType(key, "a name; it is a string holding the byte 0, which no name can contain (§7.3.5), so it reads as Off");
                return null;
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
        bool off = FormNames.Off.Equals(value);
        bool known = off;
        bool anyStates = false;
        string? export = !off && UnisonByExportValue ? ExportOf(value) : null;
        bool exclusive = IsExclusiveRadio;
        IReadOnlyList<PdfWidgetAnnotation> widgets = Widgets;
        foreach (PdfWidgetAnnotation widget in widgets)
        {
            CosName? on = GetOnState(widget);
            anyStates |= on is not null;
            known |= value.Equals(on);
            bool isOn = !off && IsOn(widget, on, value, export);

            // Radio buttons that share an on state without RadiosInUnison: one shows it and the others Off (§12.7.5.2.4).
            bool sharedOff = exclusive && isOn && FormNames.Off.Equals(widget.AppearanceState)
                && widgets.Any(other => !ReferenceEquals(other.Dictionary, widget.Dictionary) && value.Equals(other.AppearanceState) && value.Equals(GetOnState(other)));
            if (on is not null && widget.AppearanceState is { } state && !state.Equals(isOn ? on : FormNames.Off) && !sharedOff)
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

    /// <inheritdoc/>
    internal override string DefaultCaption => "4";

    /// <summary>Checks the box (in its current on state, else the first widget's) or clears it.</summary>
    /// <param name="isChecked">Whether the box is checked.</param>
    /// <exception cref="InvalidOperationException">No widget has an on state to check; call <see cref="PdfToggleButtonField.GenerateAppearances"/> first.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.2.3, as <see cref="PdfToggleButtonField.SetValue(CosName)"/> describes.</remarks>
    public void SetChecked(bool isChecked)
    {
        if (!isChecked)
        {
            SetValue(FormNames.Off);
            return;
        }

        CosName value = Value;
        IReadOnlyList<CosName> states = OnStateNames;
        if (!states.Contains(value))
        {
            value = states.Count > 0 ? states[0] : throw new InvalidOperationException($"No widget of the check box {FullyQualifiedName} has an on state in its normal appearance (ISO 32000-2 §12.7.5.2.3); call {nameof(GenerateAppearances)} first.");
        }

        SetValue(value);
    }

    /// <inheritdoc/>
    internal override CosName DefaultOnState(int index) => new("Yes");
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

    /// <inheritdoc/>
    internal override string DefaultCaption => "l";

    /// <inheritdoc/>
    internal override CosName DefaultOnState(int index) => new(index.ToString(CultureInfo.InvariantCulture));
}
