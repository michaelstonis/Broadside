using Broadside.Fonts;

namespace Broadside.Layout.Flow;

/// <summary>How the lines of a paragraph sit between the left and right edges of the content area.</summary>
/// <remarks>Justification follows ISO 32000-2 §9.4.4: word spacing (<c>Tw</c>) where the font applies it, otherwise <c>TJ</c> adjustments after each space.</remarks>
public enum TextAlignment
{
    /// <summary>Each line starts at the left edge (after the first-line indent).</summary>
    Left,

    /// <summary>Each line is centred between the edges.</summary>
    Center,

    /// <summary>Each line ends at the right edge.</summary>
    Right,

    /// <summary>Each line but the last of a paragraph (or one ended by a newline) stretches its spaces to end at the right edge.</summary>
    Justify,
}

/// <summary>The base direction of a paragraph: the paragraph embedding level of the Unicode Bidirectional Algorithm.</summary>
/// <remarks>UAX #9 (Unicode 16.0.0) rules P2 and P3 (<see cref="Auto"/>), or a higher-level protocol setting the level (HL1).</remarks>
public enum TextDirection
{
    /// <summary>From the first strong character outside isolates: right to left when it is of a right-to-left script (Hebrew, Arabic), else left to right.</summary>
    Auto,

    /// <summary>Left to right (paragraph embedding level 0).</summary>
    LeftToRight,

    /// <summary>Right to left (paragraph embedding level 1).</summary>
    RightToLeft,
}

/// <summary>
/// The font, size, line height, alignment, indent and spacing of a <see cref="Paragraph"/>. Immutable; <c>With*</c> returns a changed
/// copy, so one style can be shared by many paragraphs.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.3 (text state: <c>Tf</c> font and size) and §9.4.4 (text space): widths are measured with
/// <see cref="PdfCanvasFont.MeasureWidth(ReadOnlySpan{char}, double, bool)"/> at character spacing 0, word spacing 0 and horizontal scaling 100.
/// </remarks>
public sealed class ParagraphStyle
{
    private TextStyle? _text;

    /// <summary>Creates a left-aligned style with line height 1.2, no indent and no paragraph spacing.</summary>
    /// <param name="font">The font, from the document the paragraph will be rendered into.</param>
    /// <param name="fontSize">The font size in points; positive and finite.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not positive and finite.</exception>
    public ParagraphStyle(PdfCanvasFont font, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(font);
        Font = font;
        FontSize = Positive(fontSize, nameof(fontSize));
    }

    private ParagraphStyle(ParagraphStyle source)
    {
        Font = source.Font;
        FontSize = source.FontSize;
        LineHeight = source.LineHeight;
        Alignment = source.Alignment;
        FirstLineIndent = source.FirstLineIndent;
        SpaceBefore = source.SpaceBefore;
        SpaceAfter = source.SpaceAfter;
        Kerning = source.Kerning;
        Shaping = source.Shaping;
        Direction = source.Direction;
    }

    /// <summary>The font text is shown in.</summary>
    public PdfCanvasFont Font { get; }

    /// <summary>The font size in points.</summary>
    public double FontSize { get; }

    /// <summary>The distance between baselines as a multiple of the font size; 1.2 by default.</summary>
    public double LineHeight { get; private init; } = 1.2;

    /// <summary>The alignment of the lines.</summary>
    public TextAlignment Alignment { get; private init; }

    /// <summary>The indent of the first line from the left edge, in points.</summary>
    public double FirstLineIndent { get; private init; }

    /// <summary>The space above the paragraph, in points; dropped when the paragraph starts a page.</summary>
    public double SpaceBefore { get; private init; }

    /// <summary>The space below the paragraph, in points.</summary>
    public double SpaceAfter { get; private init; }

    /// <summary>Whether pair kerning from the font is applied when measuring and showing text.</summary>
    public bool Kerning { get; private init; }

    /// <summary>
    /// How text is shaped into glyphs (<see cref="TextShaping.Default"/> by default: the font's ligatures, contextual and localised
    /// forms and Arabic joining, for an embedded OpenType or TrueType font). <see cref="Kerning"/> decides the <c>kern</c> feature.
    /// </summary>
    /// <remarks>OpenType specification, GSUB; see <see cref="PdfCanvasFont.Shape"/>.</remarks>
    public TextShaping Shaping { get; private init; } = TextShaping.Default;

