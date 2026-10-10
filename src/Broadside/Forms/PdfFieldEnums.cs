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
