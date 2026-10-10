using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>A field of an interactive form: a live view over its field dictionary and the ancestors it inherits from.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.2 and §12.7.4.1, Table 226. A field whose children are fields is a <see cref="PdfNonTerminalField"/>; every
/// other field is a <see cref="PdfTerminalField"/> of the class its type (<c>FT</c>) and flags (<c>Ff</c>) select.
/// </para>
/// <para>
/// Inheritable entries (<c>FT</c>, <c>Ff</c>, <c>V</c>, <c>DV</c>, <c>DA</c>, <c>Q</c> and the type-specific ones the tables mark
/// inheritable) come from the field when it has them and otherwise from the nearest ancestor that does, the whole value and never
/// merged; <c>DA</c> and <c>Q</c> then fall back to the interactive form dictionary. Ancestors are the fields on the path from the
/// form's <c>Fields</c> array that the field was found through (§12.7.4.1: inheritance is not limited in range), including any
/// unnamed dictionaries between them. Every property reads the dictionaries when called and writes nothing; a malformed entry
/// reads as its default and records a diagnostic the first time it is read (lenient mode), or throws (strict mode).
/// </para>
/// </remarks>
public abstract class PdfField
{
    private readonly CosDictionary[] _intermediates;

    private protected PdfField(FieldInfo info, PdfFieldKind kind)
    {
        Form = info.Form;
        Dictionary = info.Dictionary;
        Reference = info.Reference;
        Parent = info.Parent;
        PartialName = info.PartialName;
        FullyQualifiedName = info.FullyQualifiedName;
        _intermediates = info.Intermediates;
        Kind = kind;
    }

    /// <summary>Gets the interactive form the field belongs to.</summary>
    /// <remarks>ISO 32000-2 §12.7.1: all fields make up a single, global interactive form.</remarks>
    public PdfAcroForm Form { get; }

    /// <summary>Gets the field dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the field dictionary, or <see langword="null"/> when it is held directly (§12.7.4.1 requires an indirect object).</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1.</remarks>
    public CosReference? Reference { get; }

    /// <summary>Gets the field's parent in the field hierarchy, or <see langword="null"/> for a root field.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.4.1, Table 226 (<c>Parent</c>). The field whose <c>Kids</c> the field was found in, which is what its
    /// <c>Parent</c> entry shall name; when the two disagree the tree wins and a <c>FieldParentMismatch</c> diagnostic is recorded.
    /// </remarks>
    public PdfNonTerminalField? Parent { get; }

    /// <summary>Gets what the field is: non-terminal, or the kind of terminal field its type and flags select.</summary>
    /// <remarks>ISO 32000-2 §12.7.2, §12.7.5.</remarks>
    public PdfFieldKind Kind { get; }

    /// <summary>Gets the partial field name (<c>T</c>).</summary>
    /// <remarks>ISO 32000-2 §12.7.4.2, Table 226 (required); a text string, which shall not contain a period.</remarks>
    public string PartialName { get; }

    /// <summary>Gets the fully qualified field name: the partial names of the field's ancestors and its own, joined by periods.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.2.</remarks>
    public string FullyQualifiedName { get; }

    /// <summary>Gets the alternative field name shown in the user interface and used for accessibility (<c>TU</c>, PDF 1.3), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226; not inheritable.</remarks>
    public string? AlternateName => ReadOwnText(FormNames.TU);

    /// <summary>Gets the name used when exporting the field's data (<c>TM</c>, PDF 1.3), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226; not inheritable.</remarks>
    public string? MappingName => ReadOwnText(FormNames.TM);

    /// <summary>Gets the field type (<c>FT</c>, inheritable) as stored, or <see langword="null"/> when neither the field nor an ancestor has one.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226 (required for terminal fields).</remarks>
    public CosName? FieldTypeName => Inherited(FormNames.FT) as CosName;

