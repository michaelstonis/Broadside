namespace Broadside.Forms;

/// <summary>A field whose children are fields: a container for entries its descendants inherit.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.2 and §12.7.4.1, Table 226: "a non-terminal field does not logically have a type of its own"; an <c>FT</c> it
/// carries is for its descendants.
/// </remarks>
public sealed class PdfNonTerminalField : PdfField
{
    private readonly List<PdfField> _children = [];

    internal PdfNonTerminalField(FieldInfo info)
        : base(info, PdfFieldKind.NonTerminal)
    {
    }

    /// <summary>Gets the field's children, in <c>Kids</c> order.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.1, Table 226 (<c>Kids</c>). A snapshot of the field tree, taken when the tree was read.</remarks>
    public IReadOnlyList<PdfField> Children => _children;

    /// <summary>Adds a child while the field tree is built.</summary>
    internal void AddChild(PdfField child) => _children.Add(child);
}
