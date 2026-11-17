using System.Diagnostics.CodeAnalysis;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Layout.Flow;

/// <summary>
/// A partial style for the document builder: every property is optional, and a null property inherits. Styles merge from the theme down
/// through the scopes (section, group, list, table, row, cell) to the element and its spans, each property taking the nearest non-null value.
/// </summary>
/// <remarks>
/// <para>
/// Text properties inherit into everything inside a scope: <see cref="FontFamily"/>, <see cref="FontSize"/>, <see cref="Bold"/>,
/// <see cref="Italic"/>, <see cref="Underline"/>, <see cref="StrikeThrough"/>, <see cref="Color"/>, <see cref="LineHeight"/>,
/// <see cref="Alignment"/>, <see cref="FirstLineIndent"/>, <see cref="Language"/> and <see cref="Shaping"/>. Box properties
/// (<see cref="SpaceBefore"/> and <see cref="SpaceAfter"/>) apply only to the element that carries them, so a group's space after does not
/// add space after every child.
/// </para>
/// <para>
/// A style is an immutable record: share one across documents and threads, and derive variants with <c>with { }</c>. The engine's
/// <see cref="TextStyle"/> and <see cref="ParagraphStyle"/> are the resolved forms the builder lays out with. ISO 32000-2 §9.3 (text
/// state) and §14.9.2 (<see cref="Language"/>, written as <c>Lang</c> where it changes).
/// </para>
/// </remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
public sealed record Style
{
    /// <summary>Gets a style that sets nothing.</summary>
    public static Style Empty { get; } = new();

    /// <summary>Gets the font family, or null to inherit.</summary>
    public FontFamily? FontFamily { get; init; }

    /// <summary>Gets the font size in points, or null to inherit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double? FontSize
    {
        get;
        init => field = value is not { } size || (double.IsFinite(size) && size > 0) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The font size shall be positive and finite.");
    }

    /// <summary>Gets whether the bold face is used, or null to inherit.</summary>
    public bool? Bold { get; init; }

    /// <summary>Gets whether the italic face is used, or null to inherit.</summary>
    public bool? Italic { get; init; }

    /// <summary>Gets whether text is underlined, or null to inherit.</summary>
    public bool? Underline { get; init; }

    /// <summary>Gets whether text is struck through, or null to inherit.</summary>
    public bool? StrikeThrough { get; init; }

    /// <summary>Gets the text color, or null to inherit.</summary>
    /// <exception cref="ArgumentException">The color is a pattern.</exception>
    public PdfColor? Color
    {
        get;
        init => field = value is { Pattern: not null } ? throw new ArgumentException("A text color shall not be a pattern.", nameof(value)) : value;
    }

    /// <summary>Gets the distance between baselines as a multiple of the font size, or null to inherit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double? LineHeight
    {
        get;
        init => field = value is not { } height || (double.IsFinite(height) && height > 0) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The line height shall be positive and finite.");
    }

    /// <summary>Gets the horizontal alignment of lines, or null to inherit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    public TextAlignment? Alignment
    {
        get;
        init => field = value is not { } alignment || Enum.IsDefined(alignment) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown alignment.");
    }

    /// <summary>Gets the space above the element (not inherited), or null for the element's default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public Length? SpaceBefore
    {
        get;
        init => field = NotNegative(value, nameof(value));
    }

    /// <summary>Gets the space below the element (not inherited), or null for the element's default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public Length? SpaceAfter
    {
        get;
        init => field = NotNegative(value, nameof(value));
    }

    /// <summary>Gets the indent of a paragraph's first line, or null to inherit.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public Length? FirstLineIndent
    {
        get;
        init => field = NotNegative(value, nameof(value));
    }

    /// <summary>Gets the natural language as a BCP 47 tag (such as <c>fr-FR</c>), or null to inherit.</summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public string? Language
    {
        get;
        init => field = value is { Length: 0 } ? throw new ArgumentException("The language shall not be empty.", nameof(value)) : value;
    }

    /// <summary>Gets the OpenType features and script used to shape text, or null to inherit.</summary>
    public TextShaping? Shaping { get; init; }

    /// <summary>Returns this style with every property that <paramref name="other"/> sets taken from it.</summary>
    /// <param name="other">The nearer style, or null.</param>
    /// <returns>The merged style.</returns>
    public Style Merge(Style? other)
    {
        if (other is null || ReferenceEquals(other, Empty) || ReferenceEquals(other, this))
        {
            return this;
        }

        if (ReferenceEquals(this, Empty))
        {
            return other;
        }

        return new Style
        {
            FontFamily = other.FontFamily ?? FontFamily,
            FontSize = other.FontSize ?? FontSize,
            Bold = other.Bold ?? Bold,
            Italic = other.Italic ?? Italic,
            Underline = other.Underline ?? Underline,
            StrikeThrough = other.StrikeThrough ?? StrikeThrough,
            Color = other.Color ?? Color,
            LineHeight = other.LineHeight ?? LineHeight,
            Alignment = other.Alignment ?? Alignment,
            SpaceBefore = other.SpaceBefore ?? SpaceBefore,
            SpaceAfter = other.SpaceAfter ?? SpaceAfter,
            FirstLineIndent = other.FirstLineIndent ?? FirstLineIndent,
            Language = other.Language ?? Language,
            Shaping = other.Shaping ?? Shaping,
        };
    }

    /// <summary>This style without its box properties: what a scope passes down to its content.</summary>
    internal Style Inherited => SpaceBefore is null && SpaceAfter is null ? this : this with { SpaceBefore = null, SpaceAfter = null };

    private static Length? NotNegative(Length? value, string name) =>
        value is { } length && length.ToPoints() < 0 ? throw new ArgumentOutOfRangeException(name, value, "The length shall not be negative.") : value;
}
