using Broadside.Graphics;
using Broadside.Tests.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Mesh decoding allocates its output arrays once per mesh and nothing per vertex or patch (CLAUDE.md "Code conventions"; ISO
/// 32000-2 §8.7.4.5.5 to §8.7.4.5.8). The build-breaking half of <c>MeshShadingBenchmarks</c>: the bytes a first geometry access
/// allocates are the arrays plus a constant, so a per-vertex allocation over 10,000 vertices would show at once. Evaluating a
/// shading's function allocates nothing.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class MeshAllocationTests
{
    private const int Vertices = 10_000;
    private const int Patches = 1_000;

    [Fact]
    public void Decoding_a_free_form_mesh_allocates_only_its_output_arrays()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        Warm(document);
        var shading = (PdfTriangleMeshShading)document.GetShading(Stream($"<< {MeshSamples.FreeFormEntries} >>", MeshSamples.FreeForm(Vertices)))!;

        long before = GC.GetAllocatedBytesForCurrentThread();
        int triangles = shading.TriangleCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Vertices - 2, triangles);
        long arrays = (Vertices * 16L) + (Vertices * 3L * 4) + ((Vertices - 2) * 3L * 4);
        Assert.InRange(allocated, arrays, arrays + 1024);
    }

    [Fact]
    public void Decoding_a_tensor_mesh_allocates_only_its_output_arrays()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        Warm(document);
        var shading = (PdfPatchMeshShading)document.GetShading(Stream($"<< {MeshSamples.TensorEntries} >>", MeshSamples.Tensor(Patches)))!;

        long before = GC.GetAllocatedBytesForCurrentThread();
        int patches = shading.PatchCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Patches, patches);

        // Sized for the smallest patches (flags 1 to 3): at most 16/12 of the patches fit, so the arrays hold up to 1,333 slots.
        long slots = (Patches * 4L / 3) + 1;
        long arrays = (slots * 16 * 16) + (slots * 4 * 4 * 4);
        Assert.InRange(allocated, Patches * 320L, arrays + 1024);
    }

    [Fact]
    public void Evaluating_a_shading_function_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type2-axial.pdf"));
        PdfAxialShading shading = ShadingTesting.OnlyShading<PdfAxialShading>(document);
        float[] input = new float[1];
        float[] color = new float[3];
        for (int i = 0; i < 64; i++)
        {
            shading.EvaluateFunction(input, color);
            _ = shading.TryGetParameter(i, 400, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            input[0] = i / 1000f;
            shading.EvaluateFunction(input, color);
            _ = shading.TryGetParameter(i, 400, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Theory]
    [InlineData("shading-type2-axial.pdf")]
    [InlineData("pattern-shading-axial.pdf")]
    [InlineData("pattern-tiling-colored.pdf")]
    public void Interpreting_sh_pattern_fills_and_cells_allocates_nothing_once_the_models_are_cached(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file));
        var processor = new PatternReader();
        for (int i = 0; i < 64; i++)
        {
            document.Pages[0].ProcessContent(processor);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        document.Pages[0].ProcessContent(processor);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(processor.Seen > 0);
    }

    private sealed class PatternReader : Broadside.Content.ContentProcessor
    {
        public int Seen { get; private set; }

        public override void PaintShading(in Broadside.Content.ShadingEvent shading, Broadside.Content.ContentContext context) =>
            Seen += shading.Model is { IsValid: true } ? 1 : 0;

        public override void PaintPath(in Broadside.Content.PathEvent path, Broadside.Content.ContentContext context)
        {
            switch (context.GetPattern(context.State.FillColor))
            {
                case PdfShadingPattern:
                    Seen++;
                    break;
                case PdfTilingPattern:
                    Seen += context.RunPatternCell(context.State.FillColor, this) ? 1 : 0;
                    break;
            }
        }
    }

    private static void Warm(PdfDocument document)
    {
        for (int i = 0; i < 3; i++)
        {
            var warm = (PdfTriangleMeshShading)document.GetShading(Stream($"<< {MeshSamples.FreeFormEntries} >>", MeshSamples.FreeForm(8)))!;
            _ = warm.TriangleCount;
            var tensor = (PdfPatchMeshShading)document.GetShading(Stream($"<< {MeshSamples.TensorEntries} >>", MeshSamples.Tensor(4)))!;
            _ = tensor.PatchCount;
        }
    }
}
