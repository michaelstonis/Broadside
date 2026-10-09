using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Security;

namespace Broadside.Fuzz;

/// <summary>
/// The <c>public-key</c> target: the input becomes recipient data of a document encrypted for certificate recipients, opened with a
/// fixed certificate that a valid recipient list names.
/// </summary>
/// <remarks>ISO 32000-2 §7.6.5 (Tables 23, 24 and 27).</remarks>
internal static class PublicKey
{
    private const string Content = "BT /F1 12 Tf 72 700 Td (fuzz) Tj ET";
    private static readonly CosName ContentsKey = new("Contents");
    private static readonly CosName ExtraKey = new("Extra");
    private static readonly byte[] Seed = [.. Enumerable.Range(1, 20).Select(i => (byte)i)];

    // Immutable after the type initializer; shared by every call.
    private static readonly X509Certificate2 Certificate = CreateCertificate();
    private static readonly byte[] ValidRecipients = Envelope([.. Seed, 0xFF, 0xFF, 0xFF, 0xFC]);
    private static readonly byte[] FilterRecipients = Envelope(Seed); // A stream crypt filter's: the seed without permissions.

    /// <summary>
    /// Byte 0 selects the layout: 0 an <c>adbe.pkcs7.s5</c> dictionary whose <c>DefaultCryptFilter</c> lists the input then the valid
    /// list, 1 an <c>adbe.pkcs7.s4</c> dictionary with the same <c>Recipients</c>, 2 a stream's own crypt filter whose
    /// <c>Recipients</c> string is the input (beside another that names the certificate). The document opens, the content stream
    /// decrypts to its plaintext (so the input was digested into the key as given), and the extra stream decodes. The only other
    /// outcomes are those of an input that forges a recipient list for the certificate: it fails to decrypt, or holds too short a seed.
    /// </summary>
    public static void Target(ReadOnlySpan<byte> data)
    {
        int layout = data.IsEmpty ? 0 : data[0] % 3;
        byte[] input = data.IsEmpty ? [] : data[1..].ToArray();
        byte[] file = Build(layout, input);
        PdfDocument document;
        try
        {
            document = new PdfEngine().Open(file, new PdfCertificateCredentials(Certificate));
        }
        catch (PdfCertificateException exception) when (exception.Failure == PdfCertificateFailure.PrivateKeyUnavailable)
        {
            return;
        }
        catch (PdfEncryptionNotSupportedException)
        {
            return;
        }
        catch (DiagnosticException exception) when (exception.Diagnostic.Code == "EncryptDictionaryInvalid")
        {
            return;
        }

        using (document)
        {
            var contents = (CosStream)document.Resolve(document.Pages[0].Dictionary[ContentsKey]);
            string text = Encoding.Latin1.GetString(document.DecodeStream(contents).Span);
            if (text != Content)
            {
                throw new InvalidOperationException($"The content stream decrypted to '{text}', not its plaintext: the input changed the file key wrongly.");
            }

            _ = document.DecodeStream((CosStream)document.Resolve(document.Catalog[ExtraKey]));
        }
    }

    private static byte[] Build(int layout, byte[] input)
    {
        string hexInput = "<" + Convert.ToHexString(input) + ">";
        string hexValid = "<" + Convert.ToHexString(ValidRecipients) + ">";
        string hexFilter = "<" + Convert.ToHexString(FilterRecipients) + ">";
        byte[][] recipients = layout == 2 ? [ValidRecipients] : [input, ValidRecipients];
        bool rc4 = layout == 1;
        byte[] key = Key(recipients, rc4 ? 16 : 32);
        string encryption = layout switch
        {
            1 => $"<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s4 /V 2 /Length 128 /Recipients [{hexInput} {hexValid}] >>",
            2 => $"<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Recipients [{hexValid}] >> /Own << /CFM /AESV3 /Recipients {hexInput} >> /Other << /CFM /AESV3 /Recipients {hexFilter} >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
            _ => $"<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Length 256 /Recipients [{hexInput} {hexValid}] >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>",
        };

        byte[] content = Encrypt(rc4, key, 4, Encoding.Latin1.GetBytes(Content));
        byte[] extra = Encoding.Latin1.GetBytes(Content); // Decrypted with whatever key its filter gets; only decoding is checked.
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /Extra 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>",
            Stream(string.Empty, content),
            encryption,
            Stream(layout == 2 ? "/Filter /Crypt /DecodeParms << /Name /Own >>" : string.Empty, extra),
        ];

        var text = new StringBuilder("%PDF-2.0\n");
        var offsets = new List<int>();
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(text.Length);
            text.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        int xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        string id = "<" + new string('0', 32) + ">";
        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Encrypt 5 0 R /ID [{id} {id}] >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(text.ToString());
    }

    private static string Stream(string entries, byte[] data) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {entries} /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream");

    /// <summary>§7.6.5.3: the digest of the seed and every recipient string.</summary>
    private static byte[] Key(byte[][] recipients, int length)
    {
        using var hash = IncrementalHash.CreateHash(length == 32 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1);
        hash.AppendData(Seed);
        foreach (byte[] recipient in recipients)
        {
            hash.AppendData(recipient);
        }

        return hash.GetHashAndReset()[..length];
    }

    /// <summary>AES-256-CBC with the file key (Algorithm 1.A), or RC4 with Algorithm 1's object key.</summary>
    private static byte[] Encrypt(bool rc4, byte[] key, int objectNumber, byte[] plain)
    {
        if (!rc4)
        {
            byte[] iv = new byte[16];
            using var aes = Aes.Create();
            aes.Key = key;
            return [.. iv, .. aes.EncryptCbc(plain, iv)];
        }

#pragma warning disable CA5351 // Algorithm 1 prescribes MD5 (§7.6.3.2).
        byte[] objectKey = MD5.HashData([.. key, (byte)objectNumber, 0, 0, 0, 0]);
#pragma warning restore CA5351
        byte[] s = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + objectKey[i % objectKey.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        byte[] output = new byte[plain.Length];
        for (int n = 0, i = 0, j = 0; n < plain.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(plain[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return output;
    }

    private static byte[] Envelope(byte[] content)
    {
        var envelope = new EnvelopedCms(new ContentInfo(content));
        envelope.Encrypt(new CmsRecipient(Certificate));
        return envelope.Encode();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Broadside fuzz recipient", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }
}
