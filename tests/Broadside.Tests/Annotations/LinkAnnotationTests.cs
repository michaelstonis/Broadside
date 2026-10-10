using Broadside.Annotations;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>Link annotations (ISO 32000-2 §12.5.6.5, Table 176) and the common entries they share (§12.5.2, Table 166).</summary>
public sealed class LinkAnnotationTests
{
    [Fact]
    public void Annotations_link_pdf_yields_one_link_whose_action_is_a_uri_action()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-link.pdf"));

        PdfLinkAnnotation link = Assert.IsType<PdfLinkAnnotation>(Assert.Single(document.Pages[0].Annotations));

        Assert.Equal(PdfAnnotationKind.Link, link.Kind);
        Assert.Equal("Link", link.Subtype?.Value);
        Assert.Equal(new PdfRectangle(72, 700, 300, 724), link.Rect);
        PdfUriAction action = Assert.IsType<PdfUriAction>(link.Action);
        Assert.Equal("https://example.com/", action.Uri);
        Assert.Null(link.Destination);
        Assert.Equal(0, link.Border.Width);
        Assert.Equal(PdfBorderStyle.Solid, link.Border.Style);
        Assert.Equal(PdfHighlightMode.Invert, link.HighlightMode);
        Assert.Null(link.AppearanceDictionary);
        Assert.Null(link.GetAppearance());
        Assert.Same(document.Pages[0], link.Page);
        Assert.Equal(PdfAnnotationFlags.None, link.Flags);
        Assert.Empty(link.QuadPoints);
        Assert.Empty(document.Diagnostics);
    }
}
