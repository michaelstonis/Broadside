using BenchmarkDotNet.Attributes;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Finds a page's kept display list (issue #103), the lookup a viewer makes for every tile it draws, alone and right after another
/// open document changes (issue #578: the other document's change leaves the list current, so the lookup is still a hit). Both must
/// read <c>-</c> in <c>Allocated</c>; <c>Broadside.Tests.Rendering.PerDocumentEpochTests</c> fails the build if the second allocates.
/// </summary>
[MemoryDiagnoser]
public class DisplayListCacheBenchmarks
{
    private static readonly CosName Rotate = new("Rotate");
    private readonly CosInteger _angle = new(90);
    private PdfDocument? _viewed;
    private PdfDocument? _edited;
    private PdfPage? _page;
    private CosDictionary? _other;

    [GlobalSetup]
    public void Setup()
    {
        byte[] file = ContentSamples.OnePage(ContentSamples.PathHeavy(16 * 1024));
        _viewed = PdfDocument.Open(file);
        _edited = PdfDocument.Open(file);
        _page = _viewed.Pages[0];
        _other = _edited.Pages[0].Dictionary;
        _ = _page.CreateDisplayList();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _viewed?.Dispose();
        _edited?.Dispose();
    }

    [Benchmark]
    public DisplayList KeptList() => _page!.CreateDisplayList();

    [Benchmark]
    public DisplayList KeptListAfterAnotherDocumentChanges()
    {
        _other![Rotate] = _angle;
        return _page!.CreateDisplayList();
    }
}
