using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Tests.Document;

namespace Broadside.Tests.Security;

/// <summary>How a test file's strings and streams are encrypted with the file encryption key (ISO 32000-2 §7.6.3).</summary>
internal enum TestCipher
{
    /// <summary>Algorithm 1 with RC4.</summary>
    Rc4,

    /// <summary>Algorithm 1 with AES-128-CBC ("sAlT").</summary>
    AesV2,

    /// <summary>Algorithm 1.A, AES-256-CBC with the file key.</summary>
    AesV3,
}

/// <summary>
/// Builds a document encrypted for certificate recipients (ISO 32000-2 §7.6.5) in memory, independently of the engine: the
/// enveloped data are made with <see cref="EnvelopedCms"/>, the file key is the digest §7.6.5.3 describes, and the content stream
/// and the Info title are encrypted with the BCL's AES and a local RC4 the way §7.6.3 describes.
/// </summary>
/// <remarks>
/// Objects: 1 catalog, 2 page tree, 3 page, 4 content stream, 5 Info, 6 encryption dictionary, then <see cref="ExtraObjects"/>
/// from 7 on, each built with the file key and its object number.
/// </remarks>
internal sealed class PublicKeyTestFile
{
    /// <summary>The page's content, in plaintext.</summary>
    public const string Content = "BT /F1 24 Tf 72 700 Td (Public key) Tj ET";

    /// <summary>The Info dictionary's title, in plaintext.</summary>
    public const string Title = "Certificate encrypted";

    /// <summary>Gets the encryption dictionary; <c>{0}</c> is replaced by the Recipients array's strings.</summary>
    public required string Encryption { get; init; }

    /// <summary>Gets the Recipients strings, each a DER-encoded CMS enveloped-data object.</summary>
    public required byte[][] Recipients { get; init; }

    /// <summary>Gets how the content stream and the title are encrypted.</summary>
    public TestCipher Cipher { get; init; } = TestCipher.AesV3;

    /// <summary>Gets the length of the file encryption key, in bytes.</summary>
    public int KeyLength { get; init; } = 32;

    /// <summary>Gets the seed the recipients' enveloped data carry.</summary>
    public byte[] Seed { get; init; } = DefaultSeed;

    /// <summary>Gets a value indicating whether the key digest ends with four 0xFF bytes (metadata left in plaintext).</summary>
    public bool MetadataInPlaintext { get; init; }

    /// <summary>Gets extra catalog entries.</summary>
    public string CatalogEntries { get; init; } = string.Empty;

    /// <summary>Gets objects 7 onward, given the file key and their object number.</summary>
    public IReadOnlyList<Func<byte[], int, string>> ExtraObjects { get; init; } = [];

    /// <summary>Gets the seed most tests use: bytes 1 to 20.</summary>
    public static byte[] DefaultSeed { get; } = [.. Enumerable.Range(1, 20).Select(i => (byte)i)];

    /// <summary>Gets the file encryption key §7.6.5.3 derives: SHA-1 or SHA-256 of the seed, every recipient string, and 0xFF x 4.</summary>
    public byte[] FileKey => DeriveKey(Seed, Recipients, MetadataInPlaintext, KeyLength);

