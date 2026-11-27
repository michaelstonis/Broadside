using System.IO.Compression;
using System.Text;
using Broadside.Css;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Graphics.Svg;
using Broadside.TestSupport;

namespace Broadside.Tests.Graphics;

/// <summary>
/// SVG import (issue #643): the parsers (CSS subset, path data, transforms, colours, lengths), the form XObject the importer writes
/// for each element and attribute group, the security boundary (no DTD, no entity, nothing read outside the resource resolver) and
/// the leniency model (each repair is a diagnostic; strict mode throws). ISO 32000-2 §8.10, §8.5, §8.7, §9.4, §11.6; SVG 1.1.
/// </summary>
public sealed class SvgImportTests
{
    private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\"";

    /// <summary>A 1 x 1 opaque red PNG.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

    // ---- Document structure and size ----

    [Fact]
    public void The_form_is_sized_from_width_and_height_at_three_quarters_of_a_point_per_pixel()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"<svg {Ns} width=\"200\" height=\"100\"/>");

        Assert.Equal(new PdfRectangle(0, 0, 150, 75), form.BoundingBox);
        Assert.True(form.IsForm);
    }

    [Theory]
    [InlineData("width=\"2in\" height=\"1in\"", 144, 72)]
    [InlineData("width=\"72pt\" height=\"36pt\"", 72, 36)]
    [InlineData("viewBox=\"0 0 40 20\"", 30, 15)]
    [InlineData("width=\"80\" viewBox=\"0 0 40 20\"", 60, 30)]
    [InlineData("height=\"80\" viewBox=\"0 0 40 20\"", 120, 60)]
    public void Units_and_view_boxes_give_the_intrinsic_size(string attributes, double width, double height)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"<svg {Ns} {attributes}/>");

        PdfRectangle box = form.BoundingBox!.Value;
        Assert.Equal(width, box.Width, 6);
        Assert.Equal(height, box.Height, 6);
    }

    [Fact]
    public void A_root_without_size_or_view_box_is_300_by_150_pixels_and_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"<svg {Ns}><rect width=\"10\" height=\"10\"/></svg>");

        Assert.Equal(new PdfRectangle(0, 0, 225, 112.5), form.BoundingBox);
        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgSizeMissing);
    }

    [Fact]
    public void WithSize_fits_the_view_box_into_the_requested_box()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = document.CreateFormXObjectFromSvg(
            Encoding.UTF8.GetBytes($"<svg {Ns} viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\"/></svg>"),
            new SvgImportOptions().WithSize(100, 50));

        Assert.Equal(new PdfRectangle(0, 0, 100, 50), form.BoundingBox);

        // Flip to y down, then xMidYMid meet: scale 5, centred horizontally (25 points each side).
        string content = Content(form);
        Assert.Contains("1 0 0 -1 0 50 cm", content, StringComparison.Ordinal);
        Assert.Contains("5 0 0 5 25 0 cm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_are_immutable_and_validate_their_values()
    {
        var options = new SvgImportOptions();
        SvgImportOptions sized = options.WithSize(10, 20);

        Assert.Null(options.Width);
        Assert.Equal(10, sized.Width);
        Assert.Equal(20, sized.Height);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithSize(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.WithSize(1, double.NaN));
        Assert.Throws<ArgumentNullException>(() => options.WithDefaultFont(null!));
        Assert.Throws<ArgumentNullException>(() => options.WithResourceResolver(null!));
    }

    [Fact]
    public void Svgz_utf16_and_a_stream_import_the_same_content()
    {
        string svg = $"<svg {Ns} width=\"10\" height=\"10\"><rect width=\"5\" height=\"5\" fill=\"red\"/></svg>";
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(svg));
        }

        using PdfDocument document = PdfDocument.Create();
        string plain = Content(Import(document, svg));
        string fromSvgz = Content(document.CreateFormXObjectFromSvg(compressed.ToArray()));
        string fromUtf16 = Content(document.CreateFormXObjectFromSvg([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(svg)]));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        string fromStream = Content(document.CreateFormXObjectFromSvg(stream));

        Assert.Equal(plain, fromSvgz);
        Assert.Equal(plain, fromUtf16);
        Assert.Equal(plain, fromStream);
    }

    [Theory]
    [InlineData("<svg")]
    [InlineData("<html><body/></html>")]
    [InlineData("<svg xmlns=\"http://example.com/not-svg\"/>")]
    [InlineData("not xml at all")]
    public void Malformed_xml_or_a_root_that_is_not_svg_throws(string text)
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes(text)));
    }

    // ---- Security ----

    [Fact]
    public void A_document_type_declaration_is_refused_so_no_external_entity_is_ever_read()
    {
        string path = Path.Combine(Path.GetTempPath(), $"svg-xxe-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "secret");
        try
        {
            string svg = $"""<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM "file://{path}">]><svg {Ns} width="10" height="10"><text>&xxe;</text></svg>""";
            using PdfDocument document = PdfDocument.Create();

            ArgumentException exception = Assert.Throws<ArgumentException>(() => document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes(svg)));
            Assert.Contains("document type declaration", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_entity_expansion_bomb_is_refused()
    {
        string svg = $"""<!DOCTYPE svg [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]><svg {Ns}><text>&b;</text></svg>""";
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<ArgumentException>(() => document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes(svg)));
    }

    [Fact]
    public void Image_references_reach_only_the_resource_resolver_and_never_the_file_system()
    {
        string path = Path.Combine(Path.GetTempPath(), $"svg-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, OnePixelPng);
        try
        {
            string svg = $"""<svg {Ns} width="10" height="10"><image href="{path}" width="10" height="10"/><image href="file://{path}" width="10" height="10"/><image href="https://example.com/a.png" width="10" height="10"/></svg>""";
            using PdfDocument document = PdfDocument.Create();

            // Without a resolver nothing is drawn, though the file exists.
            PdfFormXObject unresolved = Import(document, svg);
            Assert.DoesNotContain("Do", Content(unresolved), StringComparison.Ordinal);
            Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgReferenceMissing);

            // With one, it receives each href as written and decides.
            var asked = new List<string>();
            PdfFormXObject resolved = document.CreateFormXObjectFromSvg(
                Encoding.UTF8.GetBytes(svg),
                new SvgImportOptions().WithResourceResolver(href =>
                {
                    asked.Add(href);
                    return href.StartsWith("https:", StringComparison.Ordinal) ? OnePixelPng : null;
                }));
            Assert.Equal([path, $"file://{path}", "https://example.com/a.png"], asked);
            Assert.Contains("Do", Content(resolved), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_import_rule_is_skipped_and_never_fetched()
    {
        using PdfDocument document = PdfDocument.Create();
        _ = Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><style>@import url(https://example.com/a.css); rect {{ fill: blue }}</style><rect width=\"5\" height=\"5\"/></svg>");

        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgStyleInvalid);
    }

    [Fact]
    public void A_data_url_image_is_decoded_inside_the_import()
    {
        string data = Convert.ToBase64String(OnePixelPng);
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><image href=\"data:image/png;base64,{data}\" width=\"10\" height=\"10\"/></svg>");

        Assert.Contains("/Im1 Do", Content(form), StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_nested_svg_image_is_a_nested_form()
    {
        string inner = Convert.ToBase64String(Encoding.UTF8.GetBytes($"<svg {Ns} width=\"4\" height=\"4\"><rect width=\"4\" height=\"4\"/></svg>"));
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><image href=\"data:image/svg+xml;base64,{inner}\" width=\"8\" height=\"8\"/></svg>");

        Assert.Contains("/Fm1 Do", Content(form), StringComparison.Ordinal);
    }

    // ---- Shapes and paths ----

    [Fact]
    public void Basic_shapes_become_paths()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"""
            <svg {Ns} width="100" height="100">
              <rect x="1" y="2" width="3" height="4"/>
              <rect x="0" y="0" width="10" height="10" rx="2"/>
              <circle cx="50" cy="50" r="10"/>
              <ellipse cx="50" cy="50" rx="10" ry="5"/>
              <line x1="0" y1="0" x2="10" y2="10" stroke="black"/>
              <polyline points="0,0 10,0 10,10" fill="none" stroke="black"/>
              <polygon points="0 0 10 0 10 10"/>
            </svg>
            """));

        Assert.Contains("1 2 m\n4 2 l\n4 6 l\n1 6 l\nh\nf", content, StringComparison.Ordinal);
        Assert.Contains("60 50 m", content, StringComparison.Ordinal);
        Assert.Contains("10 10 l\nS", content, StringComparison.Ordinal);
        Assert.Contains("10 10 l\nh\nf", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_sized_shapes_draw_nothing()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><rect width=\"0\" height=\"5\"/><circle r=\"0\"/><polyline points=\"1 1\"/></svg>"));

        Assert.DoesNotContain(" f", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("M10 10 L20 20", "M10,10;L20,20")]
    [InlineData("M10 10 20 20 30 30", "M10,10;L20,20;L30,30")]
    [InlineData("m10 10 10 0 v10 h-10 z", "M10,10;L20,10;L20,20;L10,20;Z10,10")]
    [InlineData("M0 0H5V5", "M0,0;L5,0;L5,5")]
    [InlineData("M-1-2.5.5.5", "M-1,-2.5;L0.5,0.5")]
    [InlineData("M0 0 Q 5 10 10 0", "M0,0;C3.333,6.667,6.667,6.667,10,0")]
    [InlineData("M0 0 C 0 5 5 5 5 0 S 10 -5 10 0", "M0,0;C0,5,5,5,5,0;C5,-5,10,-5,10,0")]
    [InlineData("M0 0 Q 5 10 10 0 T 20 0", "M0,0;C3.333,6.667,6.667,6.667,10,0;C13.333,-6.667,16.667,-6.667,20,0")]
    [InlineData("M0 0 L5 5 X 3 3 L9 9", "M0,0;L5,5")]
    [InlineData("L5 5", "")]
    public void Path_data_reads_every_command_and_stops_at_an_error(string data, string expected)
    {
        var path = new SvgPath();
        _ = SvgPathParser.TryParse(data, path);

        Assert.Equal(expected, string.Join(';', path.Segments.Select(Describe)));
    }

    [Fact]
    public void Arc_flags_may_be_written_without_separators_and_arcs_become_quarter_cubics()
    {
        var path = new SvgPath();
        Assert.True(SvgPathParser.TryParse("M0 0a10 10 0 1110 10", path));

        SvgSegment last = path.Segments[^1];
        Assert.Equal(SvgSegmentKind.Cubic, last.Kind);
        Assert.Equal(10, last.X, 9);
        Assert.Equal(10, last.Y, 9);
        Assert.Equal(4, path.Segments.Count);
    }

    [Fact]
    public void Arc_radii_too_small_are_scaled_up_and_zero_radii_are_a_line()
    {
        var scaled = new SvgPath();
        Assert.True(SvgPathParser.TryParse("M0 0 A1 1 0 0 1 10 0", scaled));
        SvgRect bounds = scaled.Bounds(Matrix.Identity);
        Assert.Equal(5, bounds.Height, 6);

        var line = new SvgPath();
        Assert.True(SvgPathParser.TryParse("M0 0 A0 5 0 0 1 10 0", line));
        Assert.Equal(SvgSegmentKind.Line, line.Segments[^1].Kind);
    }

    [Fact]
    public void Bad_path_data_draws_up_to_the_error_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><path d=\"M0 0 L5 5 L oops\" stroke=\"red\"/></svg>"));

        Assert.Contains("5 5 l", content, StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgValueInvalid);
    }

    // ---- Transforms, colours and lengths ----

    [Theory]
    [InlineData("translate(10 20)", 1, 0, 0, 1, 10, 20)]
    [InlineData("translate(10)", 1, 0, 0, 1, 10, 0)]
    [InlineData("scale(2)", 2, 0, 0, 2, 0, 0)]
    [InlineData("scale(2,3)", 2, 0, 0, 3, 0, 0)]
    [InlineData("matrix(1 2 3 4 5 6)", 1, 2, 3, 4, 5, 6)]
    [InlineData("rotate(90)", 0, 1, -1, 0, 0, 0)]
    [InlineData("rotate(90 10 10)", 0, 1, -1, 0, 20, 0)]
    [InlineData("translate(10,0) scale(2)", 2, 0, 0, 2, 10, 0)]
    [InlineData("skewX(45)", 1, 0, 1, 1, 0, 0)]
    public void Transform_lists_apply_with_the_first_outermost(string text, double a, double b, double c, double d, double e, double f)
    {
        Assert.True(SvgTransformParser.TryParse(text, out Matrix matrix));

        Assert.Equal(new[] { a, b, c, d, e, f }, new[] { matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F }.Select(v => Math.Round(v, 9)));
    }

    [Theory]
    [InlineData("translate(10")]
    [InlineData("spin(4)")]
    [InlineData("scale()")]
    public void Unreadable_transforms_are_refused(string text) => Assert.False(SvgTransformParser.TryParse(text, out _));

    [Theory]
    [InlineData("red", 1, 0, 0, 1)]
    [InlineData("#f00", 1, 0, 0, 1)]
    [InlineData("#ff000080", 1, 0, 0, 0.50196)]
    [InlineData("#0f08", 0, 1, 0, 0.53333)]
    [InlineData("rgb(0, 128, 255)", 0, 0.50196, 1, 1)]
    [InlineData("rgb(0% 50% 100% / 0.25)", 0, 0.5, 1, 0.25)]
    [InlineData("rgba(255,0,0,0.5)", 1, 0, 0, 0.5)]
    [InlineData("hsl(120, 100%, 50%)", 0, 1, 0, 1)]
    [InlineData("hsla(240deg 100% 50% / 50%)", 0, 0, 1, 0.5)]
    [InlineData("RebeccaPurple", 0.4, 0.2, 0.6, 1)]
    [InlineData("transparent", 0, 0, 0, 0)]
    public void Colours_read_every_css_form(string text, double r, double g, double b, double a)
    {
        Assert.True(SvgColorParser.TryParse(text, out SvgRgba color));

        Assert.Equal(r, color.R, 4);
        Assert.Equal(g, color.G, 4);
        Assert.Equal(b, color.B, 4);
        Assert.Equal(a, color.A, 4);
    }

    [Theory]
    [InlineData("#12")]
    [InlineData("rgb(1,2)")]
    [InlineData("notacolor")]
    [InlineData("#gggggg")]
    public void Unreadable_colours_are_refused(string text) => Assert.False(SvgColorParser.TryParse(text, out _));

    [Theory]
    [InlineData("10", 10)]
    [InlineData("10px", 10)]
    [InlineData("1in", 96)]
    [InlineData("3pt", 4)]
    [InlineData("1pc", 16)]
    [InlineData("25.4mm", 96)]
    [InlineData("2em", 20)]
    [InlineData("2ex", 10)]
    [InlineData("50%", 100)]
    public void Lengths_resolve_units_against_the_viewport_and_font_size(string text, double expected)
    {
        Assert.True(SvgLength.TryParse(text, out SvgLength length));

        Assert.Equal(expected, length.Resolve(200, 10), 6);
    }

    // ---- Paint ----

    [Fact]
    public void Fill_and_stroke_properties_are_written_as_graphics_state()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"""
            <svg {Ns} width="10" height="10">
              <path d="M0 0 L5 5" fill="none" stroke="#00f" stroke-width="2" stroke-linecap="round" stroke-linejoin="bevel"
                    stroke-miterlimit="6" stroke-dasharray="1 2 3" stroke-dashoffset="1" stroke-opacity="0.5"/>
              <rect width="5" height="5" fill="red" fill-rule="evenodd" fill-opacity="0.25"/>
            </svg>
            """));

        Assert.Contains("0 0 1 RG", content, StringComparison.Ordinal);
        Assert.Contains("2 w", content, StringComparison.Ordinal);
        Assert.Contains("1 J", content, StringComparison.Ordinal);
        Assert.Contains("2 j", content, StringComparison.Ordinal);
        Assert.Contains("6 M", content, StringComparison.Ordinal);
        Assert.Contains("[1 2 3 1 2 3] 1 d", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 rg", content, StringComparison.Ordinal);
        Assert.Contains("f*", content, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(content, " gs"));
    }

    [Fact]
    public void CurrentColor_takes_the_color_property_and_inherit_keeps_the_parent_value()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><g color=\"lime\" fill=\"blue\"><rect width=\"5\" height=\"5\" fill=\"currentColor\"/><rect width=\"5\" height=\"5\" fill=\"inherit\"/></g></svg>"));

        Assert.Contains("0 1 0 rg", content, StringComparison.Ordinal);
        Assert.Contains("0 0 1 rg", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Gradients_become_shading_patterns_and_href_inherits_stops()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"""
            <svg {Ns} width="10" height="10">
              <linearGradient id="base"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue"/></linearGradient>
              <radialGradient id="r" href="#base" fx="0.3"/>
              <rect width="5" height="5" fill="url(#base)"/>
              <rect width="5" height="5" stroke="url(#r)"/>
            </svg>
            """);

        string content = Content(form);
        Assert.Contains("/Pattern cs", content, StringComparison.Ordinal);
        Assert.Contains("/Pattern CS", content, StringComparison.Ordinal);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_missing_paint_server_uses_its_fallback_or_paints_nothing_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><rect width=\"5\" height=\"5\" fill=\"url(#nope) lime\"/><rect width=\"5\" height=\"5\" fill=\"url(#gone)\"/></svg>"));

        Assert.Contains("0 1 0 rg", content, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(content, "\nf"));
        Assert.Single(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgReferenceMissing);
    }

    [Fact]
    public void Group_opacity_masks_and_blend_modes_make_isolated_transparency_groups()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"""
            <svg {Ns} width="10" height="10">
              <mask id="m"><rect width="10" height="10" fill="white"/></mask>
              <g opacity="0.5"><rect width="5" height="5"/><rect width="5" height="5" stroke="red"/></g>
              <rect width="5" height="5" mask="url(#m)"/>
              <rect width="5" height="5" style="mix-blend-mode: screen"/>
            </svg>
            """);

        string content = Content(form);
        Assert.Equal(3, CountOf(content, " Do"));
        Assert.Contains("/SMask", string.Join(' ', document.Diagnostics.Select(static d => d.Message)) + SavedText(document), StringComparison.Ordinal);
    }

    // ---- Structure, use, clip ----

    [Fact]
    public void Use_cycles_stop_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        _ = Import(document, $"""
            <svg {Ns} xmlns:xlink="http://www.w3.org/1999/xlink" width="10" height="10">
              <g id="a"><use xlink:href="#b"/></g>
              <g id="b"><use xlink:href="#a"/></g>
              <use href="#self" id="self"/>
            </svg>
            """);

        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgReferenceCycle);
    }

    [Fact]
    public void Display_none_and_defs_draw_nothing_and_visibility_hidden_skips_painting()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"""
            <svg {Ns} width="10" height="10">
              <defs><rect width="5" height="5"/></defs>
              <rect width="5" height="5" display="none"/>
              <g visibility="hidden"><rect width="5" height="5"/></g>
              <symbol><rect width="5" height="5"/></symbol>
            </svg>
            """));

        Assert.DoesNotContain("\nf", content, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clip_path_writes_a_clipping_path_with_its_clip_rule()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><clipPath id=\"c\"><rect width=\"4\" height=\"4\" clip-rule=\"evenodd\"/></clipPath><rect width=\"5\" height=\"5\" clip-path=\"url(#c)\"/></svg>"));

        Assert.Contains("W*\nn", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_elements_and_properties_are_skipped_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        _ = Import(document, $"""
            <svg {Ns} width="10" height="10">
              <filter id="f"><feGaussianBlur stdDeviation="2"/></filter>
              <foreignObject width="5" height="5"/>
              <script>alert(1)</script>
              <rect width="5" height="5"><animate attributeName="x" to="5"/></rect>
              <rect width="5" height="5" filter="url(#f)"/>
            </svg>
            """);

        // The document records a code once; the first skipped element names it.
        Assert.Contains("filter", Assert.Single(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgFeatureUnsupported).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\nf", Content(document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes($"<svg {Ns} width=\"1\" height=\"1\"><foreignObject><rect width=\"1\" height=\"1\"/></foreignObject></svg>"))), StringComparison.Ordinal);
    }

    // ---- CSS ----

    [Fact]
    public void Selectors_match_type_class_id_attribute_descendant_and_child()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $$"""
            <svg {{Ns}} width="10" height="10">
              <style><![CDATA[
                rect { fill: #010101 }
                .a { fill: #020202 }
                #i { fill: #030303 }
                g rect.b { fill: #040404 }
                g > [data-x="y"] { fill: #050505 }
                rect:first-child { stroke: #060606 }
                .c { fill: #070707 !important }
                @media print { rect { fill: #ffffff } }
              ]]></style>
              <rect width="1" height="1"/>
              <rect class="a" width="1" height="1"/>
              <rect class="a" id="i" width="1" height="1"/>
              <g><g><rect class="b" width="1" height="1"/></g></g>
              <g><rect data-x="y" width="1" height="1"/></g>
              <rect class="c" style="fill: red" width="1" height="1"/>
            </svg>
            """));

        foreach (string gray in new[] { "0.0039", "0.0078", "0.0118", "0.0157", "0.0196", "0.0275" })
        {
            Assert.Contains($"{gray}", content, StringComparison.Ordinal);
        }

        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgStyleInvalid);
    }

    [Theory]
    [InlineData("rect", 0x000001)]
    [InlineData(".a", 0x000100)]
    [InlineData("#a", 0x010000)]
    [InlineData("g rect.a#b", 0x010102)]
    [InlineData("*", 0)]
    public void Specificity_counts_ids_classes_and_types(string selector, int specificity)
    {
        Assert.True(CssSelector.TryParse(selector, out CssSelector? parsed));

        Assert.Equal(specificity, parsed.Specificity);
    }

    [Theory]
    [InlineData("a + b")]
    [InlineData("a ~ b")]
    [InlineData("a:hover")]
    [InlineData("a::before")]
    [InlineData("> a")]
    public void Unsupported_selectors_are_refused(string selector) => Assert.False(CssSelector.TryParse(selector, out _));

    [Fact]
    public void Declarations_read_important_and_skip_malformed_ones()
    {
        var declarations = new List<CssDeclaration>();
        var errors = new List<string>();
        CssStyleSheet.ParseDeclarations("fill: red !important; nonsense; stroke:url(#a;b); /* c */ opacity : 0.5", declarations, errors.Add);

        Assert.Equal(
            [new CssDeclaration("fill", "red", true), new CssDeclaration("stroke", "url(#a;b)", false), new CssDeclaration("opacity", "0.5", false)],
            declarations);
        Assert.Single(errors);
    }

    // ---- Text ----

    [Fact]
    public void Text_is_shown_as_real_text_that_extracts_and_anchors_by_its_width()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFormXObject form = Import(document, $"""
            <svg {Ns} width="200" height="100">
              <text x="100" y="50" font-family="'Helvetica', sans-serif" font-size="20" text-anchor="middle">Hello <tspan font-weight="bold">world</tspan></text>
            </svg>
            """);
        PdfPage page = document.Pages[0];
        using (PdfCanvas canvas = page.CreateCanvas())
        {
            canvas.DrawForm(form);
        }

        string content = Content(form);
        Assert.Contains("Tf", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 -1", content, StringComparison.Ordinal);
        Assert.Contains("Hello world", page.ExtractText().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_a_font_cannot_show_become_question_marks_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        _ = Import(document, $"<svg {Ns} width=\"100\" height=\"20\"><text y=\"10\" font-family=\"sans-serif\">日本</text></svg>");

        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgTextNotEncodable);
    }

    // ---- Leniency ----

    [Fact]
    public void Strict_mode_throws_the_first_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create(new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(
            () => document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes($"<svg {Ns} width=\"10\" height=\"10\"><rect width=\"5\" height=\"5\" fill=\"url(#gone)\"/></svg>")));
        Assert.Equal(SvgDiagnostics.SvgReferenceMissing, exception.Diagnostic.Kind);
    }

    [Fact]
    public void Unreadable_style_values_keep_the_inherited_value_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Create();
        string content = Content(Import(document, $"<svg {Ns} width=\"10\" height=\"10\"><g fill=\"blue\"><rect width=\"5\" height=\"5\" fill=\"bluish\" opacity=\"lots\"/></g></svg>"));

        Assert.Contains("0 0 1 rg", content, StringComparison.Ordinal);
        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgValueInvalid);
    }

    [Fact]
    public void Every_svg_code_is_in_the_catalog_under_the_svg_area()
    {
        foreach (DiagnosticCode code in SvgDiagnostics.Codes)
        {
            Assert.Equal(DiagnosticArea.Svg, code.Area);
            Assert.True(DiagnosticCatalog.TryGet(code.Name, out DiagnosticCode? found));
            Assert.Same(code, found);
        }
    }

    // ---- Resource limits ----

    [Fact]
    public void A_use_bomb_stops_at_the_element_budget_with_a_diagnostic()
    {
        var builder = new StringBuilder($"<svg {Ns} width=\"10\" height=\"10\"><g id=\"l0\"><rect width=\"1\" height=\"1\"/></g>");
        for (int level = 1; level <= 12; level++)
        {
            builder.Append($"<g id=\"l{level}\">");
            for (int copy = 0; copy < 10; copy++)
            {
                builder.Append($"<use href=\"#l{level - 1}\"/>");
            }

            builder.Append("</g>");
        }

        builder.Append("</svg>");
        using PdfDocument document = PdfDocument.Create();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = Import(document, builder.ToString());

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"The import took {stopwatch.Elapsed}.");
        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgReferenceCycle);
    }

    [Fact]
    public void Elements_nested_past_the_depth_limit_are_dropped_with_a_diagnostic()
    {
        string svg = $"<svg {Ns} width=\"10\" height=\"10\">" + string.Concat(Enumerable.Repeat("<g>", 20_000)) + "<rect width=\"1\" height=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", 20_000)) + "</svg>";
        using PdfDocument document = PdfDocument.Create();
        _ = Import(document, svg);

        Assert.Contains(document.Diagnostics, d => d.Kind == SvgDiagnostics.SvgReferenceCycle);
    }

    [Fact]
    public void Descendant_selectors_do_not_backtrack_exponentially()
    {
        string selector = string.Join(' ', Enumerable.Repeat("g", 30)) + " rect";
        string svg = $"<svg {Ns} width=\"10\" height=\"10\"><style>{selector} {{ fill: red }}</style>" + string.Concat(Enumerable.Repeat("<g>", 100)) + "<path d=\"M0 0\"/>" + string.Concat(Enumerable.Repeat("</g>", 100)) + "</svg>";
        using PdfDocument document = PdfDocument.Create();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = Import(document, svg);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"The import took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void The_corpus_svg_files_import_with_the_diagnostics_their_readme_rows_state()
    {
        using PdfDocument document = PdfDocument.Create();
        _ = document.CreateFormXObjectFromSvg(Corpus.Bytes("svg-features.svg"));
        Assert.Empty(document.Diagnostics);

        _ = document.CreateFormXObjectFromSvg(Corpus.Bytes("svg-styled.svg"));
        Assert.Equal(SvgDiagnostics.SvgStyleInvalid, Assert.Single(document.Diagnostics).Kind);
    }

    private static PdfFormXObject Import(PdfDocument document, string svg) => document.CreateFormXObjectFromSvg(Encoding.UTF8.GetBytes(svg));

    private static string SavedText(PdfDocument document)
    {
        using var saved = new MemoryStream();
        document.Save(saved, new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None));
        return Encoding.Latin1.GetString(saved.ToArray());
    }

    private static string Content(PdfFormXObject form) => Encoding.Latin1.GetString(form.Decode().Span);

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Describe(SvgSegment segment)
    {
        static string N(double value) => Math.Round(value, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return segment.Kind switch
        {
            SvgSegmentKind.Move => $"M{N(segment.X)},{N(segment.Y)}",
            SvgSegmentKind.Line => $"L{N(segment.X)},{N(segment.Y)}",
            SvgSegmentKind.Cubic => $"C{N(segment.X1)},{N(segment.Y1)},{N(segment.X2)},{N(segment.Y2)},{N(segment.X)},{N(segment.Y)}",
            _ => $"Z{N(segment.X)},{N(segment.Y)}",
        };
    }
}
