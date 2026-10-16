namespace Broadside.Annotations;

/// <summary>
/// Options for a new annotation: its colours, border, opacity, flags and the markup texts. The defaults draw a one-point border in
/// the subtype's usual colour (yellow highlights and notes, red shapes, markups and stamps, a borderless free text in black 12-point
/// Helvetica), printed, with no title or subject.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.5.2 (Table 166: <c>C</c>, <c>F</c>, <c>NM</c>, <c>CA</c>), §12.5.4 (Table 168: <c>BS</c> <c>W</c>), §12.5.6.2
/// (Table 172: <c>T</c>, <c>Subj</c>) and the subtype tables of §12.5.6 (<c>IC</c>, <c>DA</c>). Used by the <c>Add…Annotation</c>
/// methods of <see cref="PdfPage"/>.
/// </remarks>
public sealed class PdfAnnotationOptions
{
    private double _borderWidth = 1;
    private double _opacity = 1;
    private double _fontSize = 12;
    private PdfDeviceColor _textColor = new(0);

    /// <summary>Gets or sets the annotation's colour (<c>C</c>): the border or stroke colour of shapes and the colour of notes, markups and stamps; <see langword="null"/>, the default, uses the subtype's usual colour.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166. A colour with no components draws no border.</remarks>
    public PdfDeviceColor? Color { get; set; }

    /// <summary>Gets or sets the interior colour (<c>IC</c>, PDF 1.4) a square, circle, polygon or line ending is filled with; <see langword="null"/>, the default, fills nothing.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.8, Table 180; §12.5.6.9, Table 181; §12.5.6.7, Table 178.</remarks>
    public PdfDeviceColor? InteriorColor { get; set; }

    /// <summary>Gets or sets the border width in points (<c>BS</c> <c>W</c>); the default is 1, and 0 draws no border.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, not finite, or above 1 000.</exception>
    /// <remarks>ISO 32000-2 §12.5.4, Table 168.</remarks>
    public double BorderWidth
    {
        get => _borderWidth;
        set => _borderWidth = Check(value, 0, 1_000, "A border width is a finite number from 0 to 1 000.");
    }

    /// <summary>Gets or sets the constant opacity the appearance is painted with (<c>CA</c>, PDF 1.4 for markup annotations); the default is 1, opaque.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166, and §12.5.5: the value applies to the appearance stream.</remarks>
    public double Opacity
    {
        get => _opacity;
        set => _opacity = Check(value, 0, 1, "An opacity is a number from 0 to 1.");
    }

    /// <summary>Gets or sets the annotation's flags (<c>F</c>); the default is <see cref="PdfAnnotationFlags.Print"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.3, Table 167.</remarks>
    public PdfAnnotationFlags Flags { get; set; } = PdfAnnotationFlags.Print;

    /// <summary>Gets or sets the annotation's contents (<c>Contents</c>), the text a viewer shows for it; <see langword="null"/>, the default, writes none. A note's and a free text's text are given to the <c>Add</c> method instead.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public string? Contents { get; set; }

    /// <summary>Gets or sets the markup annotation's title (<c>T</c>), by convention its author; <see langword="null"/>, the default, writes none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172.</remarks>
    public string? Title { get; set; }

    /// <summary>Gets or sets the markup annotation's subject (<c>Subj</c>, PDF 1.5); <see langword="null"/>, the default, writes none.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.2, Table 172.</remarks>
    public string? Subject { get; set; }

    /// <summary>Gets or sets the annotation name (<c>NM</c>, PDF 1.4), unique among the page's annotations; <see langword="null"/>, the default, writes none.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166.</remarks>
    public string? Name { get; set; }

    /// <summary>Gets or sets the size in points of a free text annotation's text; the default is 12.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not above 0, not finite, or above 1 000.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177: the size operand of <c>DA</c>'s <c>Tf</c>.</remarks>
    public double FontSize
    {
        get => _fontSize;
        set => _fontSize = value > 0 ? Check(value, 0, 1_000, "A font size is a finite number above 0, at most 1 000.") : throw new ArgumentOutOfRangeException(nameof(value), value, "A font size is a finite number above 0, at most 1 000.");
    }

    /// <summary>Gets or sets the colour of a free text annotation's text (DeviceGray, DeviceRGB or DeviceCMYK); the default is black.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The colour has no components.</exception>
    /// <remarks>ISO 32000-2 §12.5.6.6, Table 177: <c>DA</c>'s <c>g</c>, <c>rg</c> or <c>k</c>.</remarks>
    public PdfDeviceColor TextColor
    {
        get => _textColor;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _textColor = value.IsTransparent ? throw new ArgumentException("A text colour has one, three or four components.", nameof(value)) : value;
        }
    }

    /// <summary>Sets <see cref="Color"/>.</summary>
    /// <param name="color">The colour, or <see langword="null"/> for the subtype's usual one.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithColor(PdfDeviceColor? color)
    {
        Color = color;
        return this;
    }

    /// <summary>Sets <see cref="InteriorColor"/>.</summary>
    /// <param name="color">The colour, or <see langword="null"/> to fill nothing.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithInteriorColor(PdfDeviceColor? color)
    {
        InteriorColor = color;
        return this;
    }

    /// <summary>Sets <see cref="BorderWidth"/>.</summary>
    /// <param name="width">The width in points.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithBorderWidth(double width)
    {
        BorderWidth = width;
        return this;
    }

    /// <summary>Sets <see cref="Opacity"/>.</summary>
    /// <param name="opacity">The opacity, 0 to 1.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithOpacity(double opacity)
    {
        Opacity = opacity;
        return this;
    }

    /// <summary>Sets <see cref="Flags"/>.</summary>
    /// <param name="flags">The flags.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithFlags(PdfAnnotationFlags flags)
    {
        Flags = flags;
        return this;
    }

    /// <summary>Sets <see cref="Contents"/>.</summary>
    /// <param name="contents">The contents, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithContents(string? contents)
    {
        Contents = contents;
        return this;
    }

    /// <summary>Sets <see cref="Title"/>.</summary>
    /// <param name="title">The title, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithTitle(string? title)
    {
        Title = title;
        return this;
    }

    /// <summary>Sets <see cref="Subject"/>.</summary>
    /// <param name="subject">The subject, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithSubject(string? subject)
    {
        Subject = subject;
        return this;
    }

    /// <summary>Sets <see cref="Name"/>.</summary>
    /// <param name="name">The annotation name, or <see langword="null"/>.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithName(string? name)
    {
        Name = name;
        return this;
    }

    /// <summary>Sets <see cref="FontSize"/>.</summary>
    /// <param name="size">The size in points.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithFontSize(double size)
    {
        FontSize = size;
        return this;
    }

    /// <summary>Sets <see cref="TextColor"/>.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>These options.</returns>
    public PdfAnnotationOptions WithTextColor(PdfDeviceColor color)
    {
        TextColor = color;
        return this;
    }

    private static double Check(double value, double minimum, double maximum, string message) =>
        double.IsFinite(value) && value >= minimum && value <= maximum ? value : throw new ArgumentOutOfRangeException(nameof(value), value, message);
}