    /// <summary>Creates a self-signed RSA certificate with its private key and a subject key identifier.</summary>
    public static X509Certificate2 Certificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));
    }

    /// <summary>The enveloped data of §7.6.5.3: the seed, then the permissions most significant byte first (unless null).</summary>
    public static byte[] Envelope(byte[] seed, uint? permissions, params X509Certificate2[] recipients) =>
        Envelope(seed, permissions, SubjectIdentifierType.IssuerAndSerialNumber, recipients);

    /// <summary>The enveloped data of §7.6.5.3 addressed to the recipients by the given kind of identifier.</summary>
    public static byte[] Envelope(byte[] seed, uint? permissions, SubjectIdentifierType identifier, params X509Certificate2[] recipients)
    {
        byte[] content = seed;
        if (permissions is { } value)
        {
            content = [.. seed, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }

        var envelope = new EnvelopedCms(new ContentInfo(content));
        var collection = new CmsRecipientCollection();
        foreach (X509Certificate2 recipient in recipients)
        {
            collection.Add(new CmsRecipient(identifier, recipient));
        }

        envelope.Encrypt(collection);
        return envelope.Encode();
    }

    /// <summary>§7.6.5.3 steps a-d.</summary>
    public static byte[] DeriveKey(byte[] seed, IEnumerable<byte[]> recipients, bool metadataInPlaintext, int keyLength)
    {
        using var hash = IncrementalHash.CreateHash(keyLength == 32 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1);
        hash.AppendData(seed);
        foreach (byte[] recipient in recipients)
        {
            hash.AppendData(recipient);
        }

        if (metadataInPlaintext)
        {
            hash.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        return hash.GetHashAndReset()[..keyLength];
    }

    /// <summary>A PDF array of hexadecimal strings.</summary>
    public static string HexArray(IEnumerable<byte[]> strings) => "[" + string.Join(' ', strings.Select(Hex)) + "]";

    /// <summary>A PDF hexadecimal string.</summary>
    public static string Hex(byte[] bytes) => "<" + Convert.ToHexString(bytes) + ">";

    /// <summary>A stream object whose data are <paramref name="data"/>, written byte for byte.</summary>
    public static string Stream(string dictionaryEntries, byte[] data) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {dictionaryEntries} /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream");

    /// <summary>Encrypts data for an object (§7.6.3, Algorithms 1 and 1.A) with a fixed IV.</summary>
    public static byte[] Encrypt(TestCipher cipher, byte[] fileKey, int objectNumber, byte[] plain)
    {
        byte[] iv = [.. Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i))];
        switch (cipher)
        {
            case TestCipher.Rc4:
                return Rc4(ObjectKey(fileKey, objectNumber, aes: false), plain);
            case TestCipher.AesV2:
                {
                    using var aes = Aes.Create();
                    aes.Key = ObjectKey(fileKey, objectNumber, aes: true);
                    return [.. iv, .. aes.EncryptCbc(plain, iv)];
                }

            default:
                {
                    using var aes = Aes.Create();
                    aes.Key = fileKey;
                    return [.. iv, .. aes.EncryptCbc(plain, iv)];
                }
        }
    }

    /// <summary>Writes the file.</summary>
    public byte[] Build()
    {
        byte[] key = FileKey;
        byte[] content = Encrypt(Cipher, key, 4, Encoding.Latin1.GetBytes(Content));
        byte[] title = Encrypt(Cipher, key, 5, Encoding.Latin1.GetBytes(Title));
        string encryption = Encryption.Replace("{0}", HexArray(Recipients), StringComparison.Ordinal);
        List<string> objects =
        [
            $"<< /Type /Catalog /Pages 2 0 R {CatalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 4 0 R >>",
            Stream(string.Empty, content),
            $"<< /Title {Hex(title)} >>",
            encryption,
        ];
        for (int index = 0; index < ExtraObjects.Count; index++)
        {
            objects.Add(ExtraObjects[index](key, objects.Count + 1));
        }

        string id = "<" + new string('0', 32) + ">";
        return new TestPdf { Header = "%PDF-2.0", TrailerEntries = $"/Info 5 0 R /Encrypt 6 0 R /ID [{id} {id}]" }.Build([.. objects]);
    }

    /// <summary>Algorithm 1 steps a-d.</summary>
    private static byte[] ObjectKey(byte[] fileKey, int objectNumber, bool aes)
    {
        byte[] input = [.. fileKey, (byte)objectNumber, (byte)(objectNumber >> 8), (byte)(objectNumber >> 16), 0, 0, .. aes ? "sAlT"u8.ToArray() : []];
#pragma warning disable CA5351 // Algorithm 1 prescribes MD5 (§7.6.3.2).
        return MD5.HashData(input)[..Math.Min(fileKey.Length + 5, 16)];
#pragma warning restore CA5351
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        byte[] s = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        byte[] output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return output;
    }
}