    /// <summary>Gets the field type (<c>FT</c>, inheritable).</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226. A non-terminal field may carry one for its descendants; it has no type of its own.</remarks>
    public PdfFieldType FieldType => ToFieldType(FieldTypeName);

    /// <summary>Gets the field flags (<c>Ff</c>, inheritable), every bit as stored; 0 when absent.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.4.1, Tables 226 and 227: an unsigned 32-bit integer. The value of the nearest dictionary that has the entry
    /// is used whole, never combined with an ancestor's. A real number is truncated and a negative integer read as its 32-bit pattern,
    /// with a <c>FieldFlagsInvalid</c> diagnostic; so is a bit no field type defines, which is kept. The type-specific bits are the
    /// properties of each field class.
    /// </remarks>
    public PdfFieldFlags Flags => (PdfFieldFlags)(int)ReadFlags(Inherited(FormNames.Ff), report: true);

    /// <summary>Gets a value indicating whether the user may not change the field's value (<c>Ff</c> bit 1, ReadOnly).</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227.</remarks>
    public bool IsReadOnly => HasFlag(PdfFieldFlags.ReadOnly);

    /// <summary>Gets a value indicating whether the field shall have a value when it is exported by a submit-form action (<c>Ff</c> bit 2, Required).</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227.</remarks>
    public bool IsRequired => HasFlag(PdfFieldFlags.Required);

    /// <summary>Gets a value indicating whether a submit-form action shall not export the field (<c>Ff</c> bit 3, NoExport).</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227.</remarks>
    public bool IsNoExport => HasFlag(PdfFieldFlags.NoExport);

    /// <summary>Gets the field's value (<c>V</c>, inheritable) as stored and resolved, or <see langword="null"/>; the field classes decode it.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226. Never falls back to <see cref="DefaultValueObject"/>.</remarks>
    public CosObject? ValueObject => Inherited(FormNames.V);

    /// <summary>Gets the value the field reverts to on a reset-form action (<c>DV</c>, inheritable) as stored and resolved, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226.</remarks>
    public CosObject? DefaultValueObject => Inherited(FormNames.DV);

    /// <summary>
    /// Gets the default appearance string (<c>DA</c>): the field's or its nearest ancestor's, else the interactive form's; <see langword="null"/>
    /// when none has one. Read as bytes, one character per byte; not parsed.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.4.3, Table 228 (required and inheritable for fields containing variable text) and §12.7.3, Table 224. A
    /// text or choice field without one records <c>DefaultAppearanceMissing</c>.
    /// </remarks>
    public string? DefaultAppearance
    {
        get
        {
            CosObject? value = Inherited(FormNames.DA);
            string? text = value switch
            {
                null => Form.DefaultAppearance,
                CosString bytes => System.Text.Encoding.Latin1.GetString(bytes.Bytes),
                _ => InvalidEntry<string>(FormNames.DA, "a string"),
            };

            if (text is null && value is null && Kind != PdfFieldKind.NonTerminal && FieldType is PdfFieldType.Text or PdfFieldType.Choice)
            {
                Report(DiagnosticCodes.DefaultAppearanceMissing, "The field contains variable text but neither it, its ancestors nor the interactive form has a DA entry, which Table 228 requires.");
            }

            return text;
        }
    }

    /// <summary>Gets the justification of the field's variable text (<c>Q</c>): the field's or its nearest ancestor's, else the interactive form's; default left.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3, Table 228 (inheritable) and §12.7.3, Table 224. A value other than 0, 1 or 2 reads as left with a diagnostic.</remarks>
    public PdfTextJustification Quadding
    {
        get
        {
            CosObject? value = Inherited(FormNames.Q);
            if (value is null)
            {
                return Form.Quadding;
            }

            if (value is CosInteger { Value: >= 0 and <= 2 } integer)
            {
                return (PdfTextJustification)(int)integer.Value;
            }

            Report(DiagnosticCodes.FieldEntryInvalid, "The field's Q entry shall be 0, 1 or 2; it reads as 0 (left).");
            return PdfTextJustification.Left;
        }
    }

