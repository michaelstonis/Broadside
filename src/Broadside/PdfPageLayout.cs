namespace Broadside;

/// <summary>How a viewer lays the pages out when the document opens: the catalog's <c>PageLayout</c> entry.</summary>
/// <remarks>ISO 32000-2 §7.7.2, Table 29. The default is <see cref="SinglePage"/>.</remarks>
public enum PdfPageLayout
{
    /// <summary>One page at a time (<c>SinglePage</c>, the default).</summary>
    SinglePage,

    /// <summary>The pages in one column (<c>OneColumn</c>).</summary>
    OneColumn,

    /// <summary>The pages in two columns, odd-numbered pages on the left (<c>TwoColumnLeft</c>).</summary>
    TwoColumnLeft,

    /// <summary>The pages in two columns, odd-numbered pages on the right (<c>TwoColumnRight</c>).</summary>
    TwoColumnRight,

    /// <summary>Two pages at a time, odd-numbered pages on the left (<c>TwoPageLeft</c>, PDF 1.5).</summary>
    TwoPageLeft,

    /// <summary>Two pages at a time, odd-numbered pages on the right (<c>TwoPageRight</c>, PDF 1.5).</summary>
    TwoPageRight,
}