    /// <summary>The line height in points: <see cref="LineHeight"/> times <see cref="FontSize"/>.</summary>
    internal double LineAdvance => LineHeight * FontSize;

    /// <summary>Returns this style with another line height.</summary>
    /// <param name="multiple">The distance between baselines as a multiple of the font size; positive and finite.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="multiple"/> is not positive and finite.</exception>
    public ParagraphStyle WithLineHeight(double multiple) => new(this) { LineHeight = Positive(multiple, nameof(multiple)) };

    /// <summary>Returns this style with another alignment.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alignment"/> is not a defined value.</exception>
    public ParagraphStyle WithAlignment(TextAlignment alignment)
    {
        if (!Enum.IsDefined(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown alignment.");
        }

        return new(this) { Alignment = alignment };
    }

    /// <summary>Returns this style with another first-line indent.</summary>
    /// <param name="indent">The indent in points; finite and not negative.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="indent"/> is negative or not finite.</exception>
    public ParagraphStyle WithFirstLineIndent(double indent) => new(this) { FirstLineIndent = NotNegative(indent, nameof(indent)) };

    /// <summary>Returns this style with other space above the paragraph.</summary>
    /// <param name="space">The space in points; finite and not negative.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative or not finite.</exception>
    public ParagraphStyle WithSpaceBefore(double space) => new(this) { SpaceBefore = NotNegative(space, nameof(space)) };

    /// <summary>Returns this style with other space below the paragraph.</summary>
    /// <param name="space">The space in points; finite and not negative.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="space"/> is negative or not finite.</exception>
    public ParagraphStyle WithSpaceAfter(double space) => new(this) { SpaceAfter = NotNegative(space, nameof(space)) };

    /// <summary>Returns this style with pair kerning on or off.</summary>
    /// <param name="kerning">Whether to apply the font's kerning pairs.</param>
    /// <returns>The changed copy.</returns>
    public ParagraphStyle WithKerning(bool kerning) => new(this) { Kerning = kerning };

    /// <summary>Returns this style with other shaping: script, language system, direction or features.</summary>
    /// <param name="shaping">How text is shaped.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shaping"/> is null.</exception>
    public ParagraphStyle WithShaping(TextShaping shaping)
    {
        ArgumentNullException.ThrowIfNull(shaping);
        return new(this) { Shaping = shaping };
    }

    /// <summary>The shaping text is measured and shown with: <see cref="Shaping"/> with <c>kern</c> as <see cref="Kerning"/> says.</summary>
    /// <summary>
    /// The base direction of the paragraph's text, default <see cref="TextDirection.Auto"/>. Each line is reordered by the Unicode
    /// Bidirectional Algorithm under it, so right-to-left text and the numbers within it appear in visual order; each directional
    /// run is shaped on its own, right to left (with mirrored glyphs) when its level is odd, whatever
    /// <see cref="TextShaping.RightToLeft"/> says.
    /// </summary>
    /// <remarks>UAX #9 (Unicode 16.0.0) rules P2, P3 and HL1; X1 to I2 per paragraph, L1, L2 and L4 per line.</remarks>
    public TextDirection Direction { get; private init; }

    /// <summary>Returns this style with another base direction.</summary>
    /// <param name="direction">The base direction.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a <see cref="TextDirection"/> value.</exception>
    public ParagraphStyle WithDirection(TextDirection direction) =>
        Enum.IsDefined(direction)
            ? new(this) { Direction = direction }
            : throw new ArgumentOutOfRangeException(nameof(direction), direction, "Not a text direction.");

    /// <summary>
    /// The paragraph's text style: its font, size, kerning and shaping, in the default colour without decorations. Inline runs
    /// without a style of their own use it; <c>TextStyle.With*</c> derives run styles from it.
    /// </summary>
    public TextStyle TextStyle => _text ??= new TextStyle(Font, FontSize).WithKerning(Kerning).WithShaping(Shaping);

    private static double Positive(double value, string name) =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be positive and finite.");

    private static double NotNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be finite and not negative.");
}
