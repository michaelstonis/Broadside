using Broadside.Graphics;

namespace Broadside.Viewer;

/// <summary>
/// One item of a <see cref="DocumentOutline"/>: its title, how it looks, whether it starts open, its children, and where activating it
/// leads: a place in the document (<see cref="Target"/>), an external address (<see cref="Uri"/>) or neither. A snapshot: it does
/// not change when the document does.
/// </summary>
/// <remarks>ISO 32000-2 §12.3.3, Tables 151 and 152. Spec #33 viewer capability 10.</remarks>
public sealed class OutlineEntry
{
    internal OutlineEntry(int id, OutlineEntry? parent, string title, int level)
    {
        Id = id;
        Parent = parent;
        Title = title;
        Level = level;
    }

    /// <summary>Gets the entry's position in <see cref="DocumentOutline.Entries"/> (depth first, as a tree view lists them).</summary>
    public int Id { get; }

    /// <summary>Gets the entry this one is a child of, or <see langword="null"/> for a top-level entry.</summary>
    public OutlineEntry? Parent { get; }

    /// <summary>Gets the displayed text.</summary>
    public string Title { get; }

    /// <summary>Gets the depth: 0 for a top-level entry.</summary>
    public int Level { get; }

    /// <summary>Gets the children, in outline order.</summary>
    public IReadOnlyList<OutlineEntry> Children { get; internal set; } = [];

    /// <summary>Gets a value indicating whether the file asks for the entry to start open (its children shown).</summary>
    public bool IsOpen { get; internal set; }

    /// <summary>Gets a value indicating whether the title is shown italic.</summary>
    public bool IsItalic { get; internal set; }

    /// <summary>Gets a value indicating whether the title is shown bold.</summary>
    public bool IsBold { get; internal set; }

    /// <summary>Gets the title's color (black by default).</summary>
    public PdfRgbColor Color { get; internal set; }

    /// <summary>
    /// Gets where activating the entry goes in this document, or <see langword="null"/> when it goes nowhere in it (an external address,
    /// another document, an action a viewer does not perform, a destination that is not valid or has no page here).
    /// </summary>
    public ViewTarget? Target { get; internal set; }

    /// <summary>Gets the external address of a URI action (resolved against the document's base URI), or <see langword="null"/>. A viewer hands it to its host; it never opens it.</summary>
    public System.Uri? Uri { get; internal set; }

    /// <summary>Gets the kind of the entry's action, or <see langword="null"/> when it has a destination or nothing.</summary>
    public PdfActionKind? ActionKind { get; internal set; }

    /// <summary>Gets the label of <see cref="Target"/>'s page (ISO 32000-2 §12.4.2), or <see langword="null"/> when there is no target.</summary>
    public string? PageLabel { get; internal set; }

    /// <summary>Gets a value indicating whether activating the entry goes somewhere: it has a <see cref="Target"/> or a <see cref="Uri"/>.</summary>
    public bool IsNavigable => Target is not null || Uri is not null;

    /// <inheritdoc/>
    public override string ToString() => Title;
}
