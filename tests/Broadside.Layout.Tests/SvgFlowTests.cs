using System.Text;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Layout.Flow;
using Broadside.Objects;
using Broadside.Structure;
using static Broadside.Layout.Tests.Builder.BuilderTestSupport;

namespace Broadside.Layout.Tests;

/// <summary>
/// SVG in the document builder (issue #643): <see cref="ContainerBuilder.Svg"/> and an SVG <see cref="ImageSource"/> drawn through
/// <see cref="ContainerBuilder.Image"/> import the SVG once as a form XObject and place it as a tagged <c>Figure</c> with its alternate
/// text and bounding box (ISO 32000-2 §8.10, §14.8.4.8.5, §14.9.3).
/// </summary>
public sealed class SvgFlowTests
{
    private const string Logo = """<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40" viewBox="0 0 80 40"><rect width="80" height="40" fill="#0a5fff"/><circle cx="20" cy="20" r="10" fill="white"/></svg>""";

    private static ImageSource LogoSource => ImageSource.FromBytes(Encoding.UTF8.GetBytes(Logo));

    [Fact]
    public void An_svg_is_a_tagged_Figure_with_its_alternate_text_at_the_svg_size()
    {
        using PdfDocument document = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Logo")
            .WithLanguage("en")
            .Content(c => c.Svg(LogoSource, "The Acme logo: a blue banner with a white dot.")));

        byte[] saved = Save(document);
        using PdfDocument reopened = PdfDocument.Open(saved);
        Assert.Equal("Document(Figure)", Shape(reopened.StructureTree!.Children));
        PdfStructureElement figure = reopened.StructureTree!.Elements.Single(element => element.StructureType?.Value == "Figure");
        Assert.Equal("The Acme logo: a blue banner with a white dot.", figure.AlternateDescription);
        double[] box = BoundingBox(figure);
        Assert.Equal(60, box[2] - box[0], 3);
        Assert.Equal(30, box[3] - box[1], 3);
        Assert.DoesNotContain(document.Diagnostics, diagnostic => diagnostic.Kind.Area == DiagnosticArea.Svg);
    }

    [Fact]
    public void A_given_width_scales_the_svg_keeping_its_aspect_ratio()
    {
        using PdfDocument document = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Logo")
            .WithLanguage("en")
            .Content(c => c.Svg(LogoSource, "The Acme logo.", width: Length.Points(200))));

        using PdfDocument reopened = PdfDocument.Open(Save(document));
        double[] box = BoundingBox(reopened.StructureTree!.Elements.Single(element => element.StructureType?.Value == "Figure"));
        Assert.Equal(200, box[2] - box[0], 3);
        Assert.Equal(100, box[3] - box[1], 3);
    }

    [Fact]
    public void An_svg_drawn_twice_is_imported_once()
    {
        ImageSource source = LogoSource;
        using PdfDocument document = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Logos")
            .WithLanguage("en")
            .Content(c =>
            {
                c.Svg(source, "The Acme logo.");
                c.Svg(source, "The Acme logo again.");
            }));

        using PdfDocument reopened = PdfDocument.Open(Save(document));
        CosDictionary xobjects = (CosDictionary)reopened.Resolve(reopened.Pages[0].Resources![new CosName("XObject")])!;
        Assert.Single(xobjects.Values.Distinct());
    }

    [Fact]
    public void An_svg_image_source_through_Image_is_a_vector_figure_and_decorative_is_an_artifact()
    {
        using PdfDocument document = PdfDocument.Create(Options(), doc => doc
            .WithTitle("Logo")
            .WithLanguage("en")
            .Content(c =>
            {
                c.Image(LogoSource, "The Acme logo.");
                c.DecorativeImage(LogoSource);
            }));

        using PdfDocument reopened = PdfDocument.Open(Save(document));
        Assert.Equal("Document(Figure)", Shape(reopened.StructureTree!.Children));
        CosDictionary xobjects = (CosDictionary)reopened.Resolve(reopened.Pages[0].Resources![new CosName("XObject")])!;
        Assert.All(xobjects.Values, value => Assert.Equal("Form", ((CosName)((CosStream)reopened.Resolve(value)!).Dictionary[new CosName("Subtype")]).Value));
    }

    [Fact]
    public void Svg_sources_are_sniffed_from_xml_declarations_and_refused_when_neither_svg_png_nor_jpeg()
    {
        _ = ImageSource.FromBytes(Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\n" + Logo));
        _ = ImageSource.FromBytes(Encoding.UTF8.GetBytes("﻿  " + Logo));
        Assert.Throws<ArgumentException>(() => ImageSource.FromBytes("GIF89a"u8.ToArray()));
    }

    [Fact]
    public void Svg_arguments_are_checked()
    {
        Assert.Throws<ArgumentException>(() => PdfDocument.Create(Options(), doc => doc.Content(c => c.Svg(LogoSource, " "))));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfDocument.Create(Options(), doc => doc.Content(c => c.Svg(LogoSource, "Logo", width: Length.Points(0)))));
        Assert.Throws<ArgumentNullException>(() => PdfDocument.Create(Options(), doc => doc.Content(c => c.Svg(null!, "Logo"))));
    }

    private static double[] BoundingBox(PdfStructureElement figure) =>
        [.. ((CosArray)figure.GetAttributeValue(new CosName("Layout"), new CosName("BBox"))!).Select(value => Math.Round(((CosNumber)value).ToDouble(), 4))];
}
