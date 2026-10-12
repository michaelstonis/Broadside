using System.Security.Cryptography;
using Broadside.Security.Cryptography;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// Hashing a byte range a window at a time allocates nothing per window, with the managed SHA-3 and through the facade's platform
/// path (CLAUDE.md, hot paths; the benchmark is <c>DigestBenchmarks</c>, which hashes 100 MB). FIPS 202; ISO/TS 32001 §5.1.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class Sha3AllocationTests
{
    private const int Window = 64 * 1024;

    public static TheoryData<string> Algorithms => ["SHA3-256", "SHA3-384", "SHA3-512"];

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void Hashing_a_range_with_the_managed_sha3_allocates_nothing_per_window(string algorithm)
    {
        byte[] window = FilterEncoders.SampleData(Window, alphabet: 256);
        byte[] digest = new byte[64];
        IncrementalDigest hash;
        using (CryptographyBackend.ForceManaged())
        {
            hash = IncrementalDigest.Create(new HashAlgorithmName(algorithm));
        }

        using (hash)
        {
            // Sixteen windows (1 MB), the last one ragged so a partial block is carried, then the digest into a reused buffer.
            Action range = () =>
            {
                for (int i = 0; i < 16; i++)
                {
                    hash.AppendData(window.AsSpan(0, i == 15 ? Window - 13 : Window));
                }

                hash.GetHashAndReset(digest);
            };

            Assert.Equal(0, Allocations.Measure(range));
        }
    }

    [Fact]
    public void Hashing_a_range_through_the_platform_digest_allocates_nothing_per_window()
    {
        byte[] window = FilterEncoders.SampleData(Window, alphabet: 256);
        using IncrementalDigest hash = IncrementalDigest.Create(HashAlgorithmName.SHA256);
        Action append = () =>
        {
            for (int i = 0; i < 16; i++)
            {
                hash.AppendData(window);
            }
        };

        Assert.Equal(0, Allocations.Measure(append));
    }
}
