namespace Broadside.Forms;

/// <summary>The type of a form field, its inheritable <c>FT</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.7.4.1, Table 226.</remarks>
public enum PdfFieldType
{
    /// <summary>No <c>FT</c> in the field or its ancestors, or a name Table 226 does not define.</summary>
    Unknown = 0,

    /// <summary><c>Btn</c>: a push button, check box or radio button field (§12.7.5.2).</summary>
    Button = 1,

    /// <summary><c>Tx</c>: a text field (§12.7.5.3).</summary>
    Text = 2,

    /// <summary><c>Ch</c>: a combo box or list box (§12.7.5.4).</summary>
    Choice = 3,

    /// <summary><c>Sig</c> (PDF 1.3): a signature field (§12.7.5.5).</summary>
    Signature = 4,
}

/// <summary>What a field is: a non-terminal field or one of the terminal field kinds, from its type and flags.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.2 (terminal and non-terminal fields), §12.7.5.2 Table 229 (Pushbutton and Radio decide the button kind) and
/// §12.7.5.4 Table 233 (Combo decides the choice kind).
/// </remarks>
public enum PdfFieldKind
{
    /// <summary>A terminal field whose type is missing or unknown (<see cref="PdfUnknownField"/>).</summary>
    Unknown = 0,

    /// <summary>A field whose children are fields (<see cref="PdfNonTerminalField"/>).</summary>
    NonTerminal = 1,

    /// <summary>A text field (<see cref="PdfTextField"/>).</summary>
    Text = 2,

    /// <summary>A push button (<see cref="PdfPushButtonField"/>).</summary>
    PushButton = 3,

    /// <summary>A check box (<see cref="PdfCheckBoxField"/>).</summary>
    CheckBox = 4,

    /// <summary>A set of radio buttons (<see cref="PdfRadioButtonField"/>).</summary>
    RadioButton = 5,

    /// <summary>A combo box (<see cref="PdfComboBoxField"/>).</summary>
    ComboBox = 6,

    /// <summary>A scrollable list box (<see cref="PdfListBoxField"/>).</summary>
    ListBox = 7,

    /// <summary>A signature field (<see cref="PdfSignatureField"/>).</summary>
    Signature = 8,
}

/// <summary>The document-level signature flags of an interactive form, its <c>SigFlags</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.7.3, Table 225 (PDF 1.3). Bits the table does not define are kept as read.</remarks>
[Flags]
public enum PdfSignatureFlags
{
    /// <summary>No flag set (the default).</summary>
    None = 0,

    /// <summary>Bit 1: the document contains at least one signature field.</summary>
    SignaturesExist = 1 << 0,

    /// <summary>Bit 2: the document contains signatures that a save other than an incremental update may invalidate.</summary>
    AppendOnly = 1 << 1,
}

/// <summary>The field flags, the bits of a field's inheritable <c>Ff</c> entry.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.4.1, Tables 226 and 227 (all fields), §12.7.5.2 Table 229 (buttons), §12.7.5.3 Table 231 (text fields) and
/// §12.7.5.4 Table 233 (choice fields). Bit n of the entry is <c>1 &lt;&lt; (n − 1)</c>. The type-specific bits mean something only
/// for their field type; bit 26 is <see cref="RichText"/> for a text field and <see cref="RadiosInUnison"/> for a button. Bits no
/// table defines shall be 0 and are kept as stored (<see cref="PdfField.Flags"/>).
/// </remarks>
[Flags]
public enum PdfFieldFlags
{
    /// <summary>No flag set (the default).</summary>
    None = 0,

    /// <summary>Bit 1 (Table 227): the user may not change the field's value.</summary>
    ReadOnly = 1 << 0,

    /// <summary>Bit 2 (Table 227): the field shall have a value when exported by a submit-form action.</summary>
    Required = 1 << 1,

    /// <summary>Bit 3 (Table 227): the field shall not be exported by a submit-form action.</summary>
    NoExport = 1 << 2,

    /// <summary>Bit 13 (Table 231, text): the text may span several lines.</summary>
    Multiline = 1 << 12,

    /// <summary>Bit 14 (Table 231, text): the text is a password, not echoed and not stored.</summary>
    Password = 1 << 13,

    /// <summary>Bit 15 (Table 229, radio buttons): exactly one radio button shall be selected at all times.</summary>
    NoToggleToOff = 1 << 14,

    /// <summary>Bit 16 (Table 229, buttons): the field is a set of radio buttons.</summary>
    Radio = 1 << 15,

    /// <summary>Bit 17 (Table 229, buttons): the field is a push button that retains no permanent value.</summary>
    Pushbutton = 1 << 16,

    /// <summary>Bit 18 (Table 233, choice): the field is a combo box, else a list box.</summary>
    Combo = 1 << 17,

    /// <summary>Bit 19 (Table 233, combo boxes): the combo box includes an editable text box.</summary>
    Edit = 1 << 18,

    /// <summary>Bit 20 (Table 233, choice): the options shall be sorted alphabetically (for authoring tools only).</summary>
    Sort = 1 << 19,

    /// <summary>Bit 21 (Table 231, text, PDF 1.4): the text is the path name of a file whose contents are submitted.</summary>
    FileSelect = 1 << 20,

    /// <summary>Bit 22 (Table 233, choice, PDF 1.4): more than one option may be selected.</summary>
    MultiSelect = 1 << 21,

    /// <summary>Bit 23 (Tables 231 and 233, PDF 1.4): the text entered shall not be spell-checked.</summary>
    DoNotSpellCheck = 1 << 22,

    /// <summary>Bit 24 (Table 231, text, PDF 1.4): the field does not scroll to accommodate more text than fits.</summary>
    DoNotScroll = 1 << 23,

    /// <summary>Bit 25 (Table 231, text, PDF 1.5): the field is divided into <c>MaxLen</c> equally spaced positions.</summary>
    Comb = 1 << 24,

    /// <summary>Bit 26 (Table 231, text, PDF 1.5): the value is rich text.</summary>
    RichText = 1 << 25,

    /// <summary>Bit 26 (Table 229, radio buttons, PDF 1.5): radio buttons with the same on state turn on and off in unison; the same bit as <see cref="RichText"/>.</summary>
    RadiosInUnison = RichText,

    /// <summary>Bit 27 (Table 233, choice, PDF 1.5): a new selection is committed as soon as it is made.</summary>
    CommitOnSelChange = 1 << 26,
}

/// <summary>Which fields a signature field lock dictionary locks, its <c>Action</c> entry.</summary>
/// <remarks>ISO 32000-2 §12.7.5.5, Table 236.</remarks>
public enum PdfSignatureFieldLockAction
{
    /// <summary>The entry is missing or is a name the table does not define.</summary>
    Unknown = 0,

    /// <summary><c>All</c>: all fields in the document.</summary>
    All = 1,

    /// <summary><c>Include</c>: the fields named in <c>Fields</c>.</summary>
    Include = 2,

    /// <summary><c>Exclude</c>: all fields except those named in <c>Fields</c>.</summary>
    Exclude = 3,
}
