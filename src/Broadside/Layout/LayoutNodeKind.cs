namespace Broadside.Layout;

/// <summary>What a node of a layout tree is.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.4 (standard structure types) and ISO/TS 32005: a structure element becomes the kind its standard structure type
/// (after role mapping, §14.8.6.2) stands for, in either standard namespace:
/// </para>
/// <list type="bullet">
/// <item><see cref="Document"/>: <c>Document</c>, <c>DocumentFragment</c> (Table 364).</item>
/// <item><see cref="Section"/>: <c>Part</c>, <c>Sect</c>, <c>Div</c>, <c>Aside</c> (Table 365); <c>Art</c>, <c>BlockQuote</c>, <c>Index</c> (PDF 1.7).</item>
/// <item><see cref="Paragraph"/>: <c>P</c> (Table 366).</item>
/// <item><see cref="Heading"/>: <c>Hn</c>, <c>H</c>, <c>Title</c> (Table 366).</item>
/// <item><see cref="Note"/>: <c>FENote</c> (Table 366); <c>Note</c> (PDF 1.7).</item>
/// <item><see cref="Label"/>: <c>Lbl</c> (Table 368).</item>
/// <item><see cref="Link"/>: <c>Link</c> (Table 368).</item>
/// <item><see cref="Span"/>: <c>Span</c>, <c>Em</c>, <c>Strong</c>, <c>Annot</c>, <c>Form</c> (Table 368), <c>Sub</c> (Table 367), <c>Ruby</c>, <c>RB</c>,
/// <c>RT</c>, <c>RP</c>, <c>Warichu</c>, <c>WT</c>, <c>WP</c> (Table 369); <c>Quote</c>, <c>Code</c>, <c>Reference</c>, <c>BibEntry</c> (PDF 1.7).</item>
/// <item><see cref="List"/>, <see cref="ListItem"/>, <see cref="ListBody"/>: <c>L</c>, <c>LI</c>, <c>LBody</c> (Table 370); <c>TOC</c> and <c>TOCI</c> (PDF 1.7) as a list and its items.</item>
/// <item><see cref="Table"/>, <see cref="TableRow"/>, <see cref="TableCell"/>: <c>Table</c>, <c>TR</c>, <c>TH</c> and <c>TD</c> (Table 371).</item>
/// <item><see cref="Caption"/>: <c>Caption</c> (Table 372).</item>
/// <item><see cref="Figure"/>: <c>Figure</c> (Table 373), <c>Formula</c> (Table 374).</item>
/// </list>
/// <para>
/// <c>NonStruct</c> and <c>Private</c>, the row groups <c>THead</c>, <c>TBody</c> and <c>TFoot</c>, and elements that resolve to no
/// standard type make no node: their children and content belong to the node above. <c>Artifact</c> elements and everything under
/// them are left out (§14.8.4.8.7).
/// </para>
/// </remarks>
public enum LayoutNodeKind
{
    /// <summary>A whole logical document or a fragment of one; the root of every tree.</summary>
    Document = 0,

    /// <summary>A grouping of blocks: a part, section, division or aside.</summary>
    Section,

    /// <summary>A paragraph or other low-level division of content.</summary>
    Paragraph,

    /// <summary>A heading; <see cref="LayoutHeading.Level"/> gives its level.</summary>
    Heading,

    /// <summary>A list.</summary>
    List,

    /// <summary>An item of a list.</summary>
    ListItem,

    /// <summary>A label: a list item's bullet or number, a heading's number, a footnote's mark.</summary>
    Label,

    /// <summary>The body of a list item.</summary>
    ListBody,

    /// <summary>A table; <see cref="LayoutTable"/> gives its size.</summary>
    Table,

    /// <summary>A row of a table.</summary>
    TableRow,

    /// <summary>A cell of a table; <see cref="LayoutTableCell"/> gives its position, spans and headers.</summary>
    TableCell,

    /// <summary>A figure or formula: graphical content, usually described by <see cref="LayoutNode.AlternateDescription"/>.</summary>
    Figure,

    /// <summary>The caption of the node it is a child of.</summary>
    Caption,

    /// <summary>An inline run of content with no particular characteristics, or inline content with a meaning the tree does not model.</summary>
    Span,

    /// <summary>Content associated with a link annotation (<see cref="LayoutNode.Annotations"/>).</summary>
    Link,

    /// <summary>A footnote or endnote.</summary>
    Note,
}
