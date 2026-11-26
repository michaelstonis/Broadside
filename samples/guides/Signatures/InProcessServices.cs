using System.Formats.Asn1;
using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

/// <summary>
/// A certification authority and a time-stamping authority that run inside the sample's process. They answer HTTP requests as an
/// <see cref="HttpMessageHandler"/>, so an <see cref="HttpClient"/> built over them never reaches the network: a GET of
/// <see cref="CrlAddress"/> returns the authority's CRL, and a POST to <see cref="TimestampAddress"/> returns an RFC 3161 time-stamp
/// response. In an application, the same <see cref="HttpClient"/> would go to a real authority.
/// </summary>
internal sealed class InProcessServices : HttpMessageHandler
{
    private const string TimeStampingUsage = "1.3.6.1.5.5.7.3.8";
    private const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    private int _serial;

    public InProcessServices()
    {
        Root = CreateRoot();
        Signer = Issue(Root, "CN=Ada Lovelace, O=Example Ltd", X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, usage: null);
        TimestampUnit = Issue(Root, "CN=Example time-stamping unit", X509KeyUsageFlags.DigitalSignature, usage: TimeStampingUsage);
    }

    /// <summary>Gets the address the certificates give as their CRL distribution point; nothing outside this process resolves it.</summary>
    public static Uri CrlAddress { get; } = new("http://ca.example.test/root.crl");

    /// <summary>Gets the time-stamping authority's address; nothing outside this process resolves it.</summary>
    public static Uri TimestampAddress { get; } = new("https://tsa.example.test/");

    /// <summary>Gets the self-signed root that issued the signer's and the time-stamping unit's certificates.</summary>
    public X509Certificate2 Root { get; }

    /// <summary>Gets the signer's certificate, with its private key.</summary>
    public X509Certificate2 Signer { get; }

    /// <summary>Gets the time-stamping unit's certificate, with its private key.</summary>
    public X509Certificate2 TimestampUnit { get; }

    /// <summary>Gets how many requests the services answered.</summary>
    public int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        return Answer(request, body);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        request.Content?.CopyTo(body, null, cancellationToken);
        return Answer(request, body.ToArray());
    }

    private HttpResponseMessage Answer(HttpRequestMessage request, byte[] body)
    {
        Requests++;
        if (request.Method == HttpMethod.Get && request.RequestUri == CrlAddress)
        {
            return Reply(Crl(), "application/pkix-crl");
        }

        if (request.Method == HttpMethod.Post && request.RequestUri == TimestampAddress
            && Rfc3161TimestampRequest.TryDecode(body, out Rfc3161TimestampRequest? query, out _))
        {
            return Reply(Timestamp(query), "application/timestamp-reply");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Reply(byte[] data, string mediaType)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private byte[] Crl() =>
        new CertificateRevocationListBuilder().Build(Root, BigInteger.One, DateTimeOffset.UtcNow.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, thisUpdate: DateTimeOffset.UtcNow.AddMinutes(-5));

    private byte[] Timestamp(Rfc3161TimestampRequest request)
    {
        var info = new Rfc3161TimestampTokenInfo(
            new Oid("1.3.6.1.4.1.32473.1"),
            request.HashAlgorithmId,
            request.GetMessageHash(),
            new BigInteger(Interlocked.Increment(ref _serial)).ToByteArray(isUnsigned: false, isBigEndian: true),
            DateTimeOffset.UtcNow,
            nonce: request.GetNonce());
        var token = new SignedCms(new ContentInfo(new Oid(TstInfoOid), info.Encode()), detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, TimestampUnit)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = request.RequestSignerCertificate ? X509IncludeOption.EndCertOnly : X509IncludeOption.None,
        };
        signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, SigningCertificateV2(TimestampUnit)));
        token.ComputeSignature(signer, silent: true);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(0); // granted
            }

            writer.WriteEncodedValue(token.Encode());
        }

        return writer.Encode();
    }

    // RFC 5035: the ESS signing-certificate-v2 attribute, the SHA-256 hash of the unit's certificate.
    private static byte[] SigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteOctetString(SHA256.HashData(certificate.RawData));
        }

        return writer.Encode();
    }

    private static X509Certificate2 CreateRoot()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Example root CA", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddYears(5));
    }

    private static X509Certificate2 Issue(X509Certificate2 issuer, string subject, X509KeyUsageFlags keyUsage, string? usage)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));
        if (usage is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], critical: true));
        }

        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([CrlAddress.ToString()]));
        using X509Certificate2 issued = request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(8).Prepend((byte)1).ToArray());
        return issued.CopyWithPrivateKey(key);
    }
}
