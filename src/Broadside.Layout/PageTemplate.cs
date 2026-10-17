using System.Text;
using Broadside.Graphics;
using Broadside.Images;

namespace Broadside.Layout.Flow;

/// <summary>Whether a page is taller than wide (portrait) or wider than tall (landscape).</summary>
public enum PageOrientation
{
    /// <summary>The height is at least the width.</summary>
    Portrait,

    /// <summary>The width is greater than the height.</summary>
    Landscape,
}

/// <summary>
/// How the pages of a section of a <see cref="FlowDocument"/> look: the page size, orientation and margins, a background colour or
/// image and a watermark under the body, and headers and footers, with their own variants for the first page of the section and
/// for even-numbered pages. One template may serve several sections. Immutable once built.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.2.2, Table 363: everything a template draws is an artifact, not real content. Backgrounds are marked
/// <c>/Artifact &lt;&lt;/Type /Background&gt;&gt;</c>, the watermark <c>/Type /Pagination /Subtype /Watermark</c>, headers and footers
/// <c>/Type /Pagination /Subtype /Header</c> or <c>/Footer</c> with <c>Attached</c> and <c>BBox</c>, so text extraction and tagged
/// structure leave them out.
/// </para>
/// <para>
/// The body flows in the media box less the margins; a header or footer that reaches past its margin moves the body's edge in so
/// the two never overlap.
/// </para>
/// </remarks>
public sealed class PageTemplate
{
    /// <summary>Creates a template of pages of <paramref name="setup"/> with no header, footer or background.</summary>
    /// <param name="setup">The page size and margins.</param>
    /// <exception cref="ArgumentException">A dimension or margin is not finite or negative, or the margins leave no content area.</exception>
    public PageTemplate(PageSetup setup)
    {
        setup.Validate(nameof(setup));
        Setup = setup;
    }

    /// <summary>The size, orientation and margins of the pages.</summary>
    public PageSetup Setup { get; }

    /// <summary>The header of odd pages, and of every page whose own variant is null; null for none.</summary>
    public HeaderFooter? Header { get; init; }

    /// <summary>The footer of odd pages, and of every page whose own variant is null; null for none.</summary>
    public HeaderFooter? Footer { get; init; }

    /// <summary>The header of the first page of each section using this template; null to use <see cref="Header"/>, <see cref="HeaderFooter.None"/> for none.</summary>
    public HeaderFooter? FirstPageHeader { get; init; }

    /// <summary>The footer of the first page of each section using this template; null to use <see cref="Footer"/>, <see cref="HeaderFooter.None"/> for none.</summary>
    public HeaderFooter? FirstPageFooter { get; init; }

    /// <summary>The header of even-numbered pages; null to use <see cref="Header"/>.</summary>
    public HeaderFooter? EvenPageHeader { get; init; }

    /// <summary>The footer of even-numbered pages; null to use <see cref="Footer"/>.</summary>
    public HeaderFooter? EvenPageFooter { get; init; }

    /// <summary>A colour filling the whole page under everything else; null for none.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2: a <c>Background</c> artifact attached to all four page edges.</remarks>
    public PdfColor? BackgroundColor { get; init; }

    /// <summary>An image of this document stretched over the whole page, above <see cref="BackgroundColor"/>; null for none.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2: a <c>Background</c> artifact attached to all four page edges.</remarks>
    public PdfImage? BackgroundImage { get; init; }

    /// <summary>Text drawn across the middle of every page under the body; null for none.</summary>
    public Watermark? Watermark { get; init; }

    /// <summary>The header of the page numbered <paramref name="pageNumber"/>, the first of its section when <paramref name="first"/>.</summary>
    internal HeaderFooter? HeaderFor(int pageNumber, bool first) => Pick(first ? FirstPageHeader : null, pageNumber % 2 == 0 ? EvenPageHeader : null, Header);

    /// <summary>The footer of the page numbered <paramref name="pageNumber"/>, the first of its section when <paramref name="first"/>.</summary>
    internal HeaderFooter? FooterFor(int pageNumber, bool first) => Pick(first ? FirstPageFooter : null, pageNumber % 2 == 0 ? EvenPageFooter : null, Footer);

    /// <summary>The body area of the page numbered <paramref name="pageNumber"/>: the margins, moved in past a header or footer that reaches over them.</summary>
    internal (double Bottom, double Top) BodyFor(int pageNumber, bool first)
    {
        double top = Setup.Height - Setup.Margins.Top;
        double bottom = Setup.Margins.Bottom;
        if (HeaderFor(pageNumber, first) is { IsEmpty: false } header)
        {
            top = Math.Min(top, Setup.Height - header.Offset - header.Height);
        }

        if (FooterFor(pageNumber, first) is { IsEmpty: false } footer)
        {
            bottom = Math.Max(bottom, footer.Offset + footer.Height);
        }

        return (bottom, top);
    }

