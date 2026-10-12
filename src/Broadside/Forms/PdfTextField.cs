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

    /// <summary>
    /// Sets the field's text (<c>V</c>) and regenerates the normal appearance of every widget to show it.
    /// </summary>
    /// <param name="value">The text; <see langword="null"/> removes the value. Line breaks are kept only by a multi-line field.</param>
    /// <exception cref="ArgumentException">The text is longer than <see cref="MaxLength"/> characters.</exception>
    /// <exception cref="InvalidOperationException">
    /// A character has no glyph in the default appearance font and no font program found by the engine's font resolvers has one
    /// either; nothing is changed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.5.3 and §12.7.4.3. The value is stored as a text string in the field's own dictionary (§7.9.2.2), and the
    /// rich text value <c>RV</c> is removed: the text is plain. A password field stores no value (§12.7.5.3, Table 231: a processor
    /// shall never store it) and shows one asterisk per character. Read-only fields are filled too: the flag restricts the user, not
    /// the program.
    /// </para>
    /// <para>
    /// Appearances are generated as <see cref="GenerateAppearances"/> describes, before anything changes, so a failure leaves the
    /// document as it was. When the form also has an XFA resource (Annex K), only the AcroForm value changes and an
    /// <c>XfaDataNotUpdated</c> information diagnostic is recorded: the XFA datasets keep the old value.
    /// </para>
    /// </remarks>
    public void SetValue(string? value)
    {
        if (value is not null && MaxLength is int maxLength && maxLength >= 0)
        {
            int length = 0;
            foreach (System.Text.Rune _ in value.EnumerateRunes())
            {
                length++;
            }

            if (length > maxLength)
            {
                throw new ArgumentException($"The text has {length} characters; the field's MaxLen allows {maxLength} (ISO 32000-2 §12.7.5.3, Table 232).", nameof(value));
            }
        }

        TextFieldAppearance appearances = TextFieldAppearance.Build(this, value);
        if (value is null || IsPassword)
        {
            Dictionary.Remove(FormNames.V);
        }
        else
        {
            Dictionary[FormNames.V] = new CosString(TextStringEncoder.Encode(value));
        }

        Dictionary.Remove(FormNames.RV);
        appearances.Apply();
        Form.ReportXfaNotUpdated(FullyQualifiedName);
    }

    /// <summary>
    /// Regenerates the normal appearance of every widget from the field's current <see cref="Value"/>, as an interactive processor
    /// does for variable text.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A character has no glyph in the default appearance font and no font program found by the engine's font resolvers has one
    /// either; nothing is changed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §12.7.4.3. Each widget gets a new form XObject as its normal appearance (<c>AP</c> <c>N</c>; other appearance
    /// states are dropped), whose bounding box is the widget rectangle's size, whose resources are copied from the default resources
    /// (<c>DR</c>) the font was found in, and whose content is the clause's: the <c>MK</c> background and border (in the border style),
    /// then <c>/Tx BMC q</c>, a clip to the inside of the border, <c>BT</c>, the default appearance (its <c>Tf</c> with the size
    /// computed when auto-sized, its colour, its other operators as written), a <c>Tm</c> per line whose translation positions the
    /// text, and <c>ET Q EMC</c>. The widget's <c>MK</c> rotation (<c>R</c>) rotates the appearance (§12.5.6.19, Table 192).
    /// </para>
    /// <para>
    /// The text follows the quadding (<see cref="PdfField.Quadding"/>) on one line, or wraps between words in a multi-line field; a
    /// comb field spaces at most <see cref="MaxLength"/> characters evenly, one per cell; a password field shows asterisks. An
    /// auto-sized font fits the field. Characters the default appearance font cannot encode are shown in a TrueType font found by the
    /// engine's font resolvers (configure <c>UseStandard14Fonts()</c> for Liberation Sans), embedded as a subset Type 0 font
    /// (§9.7, §9.9.2) with a <c>ToUnicode</c> CMap (§9.10.3). Rich text (<see cref="IsRichText"/>) is shown as plain text.
    /// </para>
    /// </remarks>
    public void GenerateAppearances() => TextFieldAppearance.Build(this, Value).Apply();
}
