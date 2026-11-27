namespace Broadside.Layout;

/// <summary>
/// Options for writing a layout tree as Markdown (<see cref="LayoutTree.SaveMarkdown"/>). Immutable: each <c>With*</c> member returns
/// a changed copy, so one instance is safe to share between threads.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4 (the standard structure types the Markdown blocks stand for) and §14.9.3 (alternate descriptions).</remarks>
public sealed class MarkdownSaveOptions
{
    /// <summary>Initializes a new instance of the <see cref="MarkdownSaveOptions"/> class with the defaults: images and emphasis written, no page breaks, pipe tables.</summary>
    public MarkdownSaveOptions()
    {
    }

    private MarkdownSaveOptions(MarkdownSaveOptions other)
    {
        IncludePageBreaks = other.IncludePageBreaks;
        IncludeImages = other.IncludeImages;
        InlineEmphasis = other.InlineEmphasis;
        Tables = other.Tables;
    }

    /// <summary>
    /// Gets a value indicating whether a comment <c>&lt;!-- page n --&gt;</c> (n from 1) is written before the first block of each page
    /// that has content. Default <see langword="false"/>.
    /// </summary>
    public bool IncludePageBreaks { get; private init; }

    /// <summary>
    /// Gets a value indicating whether a figure is written as a Markdown image whose text is its alternate description and whose
    /// destination is the name <c>page-n-figure-k</c>, k counting the figures of page n from 1; when <see langword="false"/>, a figure
    /// is its alternate description as an italic line, and nothing when it has none. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.4.8.5 and §14.9.3.</remarks>
    public bool IncludeImages { get; private init; } = true;

    /// <summary>
    /// Gets a value indicating whether bold and italic text is written as <c>**bold**</c> and <c>*italic*</c>, read from the fonts of
    /// each word's glyphs. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.8.2 (Table 121, <c>ForceBold</c> and <c>Italic</c>) and §9.8.1 (Table 120, <c>FontWeight</c>, <c>ItalicAngle</c>).</remarks>
    public bool InlineEmphasis { get; private init; } = true;

    /// <summary>Gets how a table with spans or no header row is written. Default <see cref="MarkdownTableStyle.Pipe"/>.</summary>
    /// <remarks>ISO 32000-2 §14.8.4.8.3 and §14.8.5.7.</remarks>
    public MarkdownTableStyle Tables { get; private init; }

    /// <summary>Returns a copy that writes, or does not write, a comment before the first block of each page.</summary>
    /// <param name="include">Whether to write page breaks.</param>
    /// <returns>The changed copy.</returns>
    public MarkdownSaveOptions WithPageBreaks(bool include) => new(this) { IncludePageBreaks = include };

    /// <summary>Returns a copy that writes figures as image references, or as their alternate descriptions in italics.</summary>
    /// <param name="include">Whether to write image references.</param>
    /// <returns>The changed copy.</returns>
    /// <remarks>ISO 32000-2 §14.9.3.</remarks>
    public MarkdownSaveOptions WithImages(bool include) => new(this) { IncludeImages = include };

    /// <summary>Returns a copy that writes, or does not write, bold and italic emphasis.</summary>
    /// <param name="include">Whether to write emphasis.</param>
    /// <returns>The changed copy.</returns>
    /// <remarks>ISO 32000-2 §9.8.</remarks>
    public MarkdownSaveOptions WithInlineEmphasis(bool include) => new(this) { InlineEmphasis = include };

    /// <summary>Returns a copy that writes tables with spans or no header row in <paramref name="style"/>.</summary>
    /// <param name="style">The table style.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> is not a defined value.</exception>
    /// <remarks>ISO 32000-2 §14.8.4.8.3.</remarks>
    public MarkdownSaveOptions WithTables(MarkdownTableStyle style)
    {
        if (style is not (MarkdownTableStyle.Pipe or MarkdownTableStyle.Html))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "Not a defined table style.");
        }

        return new(this) { Tables = style };
    }
}
