namespace Broadside.Viewer;

/// <summary>
/// The role of an <see cref="AccessibleNode"/>: what a shell maps to an ARIA role, a UI Automation control type or an
/// NSAccessibility role.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.8.4 (standard structure types), Spec #33 viewer capability 8. Content roles come from the
/// <see cref="Broadside.Layout.LayoutNodeKind"/> of the page's layout tree; control roles come from an
/// <see cref="IAccessibleElementProvider"/>.
/// </remarks>
public enum AccessibleRole
{
    /// <summary>The document: the root, whose children are its pages.</summary>
    Document,

    /// <summary>A page.</summary>
    Page,

    /// <summary>A section (Sect, Part, Art, Chapter and other groupings): a landmark.</summary>
    Section,

    /// <summary>A grouping with no role of its own, such as a list item's body.</summary>
    Group,

    /// <summary>A paragraph.</summary>
    Paragraph,

    /// <summary>A heading; <see cref="AccessibleNode.Level"/> gives its level.</summary>
    Heading,

    /// <summary>A list.</summary>
    List,

    /// <summary>An item of a list.</summary>
    ListItem,

    /// <summary>A list item's bullet or number, or another label.</summary>
    ListMarker,

    /// <summary>A table; <see cref="AccessibleNode.RowCount"/> and <see cref="AccessibleNode.ColumnCount"/> give its size.</summary>
    Table,

    /// <summary>A row of a table.</summary>
    Row,

    /// <summary>A header cell for the cells below it.</summary>
    ColumnHeader,

    /// <summary>A header cell for the cells beside it.</summary>
    RowHeader,

    /// <summary>A data cell; <see cref="AccessibleNode.Headers"/> gives its header cells.</summary>
    Cell,

    /// <summary>A figure or formula, named by its alternate description.</summary>
    Figure,

    /// <summary>The caption of its parent.</summary>
    Caption,

    /// <summary>An inline run of text.</summary>
    Text,

    /// <summary>A link.</summary>
    Link,

    /// <summary>A footnote or endnote.</summary>
    Note,

    /// <summary>A push button.</summary>
    Button,

    /// <summary>A check box.</summary>
    CheckBox,

    /// <summary>A radio button.</summary>
    RadioButton,

    /// <summary>A text field.</summary>
    TextBox,

    /// <summary>A combo box.</summary>
    ComboBox,

    /// <summary>A list box.</summary>
    ListBox,

    /// <summary>An annotation other than a link or a widget, such as a comment.</summary>
    Annotation,
}
