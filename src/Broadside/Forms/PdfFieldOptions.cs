using Broadside.Annotations;

namespace Broadside.Forms;

/// <summary>
/// Options for a new form field and its widgets: the field's flags and names, the default appearance of its text, and the colours
/// and border of its widgets. The defaults make a field with no flags, auto-sized black Helvetica text and a one-point black border.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.4.1 (Tables 226 and 227: <c>Ff</c>, <c>TU</c>), §12.7.4.3 (Table 228: <c>DA</c>, <c>Q</c>), §12.7.5.3
/// (Table 232: <c>MaxLen</c>) and §12.5.6.19 (Table 192: <c>MK</c> <c>BC</c>, <c>BG</c>, <c>CA</c>; Table 168: <c>BS</c>
/// <c>W</c>). Used by the <c>Add</c> methods of <see cref="PdfAcroForm"/>.
/// </remarks>
public sealed class PdfFieldOptions
{
    private double _fontSize;
    private PdfDeviceColor _textColor = new(0);
    private PdfTextJustification _quadding = PdfTextJustification.Left;
    private PdfDeviceColor? _borderColor = new(0);
    private PdfDeviceColor? _backgroundColor;
    private double _borderWidth = 1;
    private int? _maxLength;

    /// <summary>
    /// Gets or sets the field flags (<c>Ff</c>) the caller chooses: those common to all fields and those of the field's type. The
    /// flags that select the type (Radio, Pushbutton, Combo) are set by the <c>Add</c> method; the default is none.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227; §12.7.5.2.1, Table 229; §12.7.5.3, Table 231; §12.7.5.4, Table 233.</remarks>
    public PdfFieldFlags Flags { get; set; }

    /// <summary>Gets or sets the field's alternative name (<c>TU</c>, PDF 1.3), shown in place of the field name in the user interface and read by assistive technology; <see langword="null"/> writes none.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226, and §14.9.3.</remarks>
    public string? AlternateName { get; set; }