    /// <summary>Gets the default style string (<c>DS</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3, Table 228; not inheritable. Kept as text: its syntax is the XFA specification's.</remarks>
    public string? DefaultStyle => ReadOwnText(FormNames.DS);

    /// <summary>Gets the rich text value (<c>RV</c>, PDF 1.5) from a text string or text stream, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3, Table 228; not inheritable. Kept as text: its syntax is the XFA specification's.</remarks>
    public string? RichValue
    {
        get
        {
            switch (Own(FormNames.RV))
            {
                case null:
                    return null;
                case CosString text:
                    return text.DecodeText();
                case CosStream stream:
                    return new CosString(Document.DecodeStream(stream).Span).DecodeText();
                default:
                    return InvalidEntry<string>(FormNames.RV, "a text string or text stream");
            }
        }
    }

    /// <summary>Gets the actions performed on the field's trigger events (<c>AA</c>: keystroke, format, validate, calculate), or <see langword="null"/>.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.4.1, Table 226, and §12.6.3, Table 199; the field's own entry (not inheritable), read through
    /// <see cref="PdfDocument.GetFieldAdditionalActions"/>. A field merged with its widget shares the dictionary with the widget's
    /// annotation triggers (<see cref="PdfWidgetAnnotation.AdditionalActions"/>).
    /// </remarks>
    public PdfFieldAdditionalActions? AdditionalActions =>
        Dictionary.TryGetValue(FormNames.AA, out CosObject? value) ? Document.GetFieldAdditionalActions(value, DiagnosticReference) : null;

    /// <summary>Gets the document the field belongs to.</summary>
    internal PdfDocument Document => Form.Document;

    /// <summary>Gets the object diagnostics about the field are reported against: the field, else its nearest indirect ancestor, else the form.</summary>
    internal CosReference? DiagnosticReference
    {
        get
        {
            for (PdfField? current = this; current is not null; current = current.Parent)
            {
                if (current.Reference is { } reference)
                {
                    return reference;
                }
            }

            return Form.Reference;
        }
    }

    /// <summary>Maps an <c>FT</c> name to its type.</summary>
    internal static PdfFieldType ToFieldType(CosName? name) => name?.Value switch
    {
        "Btn" => PdfFieldType.Button,
        "Tx" => PdfFieldType.Text,
        "Ch" => PdfFieldType.Choice,
        "Sig" => PdfFieldType.Signature,
        _ => PdfFieldType.Unknown,
    };

    /// <summary>Returns the inheritable entry <paramref name="key"/>: the field's, an unnamed dictionary's above it, or the nearest ancestor's; resolved.</summary>
    internal CosObject? Inherited(CosName key)
    {
        for (PdfField? current = this; current is not null; current = current.Parent)
        {
            if (FieldTree.Get(Document, current.Dictionary, key) is { } value)
            {
                return value;
            }

            // Unnamed levels are listed outermost first; the nearest one wins (Table 226: the nearest ancestor's value).
            for (int index = current._intermediates.Length - 1; index >= 0; index--)
            {
                if (FieldTree.Get(Document, current._intermediates[index], key) is { } carried)
                {
                    return carried;
                }
            }
        }

        return null;
    }

    /// <summary>Returns the field's own entry <paramref name="key"/>, resolved; <see langword="null"/> when absent or null.</summary>
    internal CosObject? Own(CosName key) => FieldTree.Get(Document, Dictionary, key);

    /// <summary>Reports a deviation in this field.</summary>
    internal void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        Document.DiagnosticSink.Report(code, severity, message, objectReference: DiagnosticReference);

