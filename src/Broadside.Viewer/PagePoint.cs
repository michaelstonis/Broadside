namespace Broadside.Viewer;

/// <summary>A point on one page in its default user space (ISO 32000-2 §8.3.2.3): where a viewport pixel falls on the page.</summary>
/// <param name="PageIndex">The zero-based page.</param>
/// <param name="X">The x coordinate in default user space units.</param>
/// <param name="Y">The y coordinate in default user space units (y up).</param>
/// <remarks>ISO 32000-2 §8.3.2.3. Spec #33 story 54 (viewer capability 14).</remarks>
public readonly record struct PagePoint(int PageIndex, double X, double Y);
