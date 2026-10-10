using BenchmarkDotNet.Attributes;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Security.Cryptography;
using Broadside.TestSupport;

namespace Broadside.Benchmarks;

/// <summary>
/// The decryption hot paths over 64 KB (ISO 32000-2 §7.6.3; ISO/TS 32003 §5.2): RC4 and AES-CBC with the platform's and the managed
/// AES, AES-GCM, and the revision 6 key derivation (Algorithm 2.B). RC4 and the cipher calls into a reused buffer must read
/// <c>Allocated</c> <c>-</c>; <c>Broadside.Tests.Security.DecryptionAllocationTests</c> fails the build otherwise.
/// </summary>
[MemoryDiagnoser]
public class SecurityBenchmarks
{
    private const int Length = 64 * 1024;

    private readonly byte[] _key = [.. Enumerable.Range(0, 32).Select(i => (byte)i)];
    private readonly byte[] _iv = new byte[16];
    private readonly byte[] _nonce = new byte[12];
    private readonly byte[] _tag = new byte[16];
    private readonly byte[] _output = new byte[Length];
    private byte[] _data = [];
    private byte[] _gcmCiphertext = [];
    private AesCipher? _platformAes;
    private AesCipher? _managedAes;

    [GlobalSetup]
    public void Setup()
    {
        _data = FilterEncoders.SampleData(Length, alphabet: 256);
        _platformAes = AesCipher.Create(_key);
        using (CryptographyBackend.ForceManaged())
        {
            _managedAes = AesCipher.Create(_key);
        }

        _gcmCiphertext = new byte[Length];
        AesGcmCipher.Encrypt(_key, _nonce, _data, _gcmCiphertext, _tag);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _platformAes?.Dispose();
        _managedAes?.Dispose();
    }

    [Benchmark]
    public byte Rc4Decrypt()
    {
        Rc4.Transform(_key.AsSpan(0, 16), _data, _output);
        return _output[^1];
    }

    [Benchmark]
    public byte AesCbcDecrypt()
    {
        _platformAes!.DecryptCbc(_data, _iv, _output);
        return _output[^1];
    }

    [Benchmark]
    public byte ManagedAesCbcDecrypt()
    {
        _managedAes!.DecryptCbc(_data, _iv, _output);
        return _output[^1];
    }

    [Benchmark]
    public bool AesGcmDecrypt() => AesGcmCipher.TryDecrypt(_key, _nonce, _gcmCiphertext, _tag, _output);

    [Benchmark]
    public byte Revision6Hash()
    {
        Span<byte> hash = stackalloc byte[32];
        StandardSecurityHandler.HashRevision6("owner12345678"u8, "owner"u8, _key.AsSpan(0, 0), hash);
        return hash[0];
    }
}

/// <summary>Opening an encrypted corpus file and decrypting its content stream, end to end (ISO 32000-2 §7.6).</summary>
[MemoryDiagnoser]
public class EncryptedDocumentBenchmarks
{
    private static readonly CosName Contents = new("Contents");
    private byte[] _file = [];

    [Params("encrypted-rc4-128.pdf", "encrypted-aes-128.pdf", "encrypted-aes-256.pdf", "encrypted-aes-gcm.pdf", "encrypted-mac.pdf")]
    public string File { get; set; } = string.Empty;

    [GlobalSetup]
    public void Setup() => _file = System.IO.File.ReadAllBytes(Path.Combine(CorpusLocator.CorpusDirectory, File));

    [Benchmark]
    public int OpenAndDecryptContent()
    {
        using PdfDocument document = PdfDocument.Open(_file);
        return document.DecodeStream((CosStream)document.Resolve(document.Pages[0].Dictionary[Contents])).Length;
    }
}
