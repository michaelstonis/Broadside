using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.SampleDocuments;

/// <summary>
/// The throwaway public-key infrastructure the signed and certificate-encrypted samples use: a root authority and two end-entity
/// certificates, one for each party to the contract. TEST ONLY: the private keys are committed in contract/test-pki/ (PKCS #12,
/// password <see cref="Password"/>) so that rebuilding does not change the certificates and anyone can open the encrypted sample.
/// They protect nothing. The first build after deleting the folder makes new ones.
/// </summary>
internal sealed class TestPki
{
    /// <summary>The password of every PKCS #12 file in contract/test-pki/ (test only).</summary>
    public const string Password = "broadside-test";

    private TestPki(X509Certificate2 root, X509Certificate2 provider, X509Certificate2 client)
    {
        Root = root;
        Provider = provider;
        Client = client;
    }

    /// <summary>Gets the root authority (certificate only, no key is needed after the build that made it).</summary>
    public X509Certificate2 Root { get; }

    /// <summary>Gets the service provider's signing certificate, with its private key.</summary>
    public X509Certificate2 Provider { get; }

    /// <summary>Gets the client's signing and decryption certificate, with its private key.</summary>
    public X509Certificate2 Client { get; }

    public static TestPki Load(GeneratorContext context)
    {
        string folder = Path.Combine(context.Root, "contract", "test-pki");
        string root = Path.Combine(folder, "root-ca.cer");
        string provider = Path.Combine(folder, "provider.p12");
        string client = Path.Combine(folder, "client.p12");
        if (!File.Exists(root) || !File.Exists(provider) || !File.Exists(client))
        {
            Create(folder, root, provider, client);
        }

        return new TestPki(
            X509CertificateLoader.LoadCertificateFromFile(root),
            X509CertificateLoader.LoadPkcs12FromFile(provider, Password, X509KeyStorageFlags.Exportable),
            X509CertificateLoader.LoadPkcs12FromFile(client, Password, X509KeyStorageFlags.Exportable));
    }

    private static void Create(string folder, string rootPath, string providerPath, string clientPath)
    {
        Directory.CreateDirectory(folder);
        var notBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using RSA rootKey = RSA.Create(3072);
        var rootRequest = new CertificateRequest("CN=Broadside Sample Documents Test Root (TEST ONLY), O=Broadside", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using X509Certificate2 root = rootRequest.CreateSelfSigned(notBefore, notAfter);
        File.WriteAllBytes(rootPath, root.Export(X509ContentType.Cert));

        Issue(root, "CN=Ashcombe Grounds Ltd (TEST ONLY), O=Ashcombe Grounds Ltd, C=US", 1, notBefore, notAfter, providerPath);
        Issue(root, "CN=Linden Housing Association (TEST ONLY), O=Linden Housing Association, C=US", 2, notBefore, notAfter, clientPath);
    }

    private static void Issue(X509Certificate2 root, string subject, byte serial, DateTimeOffset notBefore, DateTimeOffset notAfter, string path)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using X509Certificate2 issued = request.Create(root, notBefore, notAfter, [0x10, serial]);
        using X509Certificate2 withKey = issued.CopyWithPrivateKey(key);
        File.WriteAllBytes(path, withKey.Export(X509ContentType.Pkcs12, Password));
    }
}
