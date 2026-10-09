using BenchmarkDotNet.Attributes;
using Broadside.Graphics.Shadings;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Decodes mesh shading data (ISO 32000-2 §8.7.4.5.5 and §8.7.4.5.8): a 10,000-vertex free-form triangle mesh and a 1,000-patch
/// tensor-product mesh. The decoder allocates its output arrays once per mesh and nothing per vertex or patch, so
/// <c>Allocated</c> is the size of those arrays; <c>Broadside.Tests.Graphics.MeshAllocationTests</c> fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class MeshShadingBenchmarks
{
    private const int Vertices = 10_000;
    private const int Patches = 1_000;

    private readonly MeshLayout _freeForm = new()
    {
        ShadingType = 4,
        BitsPerCoordinate = 16,
        BitsPerComponent = 8,
        BitsPerFlag = 8,
        Decode = [0, 612, 0, 792, 0, 1, 0, 1, 0, 1],
        ValueCount = 3,
    };

    private readonly MeshLayout _tensor = new()
    {
        ShadingType = 7,
        BitsPerCoordinate = 16,
        BitsPerComponent = 8,
        BitsPerFlag = 8,
        Decode = [0, 612, 0, 792, 0, 1, 0, 1, 0, 1, 0, 1],
        ValueCount = 4,
    };

    private byte[] _freeFormData = [];
    private byte[] _tensorData = [];

    [GlobalSetup]
    public void Setup()
    {
        _freeFormData = MeshSamples.FreeForm(Vertices);
        _tensorData = MeshSamples.Tensor(Patches);
    }

    /// <summary>Decodes 10,000 vertices into 9,998 triangles.</summary>
    [Benchmark(OperationsPerInvoke = Vertices)]
    public int DecodeFreeFormMesh() => MeshDecoder.Decode(_freeForm, _freeFormData).TriangleCount;

    /// <summary>Decodes 1,000 tensor-product patches.</summary>
    [Benchmark(OperationsPerInvoke = Patches)]
    public int DecodeTensorMesh() => MeshDecoder.Decode(_tensor, _tensorData).PatchCount;
}
