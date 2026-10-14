using Broadside.Text;

namespace Broadside.Layout;

/// <summary>A page a layout tree was built from, with its text in reading order.</summary>
/// <remarks>ISO 32000-2 §14.8.2.5 and §9.10; the text is <see cref="PdfPage.ExtractText()"/>'s for the page.</remarks>
public sealed class LayoutPage
{
    internal LayoutPage(PdfPage page, int pageIndex, TextPage text)
    {
        Page = page;
        PageIndex = pageIndex;
        Text = text;
    }

    /// <summary>Gets the page.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the page's index in the document.</summary>
    public int PageIndex { get; }

    /// <summary>Gets the page's text: its glyphs in logical content order when it is tagged, which the extents' ranges index.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.5.1.</remarks>
    public TextPage Text { get; }

    /// <inheritdoc/>
    public override string ToString() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"page {PageIndex + 1}");
}