    /// <summary>Throws when a header, footer or watermark has a character its font cannot encode, or leaves no body area.</summary>
    internal void Validate()
    {
        foreach (bool first in (ReadOnlySpan<bool>)[true, false])
        {
            for (int pageNumber = 1; pageNumber <= 2; pageNumber++)
            {
                (double bottom, double top) = BodyFor(pageNumber, first);
                if (top - bottom <= 0)
                {
                    throw new ArgumentException($"The header and footer of a {Setup.Width} by {Setup.Height} page template leave no body area.");
                }
            }
        }

        foreach ((HeaderFooter? region, string name) in (ReadOnlySpan<(HeaderFooter?, string)>)
            [(Header, "header"), (Footer, "footer"), (FirstPageHeader, "first-page header"), (FirstPageFooter, "first-page footer"), (EvenPageHeader, "even-page header"), (EvenPageFooter, "even-page footer")])
        {
            if (region is null)
            {
                continue;
            }

            foreach (string? text in (ReadOnlySpan<string?>)[region.Left, region.Center, region.Right])
            {
                if (text is not null)
                {
                    CheckEncodable(HeaderFooter.Expand(text, 1234567890, 1234567890), region.Style, $"The {name} of a page template");
                }
            }
        }

        if (Watermark is { } watermark)
        {
            CheckEncodable(watermark.Text, watermark.Style, "The watermark of a page template");
        }
    }

    private static HeaderFooter? Pick(HeaderFooter? first, HeaderFooter? even, HeaderFooter? fallback) => first ?? even ?? fallback;

    private static void CheckEncodable(string text, TextStyle style, string what)
    {
        Span<char> buffer = stackalloc char[2];
        foreach (Rune rune in text.EnumerateRunes())
        {
            int length = rune.EncodeToUtf16(buffer);
            if (!style.Font.CanEncode(buffer[..length]))
            {
                throw new ArgumentException($"{what} has U+{rune.Value:X4}, which the font {style.Font.Name} cannot encode.");
            }
        }
    }
}

/// <summary>
/// A one-line header or footer: up to three texts, left-aligned at the left margin, centred between the margins and right-aligned at
/// the right margin, in one style. A text may hold the fields <see cref="PageNumberField"/> and <see cref="PageCountField"/>.
/// </summary>
/// <remarks>
/// <para>
/// The page count is known only when the last page is laid out, so headers and footers are drawn after it, each page receiving a
/// second content stream; the body never depends on them (their space is fixed by <see cref="Offset"/> and the style), so nothing is
/// laid out twice.
/// </para>
/// <para>ISO 32000-2 §14.8.2.2.2, Table 363: drawn as a pagination artifact (<c>Subtype</c> <c>Header</c> or <c>Footer</c>).</para>
/// </remarks>
public sealed class HeaderFooter
{
    /// <summary>The field replaced by the page's number (1 for the first page of the flow).</summary>
    public const string PageNumberField = "{page}";

    /// <summary>The field replaced by the number of pages the flow laid out.</summary>
    public const string PageCountField = "{pages}";

    /// <summary>Creates an empty header or footer drawn in <paramref name="style"/>.</summary>
    /// <param name="style">The font, size and colour of the texts (underline and strike-through are not drawn).</param>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public HeaderFooter(TextStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style;
    }

    /// <summary>A header or footer that draws nothing: set it as a first-page or even-page variant to leave those pages without one.</summary>
    public static HeaderFooter None { get; } = new();

    /// <summary>The style of the texts.</summary>
    public TextStyle Style { get; } = null!;

    /// <summary>The text at the left margin, or null.</summary>
    public string? Left { get; init; }

    /// <summary>The text centred between the margins, or null.</summary>
    public string? Center { get; init; }

    /// <summary>The text ending at the right margin, or null.</summary>
    public string? Right { get; init; }

    /// <summary>The distance in points from the page edge (top for a header, bottom for a footer) to the line's outer edge; 36 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Offset
    {
        get;
        init => field = double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The offset shall be finite and not negative.");
    } = 36;

    /// <summary>True when there is no text to draw.</summary>
    internal bool IsEmpty => string.IsNullOrEmpty(Left) && string.IsNullOrEmpty(Center) && string.IsNullOrEmpty(Right);

    /// <summary>The height of the line: the style's ascent less its descent.</summary>
    internal double Height => Style.Ascent - Style.Descent;

    private HeaderFooter()
    {
    }

    /// <summary>Replaces the fields of <paramref name="text"/>.</summary>
    internal static string Expand(string text, int pageNumber, int pageCount) =>
        text.Contains('{', StringComparison.Ordinal)
            ? text.Replace(PageCountField, pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace(PageNumberField, pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : text;
}

/// <summary>Text drawn large, rotated and translucent across the centre of every page of a template, under the body.</summary>
/// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363: a pagination artifact with <c>Subtype</c> <c>Watermark</c>; the opacity is the
/// <c>ca</c> constant alpha of an ExtGState (§11.6.4.4).</remarks>
public sealed class Watermark
{
    /// <summary>Creates a watermark of <paramref name="text"/> in <paramref name="style"/>.</summary>
    /// <param name="text">The text, on one line.</param>
    /// <param name="style">The font, size and colour (null colour = black).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    public Watermark(string text, TextStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentNullException.ThrowIfNull(style);
        Text = text;
        Style = style;
    }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <summary>The style of the text.</summary>
    public TextStyle Style { get; }

    /// <summary>The counter-clockwise rotation in degrees; 45 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public double Angle
    {
        get;
        init => field = double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The angle shall be finite.");
    } = 45;

    /// <summary>The opacity, from 0 (invisible) to 1 (opaque); 0.2 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 1.</exception>
    public double Opacity
    {
        get;
        init => field = value is >= 0 and <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The opacity shall be from 0 to 1.");
    } = 0.2;
}
