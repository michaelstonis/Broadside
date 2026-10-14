using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Forms;
using Broadside.Objects;
using Broadside.Tests.Writing;

namespace Broadside.Tests.Signatures;

/// <summary>Certificates made in the test with <see cref="CertificateRequest"/>: nothing is read from a store or the network.</summary>
internal static class TestCertificates
{
    /// <summary>A self-signed RSA certificate with its private key, valid from yesterday until next year.</summary>
    public static X509Certificate2 SelfSigned(string subject, int keySize = 2048)
    {
        using var rsa = RSA.Create(keySize);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyCertSign, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>A self-signed ECDSA certificate on <paramref name="curve"/> with its private key (a named curve, RFC 5480 §2.1.1).</summary>
    public static X509Certificate2 SelfSignedEcdsa(string subject, ECCurve curve)
    {
        using var ecdsa = ECDsa.Create(curve);
        var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyCertSign, critical: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>
    /// A certificate without a private key whose subject key has the algorithm <paramref name="algorithm"/> and the key bytes
    /// <paramref name="key"/>, issued by <paramref name="issuer"/> (an RSA certificate with its private key).
    /// </summary>
    public static X509Certificate2 WithKeyAlgorithm(string subject, string algorithm, byte[] key, X509Certificate2 issuer)
    {
        var publicKey = new PublicKey(new Oid(algorithm), null, new AsnEncodedData(key));
        var request = new CertificateRequest(new X500DistinguishedName(subject), publicKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        using RSA issuerKey = issuer.GetRSAPrivateKey()!;
        return request.Create(
            issuer.SubjectName,
            X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddMonths(6),
            [0x02, 0x07]);
    }

    /// <summary>A certificate authority and a leaf certificate it issued, both with private keys.</summary>
    public static (X509Certificate2 Authority, X509Certificate2 Leaf) Chain(string leafSubject)
    {
        X509Certificate2 authority = SelfSigned("CN=Broadside test authority");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(leafSubject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        byte[] serial = [0x01, 0x93, 0x00, 0x42];
        using X509Certificate2 issued = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(6), serial);
        return (authority, issued.CopyWithPrivateKey(rsa));
    }
}

/// <summary>
/// One signature of a signed file, read through the public document API: its <c>ByteRange</c>, the DER-encoded CMS object in its
/// <c>Contents</c> (the zero padding after it removed), and the file it is in.
/// </summary>
internal sealed record SignedFile(byte[] File, long[] ByteRange, byte[] Cms, byte[] Contents, string FieldName)
{
    /// <summary>Every signature of <paramref name="file"/>, in the order their byte ranges grow (§12.8.1, Table 255, <c>Changes</c>).</summary>
    public static IReadOnlyList<SignedFile> All(byte[] file)
    {
        using PdfDocument document = PdfDocument.Open(file);
        Assert.NotNull(document.AcroForm);
        var signatures = new List<SignedFile>();
        foreach (PdfSignatureField field in document.AcroForm.TerminalFields.OfType<PdfSignatureField>())
        {
            if (field.SignatureDictionary is not { } dictionary)
            {
                continue;
            }

            var range = (CosArray)dictionary[new CosName("ByteRange")];
            long[] byteRange = [.. range.Select(element => ((CosInteger)element).Value)];
            byte[] contents = ((CosString)dictionary[new CosName("Contents")]).Bytes.ToArray();
            AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out int length);
            signatures.Add(new SignedFile(file, byteRange, contents[..length], contents, field.FullyQualifiedName));
        }

        return [.. signatures.OrderBy(signature => signature.ByteRange[2] + signature.ByteRange[3])];
    }

    /// <summary>The signature whose byte ranges cover the most of <paramref name="file"/>.</summary>
    public static SignedFile Last(byte[] file) => All(file)[^1];

    /// <summary>The bytes the byte ranges describe, concatenated.</summary>
    public byte[] CoveredBytes()
    {
        using var covered = new MemoryStream();
        for (int index = 0; index < ByteRange.Length; index += 2)
        {
            covered.Write(File, checked((int)ByteRange[index]), checked((int)ByteRange[index + 1]));
        }

        return covered.ToArray();
    }
}

/// <summary>OpenSSL's CMS verification of a detached signature, as an independent check.</summary>
internal static class OpenSsl
{
    /// <summary>Verifies <paramref name="cms"/> over <paramref name="content"/>, trusting <paramref name="trusted"/>.</summary>
    public static void VerifyCms(byte[] cms, byte[] content, X509Certificate2 trusted)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string contentPath = Path.Combine(directory, "content.bin");
            string trustedPath = Path.Combine(directory, "trusted.pem");
            File.WriteAllBytes(contentPath, content);
            File.WriteAllText(trustedPath, trusted.ExportCertificatePem());
            (int exitCode, string output) = ExternalTool.Run(
                "openssl",
                cms,
                "cms", "-verify", "-binary", "-inform", "DER", "-content", contentPath, "-CAfile", trustedPath, "-purpose", "any", "-out", Path.Combine(directory, "out.bin"), "-in");
            Assert.True(exitCode == 0, output);
            Assert.Contains("Verification successful", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies the RFC 3161 time-stamp token <paramref name="token"/> over <paramref name="data"/> with <c>openssl ts -verify</c>:
    /// the imprint against the data's digest, the authority's signature, its certificate's time-stamping usage and its chain to
    /// <paramref name="trusted"/>.
    /// </summary>
    public static void VerifyTimestamp(byte[] token, byte[] data, X509Certificate2 trusted)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string dataPath = Path.Combine(directory, "data.bin");
            string trustedPath = Path.Combine(directory, "trusted.pem");
            File.WriteAllBytes(dataPath, data);
            File.WriteAllText(trustedPath, trusted.ExportCertificatePem());
            (int exitCode, string output) = ExternalTool.Run("openssl", token, "ts", "-verify", "-token_in", "-data", dataPath, "-CAfile", trustedPath, "-in");
            Assert.True(exitCode == 0, output);
            Assert.Contains("Verification: OK", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

/// <summary>poppler's <c>pdfsig</c>, which verifies every signature of a file (the certificate's trust aside).</summary>
internal static class Pdfsig
{
    /// <summary>Runs <c>pdfsig</c> on <paramref name="file"/> and returns its report.</summary>
    public static string Run(byte[] file)
    {
        (int exitCode, string output) = ExternalTool.Run("pdfsig", file);
        Assert.True(exitCode == 0, output);
        return output;
    }

    /// <summary>How many signatures the report says are valid.</summary>
    public static int ValidSignatures(string output) =>
        output.Split('\n').Count(line => line.Contains("Signature Validation: Signature is Valid.", StringComparison.Ordinal));
}
