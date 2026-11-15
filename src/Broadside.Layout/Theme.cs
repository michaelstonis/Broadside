using System.Diagnostics.CodeAnalysis;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// The styles of the six heading levels for the document builder, each a partial <see cref="Style"/> over the theme's heading root (the
/// theme's <see cref="Theme.HeadingFont"/>, bold). A level whose style sets no <see cref="Style.FontSize"/> is sized from the theme's
/// <see cref="Theme.BaseSize"/>: 2, 1.6, 1.3, 1.15, 1 and 0.9 times it for <c>H1</c> to <c>H6</c>.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.3.6 (<c>H1</c> to <c>H6</c>). Immutable: derive variants with <c>with { }</c>.</remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public sealed record HeadingStyles
{
    private static readonly double[] Scales = [2, 1.6, 1.3, 1.15, 1, 0.9];

    /// <summary>Gets the default heading styles: space before 12 points (H1, H2) or 9 points (H3 to H6) and space after 6 points (H1, H2) or 4 points.</summary>
    public static HeadingStyles Default { get; } = new()
    {
        H1 = new Style { SpaceBefore = 12, SpaceAfter = 6 },
        H2 = new Style { SpaceBefore = 12, SpaceAfter = 6 },
        H3 = new Style { SpaceBefore = 9, SpaceAfter = 4 },
        H4 = new Style { SpaceBefore = 9, SpaceAfter = 4 },
        H5 = new Style { SpaceBefore = 9, SpaceAfter = 4 },
        H6 = new Style { SpaceBefore = 9, SpaceAfter = 4 },
    };

    /// <summary>Gets the style of level 1 headings.</summary>
    public Style H1 { get; init; } = Style.Empty;

    /// <summary>Gets the style of level 2 headings.</summary>
    public Style H2 { get; init; } = Style.Empty;

    /// <summary>Gets the style of level 3 headings.</summary>
    public Style H3 { get; init; } = Style.Empty;

    /// <summary>Gets the style of level 4 headings.</summary>
    public Style H4 { get; init; } = Style.Empty;

    /// <summary>Gets the style of level 5 headings.</summary>
    public Style H5 { get; init; } = Style.Empty;

    /// <summary>Gets the style of level 6 headings.</summary>
    public Style H6 { get; init; } = Style.Empty;

    /// <summary>Gets the style of a level.</summary>
    /// <param name="level">The level, 1 to 6.</param>
    /// <returns>The style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside 1 to 6.</exception>
    public Style this[int level] => level switch
    {
        1 => H1,
        2 => H2,
        3 => H3,
        4 => H4,
        5 => H5,
        6 => H6,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "A heading level is 1 to 6."),
    };

    /// <summary>The size factor of a level against the base size.</summary>
    internal static double ScaleOf(int level) => Scales[level - 1];
}

