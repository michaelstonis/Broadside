using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using static Broadside.Tests.Graphics.FunctionTesting;

namespace Broadside.Tests.Graphics;

/// <summary>
/// Mesh decoding allocates its output arrays once per mesh and nothing per vertex or patch (CLAUDE.md "Code conventions"; ISO
/// 32000-2 §8.7.4.5.5 to §8.7.4.5.8). The build-breaking half of <c>MeshShadingBenchmarks</c>. Decoding happens once per shading,
/// on the first geometry access, so each measured call (through <see cref="Allocations.Measure"/>) reads a new shading over the same
/// data: it allocates the shading model, its output arrays and a constant, and a per-vertex allocation over 10,000 vertices would
/// show at once. Evaluating a shading's function allocates nothing.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public class MeshAllocationTests
{
    private const int Vertices = 10_000;
    private const int Patches = 1_000;

    /// <summary>The most a shading's model, layout, stream wrapper and cache entry may take besides the output arrays.</summary>
    private const long ModelBytes = 4096;

    [Fact]
    public void Decoding_a_free_form_mesh_allocates_only_its_output_arrays()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        var dictionary = (CosDictionary)Cos($"<< {MeshSamples.FreeFormEntries} >>");
        byte[] data = MeshSamples.FreeForm(Vertices);
        int triangles = 0;

        long allocated = Allocations.Measure(() => triangles = ((PdfTriangleMeshShading)document.GetShading(new CosStream(dictionary, data))!).TriangleCount);

        Assert.Equal(Vertices - 2, triangles);
        long arrays = (Vertices * 16L) + (Vertices * 3L * 4) + ((Vertices - 2) * 3L * 4);
        Assert.InRange(allocated, arrays, arrays + ModelBytes);
    }

    [Fact]
    public void Decoding_a_tensor_mesh_allocates_only_its_output_arrays()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(string.Empty));
        var dictionary = (CosDictionary)Cos($"<< {MeshSamples.TensorEntries} >>");
        byte[] data = MeshSamples.Tensor(Patches);
        int patches = 0;

        long allocated = Allocations.Measure(() => patches = ((PdfPatchMeshShading)document.GetShading(new CosStream(dictionary, data))!).PatchCount);

        Assert.Equal(Patches, patches);

        // Sized for the smallest patches (flags 1 to 3): at most 16/12 of the patches fit, so the arrays hold up to 1,333 slots.
        long slots = (Patches * 4L / 3) + 1;
        long arrays = (slots * 16 * 16) + (slots * 4 * 4 * 4);
        Assert.InRange(allocated, Patches * 320L, arrays + ModelBytes);
    }

    [Fact]
    public void Evaluating_a_shading_function_allocates_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("shading-type2-axial.pdf"));
        PdfAxialShading shading = ShadingTesting.OnlyShading<PdfAxialShading>(document);
        float[] input = new float[1];
        float[] color = new float[3];
        long allocated = Allocations.Measure(
            () =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    input[0] = i / 1000f;
                    shading.EvaluateFunction(input, color);
                    _ = shading.TryGetParameter(i, 400, out _);
                }
            },
            warmUpCalls: 1);

        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("shading-type2-axial.pdf")]
    [InlineData("pattern-shading-axial.pdf")]
    [InlineData("pattern-tiling-colored.pdf")]
    public void Interpreting_sh_pattern_fills_and_cells_allocates_nothing_once_the_models_are_cached(string file)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(file));
        var processor = new PatternReader();
        long allocated = Allocations.Measure(() => document.Pages[0].ProcessContent(processor), warmUpCalls: 64);

        Assert.Equal(0, allocated);
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
}
