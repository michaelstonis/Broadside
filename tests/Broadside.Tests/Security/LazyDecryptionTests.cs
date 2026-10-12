using System.Security.Cryptography;
using System.Text;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// An encrypted stream is decrypted when its data is first read, not when its object loads, and once: the lazy loading of issue #45
/// holds for encrypted documents too. ISO 32000-2 §7.6.2 and §7.5.1.
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class LazyDecryptionTests
{
    private const int PayloadLength = 4 << 20;
    private static readonly CosReference Large = new(8, 0);

    [Fact]
    public void Loading_an_encrypted_stream_object_does_not_decrypt_its_data()
    {
        (byte[] file, _) = WithLargeEncryptedStream();

        long allocated = Allocations.Measure(
            () =>
            {
                using PdfDocument document = PdfDocument.Open(file);
                _ = document.Resolve(Large);
            },
            warmUpCalls: 2);

        Assert.InRange(allocated, 0, 1L << 20);
    }

    [Fact]
    public void An_encrypted_stream_is_decrypted_on_first_read_and_once()
    {
        (byte[] file, byte[] payload) = WithLargeEncryptedStream();
        using PdfDocument document = PdfDocument.Open(file);
        var stream = Assert.IsType<CosStream>(document.Resolve(Large));

        ReadOnlyMemory<byte> first = stream.EncodedData;
        ReadOnlyMemory<byte> second = stream.EncodedData;

        Assert.True(first.Span.SequenceEqual(payload));
        Assert.True(first.Equals(second), "The second read decrypted the data again.");
        Assert.Empty(document.Diagnostics);
    }

    /// <summary><c>encrypted-aes-256.pdf</c> with an update adding object 8, a 4 MiB stream encrypted with Algorithm 1.A.</summary>
    private static (byte[] File, byte[] Payload) WithLargeEncryptedStream()
    {
        byte[] original = Corpus.Bytes("encrypted-aes-256.pdf");
        string text = Encoding.Latin1.GetString(original);
        byte[] payload = new byte[PayloadLength];
        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index * 31);
        }

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData("broadside-corpus:encrypted-aes-256:file-key:0"u8);
        byte[] iv = new byte[16];
        byte[] data = [.. iv, .. aes.EncryptCbc(payload, iv)];
        int idStart = text.IndexOf("/ID [", StringComparison.Ordinal);
        string id = text[idStart..(text.IndexOf(']', idStart) + 1)];
        byte[] file = TestPdf.AppendUpdate(
            original,
            $"/Size 9 /Root 1 0 R /Encrypt 6 0 R {id}",
            (8, 0, $"<< /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream"));
        return (file, payload);
    }
}
