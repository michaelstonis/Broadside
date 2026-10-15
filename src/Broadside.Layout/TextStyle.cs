using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The character style of a run of text: font, size, fill colour, underline, strike-through, kerning and shaping. Immutable; the
/// <c>With*</c> methods return a changed copy. A bold or italic run is a run in a bold or italic font.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.3 (text state parameters: <c>Tf</c> selects the font and size) and §8.6.8 (the fill colour, set with the colour
/// operators, paints the glyphs of the default text rendering mode). Underline and strike-through are not text state: they are
/// filled rectangles (§8.5.3.3) drawn in the run's colour after the line's text object, as wide as the run on that line.
/// </remarks>
public sealed class TextStyle
{
    private TextShaping? _effective;

    /// <summary>Creates a style in <paramref name="font"/> at <paramref name="fontSize"/> points, in the default colour.</summary>
    /// <param name="font">The font; it shall belong to the document the flow renders into.</param>
    /// <param name="fontSize">The font size in points.</param>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not positive and finite.</exception>
    public TextStyle(PdfCanvasFont font, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(font);
        Font = font;
        FontSize = Positive(fontSize, nameof(fontSize));
    }

    private TextStyle(TextStyle source)
    {
        Font = source.Font;
        FontSize = source.FontSize;
        Color = source.Color;
        Underline = source.Underline;
        StrikeThrough = source.StrikeThrough;
        Kerning = source.Kerning;
        Shaping = source.Shaping;
    }

    /// <summary>The font.</summary>
    public PdfCanvasFont Font { get; private init; }

    /// <summary>The font size in points.</summary>
    public double FontSize { get; private init; }

    /// <summary>The fill colour of the glyphs and decorations, or null for the default (black in DeviceGray).</summary>
    public PdfColor? Color { get; private init; }

    /// <summary>Whether a line is drawn under the run.</summary>
    public bool Underline { get; private init; }

    /// <summary>Whether a line is drawn through the run.</summary>
    public bool StrikeThrough { get; private init; }

    /// <summary>Whether the font's kerning applies (the <c>kern</c> feature of <see cref="Shaping"/>).</summary>
    public bool Kerning { get; private init; }

    /// <summary>The OpenType shaping of the run: script, language and features.</summary>
    public TextShaping Shaping { get; private init; } = TextShaping.Default;

    /// <summary>The shaping with <c>kern</c> following <see cref="Kerning"/>.</summary>
    internal TextShaping EffectiveShaping => _effective ??= Shaping.WithFeature("kern", Kerning);

    /// <summary>Returns a copy in another font (such as the bold of the same family).</summary>
    /// <param name="font">The font.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is null.</exception>
    public TextStyle WithFont(PdfCanvasFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return new(this) { Font = font };
    }

    /// <summary>Returns a copy at another size.</summary>
    /// <param name="fontSize">The font size in points.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not positive and finite.</exception>
    public TextStyle WithFontSize(double fontSize) => new(this) { FontSize = Positive(fontSize, nameof(fontSize)) };

    /// <summary>Returns a copy in another fill colour.</summary>
    /// <param name="color">The colour, or null for the default black. A pattern colour is not accepted.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentException"><paramref name="color"/> is a pattern colour.</exception>
    public TextStyle WithColor(PdfColor? color)
    {
        if (color is { } value && value.Pattern is not null)
        {
            throw new ArgumentException("A text colour shall not be a pattern.", nameof(color));
        }

        return new(this) { Color = color };
    }

    /// <summary>Returns a copy with or without an underline.</summary>
    /// <param name="underline">Whether to underline.</param>
    /// <returns>The changed style.</returns>
    public TextStyle WithUnderline(bool underline) => new(this) { Underline = underline };

    /// <summary>Returns a copy with or without a strike-through line.</summary>
    /// <param name="strikeThrough">Whether to strike through.</param>
    /// <returns>The changed style.</returns>
    public TextStyle WithStrikeThrough(bool strikeThrough) => new(this) { StrikeThrough = strikeThrough };

    /// <summary>Returns a copy with kerning on or off.</summary>
    /// <param name="kerning">Whether to kern.</param>
    /// <returns>The changed style.</returns>
    public TextStyle WithKerning(bool kerning) => new(this) { Kerning = kerning };

    /// <summary>Returns a copy with other OpenType shaping.</summary>
    /// <param name="shaping">The shaping.</param>
    /// <returns>The changed style.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shaping"/> is null.</exception>
    public TextStyle WithShaping(TextShaping shaping)
    {
        ArgumentNullException.ThrowIfNull(shaping);
        return new(this) { Shaping = shaping };
    }

    /// <summary>The height above the baseline of the font's glyph box, in points.</summary>
    internal double Ascent => Font.Ascent * FontSize / 1000;

    /// <summary>The depth below the baseline of the font's glyph box, in points (negative or zero).</summary>
    internal double Descent => Font.Descent * FontSize / 1000;

    private static double Positive(double value, string name) =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be positive and finite.");
}
