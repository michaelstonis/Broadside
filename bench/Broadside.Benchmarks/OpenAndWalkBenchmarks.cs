using BenchmarkDotNet.Attributes;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The Phase 1 baseline (issue #48): open a file from memory and read everything the document model exposes, as the real-world
/// corpus gate does (<see cref="DocumentWalker"/>): version, catalog, security, revisions, linearization and hint tables, every page's
/// boxes and resources, every object reachable from the trailer or numbered below <c>Size</c>, and every stream decoded through its
/// filters into a discarding writer. Runs over every file of <c>tests/Corpus/</c> and over <see cref="RealWorldSubset"/> when the
/// real-world corpora are fetched. Opening allocates the document model by design, so <c>Allocated</c> is a baseline to watch, not
/// a zero to assert. Results are recorded under <c>bench/baselines/phase1/</c>.
/// </summary>
/// <remarks>ISO 32000-2 §7.3 to §7.7.</remarks>
[MemoryDiagnoser]
public class OpenAndWalkBenchmarks
{
    /// <summary>The two corpus files that open only with their user password (tests/Corpus/README.md, "Password-protected files").</summary>
    private static readonly Dictionary<string, string> Passwords = new(StringComparer.Ordinal)
    {
        ["encrypted-user-password.pdf"] = "pässwort",
        ["encrypted-rc4-user-password.pdf"] = "café",
    };

    private readonly PdfEngine _engine = new();
    private byte[] _bytes = [];
    private PdfPassword? _password;

    /// <summary>A file name of <c>tests/Corpus/</c>, or a path under the real-world corpus directory (it contains a slash).</summary>
    [ParamsSource(nameof(Files))]
    public string File { get; set; } = "";

    /// <summary>Every minimal-corpus file, then the real-world subset files present on this machine.</summary>
    public static IEnumerable<string> Files() =>
        [.. CorpusLocator.CorpusFiles().Select(Path.GetFileName).OfType<string>(), .. RealWorldSubset.Available()];

    [GlobalSetup]
    public void Setup()
    {
        _bytes = File.Contains('/', StringComparison.Ordinal)
            ? RealWorldSubset.Read(File)
            : System.IO.File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, File));
        _password = Passwords.TryGetValue(File, out string? password) ? new PdfPassword(password) : null;
    }

    [Benchmark]
    public long OpenAndWalk()
    {
        using PdfDocument document = _password is null ? _engine.Open(_bytes) : _engine.Open(_bytes, _password);
        DocumentWalkResult walk = DocumentWalker.Walk(document);
        return walk.Objects + walk.DecodedBytes;
    }
}
