using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// Hashing a 100 MB byte range in 64 KB windows, as a signature or a PDF MAC token digests a file (ISO/TS 32001 §5.1, ISO/TS 32004
/// §6.6; FIPS 202): the managed SHA-3 the browser and macOS use, and the digest facade's default (the base class library's SHA-3
/// where the platform has it, the managed one elsewhere). One operation is the whole range, so <c>Allocated</c> must read
/// <c>-</c>: nothing per window. <c>Broadside.Tests.Security.Sha3AllocationTests</c> fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class DigestBenchmarks
{
    private const int Window = 64 * 1024;
    private const int Windows = 1600; // 100 MiB

    private readonly byte[] _digest = new byte[64];
    private byte[] _window = [];
    private IncrementalDigest? _managed;
    private IncrementalDigest? _default;

    [Params("SHA3-256", "SHA3-384", "SHA3-512")]
    public string Algorithm { get; set; } = "SHA3-256";

    [GlobalSetup]
    public void Setup()
    {
        _window = FilterEncoders.SampleData(Window, alphabet: 256);
        var name = new HashAlgorithmName(Algorithm);
        _default = IncrementalDigest.Create(name);
        using (CryptographyBackend.ForceManaged())
        {
            _managed = IncrementalDigest.Create(name);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _managed?.Dispose();
        _default?.Dispose();
    }

    [Benchmark]
    public byte ManagedDigest100MB() => HashRange(_managed!);

    [Benchmark]
    public byte DefaultDigest100MB() => HashRange(_default!);

    private byte HashRange(IncrementalDigest digest)
    {
        for (int i = 0; i < Windows; i++)
        {
            digest.AppendData(_window);
        }

        digest.GetHashAndReset(_digest);
        return _digest[0];
    }
}