    /// <summary>Reads an <c>Ff</c> value: an unsigned 32-bit integer; reports what it repairs when <paramref name="report"/> is set.</summary>
    internal uint ReadFlags(CosObject? value, bool report)
    {
        long bits;
        switch (value)
        {
            case null:
                return 0;
            case CosInteger integer when integer.Value is >= 0 and <= uint.MaxValue:
                bits = integer.Value;
                break;
            case CosInteger integer when integer.Value is < 0 and >= int.MinValue:
                bits = (uint)(int)integer.Value;
                if (report)
                {
                    Report(DiagnosticCodes.FieldFlagsInvalid, "The field's Ff entry is negative; it reads as its 32-bit pattern.");
                }

                break;
            case CosReal real when double.IsFinite(real.Value) && real.Value is >= 0 and <= uint.MaxValue:
                bits = (long)real.Value;
                if (report)
                {
                    Report(DiagnosticCodes.FieldFlagsInvalid, "The field's Ff entry shall be an integer; it is a real number, truncated.");
                }

                break;
            default:
                if (report)
                {
                    Report(DiagnosticCodes.FieldFlagsInvalid, "The field's Ff entry shall be an integer; it reads as 0.");
                }

                return 0;
        }

        // Bits 1-3 (Table 227) and 13-27 (Tables 229, 231, 233) are defined; every other bit shall be 0.
        if (report && (bits & ~0x7FFF007L) != 0)
        {
            Report(DiagnosticCodes.FieldFlagsInvalid, "The field's Ff entry sets bits no field type defines, which shall be 0; they are kept.");
        }

        return (uint)bits;
    }

    /// <summary>Whether <paramref name="flag"/> is set in <see cref="Flags"/>.</summary>
    private protected bool HasFlag(PdfFieldFlags flag) => (Flags & flag) != 0;

    /// <summary>Reads the field's own text string entry (§7.9.2.2); another type reads as absent with a diagnostic.</summary>
    private protected string? ReadOwnText(CosName key) => Own(key) switch
    {
        null => null,
        CosString text => text.DecodeText(),
        _ => InvalidEntry<string>(key, "a text string"),
    };

    /// <summary>Reads an integer entry, own or inherited; another type reads as absent with a diagnostic.</summary>
    private protected int? ReadInteger(CosName key, bool inheritable)
    {
        switch (inheritable ? Inherited(key) : Own(key))
        {
            case null:
                return null;
            case CosInteger integer when integer.Value is >= int.MinValue and <= int.MaxValue:
                return (int)integer.Value;
            default:
                return InvalidEntry<int?>(key, "an integer");
        }
    }

    /// <summary>Decodes a text value (a text string, or a text stream from PDF 1.5); a name or number reads as its text with a diagnostic.</summary>
    private protected string? ReadTextValue(CosObject? value, CosName key)
    {
        switch (value)
        {
            case null:
                return null;
            case CosString text:
                return text.DecodeText();
            case CosStream stream:
                return new CosString(Document.DecodeStream(stream).Span).DecodeText();
            case CosName name:
                ReportValueType(key, "a text string; it is a name, read as its text");
                return name.Value;
            case CosNumber number:
                ReportValueType(key, "a text string; it is a number, read as its text");
                return number is CosInteger integer
                    ? integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : number.ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            default:
                ReportValueType(key, "a text string; it reads as absent");
                return null;
        }
    }

    /// <summary>Reports a value (<c>V</c> or <c>DV</c>) of the wrong type.</summary>
    private protected void ReportValueType(CosName key, string what) =>
        Report(DiagnosticCodes.FieldValueTypeInvalid, $"The field's {key.Value} entry shall be {what}.");

    /// <summary>Reports an entry of the wrong type and returns the default.</summary>
    private protected T? InvalidEntry<T>(CosName key, string expected)
    {
        Report(DiagnosticCodes.FieldEntryInvalid, $"The field's {key.Value} entry shall be {expected}; it is ignored.");
        return default;
    }
}

/// <summary>What the field tree knows about a field when it creates the view.</summary>
internal sealed record FieldInfo(
    PdfAcroForm Form,
    CosDictionary Dictionary,
    CosReference? Reference,
    PdfNonTerminalField? Parent,
    string PartialName,
    string FullyQualifiedName,
    CosDictionary[] Intermediates);