/// <summary>
/// The look of a document built with the document builder: its font families, base size, palette, element styles, table style and page
/// setup. <see cref="Default"/> is A4 with 20 mm margins and Helvetica (embedded Liberation Sans); <see cref="Letter"/> is the same on US
/// Letter. Pass a theme with <see cref="DocumentBuilder.WithTheme"/>.
/// </summary>
/// <remarks>
/// <para>
/// A theme is an immutable record: one static theme serves many documents built at once on many threads. Derive variants with the
/// <c>With*</c> helpers or <c>with { }</c>. Styles resolve from <see cref="Body"/> (whose unset properties come from <see cref="BodyFont"/>,
/// <see cref="BaseSize"/>, <see cref="LineHeight"/> and <see cref="Text"/>) through the element styles (<see cref="Headings"/>,
/// <see cref="Caption"/>, <see cref="TableHeader"/>, <see cref="Link"/>) to the scopes and elements.
/// </para>
/// <para>Colors are DeviceRGB (ISO 32000-2 §8.6.4.3); a text color equal to black writes no color operator.</para>
/// </remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public sealed record Theme
{
    /// <summary>
    /// Gets the default theme, value by value: Helvetica (embedded Liberation Sans) for body and headings, Courier (embedded Liberation
    /// Mono) for monospace; base size 11 points, line height 1.2; text black, muted #5B6770, accent #0B5FFF, border #C8CDD2, subtle #F2F4F6,
    /// negative #B42318; body paragraphs with 6 points after; captions italic, 9 points; links underlined in the accent color; table header
    /// cells bold; headers and footers 8.5 points, muted; the engine's default table style with a 0.5 point border in the border color;
    /// A4 pages with 20 mm margins; no logo.
    /// </summary>
    public static Theme Default { get; } = new();

    /// <summary>Gets <see cref="Default"/> on US Letter pages with 0.75 inch margins.</summary>
    public static Theme Letter { get; } = new() { Page = new PageSetup(612, 792, new PageMargins(54)) };

    /// <summary>Gets the family of body text.</summary>
    public FontFamily BodyFont { get; init; } = FontFamily.Standard(Standard14Family.Helvetica);

    /// <summary>Gets the family of headings.</summary>
    public FontFamily HeadingFont { get; init; } = FontFamily.Standard(Standard14Family.Helvetica);

    /// <summary>Gets the monospace family.</summary>
    public FontFamily MonospaceFont { get; init; } = FontFamily.Standard(Standard14Family.Courier);

    /// <summary>Gets the size of body text in points.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double BaseSize
    {
        get;
        init => field = double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The base size shall be positive and finite.");
    } = 11;

    /// <summary>Gets the distance between baselines as a multiple of the font size.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double LineHeight
    {
        get;
        init => field = double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The line height shall be positive and finite.");
    } = 1.2;

    /// <summary>Gets the color of text.</summary>
    public PdfColor Text { get; init; } = PdfColor.FromGray(0);

    /// <summary>Gets the color of secondary text.</summary>
    public PdfColor Muted { get; init; } = Rgb(0x5B, 0x67, 0x70);

    /// <summary>Gets the accent color.</summary>
    public PdfColor Accent { get; init; } = Rgb(0x0B, 0x5F, 0xFF);

    /// <summary>Gets the color of borders and rules.</summary>
    public PdfColor Border { get; init; } = Rgb(0xC8, 0xCD, 0xD2);

    /// <summary>Gets the background of alternate table rows.</summary>
    public PdfColor Subtle { get; init; } = Rgb(0xF2, 0xF4, 0xF6);

    /// <summary>Gets the color of negative values and warnings.</summary>
    public PdfColor Negative { get; init; } = Rgb(0xB4, 0x23, 0x18);

    /// <summary>Gets the style of body text, the root of every other style.</summary>
    public Style Body { get; init; } = new() { SpaceAfter = 6 };

    /// <summary>Gets the heading styles.</summary>
    public HeadingStyles Headings { get; init; } = HeadingStyles.Default;

    /// <summary>Gets the style of captions.</summary>
    public Style Caption { get; init; } = new() { Italic = true, FontSize = 9, SpaceBefore = 3, SpaceAfter = 9 };

    /// <summary>Gets the style of links; a link whose style sets no color is drawn in <see cref="Accent"/>.</summary>
    public Style Link { get; init; } = new() { Underline = true };

    /// <summary>Gets the style of table header cells (header rows and row headers).</summary>
    public Style TableHeader { get; init; } = new() { Bold = true };

    /// <summary>Gets the style of header and footer text; a style that sets no color draws in <see cref="Muted"/>.</summary>
    public Style HeaderFooter { get; init; } = new() { FontSize = 8.5 };

    /// <summary>Gets the style of tables.</summary>
    public TableStyle Table { get; init; } = TableStyle.Default.WithBorder(new TableBorder(0.5, Rgb(0xC8, 0xCD, 0xD2)));

    /// <summary>Gets the page size and margins of sections that set none.</summary>
    public PageSetup Page
    {
        get;
        init
        {
            value.Validate(nameof(value));
            field = value;
        }
    } = new PageSetup(595.276, 841.89, new PageMargins(56.693));

    /// <summary>Gets the logo for templates to place, or null.</summary>
    public ImageSource? Logo { get; init; }

    /// <summary>Returns a copy with the base size set.</summary>
    /// <param name="size">The size of body text in points.</param>
    /// <returns>The new theme.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not positive and finite.</exception>
    public Theme WithBaseSize(double size) => this with { BaseSize = size };

    /// <summary>Returns a copy with the body font set.</summary>
    /// <param name="family">The family.</param>
    /// <returns>The new theme.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    public Theme WithBodyFont(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return this with { BodyFont = family };
    }

    /// <summary>Returns a copy with the heading font set.</summary>
    /// <param name="family">The family.</param>
    /// <returns>The new theme.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="family"/> is null.</exception>
    public Theme WithHeadingFont(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return this with { HeadingFont = family };
    }

    /// <summary>Returns a copy with the accent color set.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The new theme.</returns>
    public Theme WithAccent(PdfColor color) => this with { Accent = color };

    /// <summary>Returns a copy with the page setup set.</summary>
    /// <param name="page">The page size and margins.</param>
    /// <returns>The new theme.</returns>
    /// <exception cref="ArgumentException">The setup leaves no content area.</exception>
    public Theme WithPage(PageSetup page) => this with { Page = page };

    private static PdfColor Rgb(byte red, byte green, byte blue) => PdfColor.FromRgb(red / 255f, green / 255f, blue / 255f);
}
