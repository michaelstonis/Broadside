using Broadside.Graphics;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.ShadingTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// The seven shading corpus files (tests/Corpus/README.md), one per type, each painted with <c>sh</c> inside a clip: the event
/// reports the resolved model, and the model holds what the generator wrote (ISO 32000-2 §8.7.4.5).
/// </summary>
public sealed class CorpusShadingTests
{
    [Fact]
    public void Sh_in_shading_type1_function_pdf_reports_a_function_based_shading_evaluated_by_its_sampled_function()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type1-function.pdf"));

        SeenShading seen = Assert.Single(Shadings(document));
        PdfFunctionShading shading = Assert.IsType<PdfFunctionShading>(seen.Model);

        Assert.Equal("Sh0", seen.Name);
        Assert.Equal(Matrix.Identity, seen.Ctm);
        Assert.True(shading.IsValid);
        Assert.Equal(PdfShadingType.FunctionBased, shading.ShadingType);
        Assert.Equal(PdfColorSpaceFamily.DeviceRgb, shading.ColorSpace.Family);
        Assert.Equal([0.0, 1, 0, 1], shading.Domain);
        Assert.Equal(new Matrix(200, 0, 0, 200, 100, 400), shading.Matrix);
        Assert.Equal(PdfFunctionType.Sampled, Assert.Single(shading.Functions).FunctionType);
        NearAll([1, 0, 0], shading.Color(0, 0));
        NearAll([1, 1, 1], shading.Color(1, 1));
        NearAll([0.5f, 0.5f, 0.5f], shading.Color(0.5f, 0.5f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sh_in_shading_type2_axial_pdf_reports_an_axial_shading_extended_at_its_start_only()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type2-axial.pdf"));

        PdfAxialShading shading = OnlyShading<PdfAxialShading>(document);

        Assert.Equal(new PathPoint(72, 400), shading.Start);
        Assert.Equal(new PathPoint(540, 400), shading.End);
        Assert.Equal([0.0, 1], shading.Domain);
        Assert.True(shading.ExtendStart);
        Assert.False(shading.ExtendEnd);
        Assert.True(shading.TryGetParameter(306, 123, out double middle));
        Assert.Equal(0.5, middle, 12);
        Assert.True(shading.TryGetParameter(10, 400, out double before));
        Assert.Equal(0, before);
        Assert.False(shading.TryGetParameter(600, 400, out _));
        NearAll([0.5f, 0, 0.5f], shading.Color(0.5f));
        Assert.Null(shading.Background);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sh_in_shading_type3_radial_pdf_reports_a_cone_whose_points_take_the_greatest_s()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type3-radial.pdf"));

        PdfRadialShading shading = OnlyShading<PdfRadialShading>(document);

        Assert.Equal(new PathPoint(200, 400), shading.StartCenter);
        Assert.Equal(20, shading.StartRadius);
        Assert.Equal(new PathPoint(400, 420), shading.EndCenter);
        Assert.Equal(100, shading.EndRadius);
        Assert.True(shading.ExtendStart && shading.ExtendEnd);

        // (300, 410) lies on the blend circles s = 0.2865 and s = 0.9959 (hand-solved); the later one wins.
        Assert.True(shading.TryGetParameter(300, 410, out double t));
        Assert.Equal(0.9958779630983843, t, 12);

        // (190, 400): s = 0.0829 within the shading, s = -0.1064 in the extension; again the greater.
        Assert.True(shading.TryGetParameter(190, 400, out t));
        Assert.Equal(0.08290280552606519, t, 12);

        // (400, 400): s = 1.8 in the extended end, which clamps to t1.
        Assert.True(shading.TryGetParameter(400, 400, out t));
        Assert.Equal(1, t);

        // (100, 400): both circles through it have a negative radius, beyond where the start's extension shrinks to a point.
        Assert.False(shading.TryGetParameter(100, 400, out _));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sh_in_shading_type4_freeform_pdf_decodes_five_padded_vertices_into_three_triangles()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type4-freeform.pdf"));

        PdfTriangleMeshShading shading = OnlyShading<PdfTriangleMeshShading>(document);

        Assert.Equal(PdfShadingType.FreeFormTriangleMesh, shading.ShadingType);
        Assert.Equal((12, 4, 2), (shading.BitsPerCoordinate, shading.BitsPerComponent, shading.BitsPerFlag));
        Assert.Equal(3, shading.ColorStride);
        Assert.Equal(
            [new PathPoint(100, 100), new PathPoint(300, 100), new PathPoint(200, 300), new PathPoint(400, 300), new PathPoint(300, 500)],
            shading.Vertices.ToArray());
        Assert.Equal([1f, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 0, 1, 0], shading.VertexColors.ToArray());
        Assert.Equal([0, 1, 2, 1, 2, 3, 1, 3, 4], shading.Triangles.ToArray());
        Assert.Equal(3, shading.TriangleCount);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sh_in_shading_type5_lattice_pdf_decodes_a_three_by_three_lattice_with_t_padded_to_32_bits()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type5-lattice.pdf"));

        PdfTriangleMeshShading shading = OnlyShading<PdfTriangleMeshShading>(document);

        Assert.Equal(PdfShadingType.LatticeFormTriangleMesh, shading.ShadingType);
        Assert.Equal(3, shading.VerticesPerRow);
        Assert.Equal(0, shading.BitsPerFlag);
        Assert.Equal(1, shading.ColorStride);
        Assert.Equal(9, shading.VertexCount);
        Assert.Equal(new PathPoint(250, 250), shading.Vertices[4]);
        Assert.Equal(new PathPoint(400, 400), shading.Vertices[8]);

        // t = (column + row) / 4 written in 4 bits: raw 0, 4, 8, 4, 8, 11, 8, 11, 15, each read back as raw / 15.
        NearAll([0, 4 / 15f, 8 / 15f, 4 / 15f, 8 / 15f, 11 / 15f, 8 / 15f, 11 / 15f, 1], shading.VertexColors.ToArray());
        Assert.Equal(
            [0, 1, 3, 1, 3, 4, 1, 2, 4, 2, 4, 5, 3, 4, 6, 4, 6, 7, 4, 5, 7, 5, 7, 8],
            shading.Triangles.ToArray());
        NearAll([0.5f, 0, 0.5f], shading.Color(0.5f));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sh_in_shading_type6_coons_pdf_decodes_four_patches_sharing_edges_through_flags_0_to_3()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type6-coons.pdf"));

        PdfPatchMeshShading shading = OnlyShading<PdfPatchMeshShading>(document);

        Assert.True(shading.IsCoons);
        Assert.Equal(4, shading.PatchCount);
        AssertPatchCorners(shading, 0, (100, 100), (100, 250), (250, 250), (250, 100));
        AssertPatchCorners(shading, 1, (100, 250), (250, 250), (250, 400), (100, 400));
        AssertPatchCorners(shading, 2, (250, 400), (100, 400), (100, 550), (250, 550));
        AssertPatchCorners(shading, 3, (250, 550), (250, 400), (400, 400), (400, 550));

        // The interior points of a straight-edged square Coons patch are its thirds (§8.7.4.5.8 equations).
        AssertPoint((150, 150), shading.GetControlPoint(0, 1, 1));
        AssertPoint((200, 150), shading.GetControlPoint(0, 2, 1));
        AssertPoint((150, 200), shading.GetControlPoint(0, 1, 2));
        AssertPoint((200, 200), shading.GetControlPoint(0, 2, 2));

        // Corner colours (c00 c03 c33 c30): implicit ones repeat the previous patch's shared corners (Table 84).
        float[] r = [1, 0, 0], g = [0, 1, 0], b = [0, 0, 1], k = [0, 0, 0];
        Assert.Equal([.. r, .. g, .. b, .. k, .. g, .. b, .. r, .. g, .. r, .. g, .. b, .. r, .. r, .. r, .. g, .. b], shading.CornerColors.ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Sh_in_shading_type7_tensor_pdf_decodes_sixteen_points_per_patch_in_stream_order()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type7-tensor.pdf"));

        PdfPatchMeshShading shading = OnlyShading<PdfPatchMeshShading>(document);

        Assert.False(shading.IsCoons);
        Assert.Equal(PdfColorSpaceFamily.DeviceCmyk, shading.ColorSpace.Family);
        Assert.Equal(4, shading.PatchCount);
        AssertPatchCorners(shading, 3, (250, 550), (250, 400), (400, 400), (400, 550));
        AssertPoint((300, 500), shading.GetControlPoint(3, 1, 1));
        AssertPoint((350, 450), shading.GetControlPoint(3, 2, 2));
        float[] c = [1, 0, 0, 0], m = [0, 1, 0, 0], y = [0, 0, 1, 0];
        Assert.Equal([.. c, .. c, .. m, .. y], shading.CornerColors.Slice(3 * 16, 16).ToArray());
        Assert.Empty(document.Diagnostics);
    }

    private static void AssertPatchCorners(PdfPatchMeshShading shading, int patch, (double, double) p00, (double, double) p03, (double, double) p33, (double, double) p30)
    {
        AssertPoint(p00, shading.GetControlPoint(patch, 0, 0));
        AssertPoint(p03, shading.GetControlPoint(patch, 0, 3));
        AssertPoint(p33, shading.GetControlPoint(patch, 3, 3));
        AssertPoint(p30, shading.GetControlPoint(patch, 3, 0));
    }

    private static void AssertPoint((double X, double Y) expected, PathPoint actual)
    {
        // Coordinates were quantized to 24 or 32 bits over 0..1000.
        Assert.True(Math.Abs(expected.X - actual.X) < 1e-4 && Math.Abs(expected.Y - actual.Y) < 1e-4, $"Expected {expected}, got {actual}.");
    }
}
