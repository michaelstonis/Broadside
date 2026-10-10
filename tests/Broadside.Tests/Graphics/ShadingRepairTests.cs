using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;
using static Broadside.Tests.Graphics.ShadingTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Malformed shadings and patterns (ISO 32000-2 §8.7): each repair keeps what can be used and records one diagnostic; what cannot be
/// used is marked invalid (it paints nothing); strict mode throws the first deviation.
/// </summary>
public sealed class ShadingRepairTests
{
    private const string Red = "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";

    [Fact]
    public void Truncated_mesh_data_keeps_the_complete_triangles_before_the_cut_with_one_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-mesh-truncated.pdf"));

        PdfTriangleMeshShading shading = OnlyShading<PdfTriangleMeshShading>(document);

        Assert.Equal(4, shading.VertexCount);
        Assert.Equal([0, 1, 2, 1, 2, 3], shading.Triangles.ToArray());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(("MeshDataTruncated", DiagnosticSeverity.Warning, new CosReference(5, 0)), (diagnostic.Code, diagnostic.Severity, diagnostic.ObjectReference));
    }

    [Fact]
    public void Truncated_mesh_data_throws_in_strict_mode_when_the_geometry_is_read()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-mesh-truncated.pdf"), new PdfOptions().UseStrict());
        PdfTriangleMeshShading shading = OnlyShading<PdfTriangleMeshShading>(document);

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => shading.VertexCount);

        Assert.Equal("MeshDataTruncated", error.Diagnostic.Code);
    }

    [Fact]
    public void A_shading_with_CS_instead_of_ColorSpace_is_read_with_a_diagnostic()
    {
        (PdfShading? shading, string[] codes) = Read($"<< /ShadingType 2 /CS /DeviceRGB /Coords [0 0 1 0] {Red} >>");

        Assert.True(shading!.IsValid);
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, shading.ColorSpace.Family);
        Assert.Equal(["ShadingColorSpaceAbbreviated"], codes);
    }

    [Theory]
    [InlineData("<< /ShadingType 2 /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>", "ShadingColorSpaceInvalid")]
    [InlineData("<< /ShadingType 2 /ColorSpace /Pattern /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>", "ShadingColorSpaceInvalid")]
    [InlineData("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] >>", "ShadingFunctionInvalid")]
    [InlineData("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0] /C1 [1 1] /N 1 >> >>", "ShadingFunctionInvalid")]
    [InlineData("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >> >>", "ShadingCoordsInvalid")]
    [InlineData("<< /ShadingType 3 /ColorSpace /DeviceGray /Coords [0 0 1 0 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>", "ShadingCoordsInvalid")]
    [InlineData("<< /ShadingType 4 /ColorSpace /DeviceGray /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 1 0 1 0 1] >>", "ShadingNotStream")]
    public void A_shading_that_cannot_be_painted_is_invalid_with_a_diagnostic(string syntax, string code)
    {
        (PdfShading? shading, string[] codes) = Read(syntax);

        Assert.False(shading!.IsValid);
        Assert.Contains(code, codes);
    }

    [Fact]
    public void An_Indexed_space_is_invalid_with_a_function_and_in_types_1_to_3()
    {
        (PdfShading? axial, string[] codes) = Read($"<< /ShadingType 2 /ColorSpace [/Indexed /DeviceRGB 1 <FF000000FF00>] /Coords [0 0 1 0] {Red} >>");

        Assert.False(axial!.IsValid);
        Assert.Contains("ShadingColorSpaceInvalid", codes);
    }

    [Fact]
    public void Indexed_mesh_colours_are_rounded_half_up_and_converted_to_the_base_space_when_decoded()
    {
        // Indices 255/255 = 1, 128/255 = 0.502 -> 1, 127/255 = 0.498 -> 0 (§8.6.6.3); the base is DeviceRGB.
        byte[] data = [0, 0, 0, 255, 0, 10, 0, 128, 0, 0, 10, 127];
        (PdfShading? shading, string[] codes) = Read(
            "<< /ShadingType 4 /ColorSpace [/Indexed /DeviceRGB 1 <FF00000000FF>] /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1] >>",
            data);
        var mesh = Assert.IsType<PdfTriangleMeshShading>(shading);

        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, mesh.InterpolationColorSpace.Family);
        Assert.Equal(3, mesh.ColorStride);
        Assert.Equal([0f, 0, 1, 0, 0, 1, 1, 0, 0], mesh.VertexColors.ToArray());
        Assert.Empty(codes);
    }

    [Fact]
    public void Repairs_keep_the_shading_usable()
    {
        Assert.Equal(["ShadingExtendInvalid"], Read($"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] {Red} /Extend [true] >>").Codes);
        Assert.Equal(["ShadingBackgroundInvalid"], Read($"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] {Red} /Background [1] >>").Codes);
        Assert.Equal(["ShadingEntryInvalid"], Read($"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] {Red} /BBox [0 0 1] >>").Codes);

        (PdfShading? radial, string[] codes) = Read($"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [0 0 -5 10 0 5] {Red} >>");
        Assert.Equal(0, Assert.IsType<PdfRadialShading>(radial).StartRadius);
        Assert.True(radial!.IsValid);
        Assert.Equal(["ShadingCoordsInvalid"], codes);
    }

    [Fact]
    public void A_field_width_outside_the_table_is_read_as_given_and_one_outside_1_to_32_is_invalid()
    {
        // BitsPerCoordinate 10: vertices (1, 1), (2, 1), (1, 2) of 2 + 10 + 10 + 8 = 30 bits, each padded to 4 bytes.
        byte[] data = [0x00, 0x10, 0x07, 0xFC, 0x00, 0x20, 0x07, 0xFC, 0x00, 0x10, 0x0B, 0xFC];
        (PdfShading? odd, string[] codes) = Read(
            "<< /ShadingType 4 /ColorSpace /DeviceGray /BitsPerCoordinate 10 /BitsPerComponent 8 /BitsPerFlag 2 /Decode [0 1023 0 1023 0 1] >>",
            data);
        var mesh = Assert.IsType<PdfTriangleMeshShading>(odd);
        Assert.True(mesh.IsValid);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(new PathPoint(1, 1), mesh.Vertices[0]);
        Assert.Equal(["ShadingBitsInvalid"], codes);

        (PdfShading? wide, codes) = Read(
            "<< /ShadingType 4 /ColorSpace /DeviceGray /BitsPerCoordinate 33 /BitsPerComponent 8 /BitsPerFlag 2 /Decode [0 1 0 1 0 1] >>", data);
        Assert.False(wide!.IsValid);
        Assert.Equal(0, ((PdfTriangleMeshShading)wide).VertexCount);
        Assert.Equal(["ShadingBitsInvalid"], codes);

        (PdfShading? decode, codes) = Read(
            "<< /ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 1 0 1 0 1] >>", data);
        Assert.False(decode!.IsValid);
        Assert.Equal(["ShadingDecodeInvalid"], codes);
    }

    [Fact]
    public void Bad_edge_flags_skip_their_vertices_or_patches_with_a_diagnostic()
    {
        // Type 4, 8-bit fields, gray: flag 1 with no triangle, then a triangle, then flag 3: the first and last are skipped.
        byte[] freeForm = [1, 9, 9, 9, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 3, 5, 5, 5];
        (PdfShading? shading, string[] codes) = Read(
            "<< /ShadingType 4 /ColorSpace /DeviceGray /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1] >>", freeForm);
        var mesh = Assert.IsType<PdfTriangleMeshShading>(shading);
        Assert.Equal([new PathPoint(0, 0), new PathPoint(1, 0), new PathPoint(0, 1)], mesh.Vertices.ToArray());
        Assert.Equal([0, 1, 2], mesh.Triangles.ToArray());
        Assert.Equal(["MeshEdgeFlagInvalid"], codes);

        // Type 6 whose first patch has flag 1: its 8 points and 2 colours are consumed and dropped.
        byte[] coons = [1, .. new byte[16], 7, 7];
        (shading, codes) = Read(
            "<< /ShadingType 6 /ColorSpace /DeviceGray /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1] >>", coons);
        Assert.Equal(0, Assert.IsType<PdfPatchMeshShading>(shading).PatchCount);
        Assert.Equal(["MeshEdgeFlagInvalid"], codes);
    }

    [Fact]
    public void A_lattice_drops_an_incomplete_last_row()
    {
        // VerticesPerRow 2, five 3-byte vertices: two rows and one vertex over.
        byte[] data = [0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0, 9, 9, 9];
        (PdfShading? shading, string[] codes) = Read(
            "<< /ShadingType 5 /ColorSpace /DeviceGray /BitsPerCoordinate 8 /BitsPerComponent 8 /VerticesPerRow 2 /Decode [0 255 0 255 0 1] >>", data);
        var mesh = Assert.IsType<PdfTriangleMeshShading>(shading);

        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal([0, 1, 2, 1, 2, 3], mesh.Triangles.ToArray());
        Assert.Equal(["MeshLatticeIncomplete"], codes);
    }

    [Fact]
    public void An_object_that_is_not_a_shading_reads_as_null_with_a_diagnostic()
    {
        (PdfShading? shading, string[] codes) = Read("<< /ShadingType 9 /ColorSpace /DeviceGray >>");

        Assert.Null(shading);
        Assert.Equal(["ShadingTypeInvalid"], codes);
    }

    [Fact]
    public void Sh_with_a_name_not_in_the_resources_paints_nothing_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("/Missing sh"));

        Assert.Empty(Shadings(document));
        Assert.Equal(["ContentShadingMissing"], document.Codes());
    }

    [Fact]
    public void Tiling_pattern_repairs_fill_in_steps_bounding_box_and_resources()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));

        var noSteps = Assert.IsType<PdfTilingPattern>(document.GetPattern(Stream("<< /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 8 6] /XStep 0 /Resources << >> >>", [])));
        Assert.Equal((8.0, 6.0), (noSteps.XStep, noSteps.YStep));
        Assert.True(noSteps.IsValid);

        var noBox = Assert.IsType<PdfTilingPattern>(document.GetPattern(Stream("<< /PatternType 1 /PaintType 2 /TilingType 3 /XStep 4 /YStep -5 >>", [])));
        Assert.Equal(new PdfRectangle(0, 0, 4, -5), noBox.BoundingBox);
        Assert.Equal(-5, noBox.YStep);
        Assert.Null(noBox.Resources);

        var nothing = Assert.IsType<PdfTilingPattern>(document.GetPattern(Stream("<< /PatternType 1 /PaintType 1 /TilingType 1 >>", [])));
        Assert.False(nothing.IsValid);

        Assert.Null(document.GetPattern(Cos("<< /PatternType 3 >>")));
        Assert.Equal(
            ["TilingPatternStepInvalid", "TilingPatternBBoxMissing", "PatternResourcesMissing", "PatternTypeInvalid"],
            document.Codes().Distinct());
    }

    [Fact]
    public void Models_are_cached_per_object_and_read_again_after_the_object_changes()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        var dictionary = (CosDictionary)Cos($"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 0] {Red} >>");

        PdfShading first = document.GetShading(dictionary)!;
        Assert.Same(first, document.GetShading(dictionary));

        dictionary[new CosName("Coords")] = Cos("[5 5 6 5]");
        var second = Assert.IsType<PdfAxialShading>(document.GetShading(dictionary));
        Assert.NotSame(first, second);
        Assert.Equal(new PathPoint(5, 5), second.Start);
        Assert.Equal(new PathPoint(0, 0), ((PdfAxialShading)first).Start);
    }

    [Fact]
    public void A_mesh_is_decoded_once_however_many_threads_ask()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type7-tensor.pdf"));
        var shading = (PdfPatchMeshShading)OnlyShading<PdfPatchMeshShading>(document);
        var spans = new System.Collections.Concurrent.ConcurrentBag<PathPoint[]>();

        Parallel.For(0, 32, _ => spans.Add(shading.ControlPoints.ToArray()));

        Assert.All(spans, points => Assert.Equal(spans.First(), points));
        Assert.Empty(document.Diagnostics);
    }

    private static (PdfShading? Shading, string[] Codes) Read(string syntax, byte[]? data = null)
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        CosObject value = data is null ? Cos(syntax) : Stream(syntax, data);
        PdfShading? shading = document.GetShading(value);
        if (shading is PdfTriangleMeshShading triangles)
        {
            _ = triangles.VertexCount;
        }
        else if (shading is PdfPatchMeshShading patches)
        {
            _ = patches.PatchCount;
        }

        return (shading, document.Codes());
    }
}
