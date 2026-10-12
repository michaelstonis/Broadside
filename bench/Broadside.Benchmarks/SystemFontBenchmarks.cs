using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.Resolution;

namespace Broadside.Benchmarks;

/// <summary>
/// The operating-system font resolver (ISO 32000-2 §9.6.2.1, §9.8; issues #59 and #93). <see cref="BuildIndex"/> scans every font
/// file of the platform's font directories (headers only), what the first lookup paid before #93 and what a lookup still pays when
/// no file name points at the font. <see cref="FirstResolveHelvetica"/> is the first lookup of a new resolver: the directory listing
/// plus the files whose names point at Helvetica and its stand-ins, and loading the face's file. <see cref="FirstResolveHelveticaFromIndex"/>
/// is the same with a stored index (a second process): the listing with each file's length and time, and the stored faces.
/// <see cref="ResolveHelvetica"/> is one lookup against a warm resolver (name ladder, family, stand-ins) with the file loaded.
/// </summary>
/// <remarks>
/// A new resolver per call runs warm code over the operating system's file cache; the cold-process figure (JIT and an unwarmed
/// cache included) is measured by starting a process per lookup, as the #93 notes record.
/// </remarks>
[MemoryDiagnoser]
public class SystemFontBenchmarks
{
    private readonly SystemFontResolver _resolver = new();
    private readonly FontQuery _helvetica = new("Helvetica-Bold") { Standard14 = Standard14Font.HelveticaBold };
    private readonly string _index = Path.Combine(Path.GetTempPath(), $"broadside-bench-{Environment.ProcessId}.fontindex");

    [GlobalSetup]
    public void Setup()
    {
        _ = _resolver.ResolveFont(_helvetica);
        _ = new SystemFontResolver(_resolver.Directories, _index).ResolveFont(_helvetica);
    }

    [GlobalCleanup]
    public void Cleanup() => File.Delete(_index);

    /// <summary>Scans every font directory of the platform.</summary>
    [Benchmark]
    public int BuildIndex() => SystemFontIndex.Build(_resolver.Directories).Faces.Count;

    /// <summary>The first lookup of a new resolver, keeping no index.</summary>
    [Benchmark]
    public FontResolution? FirstResolveHelvetica() => new SystemFontResolver(_resolver.Directories).ResolveFont(_helvetica);

    /// <summary>The first lookup of a new resolver over a stored index.</summary>
    [Benchmark]
    public FontResolution? FirstResolveHelveticaFromIndex() => new SystemFontResolver(_resolver.Directories, _index).ResolveFont(_helvetica);

    /// <summary>Resolves Helvetica-Bold against the built index.</summary>
    [Benchmark]
    public FontResolution? ResolveHelvetica() => _resolver.ResolveFont(_helvetica);
}
