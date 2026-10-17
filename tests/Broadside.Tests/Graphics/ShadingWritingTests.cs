using Broadside.Content;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Tests.Content;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Shadings written through the <see cref="PdfDocument"/> factories and painted with <see cref="PdfCanvas.PaintShading"/>: after a
/// save and a reopen, each re-reads through the shading models (#79) with the geometry and colours it was given. ISO 32000-2
/// §8.7.4.3 (Table 77), §8.7.4.5 (Tables 78 to 85) and §7.10.3, §7.10.4 (the functions gradient stops become).
/// </summary>
public sealed class ShadingWritingTests
{
    [Fact]
    public void An_axial_shading_from_two_stops_re_reads_with_its_axis_and_colours()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfAxialShading shading = document.CreateAxialShading(
            new PathPoint(100, 200),
            new PathPoint(400, 200),
            [new PdfGradientStop(0, PdfColor.FromRgb(1, 0, 0)), new PdfGradientStop(1, PdfColor.FromRgb(0, 0, 1))]);
        Paint(document, shading);

        ShadingRecorder recorder = Replay(document);

        PdfAxialShading read = Assert.IsType<PdfAxialShading>(Assert.Single(recorder.Shadings));
        Assert.True(read.IsValid);
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, read.ColorSpace.Family);
        Assert.Equal(new PathPoint(100, 200), read.Start);
        Assert.Equal(new PathPoint(400, 200), read.End);
        Assert.Equal([0.0, 1.0], read.Domain);
        Assert.False(read.ExtendStart);
        Assert.False(read.ExtendEnd);
        Assert.Equal([1f, 0f, 0f], Evaluate(read, 0));
        Assert.Equal([0.5f, 0f, 0.5f], Evaluate(read, 0.5f));
        Assert.Equal([0f, 0f, 1f], Evaluate(read, 1));
    }

    [Fact]
    public void Stops_with_a_hard_edge_make_a_stitching_function_that_switches_colour_at_the_shared_offset()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfAxialShading shading = document.CreateAxialShading(
            new PathPoint(0, 0),
            new PathPoint(100, 0),
            [
                new PdfGradientStop(0, PdfColor.FromRgb(1, 0, 0)),
                new PdfGradientStop(0.5, PdfColor.FromRgb(1, 0, 0)),
                new PdfGradientStop(0.5, PdfColor.FromRgb(0, 0, 1)),
                new PdfGradientStop(1, PdfColor.FromRgb(0, 1, 0)),
            ]);
        Paint(document, shading);

        PdfAxialShading read = Assert.IsType<PdfAxialShading>(Assert.Single(Replay(document).Shadings));

        Assert.IsType<PdfStitchingFunction>(Assert.Single(read.Functions));
        Assert.Equal([1f, 0f, 0f], Evaluate(read, 0.25f));
        Assert.Equal([0f, 0.5f, 0.5f], Evaluate(read, 0.75f));
        Assert.Equal([0f, 1f, 0f], Evaluate(read, 1));
    }

    [Fact]
    public void A_radial_shading_re_reads_its_circles_and_every_option()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfShadingOptions options = new PdfShadingOptions()
            .WithBoundingBox(new PdfRectangle(0, 0, 200, 200))
            .WithBackground(PdfColor.FromGray(1))
            .WithAntiAlias(true)
            .WithExtend(true, true);
        PdfRadialShading shading = document.CreateRadialShading(
            new PathPoint(100, 100),
            0,
            new PathPoint(100, 100),
            80,
            [new PdfGradientStop(0, PdfColor.FromGray(0)), new PdfGradientStop(1, PdfColor.FromGray(1))],
            options);
        Paint(document, shading);

        PdfRadialShading read = Assert.IsType<PdfRadialShading>(Assert.Single(Replay(document).Shadings));

        Assert.Equal(new PathPoint(100, 100), read.StartCenter);
        Assert.Equal(0, read.StartRadius);
        Assert.Equal(80, read.EndRadius);
        Assert.True(read.ExtendStart);
        Assert.True(read.ExtendEnd);
        Assert.True(read.AntiAlias);
        Assert.Equal(new PdfRectangle(0, 0, 200, 200), read.BoundingBox);
        Assert.Equal([1f], read.Background);
        Assert.Equal([0.5f], Evaluate(read, 0.5f));
    }

    [Fact]
    public void Axial_and_radial_shadings_take_a_function_of_the_callers_with_its_domain()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.CreateExponentialFunction([0], [1], 2);
        PdfAxialShading axial = document.CreateAxialShading(PdfDeviceGrayColorSpace.Instance, new PathPoint(0, 0), new PathPoint(10, 0), function);
        PdfRadialShading radial = document.CreateRadialShading(PdfDeviceGrayColorSpace.Instance, new PathPoint(0, 0), 0, new PathPoint(0, 0), 10, function);
        Paint(document, axial, radial);

        ShadingRecorder recorder = Replay(document);

        Assert.Equal(2, recorder.Shadings.Count);
        Assert.All(recorder.Shadings, shading => Assert.Equal([0.25f], Evaluate(shading, 0.5f)));
        Assert.Equal([0.0, 1.0], Assert.IsType<PdfAxialShading>(recorder.Shadings[0]).Domain);
    }

    [Fact]
    public void A_function_based_shading_re_reads_its_domain_matrix_and_colours()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.CreatePostScriptFunction([0, 1, 0, 1], [0, 1, 0, 1, 0, 1], "{ 0 }");
        var matrix = new Matrix(100, 0, 0, 100, 50, 50);
        PdfFunctionShading shading = document.CreateFunctionShading(PdfDeviceRgbColorSpace.Instance, function, new PdfRectangle(0, 0, 1, 1), matrix);
        Paint(document, shading);

        PdfFunctionShading read = Assert.IsType<PdfFunctionShading>(Assert.Single(Replay(document).Shadings));

        Assert.Equal([0.0, 1.0, 0.0, 1.0], read.Domain);
        Assert.Equal(matrix, read.Matrix);
        Assert.Equal([0.25f, 0.75f, 0f], Evaluate(read, 0.25f, 0.75f));
    }

    [Fact]
    public void A_free_form_mesh_re_reads_its_triangles_strips_and_fans_with_their_colours()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFreeFormMeshBuilder mesh = new PdfFreeFormMeshBuilder(PdfDeviceRgbColorSpace.Instance)
            .AddTriangle(new PdfMeshVertex(0, 0, PdfColor.FromRgb(1, 0, 0)), new PdfMeshVertex(100, 0, PdfColor.FromRgb(0, 1, 0)), new PdfMeshVertex(0, 100, PdfColor.FromRgb(0, 0, 1)))
            .AddStripVertex(new PdfMeshVertex(100, 100, PdfColor.FromRgb(1, 1, 0)))
            .AddFanVertex(new PdfMeshVertex(200, 50, PdfColor.FromRgb(0, 1, 1)));
        Assert.Equal(3, mesh.TriangleCount);
        Paint(document, document.CreateMeshShading(mesh));

        PdfTriangleMeshShading read = Assert.IsType<PdfTriangleMeshShading>(Assert.Single(Replay(document).Shadings));

        Assert.Equal(PdfShadingType.FreeFormTriangleMesh, read.ShadingType);
        Assert.Equal(3, read.TriangleCount);
        AssertNear(new PathPoint(200, 50), read.Vertices[read.VertexCount - 1]);
        Assert.Equal([0f, 1f, 1f], Round(read.VertexColors[^3..]));
    }

    [Fact]
    public void A_lattice_mesh_with_a_function_re_reads_its_rows_and_parametric_values()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.CreateGradientFunction([new PdfGradientStop(0, PdfColor.FromGray(0)), new PdfGradientStop(1, PdfColor.FromGray(1))]);
        PdfLatticeMeshBuilder mesh = new PdfLatticeMeshBuilder(PdfDeviceGrayColorSpace.Instance, function, 3)
            .AddRow(new PdfMeshVertex(0, 0, 0.0), new PdfMeshVertex(50, 0, 0.5), new PdfMeshVertex(100, 0, 1.0))
            .AddRow(new PdfMeshVertex(0, 50, 1.0), new PdfMeshVertex(50, 50, 0.5), new PdfMeshVertex(100, 50, 0.0));
        Assert.Equal(2, mesh.RowCount);
        Paint(document, document.CreateMeshShading(mesh, new PdfShadingOptions().WithAntiAlias(true)));

        PdfTriangleMeshShading read = Assert.IsType<PdfTriangleMeshShading>(Assert.Single(Replay(document).Shadings));

        Assert.Equal(PdfShadingType.LatticeFormTriangleMesh, read.ShadingType);
        Assert.Equal(3, read.VerticesPerRow);
        Assert.Equal(4, read.TriangleCount);
        Assert.True(read.HasFunction);
        Assert.True(read.AntiAlias);
        Assert.Equal(0.5f, read.VertexColors[1], 0.0001f);
        AssertNear(new PathPoint(100, 50), read.Vertices[5]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_patch_mesh_re_reads_its_control_points_and_corner_colours(bool tensor)
    {
        using PdfDocument document = PdfDocument.Create();
        PathPoint[] points = PatchPoints(tensor);
        PdfPatchMeshBuilder mesh = new PdfPatchMeshBuilder(PdfDeviceRgbColorSpace.Instance, tensor)
            .AddPatch(points, [PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(0, 1, 0), PdfColor.FromRgb(0, 0, 1), PdfColor.FromRgb(1, 1, 1)]);
        Assert.Equal(1, mesh.PatchCount);
        Paint(document, document.CreateMeshShading(mesh));

        PdfPatchMeshShading read = Assert.IsType<PdfPatchMeshShading>(Assert.Single(Replay(document).Shadings));

        Assert.Equal(!tensor, read.IsCoons);
        Assert.Equal(1, read.PatchCount);
        AssertNear(points[0], read.GetControlPoint(0, 0, 0));
        Assert.Equal([1f, 0f, 0f], Round(read.CornerColors[..3]));
    }

    [Theory]
    [InlineData("colour where t expected")]
    [InlineData("t where colour expected")]
    [InlineData("other space")]
    [InlineData("component range")]
    [InlineData("not finite")]
    [InlineData("t outside domain")]
    [InlineData("lattice row length")]
    [InlineData("patch point count")]
    [InlineData("pattern space")]
    public void Mesh_input_that_does_not_fit_is_refused_when_added(string input)
    {
        using PdfDocument document = PdfDocument.Create();
        PdfFunction function = document.CreateGradientFunction([new PdfGradientStop(0, PdfColor.FromGray(0)), new PdfGradientStop(1, PdfColor.FromGray(1))]);
        var rgb = new PdfFreeFormMeshBuilder(PdfDeviceRgbColorSpace.Instance);
        var parametric = new PdfFreeFormMeshBuilder(PdfDeviceGrayColorSpace.Instance, function);
        PdfMeshVertex red = new(0, 0, PdfColor.FromRgb(1, 0, 0));
        PdfColor[] corners = [PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(1, 0, 0)];

        Assert.ThrowsAny<ArgumentException>(() => input switch
        {
            "colour where t expected" => parametric.AddTriangle(new PdfMeshVertex(0, 0, PdfColor.FromGray(0)), new PdfMeshVertex(1, 0, 0.0), new PdfMeshVertex(0, 1, 0.0)),
            "t where colour expected" => rgb.AddTriangle(red, red, new PdfMeshVertex(0, 1, 0.5)),
            "other space" => rgb.AddTriangle(red, red, new PdfMeshVertex(0, 1, PdfColor.FromCmyk(0, 0, 0, 1))),
            "component range" => rgb.AddTriangle(red, red, new PdfMeshVertex(0, 1, PdfColor.FromRgb(2, 0, 0))),
            "not finite" => rgb.AddTriangle(red, red, new PdfMeshVertex(double.NaN, 1, PdfColor.FromRgb(1, 0, 0))),
            "t outside domain" => parametric.AddTriangle(new PdfMeshVertex(0, 0, 0.0), new PdfMeshVertex(1, 0, 0.0), new PdfMeshVertex(0, 1, 1.5)),
            "lattice row length" => new PdfLatticeMeshBuilder(PdfDeviceRgbColorSpace.Instance, 3).AddRow(red, red),
            "patch point count" => new PdfPatchMeshBuilder(PdfDeviceRgbColorSpace.Instance, false).AddPatch(PatchPoints(true), corners),
            _ => (object)new PdfFreeFormMeshBuilder(PdfPatternColorSpace.Colored),
        });
        Assert.Equal(0, rgb.TriangleCount);
        Assert.Equal(0, parametric.TriangleCount);
    }

    [Fact]
    public void An_empty_mesh_or_a_lattice_of_one_row_is_refused_when_written()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Throws<InvalidOperationException>(() => document.CreateMeshShading(new PdfFreeFormMeshBuilder(PdfDeviceRgbColorSpace.Instance)));
        Assert.Throws<InvalidOperationException>(() => document.CreateMeshShading(new PdfPatchMeshBuilder(PdfDeviceRgbColorSpace.Instance, true)));
        PdfLatticeMeshBuilder lattice = new PdfLatticeMeshBuilder(PdfDeviceGrayColorSpace.Instance, 2)
            .AddRow(new PdfMeshVertex(0, 0, PdfColor.FromGray(0)), new PdfMeshVertex(1, 0, PdfColor.FromGray(1)));
        Assert.Throws<InvalidOperationException>(() => document.CreateMeshShading(lattice));
        Assert.Throws<InvalidOperationException>(() => new PdfFreeFormMeshBuilder(PdfDeviceGrayColorSpace.Instance).AddStripVertex(new PdfMeshVertex(0, 0, PdfColor.FromGray(0))));
    }

    [Fact]
    public void Gradient_stops_that_cannot_make_a_function_are_refused()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfGradientStop black = new(0, PdfColor.FromGray(0));
        PdfGradientStop white = new(1, PdfColor.FromGray(1));

        Assert.Throws<ArgumentException>(() => document.CreateGradientFunction([black]));
        Assert.Throws<ArgumentException>(() => document.CreateGradientFunction([new PdfGradientStop(0.6, PdfColor.FromGray(0)), new PdfGradientStop(0.4, PdfColor.FromGray(1))]));
        Assert.Throws<ArgumentException>(() => document.CreateGradientFunction([black, new PdfGradientStop(1.5, PdfColor.FromGray(1))]));
        Assert.Throws<ArgumentException>(() => document.CreateGradientFunction([black, new PdfGradientStop(double.NaN, PdfColor.FromGray(1))]));
        Assert.Throws<ArgumentException>(() => document.CreateGradientFunction([black, new PdfGradientStop(1, PdfColor.FromRgb(1, 1, 1))]));
        Assert.Throws<ArgumentException>(() => document.CreateAxialShading(new PathPoint(5, 5), new PathPoint(5, 5), [black, white]));
        Assert.Throws<ArgumentException>(() => document.CreateRadialShading(new PathPoint(0, 0), -1, new PathPoint(5, 5), 1, [black, white]));
    }

    [Fact]
    public void A_shading_pattern_fills_and_strokes_and_re_reads_with_its_matrix_and_shading()
    {
        using PdfDocument document = PdfDocument.Create();
        var matrix = new Matrix(1, 0, 0, 1, 10, 20);
        PdfShadingPattern pattern = document.CreateShadingPattern(TwoStopAxial(document), matrix);
        using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
        {
            canvas.SetFillPattern(pattern);
            canvas.Rectangle(0, 0, 100, 100);
            canvas.Fill();
            canvas.SetStrokePattern(pattern);
            canvas.Rectangle(0, 0, 100, 100);
            canvas.Stroke();
        }

        ShadingRecorder recorder = Replay(document);

        Assert.Equal(2, recorder.Patterns.Count);
        PdfShadingPattern read = Assert.IsType<PdfShadingPattern>(recorder.Patterns[0]);
        Assert.Equal(matrix, read.Matrix);
        Assert.IsType<PdfAxialShading>(read.Shading);
    }

    [Fact]
    public void A_coloured_tiling_pattern_fills_with_the_cell_its_canvas_draws()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfTilingPattern pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20, PdfTilingPaintType.Colored);
        using (PdfCanvas cell = pattern.CreateCanvas())
        {
            cell.SetFillColor(PdfColor.FromRgb(1, 0, 0));
            cell.Rectangle(0, 0, 10, 10);
            cell.Fill();
            Assert.Throws<InvalidOperationException>(() => cell.SetFillPattern(pattern));
        }

        FillWith(document, canvas => canvas.SetFillPattern(pattern));

        ShadingRecorder recorder = Replay(document);

        PdfTilingPattern read = Assert.IsType<PdfTilingPattern>(Assert.Single(recorder.Patterns));
        Assert.Equal(PdfTilingPaintType.Colored, read.PaintType);
        Assert.Equal(PdfTilingType.ConstantSpacing, read.TilingType);
        Assert.Equal(20, read.XStep);
        Assert.Equal(new PdfRectangle(0, 0, 20, 20), read.BoundingBox);
        Assert.Equal(1, Assert.Single(recorder.Cells).Counts["PaintPath"]);
    }

    [Fact]
    public void An_uncoloured_tiling_pattern_fills_in_the_colour_given_with_it()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfTilingPattern pattern = document.CreateTilingPattern(
            new PdfRectangle(0, 0, 20, 20), 25, 25, PdfTilingPaintType.Uncolored, new Matrix(2, 0, 0, 2, 0, 0), PdfTilingType.NoDistortion);
        using (PdfCanvas cell = pattern.CreateCanvas())
        {
            cell.Rectangle(0, 0, 10, 10);
            cell.Fill();
        }

        FillWith(document, canvas => canvas.SetFillPattern(pattern, PdfColor.FromRgb(0, 1, 0)));

        ShadingRecorder recorder = Replay(document);

        PdfTilingPattern read = Assert.IsType<PdfTilingPattern>(Assert.Single(recorder.Patterns));
        Assert.Equal(PdfTilingPaintType.Uncolored, read.PaintType);
        Assert.Equal(PdfTilingType.NoDistortion, read.TilingType);
        Assert.Equal(new Matrix(2, 0, 0, 2, 0, 0), read.Matrix);
        Assert.Equal([0f, 1f, 0f], Assert.Single(recorder.PatternColors).Components.ToArray());
        Assert.Equal(1, Assert.Single(recorder.Cells).Counts["PaintPath"]);
    }

    [Fact]
    public void An_uncoloured_cell_refuses_colours_patterns_shadings_intents_and_images()
    {
        using PdfDocument document = PdfDocument.Create();
        PdfTilingPattern pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 10, PdfTilingPaintType.Uncolored);
        PdfTilingPattern colored = document.CreateTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 10, PdfTilingPaintType.Colored);
        PdfAxialShading shading = TwoStopAxial(document);
        PdfImage image = document.CreateImage([255], new PdfImageLayout(1, 1, PdfDeviceGrayColorSpace.Instance), PdfImageCompression.Flate);

        using (PdfCanvas cell = pattern.CreateCanvas())
        {
            Assert.Throws<InvalidOperationException>(() => cell.SetFillColor(PdfColor.FromGray(0)));
            Assert.Throws<InvalidOperationException>(() => cell.SetStrokeColor(PdfColor.FromGray(0)));
            Assert.Throws<InvalidOperationException>(() => cell.SetFillPattern(colored));
            Assert.Throws<InvalidOperationException>(() => cell.PaintShading(shading));
            Assert.Throws<InvalidOperationException>(() => cell.SetRenderingIntent(RenderingIntent.Perceptual));
            Assert.Throws<InvalidOperationException>(() => cell.DrawImage(image));
        }

        using PdfCanvas page = document.Pages[0].CreateCanvas();
        Assert.Throws<ArgumentException>(() => page.SetFillPattern(pattern));
        Assert.Throws<ArgumentException>(() => page.SetFillPattern(colored, PdfColor.FromGray(0)));
        using PdfDocument other = PdfDocument.Create();
        Assert.Throws<ArgumentException>(() => page.PaintShading(TwoStopAxial(other)));
    }

    [Theory]
    [InlineData("shading", "1.3", "a shading (sh, Shading resources, shading patterns)")]
    [InlineData("shading pattern", "1.3", "a shading (sh, Shading resources, shading patterns)")]
    [InlineData("tiling pattern", "1.2", "a Pattern colour space or tiling pattern")]
    public void Shadings_and_patterns_raise_an_older_document_to_their_version(string feature, string version, string named)
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.0"));
        WriteFeature(document, feature);

        byte[] saved = CanvasTests.Save(document, new PdfSaveOptions().WithUpdateModificationDate(false));

        Assert.Equal("%PDF-" + version, System.Text.Encoding.ASCII.GetString(saved, 0, 8));
        Assert.Contains(named, Assert.Single(document.Diagnostics, diagnostic => diagnostic.Code == "VersionRaised").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_refuses_to_save_a_shading_in_a_pdf_1_2_document()
    {
        using PdfDocument document = PdfDocument.Open(Document.TestPdf.OnePage("/MediaBox [0 0 612 792]", header: "%PDF-1.2"), new PdfOptions().UseStrict());
        WriteFeature(document, "shading");

        using var output = new MemoryStream();
        Broadside.Diagnostics.DiagnosticException exception = Assert.Throws<Broadside.Diagnostics.DiagnosticException>(() => document.Save(output));

        Assert.Equal("VersionRaised", exception.Diagnostic.Code);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void A_page_with_every_shading_and_pattern_passes_qpdf_check_and_renders_in_poppler_without_warnings()
    {
        byte[] saved;
        using (PdfDocument document = PdfDocument.Create())
        {
            foreach (string feature in (string[])["shading", "shading pattern", "tiling pattern", "uncoloured pattern", "meshes"])
            {
                WriteFeature(document, feature);
            }

            saved = CanvasTests.Saved(document);
        }

        (int exitCode, string output) = Writing.ExternalTool.Run("qpdf", saved, "--check");
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain("WARNING", output, StringComparison.OrdinalIgnoreCase);
        (int renderCode, byte[] image, string error) = Writing.ExternalTool.RunBinary("pdftoppm", saved, "-r", "36", "-f", "1", "-l", "1");
        Assert.True(renderCode == 0, error);
        Assert.Equal(string.Empty, error.Trim());
        Assert.NotEmpty(image);
        using PdfDocument reopened = PdfDocument.Open(saved, new PdfOptions().UseStrict());
        reopened.Pages[0].ProcessContent(new ShadingRecorder());
        Assert.Empty(reopened.Diagnostics);
    }

    private static void WriteFeature(PdfDocument document, string feature)
    {
        switch (feature)
        {
            case "shading":
                Paint(document, TwoStopAxial(document));
                break;
            case "shading pattern":
                PdfShadingPattern shadingPattern = document.CreateShadingPattern(TwoStopAxial(document));
                FillWith(document, canvas => canvas.SetFillPattern(shadingPattern));
                break;
            case "uncoloured pattern":
                PdfTilingPattern stencil = document.CreateTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 10, PdfTilingPaintType.Uncolored);
                using (PdfCanvas cell = stencil.CreateCanvas())
                {
                    cell.Rectangle(0, 0, 5, 5);
                    cell.Fill();
                }

                FillWith(document, canvas => canvas.SetFillPattern(stencil, PdfColor.FromRgb(0, 0, 1)));
                break;
            case "meshes":
                Paint(
                    document,
                    document.CreateMeshShading(new PdfFreeFormMeshBuilder(PdfDeviceGrayColorSpace.Instance)
                        .AddTriangle(new PdfMeshVertex(0, 0, PdfColor.FromGray(0)), new PdfMeshVertex(50, 0, PdfColor.FromGray(1)), new PdfMeshVertex(0, 50, PdfColor.FromGray(0.5f)))),
                    document.CreateMeshShading(new PdfPatchMeshBuilder(PdfDeviceGrayColorSpace.Instance, true)
                        .AddPatch(PatchPoints(true), [PdfColor.FromGray(0), PdfColor.FromGray(1), PdfColor.FromGray(0), PdfColor.FromGray(1)])));
                break;
            default:
                PdfTilingPattern tiling = document.CreateTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 10, PdfTilingPaintType.Colored);
                using (PdfCanvas cell = tiling.CreateCanvas())
                {
                    cell.SetFillColor(PdfColor.FromGray(0));
                    cell.Rectangle(0, 0, 5, 5);
                    cell.Fill();
                }

                FillWith(document, canvas => canvas.SetFillPattern(tiling));
                break;
        }
    }

    private static PdfAxialShading TwoStopAxial(PdfDocument document) => document.CreateAxialShading(
        new PathPoint(0, 0), new PathPoint(100, 0), [new PdfGradientStop(0, PdfColor.FromGray(0)), new PdfGradientStop(1, PdfColor.FromGray(1))]);

    private static void Paint(PdfDocument document, params PdfShading[] shadings)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        foreach (PdfShading shading in shadings)
        {
            canvas.PaintShading(shading);
        }
    }

    private static void FillWith(PdfDocument document, Action<PdfCanvas> select)
    {
        using PdfCanvas canvas = document.Pages[0].CreateCanvas();
        select(canvas);
        canvas.Rectangle(0, 0, 100, 100);
        canvas.Fill();
    }

    /// <summary>A patch over 0 to 100: 12 Coons boundary points in data-stream order, or those and 4 inner tensor-product points.</summary>
    private static PathPoint[] PatchPoints(bool tensor)
    {
        PathPoint[] boundary =
        [
            new(0, 0), new(0, 33), new(0, 67), new(0, 100), new(33, 100), new(67, 100),
            new(100, 100), new(100, 67), new(100, 33), new(100, 0), new(67, 0), new(33, 0),
        ];
        return tensor ? [.. boundary, new(33, 33), new(33, 67), new(67, 67), new(67, 33)] : boundary;
    }

    private static void AssertNear(PathPoint expected, PathPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01);
        Assert.Equal(expected.Y, actual.Y, 0.01);
    }

    private static float[] Round(ReadOnlySpan<float> values) => [.. values.ToArray().Select(value => MathF.Round(value, 3))];

    /// <summary>Saves, reopens and records the shadings painted on the first page and the pattern colours it fills with.</summary>
    internal static ShadingRecorder Replay(PdfDocument document, int page = 0)
    {
        using PdfDocument reopened = PdfDocument.Open(CanvasTests.Saved(document));
        var recorder = new ShadingRecorder();
        reopened.Pages[page].ProcessContent(recorder);
        Assert.Empty(reopened.Diagnostics);
        return recorder;
    }

    internal static float[] Evaluate(PdfShading shading, params float[] input)
    {
        float[] color = new float[shading.ColorComponentCount];
        shading.EvaluateFunction(input, color);
        return [.. color.Select(value => MathF.Round(value, 4))];
    }

    /// <summary>Records the shading models painted with <c>sh</c>, and the pattern of every pattern-coloured fill or stroke.</summary>
    internal sealed class ShadingRecorder : ContentProcessor
    {
        public List<PdfShading> Shadings { get; } = [];

        public List<Matrix> ShadingMatrices { get; } = [];

        public List<PdfPattern> Patterns { get; } = [];

        public List<PdfColor> PatternColors { get; } = [];

        public List<RecordingProcessor> Cells { get; } = [];

        public override ContentEvents Events => ContentEvents.All & ~ContentEvents.Operators;

        public override void PaintShading(in ShadingEvent shading, ContentContext context)
        {
            Shadings.Add(shading.Model!);
            ShadingMatrices.Add(shading.Ctm);
        }

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            if (path.Paint is PathPaint.Fill or PathPaint.FillAndStroke)
            {
                Record(context.State.FillColor, context);
            }

            if (path.Paint is PathPaint.Stroke or PathPaint.FillAndStroke)
            {
                Record(context.State.StrokeColor, context);
            }
        }

        private void Record(in PdfColor color, ContentContext context)
        {
            if (context.GetPattern(color) is not { } pattern)
            {
                return;
            }

            Patterns.Add(pattern);
            PatternColors.Add(color);
            if (pattern is PdfTilingPattern)
            {
                var cell = new RecordingProcessor(ContentEvents.All & ~ContentEvents.Operators);
                context.RunPatternCell(color, cell);
                Cells.Add(cell);
            }
        }
    }
}
