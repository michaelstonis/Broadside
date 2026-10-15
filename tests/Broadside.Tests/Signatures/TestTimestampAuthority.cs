using System.Formats.Asn1;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Tests.Signatures;

/// <summary>What the in-process timestamp authority does wrong, to test that the client notices.</summary>
public enum TimestampFault
{
    /// <summary>Answers correctly.</summary>
    None,

    /// <summary>Answers with a nonce other than the request's.</summary>
    WrongNonce,

    /// <summary>Answers without a nonce although the request had one.</summary>
    MissingNonce,

    /// <summary>Answers with a token for another message imprint.</summary>
    WrongImprint,

    /// <summary>Refuses the request: status rejection, a status string and the failure info badDataFormat.</summary>
    Rejection,

    /// <summary>Answers HTTP 503.</summary>
    HttpError,

    /// <summary>Answers bytes that are not a TimeStampResp.</summary>
    Garbage,
}

/// <summary>
/// An RFC 3161 time-stamp authority run inside the test process as an <see cref="HttpMessageHandler"/>, so a <see cref="HttpClient"/>
/// over it never touches the network. Requests are decoded with the platform PKCS package (<see cref="Rfc3161TimestampRequest"/>), the
/// TSTInfo is encoded with <see cref="Rfc3161TimestampTokenInfo"/> and signed with <see cref="SignedCms"/>, with the ESS
/// signing-certificate-v2 attribute (RFC 5816) OpenSSL's verification requires. The authority's certificate is issued by
/// <see cref="Root"/> and has the critical time-stamping extended key usage (RFC 3161 §2.3).
/// </summary>
internal sealed class TestTimestampAuthority : HttpMessageHandler
{
    private const string TimeStampingUsage = "1.3.6.1.5.5.7.3.8";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";
    private const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";

    private static readonly Lazy<(X509Certificate2 Root, X509Certificate2 Unit)> Certificates = new(CreateCertificates);

    private int _serial;

    /// <summary>Gets the policy the authority issues tokens under.</summary>
    public static string Policy => "1.3.6.1.4.1.32473.1.1";

    /// <summary>Gets the self-signed root that issued the authority's certificate.</summary>
    public static X509Certificate2 Root => Certificates.Value.Root;

    /// <summary>Gets the authority's (the time-stamping unit's) certificate, with its private key.</summary>
    public static X509Certificate2 Unit => Certificates.Value.Unit;

    /// <summary>Gets the certificate (with its private key) the authority signs tokens with: <see cref="Unit"/> unless another is given.</summary>
    public X509Certificate2 Certificate { get; init; } = Unit;

    /// <summary>Gets or sets a value indicating whether tokens leave out the ESS signing-certificate-v2 attribute RFC 3161 §2.4.1 requires.</summary>
    public bool OmitSigningCertificate { get; set; }

    /// <summary>Gets or sets what the authority does wrong.</summary>
    public TimestampFault Fault { get; set; }

    /// <summary>Gets or sets how many arbitrary bytes to add to each token as an unsigned attribute, to make it longer.</summary>
    public int Padding { get; set; }

    /// <summary>Gets the requests the authority received, decoded.</summary>
    public List<Rfc3161TimestampRequest> Requests { get; } = [];

    /// <summary>Gets the content types of the requests the authority received.</summary>
    public List<string?> ContentTypes { get; } = [];

    /// <summary>Gets the tokens the authority issued.</summary>
    public List<byte[]> Tokens { get; } = [];

