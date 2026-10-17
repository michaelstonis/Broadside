namespace Broadside.Rendering;

/// <summary>Where a <see cref="DisplayGroup"/> comes from.</summary>
/// <remarks>ISO 32000-2 §11.4.</remarks>
public enum DisplayGroupKind
{
    /// <summary>A transparency group XObject painted by <c>Do</c> (§11.6.6, Table 145).</summary>
    Transparency,

    /// <summary>The page group of a page whose dictionary has a transparency <c>Group</c> entry (§11.4.7).</summary>
    Page,

    /// <summary>
    /// The implicit non-isolated knockout group around a combined fill and stroke (<c>B</c>, <c>B*</c>, <c>b</c>, <c>b*</c>, and a
    /// glyph in text rendering mode 2 or 6) painted with an alpha below 1 or a blend mode other than Normal (§11.7.4.4).
    /// </summary>
    FillAndStroke,
}
