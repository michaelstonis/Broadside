using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>The emphasis of a run of inline text, read from its glyphs' fonts.</summary>
[Flags]
internal enum InlineEmphasis
{
    None = 0,
    Bold = 1,
    Italic = 2,
}

/// <summary>One item of a node's content in reading order: a child node, or a range of the node's own glyphs that no child holds.</summary>
/// <param name="Child">The child, or <see langword="null"/> for a range of glyphs.</param>
/// <param name="Page">The page of the range.</param>
/// <param name="GlyphStart">The first glyph of the range, an index into the page's glyphs.</param>
/// <param name="GlyphCount">The number of glyphs of the range.</param>
internal readonly record struct LayoutContentItem(LayoutNode? Child, LayoutPage? Page, int GlyphStart, int GlyphCount);

/// <summary>Receives the inline content of a run of layout nodes, word by word, from <see cref="LayoutInlines"/>.</summary>
internal interface ILayoutInlineSink
{
    /// <summary>Receives a word, or the part of a word that one inline node holds.</summary>
    /// <param name="text">The text, from the page's text.</param>
    /// <param name="emphasis">The word's emphasis.</param>
    /// <param name="link">The innermost link node the word is in, or <see langword="null"/>.</param>
    /// <param name="uri">The URI the link goes to, or <see langword="null"/> for a link to a destination in the document or none.</param>
    /// <param name="spaceBefore">Whether a word break separates the text from the text before it.</param>
    /// <param name="page">The page the text is on.</param>
    void Text(ReadOnlySpan<char> text, InlineEmphasis emphasis, LayoutNode? link, string? uri, bool spaceBefore, LayoutPage page);

    /// <summary>Receives a note that stands inline, where its reference goes.</summary>
    /// <param name="note">The note.</param>
    void NoteReference(LayoutNode note);

    /// <summary>Receives a node inline content cannot hold, such as a figure in a table cell; the sink writes what it can of it.</summary>
    /// <param name="node">The node.</param>
    void Embedded(LayoutNode node);
}

/// <summary>
/// The inline content of layout nodes, shared by the serializers (issue #629): a node's content split into its children and the glyph
/// ranges between them, and the words of those ranges with the emphasis of their fonts and the link they belong to.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.2.5.1 (logical content order), §14.8.4.7 (inline-level structure elements: <c>Span</c>, <c>Link</c>), §9.8
/// (font descriptors) and §12.6.4.8 (URI actions). Reads only the tree: the glyphs its pages already hold and their fonts.
/// Not thread-safe: use one instance per serialization.
/// </remarks>
internal sealed class LayoutInlines
{
    private readonly Dictionary<PdfFont, InlineEmphasis> _fonts = new(ReferenceEqualityComparer.Instance);
    private readonly bool _emphasis;
    private LayoutPage? _lastPage;
    private int _lastGlyph = -1;

    /// <summary>Initializes a new instance of the <see cref="LayoutInlines"/> class.</summary>
    /// <param name="emphasis">Whether to read emphasis from the fonts; when <see langword="false"/> every word is <see cref="InlineEmphasis.None"/>.</param>
    public LayoutInlines(bool emphasis)
    {
        _emphasis = emphasis;
    }

    /// <summary>Gets whether a node is inline: a span, a link, or a note inside a block of text.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> for inline content.</returns>
    public static bool IsInline(LayoutNode node) => node.Kind switch
    {
        LayoutNodeKind.Span or LayoutNodeKind.Link => true,
        LayoutNodeKind.Note => node.Parent?.Kind is LayoutNodeKind.Paragraph or LayoutNodeKind.Heading or LayoutNodeKind.Span
            or LayoutNodeKind.Link or LayoutNodeKind.Caption or LayoutNodeKind.Label or LayoutNodeKind.TableCell,
        _ => false,
    };

