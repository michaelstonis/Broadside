namespace Broadside.Viewer;

/// <summary>How a <see cref="PageLayout"/> arranges the pages into spreads and the spreads into a strip.</summary>
/// <remarks>ISO 32000-2 §7.7.2, Table 29 (<c>PageLayout</c>). Spec #33 story 54 (viewer capability 14).</remarks>
public enum PageLayoutMode
{
    /// <summary>One page per spread, one spread at a time (<c>SinglePage</c>).</summary>
    SinglePage,

    /// <summary>One page per spread, the spreads in one scrolling column (<c>OneColumn</c>).</summary>
    Continuous,

    /// <summary>Two facing pages per spread, one spread at a time (<c>TwoPageLeft</c>, or <c>TwoPageRight</c> with a cover).</summary>
    TwoUp,

    /// <summary>Two facing pages per spread, the spreads in one scrolling column (<c>TwoColumnLeft</c>, or <c>TwoColumnRight</c> with a cover).</summary>
    TwoUpContinuous,

    /// <summary>One page per spread, the spreads in one scrolling row.</summary>
    Horizontal,
}
