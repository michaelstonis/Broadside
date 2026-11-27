namespace Broadside.Layout;

/// <summary>
/// Options for writing a layout tree as JSON (<see cref="LayoutTree.SaveJson"/>). Immutable: each <c>With*</c> member returns a
/// changed copy, so one instance is safe to share between threads.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4 and §14.8.5.4.3 (<c>BBox</c>).</remarks>
public sealed class LayoutJsonOptions
{
    /// <summary>Initializes a new instance of the <see cref="LayoutJsonOptions"/> class with the defaults: no bounds, not indented.</summary>
    public LayoutJsonOptions()
    {
    }

    private LayoutJsonOptions(LayoutJsonOptions other)
    {
        IncludeBounds = other.IncludeBounds;
        Indented = other.Indented;
    }

    /// <summary>
    /// Gets a value indicating whether each node carries <c>"bounds"</c>: one <c>[left, bottom, right, top]</c> array (or
    /// <see langword="null"/>) per page it lies on, in default user space. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.8.5.4.3; see <see cref="LayoutExtent.Bounds"/>.</remarks>
    public bool IncludeBounds { get; private init; }

    /// <summary>Gets a value indicating whether the JSON is indented. Default <see langword="false"/>.</summary>
    public bool Indented { get; private init; }

    /// <summary>Returns a copy that writes, or does not write, each node's bounds.</summary>
    /// <param name="include">Whether to write bounds.</param>
    /// <returns>The changed copy.</returns>
    /// <remarks>ISO 32000-2 §14.8.5.4.3.</remarks>
    public LayoutJsonOptions WithBounds(bool include) => new(this) { IncludeBounds = include };

    /// <summary>Returns a copy that indents, or does not indent, the JSON.</summary>
    /// <param name="indented">Whether to indent.</param>
    /// <returns>The changed copy.</returns>
    public LayoutJsonOptions WithIndented(bool indented) => new(this) { Indented = indented };
}
