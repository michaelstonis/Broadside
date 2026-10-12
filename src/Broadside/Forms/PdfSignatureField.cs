using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>A signature field: the place of a digital signature, signed when its value is a signature dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.5, Tables 235 to 237; field type <c>Sig</c> (PDF 1.3). A signature field "shall never refer to more than one
/// annotation"; one with several widgets keeps them all and records <c>SignatureFieldMultipleWidgets</c>. Signature dictionaries,
/// seed values and validation are read raw here.
/// </remarks>
public sealed class PdfSignatureField : PdfTerminalField
{
    internal PdfSignatureField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.Signature, widgets)
    {
    }

    /// <summary>Gets the signature dictionary the field holds when it is signed (<c>V</c>, inheritable), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5 and §12.8.1, Table 255. A value that is not a dictionary reads as absent with a <c>FieldValueTypeInvalid</c> diagnostic.</remarks>
    public CosDictionary? SignatureDictionary
    {
        get
        {
            switch (ValueObject)
            {
                case null:
                    return null;
                case CosDictionary signature:
                    return signature;
                default:
                    ReportValueType(FormNames.V, "a signature dictionary; it reads as absent");
                    return null;
            }
        }
    }

    /// <summary>Gets a value indicating whether the field has a signature value.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5.</remarks>
    public bool IsSigned => SignatureDictionary is not null;

    /// <summary>
    /// Gets a value indicating whether the signature is visible: it has a widget whose rectangle has a width or height and whose
    /// Hidden and NoView flags are clear.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.5.5: processors "shall treat such signatures as not visible".</remarks>
    public bool IsVisible => Widgets.Any(widget =>
        (widget.Rect.Width != 0 || widget.Rect.Height != 0) && (widget.Flags & (PdfAnnotationFlags.Hidden | PdfAnnotationFlags.NoView)) == 0);

    /// <summary>Gets the fields locked when the field is signed (<c>Lock</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Tables 235 and 236; shall be an indirect reference, else read with <c>SignatureFieldDictNotIndirect</c>.</remarks>
    public PdfSignatureFieldLock? Lock => ReadIndirectDictionary(FormNames.Lock) is { } dictionary ? new PdfSignatureFieldLock(this, dictionary) : null;

    /// <summary>Gets the seed value dictionary that constrains the signature applied to the field (<c>SV</c>, PDF 1.5), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Tables 235 and 237; shall be an indirect reference, else read with <c>SignatureFieldDictNotIndirect</c>.</remarks>
    public CosDictionary? SeedValue => ReadIndirectDictionary(FormNames.SV);

    private CosDictionary? ReadIndirectDictionary(CosName key)
    {
        switch (Own(key))
        {
            case null:
                return null;
            case CosDictionary dictionary:
                if (Dictionary[key] is not CosReference)
                {
                    Report(DiagnosticCodes.SignatureFieldDictNotIndirect, $"The signature field's {key.Value} entry shall be an indirect reference; it is a direct dictionary, read anyway.");
                }

                return dictionary;
            default:
                return InvalidEntry<CosDictionary>(key, "a dictionary");
        }
    }
}

/// <summary>A signature field lock dictionary: the fields whose values may no longer change once the signature field is signed.</summary>
/// <remarks>ISO 32000-2 §12.7.5.5, Table 236 (PDF 1.5). A live view over the dictionary.</remarks>
public sealed class PdfSignatureFieldLock
{
    private readonly PdfSignatureField _field;

    internal PdfSignatureFieldLock(PdfSignatureField field, CosDictionary dictionary)
    {
        _field = field;
        Dictionary = dictionary;
    }

    /// <summary>Gets the lock dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets which fields are locked (<c>Action</c>, required).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236. A missing or unknown name reads as <see cref="PdfSignatureFieldLockAction.Unknown"/> with a diagnostic.</remarks>
    public PdfSignatureFieldLockAction Action
    {
        get
        {
            PdfSignatureFieldLockAction action = (FieldTree.Get(_field.Document, Dictionary, FormNames.Action) as CosName)?.Value switch
            {
                "All" => PdfSignatureFieldLockAction.All,
                "Include" => PdfSignatureFieldLockAction.Include,
                "Exclude" => PdfSignatureFieldLockAction.Exclude,
                _ => PdfSignatureFieldLockAction.Unknown,
            };
            if (action == PdfSignatureFieldLockAction.Unknown)
            {
                _field.Report(DiagnosticCodes.FieldEntryInvalid, "The signature field lock dictionary's Action shall be All, Include or Exclude (Table 236).");
            }

            return action;
        }
    }

    /// <summary>Gets the fully qualified names of the fields included or excluded (<c>Fields</c>); empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236: required for Include and Exclude. Resolve them with <see cref="PdfAcroForm.FindFields(string)"/>.</remarks>
    public IReadOnlyList<string> FieldNames
    {
        get
        {
            if (FieldTree.Get(_field.Document, Dictionary, FormNames.Fields) is not CosArray array)
            {
                if (Action is PdfSignatureFieldLockAction.Include or PdfSignatureFieldLockAction.Exclude)
                {
                    _field.Report(DiagnosticCodes.FieldEntryInvalid, "The signature field lock dictionary has no Fields array, which Table 236 requires for Include and Exclude.");
                }

                return [];
            }

            return [.. array.Select(element => _field.Document.Resolve(element)).OfType<CosString>().Select(name => name.DecodeText())];
        }
    }

    /// <summary>Gets the access permissions granted once signed (<c>P</c>, PDF 2.0): 1, 2 or 3; <see langword="null"/> when absent (no effect).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.5, Table 236. Another value reads as absent with a diagnostic.</remarks>
    public int? Permissions
    {
        get
        {
            switch (FieldTree.Get(_field.Document, Dictionary, FormNames.P))
            {
                case null:
                    return null;
                case CosInteger { Value: >= 1 and <= 3 } integer:
                    return (int)integer.Value;
                default:
                    _field.Report(DiagnosticCodes.FieldEntryInvalid, "The signature field lock dictionary's P shall be 1, 2 or 3 (Table 236); it is ignored.", DiagnosticSeverity.Warning);
                    return null;
            }
        }
    }
}

/// <summary>A terminal field whose type (<c>FT</c>) is missing or is not one Table 226 defines.</summary>
/// <remarks>ISO 32000-2 §12.7.4.1, Table 226: <c>FT</c> is required for terminal fields. Read with a <c>FieldTypeMissing</c> diagnostic, its widgets kept.</remarks>
public sealed class PdfUnknownField : PdfTerminalField
{
    internal PdfUnknownField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.Unknown, widgets)
    {
    }
}
