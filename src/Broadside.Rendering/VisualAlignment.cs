namespace Broadside.Rendering;

/// <summary>Where <see cref="VisualComparison"/> places each page on a canvas the size of the larger one.</summary>
/// <remarks>ISO 32000-2 §14.11.2 (crop box) and §7.7.3.3 Table 31 (<c>Rotate</c>): each page as it is shown.</remarks>
public enum VisualAlignment
{
    /// <summary>Both pages at the canvas's top-left corner.</summary>
    TopLeft,

    /// <summary>Both pages centered on the canvas.</summary>
    Center,
}