    /// <summary>Gets the URI the tests send requests to; nothing resolves it.</summary>
    public static Uri Address { get; } = new("https://tsa.broadside.test/tsr");

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Address, request.RequestUri);
        ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
        using var body = new MemoryStream();
        request.Content!.CopyTo(body, null, cancellationToken);
        return Answer(body.ToArray());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Address, request.RequestUri);
        ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
        byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        return Answer(body);
    }

    private HttpResponseMessage Answer(byte[] body)
    {
        Assert.True(Rfc3161TimestampRequest.TryDecode(body, out Rfc3161TimestampRequest? request, out int consumed));
        Assert.Equal(body.Length, consumed);
        Requests.Add(request);
        if (Fault == TimestampFault.HttpError)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") };
        }

        byte[] response = Fault switch
        {
            TimestampFault.Rejection => Rejection(),
            TimestampFault.Garbage => [0x30, 0x03, 0x02, 0x01],
            _ => Granted(request),
        };
        var content = new ByteArrayContent(response);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-reply");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private byte[] Granted(Rfc3161TimestampRequest request)
    {
        byte[] hash = request.GetMessageHash().ToArray();
        if (Fault == TimestampFault.WrongImprint)
        {
            hash[0] ^= 0xFF;
        }

        ReadOnlyMemory<byte>? nonce = request.GetNonce();
        if (Fault == TimestampFault.WrongNonce && nonce is { } requested)
        {
            byte[] changed = requested.ToArray();
            changed[^1] ^= 0x01;
            nonce = changed;
        }
        else if (Fault == TimestampFault.MissingNonce)
        {
            nonce = null;
        }

        byte[] serial = new System.Numerics.BigInteger(Interlocked.Increment(ref _serial)).ToByteArray(isUnsigned: false, isBigEndian: true);
        var info = new Rfc3161TimestampTokenInfo(
            new Oid(Policy),
            request.HashAlgorithmId,
            hash,
            serial,
            DateTimeOffset.UtcNow,
            accuracyInMicroseconds: 1_000_000,
            nonce: nonce);

        var signed = new SignedCms(new ContentInfo(new Oid(TstInfoOid), info.Encode()), detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, Certificate)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = request.RequestSignerCertificate ? X509IncludeOption.EndCertOnly : X509IncludeOption.None,
        };
        if (!OmitSigningCertificate)
        {
            signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, SigningCertificateV2(Certificate)));
        }

        signed.ComputeSignature(signer, silent: true);
        if (Padding > 0)
        {
            signed.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData("1.3.6.1.4.1.32473.1.2", Filler(Padding)));
        }

        byte[] token = signed.Encode();
        Tokens.Add(token);
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(0);
            }

            writer.WriteEncodedValue(token);
        }

        return writer.Encode();

        static byte[] Filler(int length)
        {
            var padding = new AsnWriter(AsnEncodingRules.DER);
            padding.WriteOctetString(new byte[length]);
            return padding.Encode();
        }
    }

    private static byte[] Rejection()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            writer.WriteInteger(2);
            using (writer.PushSequence())
            {
                writer.WriteCharacterString(UniversalTagNumber.UTF8String, "unsupported request");
            }

            // PKIFailureInfo badDataFormat (bit 5).
            writer.WriteNamedBitList(BadDataFormat.Value);
        }

        return writer.Encode();
    }

    /// <summary>The ESS signing-certificate-v2 attribute value (RFC 5035 §3): SHA-256 (the default, omitted) of the certificate.</summary>
    public static byte[] SigningCertificateV2(X509Certificate2 certificate)
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

    private static (X509Certificate2 Root, X509Certificate2 Unit) CreateCertificates()
    {
        X509Certificate2 root = TestCertificates.SelfSigned("CN=Broadside test time-stamping root");
        return (root, IssueUnit(root));
    }

    /// <summary>
    /// A time-stamping unit's certificate <paramref name="issuer"/> issues, valid from yesterday for six months, with the critical
    /// time-stamping extended key usage (RFC 3161 §2.3) unless <paramref name="timeStamping"/> is false.
    /// </summary>
    public static X509Certificate2 IssueUnit(X509Certificate2 issuer, bool timeStamping = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Broadside test time-stamping unit", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        if (timeStamping)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(TimeStampingUsage)], critical: true));
        }

        if (issuer.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault() is { } key)
        {
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(key));
        }

        using X509Certificate2 issued = request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(6), RandomNumberGenerator.GetBytes(8).Prepend((byte)0x01).ToArray());
        return issued.CopyWithPrivateKey(rsa);
    }

    /// <summary>
    /// A token from this authority over the hash of <paramref name="data"/> (SHA-256), requested as a client would: what a signature
    /// time-stamp attribute holds when <paramref name="data"/> is the signature value (RFC 3161 Appendix A).
    /// </summary>
    public byte[] Stamp(byte[] data)
    {
        Rfc3161TimestampRequest request = Rfc3161TimestampRequest.CreateFromHash(SHA256.HashData(data), HashAlgorithmName.SHA256, requestSignerCertificates: true, nonce: RandomNumberGenerator.GetBytes(8));
        using var http = new HttpClient(this, disposeHandler: false);
        using var content = new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var message = new HttpRequestMessage(HttpMethod.Post, Address) { Content = content };
        using HttpResponseMessage response = http.Send(message);
        byte[] body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        AsnReader reply = new AsnReader(body, AsnEncodingRules.DER).ReadSequence();
        _ = reply.ReadSequence();
        return reply.ReadEncodedValue().ToArray();
    }

    /// <summary>A named-bit-list stand-in for PKIFailureInfo with only badDataFormat (bit 5) set.</summary>
    [Flags]
    private enum BadDataFormat
    {
        Value = 1 << 5,
    }
}
