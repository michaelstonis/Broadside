using Broadside.Annotations;
using Broadside.Structure;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>
/// One node of a layout tree: a section, paragraph, heading, list part, table part, figure, caption or inline run, with its children
/// in reading order and where it lies on each page. Headings, tables and table cells are the derived <see cref="LayoutHeading"/>,
/// <see cref="LayoutTable"/> and <see cref="LayoutTableCell"/>.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4 and ISO/TS 32005; the node kinds and the structure types they come from are listed on
/// <see cref="LayoutNodeKind"/>. A node holds the content of its descendants: its <see cref="Extents"/> and <see cref="Text"/> cover
/// theirs. Immutable once its tree is built.
/// </remarks>
public class LayoutNode
{
    private static readonly LayoutExtent[] NoExtents = [];
    private readonly List<LayoutNode> _children = [];
    private List<LayoutExtent>? _extents;
    private List<PdfAnnotation>? _annotations;
    private string? _text;

    private protected LayoutNode(LayoutNodeKind kind, PdfStructureElement? element)
    {
        Kind = kind;
        StructureElement = element;
    }

    /// <summary>Gets what the node is.</summary>
    public LayoutNodeKind Kind { get; }

    /// <summary>Gets the node above, or <see langword="null"/> for the root.</summary>
    public LayoutNode? Parent { get; private set; }

    /// <summary>Gets the nodes below, in reading order.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.5.1: the order of the structure hierarchy.</remarks>
    public IReadOnlyList<LayoutNode> Children => _children;

    /// <summary>Gets where the node lies on each page it shows content on, in page order; empty when it shows nothing on the tree's pages.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.5.1 and §14.8.5.4.3; see <see cref="LayoutExtent"/>.</remarks>
    public IReadOnlyList<LayoutExtent> Extents => (IReadOnlyList<LayoutExtent>?)_extents ?? NoExtents;

    /// <summary>Gets the structure element the node was built from, or <see langword="null"/> when it was not built from one.</summary>
    /// <remarks>ISO 32000-2 §14.7.2.</remarks>
    public PdfStructureElement? StructureElement { get; }

    /// <summary>Gets the natural language of the node's content: its element's <c>Lang</c>, else the nearest ancestor's, else the document's; <see langword="null"/> when none is given.</summary>
    /// <remarks>ISO 32000-2 §14.9.2 (Table 355 <c>Lang</c>, Table 29 <c>Lang</c>).</remarks>
    public string? Language { get; internal set; }

    /// <summary>Gets the alternate description of the node's content (<c>Alt</c>), such as a figure's, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §14.9.3; §14.8.4.8.5 (a figure should have one).</remarks>
    public string? AlternateDescription { get; internal set; }

    /// <summary>Gets the annotations the node's content includes on the tree's pages: a link's link annotation, a form field's widgets.</summary>
    /// <remarks>ISO 32000-2 §14.7.5.3 (object references) and Table 368 (<c>Link</c>, <c>Annot</c>, <c>Form</c>).</remarks>
    public IReadOnlyList<PdfAnnotation> Annotations => (IReadOnlyList<PdfAnnotation>?)_annotations ?? [];

    /// <summary>
    /// Gets the node's text: on each page, its glyphs' text with words separated by one space and lines by one line feed
    /// (U+000A), as <see cref="TextPage.PlainText"/> joins them; the pages' texts joined by a line feed. Empty when it shows no text.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10, §14.8.2.6.2 and §14.9.4 (replacement text is applied, as <see cref="PdfPage.ExtractText()"/> does).</remarks>
    public string Text
    {
        get
        {
            if (Volatile.Read(ref _text) is { } text)
            {
                return text;
            }

            text = AssembleText();
            return Interlocked.CompareExchange(ref _text, text, null) ?? text;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Kind.ToString();

    /// <summary>Creates a node with no data beyond its kind.</summary>
    internal static LayoutNode Create(LayoutNodeKind kind, PdfStructureElement? element) => new(kind, element);

    internal void Add(LayoutNode child)
    {
        child.Parent = this;
        _children.Add(child);
    }

    /// <summary>Keeps only the children <paramref name="keep"/> accepts.</summary>
    internal void Prune(Predicate<LayoutNode> keep) => _children.RemoveAll(child => !keep(child));

    internal void AddExtent(LayoutExtent extent) => (_extents ??= []).Add(extent);

    internal void AddAnnotation(PdfAnnotation annotation)
    {
        _annotations ??= [];
        if (!_annotations.Contains(annotation))
        {
            _annotations.Add(annotation);
        }
    }

    private string AssembleText()
    {
        if (_extents is null)
        {
            return string.Empty;
        }

        var text = new System.Text.StringBuilder();
        TextLayout layout = TextLayout.Rent();
        try
        {
            foreach (LayoutExtent extent in _extents)
            {
                if (extent.GlyphCount == 0)
                {
                    continue;
                }

                TextPage page = extent.Page.Text;
                layout.Build(page.GlyphSpan.Slice(extent.GlyphStart, extent.GlyphCount), page.Text);
                if (layout.PlainText.IsEmpty)
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(layout.PlainText);
            }
        }
        finally
        {
            TextLayout.Return(layout);
        }

        return text.ToString();
    }
}
