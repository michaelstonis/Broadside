namespace Broadside.Graphics;

/// <summary>The rule that decides which points lie inside a path, for filling and for clipping.</summary>
/// <remarks>ISO 32000-2 §8.5.3.3.2 (non-zero winding number) and §8.5.3.3.3 (even-odd).</remarks>
public enum FillRule
{
    /// <summary>The non-zero winding number rule: <c>f</c>, <c>B</c>, <c>b</c>, <c>W</c>.</summary>
    NonZero,

    /// <summary>The even-odd rule: <c>f*</c>, <c>B*</c>, <c>b*</c>, <c>W*</c>.</summary>
    EvenOdd,
}
