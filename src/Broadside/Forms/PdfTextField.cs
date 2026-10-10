using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>A text field: a box the user types text into.</summary>
/// <remarks>ISO 32000-2 §12.7.5.3, Tables 231 and 232; field type <c>Tx</c>. Its text is variable text (§12.7.4.3).</remarks>
public sealed class PdfTextField : PdfTerminalField
{
    internal PdfTextField(FieldInfo info, CosObject[] widgets)
        : base(info, PdfFieldKind.Text, widgets)
    {
    }

    /// <summary>Gets the field's text (<c>V</c>, inheritable): a text string, or a text stream (PDF 1.5); <see langword="null"/> when there is none.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.3. A name or number reads as its text with a <c>FieldValueTypeInvalid</c> diagnostic. A password field
    /// may hold a value although writers should never store one; it is returned as stored.
    /// </remarks>
    public string? Value => ReadTextValue(ValueObject, FormNames.V);

    /// <summary>Gets the text the field reverts to on a reset-form action (<c>DV</c>, inheritable), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226, and §12.7.5.3.</remarks>
    public string? DefaultValue => ReadTextValue(DefaultValueObject, FormNames.DV);

    /// <summary>Gets the maximum length of the field's text in characters (<c>MaxLen</c>, inheritable), or <see langword="null"/> when unlimited.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 232.</remarks>
    public int? MaxLength => ReadInteger(FormNames.MaxLen, inheritable: true);

    /// <summary>Gets a value indicating whether the text may span several lines (<c>Ff</c> bit 13, Multiline).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 231.</remarks>
    public bool IsMultiline => HasFlag(PdfFieldFlags.Multiline);

    /// <summary>Gets a value indicating whether the field is for a password, not echoed (<c>Ff</c> bit 14, Password).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 231.</remarks>
    public bool IsPassword => HasFlag(PdfFieldFlags.Password);

    /// <summary>Gets a value indicating whether the text is the path of a file submitted as the value (<c>Ff</c> bit 21, FileSelect, PDF 1.4).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 231.</remarks>
    public bool IsFileSelect => HasFlag(PdfFieldFlags.FileSelect);

    /// <summary>Gets a value indicating whether the text shall not be spell-checked (<c>Ff</c> bit 23, DoNotSpellCheck, PDF 1.4).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 231.</remarks>
    public bool IsDoNotSpellCheck => HasFlag(PdfFieldFlags.DoNotSpellCheck);

    /// <summary>Gets a value indicating whether the field shall not scroll to fit more text (<c>Ff</c> bit 24, DoNotScroll, PDF 1.4).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 231.</remarks>
    public bool IsDoNotScroll => HasFlag(PdfFieldFlags.DoNotScroll);

    /// <summary>Gets a value indicating whether the field is divided into <see cref="MaxLength"/> equally spaced combs (<c>Ff</c> bit 25, Comb, PDF 1.5).</summary>
    /// <remarks>
    /// ISO 32000-2 §12.7.5.3, Table 231: may be set only with <c>MaxLen</c> and with Multiline, Password and FileSelect clear. The flag
    /// is returned as stored; when those conditions fail a <c>TextCombInvalid</c> diagnostic is recorded.
    /// </remarks>
    public bool IsComb
    {
        get
        {
            if (!HasFlag(PdfFieldFlags.Comb))
            {
                return false;
            }

            if (MaxLength is null || IsMultiline || IsPassword || IsFileSelect)
            {
                Report(DiagnosticCodes.TextCombInvalid, "The Comb flag may be set only with a MaxLen entry and with the Multiline, Password and FileSelect flags clear (Table 231).");
            }

            return true;
        }
    }

    /// <summary>Gets a value indicating whether the value is rich text, held in <see cref="PdfField.RichValue"/> (<c>Ff</c> bit 26, RichText, PDF 1.5).</summary>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 231.</remarks>
    public bool IsRichText => HasFlag(PdfFieldFlags.RichText);
}
