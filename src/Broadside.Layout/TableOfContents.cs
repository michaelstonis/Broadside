namespace Broadside.Layout.Flow;

/// <summary>
/// A table of contents: one entry per heading of the flow (a <see cref="Paragraph"/> tagged <c>H1</c> to <c>H6</c>, wherever it is, down to
/// <see cref="Depth"/>), in flow order, each the heading's text linked to it, a <see cref="Leader"/> and the number of the page it
/// is on, indented by level. The entries are laid out as paragraphs in <see cref="Style"/>.
/// </summary>
/// <remarks>
/// <para>
/// The page numbers are forward references: a flow with a table of contents is laid out again until they are stable, as for a
/// <see cref="PageReference"/>. Each entry's link goes to its heading's named destination when the heading has an
/// <see cref="FlowBlock.Id"/>, else to an explicit destination at the heading.
/// </para>
/// <para>
/// ISO 32000-2 §14.8.4.4 (<c>TOC</c> and <c>TOCI</c>, standard in the PDF 1.7 namespace only, so written there in either namespace),
/// §12.5.6.5 (link annotations) and §14.8.4.7.2 (<c>Link</c>): the block is a <c>TOC</c> element holding a <c>TOCI</c> per entry, whose
/// text is a <c>Link</c> with the annotation's object reference and whose page number is its content; the leader is an artifact.
/// </para>
/// </remarks>
public sealed class TableOfContents : FlowBlock
{
    /// <summary>Creates a table of contents with structure type <c>TOC</c>, listing headings down to <c>H3</c>.</summary>
    /// <param name="style">The style of the entries; its font shall show the headings' text, the page numbers and the leader.</param>
    /// <exception cref="ArgumentNullException"><paramref name="style"/> is null.</exception>
    public TableOfContents(ParagraphStyle style)
        : base("TOC")
    {
        ArgumentNullException.ThrowIfNull(style);
        Style = style;
        LevelIndent = style.FontSize * 1.5;
    }

    /// <summary>The style of the entries.</summary>
    public ParagraphStyle Style { get; }

    /// <summary>Gets the deepest heading level listed, 1 (<c>H1</c> only) to 6; 3 by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to 6.</exception>
    public int Depth
    {
        get;
        init => field = value is >= 1 and <= 6 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The depth is a heading level from 1 to 6.");
    } = 3;

    /// <summary>Gets how far each level's entries are indented from the one above, in points; 1.5 times the entries' font size by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double LevelIndent
    {
        get;
        init => field = ImageBlock.NotNegative(value, nameof(value));
    }

    /// <summary>Gets the text the leader between an entry's text and its page number repeats; <c>.</c> by default.</summary>
    /// <exception cref="ArgumentException">The value is null or empty.</exception>
    public string LeaderText
    {
        get;
        init => field = string.IsNullOrEmpty(value) ? throw new ArgumentException("The leader text shall not be empty.", nameof(value)) : value;
    } = ".";

    internal override void Validate(int index)
    {
        if (!Style.Font.CanEncode(LeaderText) || !Style.Font.CanEncode("0123456789?"))
        {
            throw new ArgumentException($"The font {Style.Font.Name} of table of contents {index} cannot encode its leader or the digits of a page number.");
        }
    }

    /// <summary>Checks that the entries' font shows the text of every heading listed (the headings are known once the flow is scanned).</summary>
    internal void ValidateEntries(FlowReferences references)
    {
        foreach ((Paragraph heading, int level) in references.Headings)
        {
            string title = FlowReferences.TitleOf(heading);
            if (level <= Depth && !Style.Font.CanEncode(title))
            {
                throw new ArgumentException($"The font {Style.Font.Name} of a table of contents cannot encode the heading '{title}' it lists.");
            }
        }
    }

    internal override ContentWidths MeasureWidths(FlowServices services) => new(0, 0);

    internal override void LayOut(FlowContext context)
    {
        context.BeginBlock(this);
        if (context.References is { } references)
        {
            TextStyle text = Style.TextStyle;
            foreach ((Paragraph heading, int level) in references.Headings)
            {
                if (level > Depth)
                {
                    continue;
                }

                var entry = new Paragraph(
                    [new TextRun(FlowReferences.TitleOf(heading), text) { Target = heading }, new Leader(LeaderText, text), new PageReference(heading, text)],
                    Style)
                {
                    StructureType = "TOCI",
                };
                double indent = LevelIndent * (level - 1);
                context.PushIndent(indent);
                entry.LayOut(context);
                context.PopIndent();
            }
        }

        context.EndBlock();
    }
}
