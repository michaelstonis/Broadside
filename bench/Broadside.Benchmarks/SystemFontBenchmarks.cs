using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.Resolution;

namespace Broadside.Benchmarks;

/// <summary>
/// The operating-system font resolver (ISO 32000-2 §9.6.2.1, §9.8; issue #59). <see cref="BuildIndex"/> scans the platform's font
/// directories (headers only), the one-time cost a process pays on the first font no configured resolver has; a cold path, measured
/// so its size is known. <see cref="ResolveHelvetica"/> is one lookup against a built index (name ladder, family, stand-ins) with
/// the file already loaded.
/// </summary>
[MemoryDiagnoser]
public class SystemFontBenchmarks
{
    private readonly SystemFontResolver _resolver = new();
    private readonly FontQuery _helvetica = new("Helvetica-Bold") { Standard14 = Standard14Font.HelveticaBold };

    [GlobalSetup]
    public void Setup() => _ = _resolver.ResolveFont(_helvetica);

    /// <summary>Scans every font directory of the platform.</summary>
    [Benchmark]
    public int BuildIndex() => SystemFontIndex.Build(_resolver.Directories).Faces.Count;

    /// <summary>Resolves Helvetica-Bold against the built index.</summary>
    [Benchmark]
    public FontResolution? ResolveHelvetica() => _resolver.ResolveFont(_helvetica);
}
