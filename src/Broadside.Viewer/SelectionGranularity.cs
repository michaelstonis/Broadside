namespace Broadside.Viewer;

/// <summary>The unit a selection gesture selects and extends by.</summary>
/// <remarks>Spec #33 viewer capability 4: click and drag, double-click (word), triple-click (line).</remarks>
public enum SelectionGranularity
{
    /// <summary>Glyph by glyph: a press and drag.</summary>
    Character,

    /// <summary>Whole words: a double-click, and a drag after it.</summary>
    Word,

    /// <summary>Whole lines: a triple-click, and a drag after it.</summary>
    Line,

    /// <summary>Whole paragraphs: lines up to a wider gap or a change of direction.</summary>
    Paragraph,
}
