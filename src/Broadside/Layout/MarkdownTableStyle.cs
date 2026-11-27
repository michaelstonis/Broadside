namespace Broadside.Layout;

/// <summary>How the Markdown writer writes a table that a pipe table cannot hold: one with cells that span rows or columns, or with no header row.</summary>
/// <remarks>ISO 32000-2 §14.8.4.8.3 (Table 371) and §14.8.5.7 (Table 384, <c>RowSpan</c> and <c>ColSpan</c>).</remarks>
public enum MarkdownTableStyle
{
    /// <summary>
    /// Every table is a pipe table. A spanned cell's text is repeated in each grid position it covers, a table with no header row
    /// takes its first row as the header, and a comment before the table says so.
    /// </summary>
    Pipe = 0,

    /// <summary>A table with spans or with no header row is an HTML <c>&lt;table&gt;</c> with <c>rowspan</c> and <c>colspan</c>; other tables are pipe tables.</summary>
    Html = 1,
}