    /// <summary>
    /// Splits a node's content into its children and the ranges of its own glyphs before, between and after them, in reading order.
    /// A child spanning pages takes the parent's glyphs it covers on each.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The items.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.5.1: a node holds the content of its descendants, so each child's ranges lie in its parent's.</remarks>
    public static List<LayoutContentItem> Items(LayoutNode node)
    {
        var items = new List<LayoutContentItem>(node.Children.Count + 1);
        IReadOnlyList<LayoutExtent> extents = node.Extents;
        int extent = 0;
        int cursor = extents.Count > 0 ? extents[0].GlyphStart : 0;
        foreach (LayoutNode child in node.Children)
        {
            if (child.Extents.Count == 0)
            {
                items.Add(new LayoutContentItem(child, null, 0, 0));
                continue;
            }

            LayoutExtent first = child.Extents[0];
            Advance(first.Page, first.GlyphStart, emit: true);
            items.Add(new LayoutContentItem(child, null, 0, 0));
            LayoutExtent last = child.Extents[^1];
            Advance(last.Page, last.GlyphStart + last.GlyphCount, emit: false);
        }

        while (extent < extents.Count)
        {
            LayoutExtent current = extents[extent];
            Emit(current.Page, cursor, current.GlyphStart + current.GlyphCount);
            extent++;
            cursor = extent < extents.Count ? extents[extent].GlyphStart : 0;
        }

        return items;

        // Moves the cursor to a glyph of a page, adding the parent's glyphs it passes when emit is set.
        void Advance(LayoutPage page, int glyph, bool emit)
        {
            int target = extent;
            while (target < extents.Count && !ReferenceEquals(extents[target].Page, page))
            {
                target++;
            }

            if (target == extents.Count)
            {
                return;
            }

            while (extent < target)
            {
                LayoutExtent current = extents[extent];
                if (emit)
                {
                    Emit(current.Page, cursor, current.GlyphStart + current.GlyphCount);
                }

                extent++;
                cursor = extents[extent].GlyphStart;
            }

            if (emit)
            {
                Emit(page, cursor, glyph);
            }

            cursor = Math.Max(cursor, glyph);
        }

        void Emit(LayoutPage page, int start, int end)
        {
            if (end > start)
            {
                items.Add(new LayoutContentItem(null, page, start, end - start));
            }
        }
    }

    /// <summary>Starts a new run of inline content: the first word of the run has no space before it.</summary>
    public void Reset()
    {
        _lastPage = null;
        _lastGlyph = -1;
    }

    /// <summary>Walks the inline content of a node: its own glyphs and its inline children, every descendant flattened into words.</summary>
    /// <param name="node">The node.</param>
    /// <param name="sink">What receives the words.</param>
    /// <param name="bold">Whether every word is bold, as the text of a heading in a table cell is written.</param>
    public void Walk(LayoutNode node, ILayoutInlineSink sink, bool bold = false) => Walk(Items(node), 0, -1, sink, null, null, bold);

    /// <summary>Walks items <paramref name="start"/> to <paramref name="end"/> (exclusive; -1 for the rest) of a node's content as inline content.</summary>
    /// <param name="items">The items, from <see cref="Items"/>.</param>
    /// <param name="start">The first item.</param>
    /// <param name="end">The item after the last, or -1.</param>
    /// <param name="sink">What receives the words.</param>
    /// <param name="link">The link the items are in.</param>
    /// <param name="uri">The link's URI.</param>
    /// <param name="bold">Whether every word is bold.</param>
    public void Walk(List<LayoutContentItem> items, int start, int end, ILayoutInlineSink sink, LayoutNode? link = null, string? uri = null, bool bold = false)
    {
        if (end < 0)
        {
            end = items.Count;
        }

        for (int index = start; index < end; index++)
        {
            LayoutContentItem item = items[index];
            if (item.Child is not { } child)
            {
                Words(item.Page!, item.GlyphStart, item.GlyphCount, sink, link, uri, bold);
                continue;
            }

            switch (child.Kind)
            {
                case LayoutNodeKind.Note:
                    sink.NoteReference(child);
                    if (child.Extents.Count > 0)
                    {
                        LayoutExtent last = child.Extents[^1];
                        _lastPage = last.Page;
                        _lastGlyph = last.GlyphStart + last.GlyphCount - 1;
                    }

                    break;
                case LayoutNodeKind.Link:
                    string? target = UriOf(child);
                    Walk(Items(child), 0, -1, sink, child, target ?? uri, bold);
                    break;
                case LayoutNodeKind.Figure:
                    sink.Embedded(child);
                    break;
                default:
                    Walk(Items(child), 0, -1, sink, link, uri, bold || child.Kind == LayoutNodeKind.Heading);
                    break;
            }
        }
    }

    /// <summary>Gets the URI a link node goes to: the URI action of its first link annotation that has one.</summary>
    /// <param name="link">The link node.</param>
    /// <returns>The URI, or <see langword="null"/> when the link goes to a destination in the document or nowhere.</returns>
    /// <remarks>ISO 32000-2 §12.5.6.5 (link annotations) and §12.6.4.8 (URI actions).</remarks>
    public static string? UriOf(LayoutNode link)
    {
        foreach (PdfAnnotation annotation in link.Annotations)
        {
            if (annotation is PdfLinkAnnotation { Action: PdfUriAction { Uri: { Length: > 0 } uri } })
            {
                return uri;
            }
        }

        return null;
    }

