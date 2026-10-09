using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// The decryption hot paths allocate nothing per call into a caller's buffer (CLAUDE.md, hot paths; the benchmarks are
/// <c>SecurityBenchmarks</c>). ISO 32000-2 §7.6.3.
/// </summary>
[Collection("Heavy")]
public class DecryptionAllocationTests
{
    private static readonly byte[] Key = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];

    [Fact]
    public void Rc4_allocates_nothing()
    {
        byte[] data = FilterEncoders.SampleData(64 * 1024, alphabet: 256);
        byte[] output = new byte[data.Length];

        Assert.Equal(0, Measure(() => Rc4.Transform(Key.AsSpan(0, 16), data, output)));
    }

    [Fact]
    public void Aes_cbc_into_a_buffer_allocates_nothing_per_block()
    {
        byte[] data = FilterEncoders.SampleData(64 * 1024, alphabet: 256);
        byte[] output = new byte[data.Length];
        byte[] iv = new byte[16];
        using AesCipher platform = AesCipher.Create(Key);
        AesCipher managed;
        using (CryptographyBackend.ForceManaged())
        {
            managed = AesCipher.Create(Key);
        }

        using (managed)
        {
            // The platform cipher costs a small constant per call (its one-shot transform object), whatever the data length.
            Assert.InRange(Measure(() => platform.DecryptCbc(data, iv, output)), 0, 256);
            Assert.Equal(0, Measure(() => managed.DecryptCbc(data, iv, output)));
        }
    }

    private static long Measure(Action action)
    {
        for (int i = 0; i < 40; i++)
        {
            action(); // past tiered compilation
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