    /// <summary>Gets or sets the size of the field's text in points; 0, the default, auto-sizes it to the widget.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, not finite, or above 10 000.</exception>
    /// <remarks>ISO 32000-2 §12.7.4.3: the size operand of the default appearance's <c>Tf</c>, 0 for auto.</remarks>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 10_000)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A font size is a finite number from 0 (auto) to 10 000.");
            }

            _fontSize = value;
        }
    }

    /// <summary>Gets or sets the colour of the field's text (DeviceGray, DeviceRGB or DeviceCMYK); the default is black.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The colour has no components.</exception>
    /// <remarks>ISO 32000-2 §12.7.4.3: the default appearance's <c>g</c>, <c>rg</c> or <c>k</c>.</remarks>
    public PdfDeviceColor TextColor
    {
        get => _textColor;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.IsTransparent)
            {
                throw new ArgumentException("The text colour needs 1, 3 or 4 components.", nameof(value));
            }

            _textColor = value;
        }
    }

    /// <summary>Gets or sets how the field's text is justified (<c>Q</c>); the default is left.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined justification.</exception>
    /// <remarks>ISO 32000-2 §12.7.4.3, Table 228.</remarks>
    public PdfTextJustification Quadding
    {
        get => _quadding;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a justification Table 228 defines.");
            }

            _quadding = value;
        }
    }

    /// <summary>Gets or sets the colour of the widgets' border (<c>MK</c> <c>BC</c>); <see langword="null"/> draws none. The default is black.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfDeviceColor? BorderColor
    {
        get => _borderColor;
        set => _borderColor = value is { IsTransparent: true } ? null : value;
    }

    /// <summary>Gets or sets the colour of the widgets' background (<c>MK</c> <c>BG</c>); <see langword="null"/>, the default, draws none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfDeviceColor? BackgroundColor
    {
        get => _backgroundColor;
        set => _backgroundColor = value is { IsTransparent: true } ? null : value;
    }

    /// <summary>Gets or sets the width of the widgets' border in points (<c>BS</c> <c>W</c>); the default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, not finite, or above 1 000.</exception>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168. Drawn only with a <see cref="BorderColor"/>.</remarks>
    public double BorderWidth
    {
        get => _borderWidth;
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 1_000)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A border width is a finite number from 0 to 1 000.");
            }

            _borderWidth = value;
        }
    }

    /// <summary>Gets or sets the maximum length of a text field's text (<c>MaxLen</c>); <see langword="null"/>, the default, is unlimited. Text fields only.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 232. A comb field (<see cref="PdfFieldFlags.Comb"/>) needs it.</remarks>
    public int? MaxLength
    {
        get => _maxLength;
        set
        {
            if (value is < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxLen is a non-negative integer.");
            }

            _maxLength = value;
        }
    }

    /// <summary>
    /// Gets or sets the widgets' caption (<c>MK</c> <c>CA</c>): a push button's label, or the ZapfDingbats character a check box or
    /// radio button shows when on (<c>4</c>, a check mark, and <c>l</c>, a dot, by default); <see langword="null"/> writes none.
    /// Buttons only.
    /// </summary>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192, and §12.7.5.2.</remarks>
    public string? Caption { get; set; }

    /// <summary>Sets <see cref="Flags"/>.</summary>
    /// <param name="flags">The flags.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 227.</remarks>
    public PdfFieldOptions WithFlags(PdfFieldFlags flags)
    {
        Flags = flags;
        return this;
    }

    /// <summary>Sets <see cref="AlternateName"/>.</summary>
    /// <param name="alternateName">The alternative name, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226.</remarks>
    public PdfFieldOptions WithAlternateName(string? alternateName)
    {
        AlternateName = alternateName;
        return this;
    }

    /// <summary>Sets <see cref="FontSize"/>.</summary>
    /// <param name="fontSize">The size in points, 0 for auto.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size is negative, not finite, or above 10 000.</exception>
    /// <remarks>ISO 32000-2 §12.7.4.3.</remarks>
    public PdfFieldOptions WithFontSize(double fontSize)
    {
        FontSize = fontSize;
        return this;
    }

    /// <summary>Sets <see cref="TextColor"/>.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The colour has no components.</exception>
    /// <remarks>ISO 32000-2 §12.7.4.3.</remarks>
    public PdfFieldOptions WithTextColor(PdfDeviceColor color)
    {
        TextColor = color;
        return this;
    }

    /// <summary>Sets <see cref="Quadding"/>.</summary>
    /// <param name="quadding">The justification.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quadding"/> is not a defined justification.</exception>
    /// <remarks>ISO 32000-2 §12.7.4.3, Table 228.</remarks>
    public PdfFieldOptions WithQuadding(PdfTextJustification quadding)
    {
        Quadding = quadding;
        return this;
    }

    /// <summary>Sets <see cref="BorderColor"/>.</summary>
    /// <param name="color">The colour, or <see langword="null"/> for no border.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfFieldOptions WithBorderColor(PdfDeviceColor? color)
    {
        BorderColor = color;
        return this;
    }

    /// <summary>Sets <see cref="BackgroundColor"/>.</summary>
    /// <param name="color">The colour, or <see langword="null"/> for no background.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfFieldOptions WithBackgroundColor(PdfDeviceColor? color)
    {
        BackgroundColor = color;
        return this;
    }

    /// <summary>Sets <see cref="BorderWidth"/>.</summary>
    /// <param name="width">The width in points.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is negative, not finite, or above 1 000.</exception>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168.</remarks>
    public PdfFieldOptions WithBorderWidth(double width)
    {
        BorderWidth = width;
        return this;
    }

    /// <summary>Sets <see cref="MaxLength"/>.</summary>
    /// <param name="maxLength">The maximum number of characters, or <see langword="null"/> for unlimited.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxLength"/> is negative.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.3, Table 232.</remarks>
    public PdfFieldOptions WithMaxLength(int? maxLength)
    {
        MaxLength = maxLength;
        return this;
    }

    /// <summary>Sets <see cref="Caption"/>.</summary>
    /// <param name="caption">The caption, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §12.5.6.19, Table 192.</remarks>
    public PdfFieldOptions WithCaption(string? caption)
    {
        Caption = caption;
        return this;
    }
}
