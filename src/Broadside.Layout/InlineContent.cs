using Broadside.Images;

namespace Broadside.Layout.Flow;

/// <summary>
/// One piece of a paragraph's content: a <see cref="TextRun"/> or an <see cref="InlineImage"/>. Every piece names the structure
/// element it becomes when the output is tagged.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4.7.2 (inline-level structure types: <c>Span</c>, <c>Link</c>) and §14.8.4.8.5 (<c>Figure</c>), §14.9.2
/// (natural language specification): <see cref="StructureType"/> and <see cref="Language"/> are what tagged output writes for it.
/// </remarks>
public abstract class InlineElement
{
    private protected InlineElement()
    {
    }

    /// <summary>The standard structure type of the element when tagged; by default <c>Span</c>, <c>Link</c> for a linked run and <c>Figure</c> for an image.</summary>
    /// <exception cref="ArgumentException">The value is null or empty.</exception>
    public string StructureType
    {
        get => field ?? DefaultStructureType;
        init => field = string.IsNullOrEmpty(value) ? throw new ArgumentException("The structure type shall not be empty.", nameof(value)) : value;
    }

    /// <summary>The natural language of the element as a BCP 47 tag, or null to inherit the paragraph's.</summary>
    public string? Language { get; init; }

    private protected abstract string DefaultStructureType { get; }
}

/// <summary>
/// A run of text in one <see cref="TextStyle"/>, optionally a link. Runs of different fonts and sizes share a line and its baseline;
/// lines break inside and between runs.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.3 (text-showing operators) and §12.5.6.5 (link annotations): a run with a <see cref="Link"/> gets one link
/// annotation with a URI action (§12.6.4.8) over its portion of each line it is on, from the font's descent to its ascent.
/// </remarks>
public sealed class TextRun : InlineElement
{
    /// <summary>Creates a run in the paragraph's text style (<see cref="ParagraphStyle.TextStyle"/>).</summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public TextRun(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text;
    }

    /// <summary>Creates a run in <paramref name="style"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="style">The style.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> or <paramref name="style"/> is null.</exception>
    public TextRun(string text, TextStyle style)
        : this(text)
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style;
    }

    /// <summary>The text.</summary>
    public string Text { get; }

    /// <summary>The style, or null for the paragraph's.</summary>
    public TextStyle? Style { get; }

    /// <summary>The target of the run as a link, or null when the run is not a link.</summary>
    public Uri? Link { get; init; }

    private protected override string DefaultStructureType => Link is null ? "Span" : "Link";
}

/// <summary>
/// An image placed in a line of text like a glyph: its bottom edge on the baseline, raising the line when taller than the text.
/// </summary>
/// <remarks>ISO 32000-2 §8.9.5 (image XObjects, painted with <c>Do</c> outside the line's text objects) and §14.8.4.8.5 (<c>Figure</c>).</remarks>
public sealed class InlineImage : InlineElement
{
    /// <summary>Creates an inline image of <paramref name="width"/> by <paramref name="height"/> points.</summary>
    /// <param name="image">The image; it shall belong to the document the flow renders into.</param>
    /// <param name="width">The width in points.</param>
    /// <param name="height">The height in points.</param>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive and finite.</exception>
    public InlineImage(PdfImage image, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(image);
        Image = image;
        Width = ImageBlock.Positive(width, nameof(width));
        Height = ImageBlock.Positive(height, nameof(height));
    }

    /// <summary>The image.</summary>
    public PdfImage Image { get; }

    /// <summary>The width in points.</summary>
    public double Width { get; }

    /// <summary>The height in points.</summary>
    public double Height { get; }

    /// <summary>The alternate description of the image for tagged output (§14.9.3), or null.</summary>
    public string? AlternateText { get; init; }

    private protected override string DefaultStructureType => "Figure";
}