    /// <summary>Gets the emphasis a font shows its text with.</summary>
    /// <param name="font">The font.</param>
    /// <returns>Bold when its descriptor forces bold or gives a weight of 600 or more, or its name says so; italic when the descriptor
    /// has the italic flag or a non-zero italic angle, or its name says so.</returns>
    /// <remarks>ISO 32000-2 §9.8.1 (Table 120, <c>FontWeight</c>, <c>ItalicAngle</c>) and §9.8.2 (Table 121, <c>Italic</c>, <c>ForceBold</c>).</remarks>
    public static InlineEmphasis EmphasisOf(PdfFont font)
    {
        InlineEmphasis emphasis = InlineEmphasis.None;
        if (font.Descriptor is { } descriptor)
        {
            PdfFontFlags flags = descriptor.Flags;
            if ((flags & PdfFontFlags.ForceBold) != 0 || descriptor.FontWeight >= 600)
            {
                emphasis |= InlineEmphasis.Bold;
            }

            if ((flags & PdfFontFlags.Italic) != 0 || Math.Abs(descriptor.ItalicAngle) >= 1)
            {
                emphasis |= InlineEmphasis.Italic;
            }
        }

        // Standard 14 and unembedded fonts often carry no descriptor flags for weight; their names do (Helvetica-Bold, Arial,BoldItalic).
        if (font.BaseFont is { } name)
        {
            ReadOnlySpan<char> family = name.Length > 7 && name[6] == '+' ? name.AsSpan(7) : name.AsSpan();
            if (family.Contains("Bold", StringComparison.OrdinalIgnoreCase) || family.Contains("Black", StringComparison.OrdinalIgnoreCase)
                || family.Contains("Heavy", StringComparison.OrdinalIgnoreCase) || family.Contains("Semibold", StringComparison.OrdinalIgnoreCase)
                || family.Contains("Demi", StringComparison.OrdinalIgnoreCase))
            {
                emphasis |= InlineEmphasis.Bold;
            }

            if (family.Contains("Italic", StringComparison.OrdinalIgnoreCase) || family.Contains("Oblique", StringComparison.OrdinalIgnoreCase))
            {
                emphasis |= InlineEmphasis.Italic;
            }
        }

        return emphasis;
    }

    private void Words(LayoutPage page, int start, int count, ILayoutInlineSink sink, LayoutNode? link, string? uri, bool bold)
    {
        TextPage text = page.Text;
        ReadOnlySpan<TextGlyph> glyphs = text.GlyphSpan.Slice(start, count);
        TextLayout layout = TextLayout.Rent();
        try
        {
            layout.Build(glyphs, text.Text);
            foreach (TextWord word in layout.Words)
            {
                ref readonly TextGlyph first = ref glyphs[word.GlyphStart];
                if ((first.Flags & TextGlyphFlags.Artifact) != 0)
                {
                    continue;
                }

                int glyph = start + word.GlyphStart;
                bool space = _lastPage is not null && (!ReferenceEquals(_lastPage, page) || glyph != _lastGlyph + 1
                    || TextLayout.Separate(text.GlyphSpan[_lastGlyph], first, text.GlyphSpan[_lastGlyph]) != TextLayout.Break.None);
                InlineEmphasis emphasis = Emphasis(glyphs.Slice(word.GlyphStart, word.GlyphCount)) | (bold ? InlineEmphasis.Bold : InlineEmphasis.None);
                if (!_emphasis)
                {
                    emphasis = InlineEmphasis.None;
                }

                sink.Text(text.Text.AsSpan(word.TextStart, word.TextLength), emphasis, link, uri, space, page);
                _lastPage = page;
                _lastGlyph = glyph + word.GlyphCount - 1;
            }
        }
        finally
        {
            TextLayout.Return(layout);
        }
    }

    // A word is bold or italic when most of its glyphs are, so a bold word's trailing comma in the regular face does not split it.
    private InlineEmphasis Emphasis(ReadOnlySpan<TextGlyph> glyphs)
    {
        if (!_emphasis)
        {
            return InlineEmphasis.None;
        }

        int bold = 0;
        int italic = 0;
        foreach (ref readonly TextGlyph glyph in glyphs)
        {
            if (!_fonts.TryGetValue(glyph.Font, out InlineEmphasis emphasis))
            {
                emphasis = EmphasisOf(glyph.Font);
                _fonts.Add(glyph.Font, emphasis);
            }

            bold += (emphasis & InlineEmphasis.Bold) != 0 ? 1 : 0;
            italic += (emphasis & InlineEmphasis.Italic) != 0 ? 1 : 0;
        }

        return (bold * 2 > glyphs.Length ? InlineEmphasis.Bold : InlineEmphasis.None) | (italic * 2 > glyphs.Length ? InlineEmphasis.Italic : InlineEmphasis.None);
    }
}
