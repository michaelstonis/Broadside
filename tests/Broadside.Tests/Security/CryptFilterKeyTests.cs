using System.Security.Cryptography;
using System.Text;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// Which key decrypts an AES-128 stream whose <c>Crypt</c> filter names the document's <c>AESV2</c> crypt filter (ISO 32000-2
/// §7.4.10, §7.6.6): Algorithm 1's object key, as qpdf and poppler write it and as Broadside re-saves it, else the filter's key used
/// as is. The two can only be told apart by the PKCS#5 pad, so when both unpad, the object key wins (issue #297).
/// </summary>
/// <remarks>
/// Each test rewrites object 10 of <c>encrypted-crypt-filters.pdf</c> (<c>/Filter /Crypt /DecodeParms &lt;&lt; /Name /StdCF &gt;&gt;</c>)
/// in place, with the same plaintext and a searched-for IV, so the cross-reference table still holds. The file key is computed here
/// with Algorithm 2 (§7.6.4.3.2) from the empty user password, independently of the engine.
/// </remarks>
public sealed class CryptFilterKeyTests
{
    private const string FileName = "encrypted-crypt-filters.pdf";
    private const string Plaintext = "BT /F1 24 Tf 72 620 Td (StdCF crypt filter) Tj ET";

    [Fact]
    public void A_stream_encrypted_with_the_object_key_reads_back_even_when_the_filter_key_also_unpads_it()
    {
        byte[] fileKey = FileKey();
        byte[] objectKey = ObjectKey(fileKey, 10);
        byte[] data = FindData(iv => Encrypt(objectKey, iv), data => EndsWithValidPad(Decrypt(fileKey, data)));

        Assert.Equal(Plaintext, ReadObjectTen(data));
    }

    [Fact]
    public void A_stream_encrypted_with_the_filter_key_as_is_reads_back()
    {
        byte[] fileKey = FileKey();
        byte[] objectKey = ObjectKey(fileKey, 10);
        byte[] data = FindData(iv => Encrypt(fileKey, iv), data => !EndsWithValidPad(Decrypt(objectKey, data)));

        Assert.Equal(Plaintext, ReadObjectTen(data));
    }

    /// <summary>The first encrypted data, over IVs 0, 1, 2, ..., that <paramref name="accept"/> accepts.</summary>
    private static byte[] FindData(Func<byte[], byte[]> encrypt, Func<byte[], bool> accept)
    {
        for (int counter = 0; counter < 1_000_000; counter++)
        {
            byte[] iv = new byte[16];
            BitConverter.TryWriteBytes(iv, counter);
            byte[] data = encrypt(iv);
            if (accept(data))
            {
                return data;
            }
        }

        throw new InvalidOperationException("No IV found.");
    }

    /// <summary>Opens the corpus file with object 10's encrypted data replaced, and decodes that stream.</summary>
    private static string ReadObjectTen(byte[] encrypted)
    {
        byte[] file = Corpus.Bytes(FileName);
        int at = file.AsSpan().IndexOf("\n10 0 obj"u8);
        int start = at + file.AsSpan(at).IndexOf("stream"u8) + "stream".Length;
        start += file[start] == '\r' ? 2 : 1;
        Assert.Equal((byte)'e', file[start + encrypted.Length + 1]); // The data is replaced by data of the same length.
        encrypted.CopyTo(file, start);

        using PdfDocument document = PdfDocument.Open(file);
        var contents = (CosArray)document.Pages[0].Dictionary[new CosName("Contents")];
        string text = Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(contents[2])).Span);
        Assert.Empty(document.Diagnostics);
        return text;
    }

    /// <summary>Algorithm 2 (§7.6.4.3.2) for revision 4, 128 bits, the empty user password, EncryptMetadata false.</summary>
    private static byte[] FileKey()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes(FileName));
        byte[] owner = ((CosString)document.Security!.EncryptionDictionary[new CosName("O")]).Bytes.ToArray();
        byte[] id = ((CosString)((CosArray)document.Trailer[new CosName("ID")])[0]).Bytes.ToArray();
        int permissions = (int)((CosInteger)document.Security.EncryptionDictionary[new CosName("P")]).Value;
        byte[] padding =
        [
            0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
            0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
        ];
#pragma warning disable CA5351 // Algorithm 2 prescribes MD5 (§7.6.4.3.2).
        byte[] hash = MD5.HashData([.. padding, .. owner, .. BitConverter.GetBytes(permissions), .. id, 0xFF, 0xFF, 0xFF, 0xFF]);
        for (int round = 0; round < 50; round++)
        {
            hash = MD5.HashData(hash);
        }

        return hash;
#pragma warning restore CA5351
    }

    /// <summary>Algorithm 1 steps a-d for AES (§7.6.3.2), generation 0.</summary>
    private static byte[] ObjectKey(byte[] fileKey, int objectNumber)
    {
#pragma warning disable CA5351 // Algorithm 1 prescribes MD5 (§7.6.3.2).
        return MD5.HashData([.. fileKey, (byte)objectNumber, (byte)(objectNumber >> 8), (byte)(objectNumber >> 16), 0, 0, .. "sAlT"u8]);
#pragma warning restore CA5351
    }

    /// <summary>The IV followed by AES-128-CBC of the plaintext with PKCS#5 padding (§7.6.3.1).</summary>
    private static byte[] Encrypt(byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return [.. iv, .. aes.EncryptCbc(Encoding.Latin1.GetBytes(Plaintext), iv)];
    }

    private static byte[] Decrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data.AsSpan(16), data.AsSpan(0, 16), PaddingMode.None);
    }

    private static bool EndsWithValidPad(byte[] plain)
    {
        int pad = plain[^1];
        return pad is >= 1 and <= 16 && plain.AsSpan(plain.Length - pad).IndexOfAnyExcept((byte)pad) < 0;
    }
}
