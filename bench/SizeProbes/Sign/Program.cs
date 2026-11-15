using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside;
using Broadside.Signatures;

// Open a file and sign it with a certificate (a throwaway self-signed one here; an app would load its own).
using RSA key = RSA.Create(2048);
var request = new CertificateRequest("CN=Size probe", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
using PdfDocument document = PdfDocument.Open(args[0]);
using var output = new MemoryStream();
document.Sign(output, new PdfCertificateSigner(certificate));
Console.WriteLine($"signed: {output.Length} bytes");
return output.Length > 0 ? 0 : 1;
