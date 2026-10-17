using System.Formats.Asn1;
using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Broadside.Tests.Signatures;

/// <summary>What an OCSP response says about a certificate.</summary>
public enum OcspStatus
{
    /// <summary>Not revoked.</summary>
    Good,

    /// <summary>Revoked.</summary>
    Revoked,

    /// <summary>The responder does not know the certificate.</summary>
    Unknown,
}

/// <summary>
/// A certification authority's revocation services run inside the test process: it signs OCSP responses (RFC 6960, encoded here with
/// <see cref="AsnWriter"/>, since the platform has no OCSP encoder) and CRLs (the platform's <see cref="CertificateRevocationListBuilder"/>),
/// and answers OCSP POSTs and CRL GETs as an <see cref="HttpMessageHandler"/>, so an <see cref="HttpClient"/> over it never touches the
/// network. Every request is recorded.
/// </summary>
internal sealed class TestRevocationAuthority(X509Certificate2 authority) : HttpMessageHandler
{
    private const string OcspSigningUsage = "1.3.6.1.5.5.7.3.9";

    /// <summary>Gets the URI certificates name for OCSP; nothing resolves it.</summary>
    public static Uri OcspAddress { get; } = new("http://ocsp.broadside.test/");

    /// <summary>Gets the URI certificates name as their CRL distribution point; nothing resolves it.</summary>
    public static Uri CrlAddress { get; } = new("http://crl.broadside.test/authority.crl");

    /// <summary>Gets the revoked serial numbers (hexadecimal, as the certificate gives them) with when and why.</summary>
    public Dictionary<string, (DateTimeOffset Time, X509RevocationReason Reason)> Revoked { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets a value indicating whether the OCSP responder answers <see cref="OcspStatus.Unknown"/> for every certificate.</summary>
    public bool AnswerUnknown { get; set; }

    /// <summary>Gets or sets a value indicating whether OCSP requests are answered HTTP 404 (so a client falls back to the CRL).</summary>
    public bool OcspOffline { get; set; }

    /// <summary>Gets the requests received, as method and URI.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Revokes <paramref name="certificate"/> at <paramref name="time"/>.</summary>
    public void Revoke(X509Certificate2 certificate, DateTimeOffset time, X509RevocationReason reason = X509RevocationReason.KeyCompromise) =>
        Revoked[Serial(certificate.SerialNumberBytes.Span)] = (time, reason);

    private static string Serial(ReadOnlySpan<byte> serial) => Convert.ToHexString(serial.TrimStart((byte)0));

    /// <summary>A DER <c>OCSPResponse</c> about <paramref name="certificate"/>, signed by the authority (or <paramref name="responder"/>).</summary>
    public byte[] Ocsp(
        X509Certificate2 certificate,
        DateTimeOffset? producedAt = null,
        DateTimeOffset? nextUpdate = null,
        X509Certificate2? responder = null,
        byte[]? nonce = null,
        bool corruptSignature = false)
    {
        return Ocsp(certificate.SerialNumberBytes.ToArray(), producedAt, nextUpdate, responder, nonce, corruptSignature);
    }

    private byte[] Ocsp(byte[] serial, DateTimeOffset? producedAt, DateTimeOffset? nextUpdate, X509Certificate2? responder, byte[]? nonce, bool corruptSignature)
    {
        bool revoked = Revoked.TryGetValue(Serial(serial), out (DateTimeOffset Time, X509RevocationReason Reason) entry);
        OcspStatus status = AnswerUnknown ? OcspStatus.Unknown : revoked ? OcspStatus.Revoked : OcspStatus.Good;
        return OcspResponses.Build(authority, serial, status, entry.Time, entry.Reason, producedAt ?? DateTimeOffset.UtcNow, nextUpdate ?? DateTimeOffset.UtcNow.AddDays(7), responder ?? authority, nonce, corruptSignature);
    }

    /// <summary>A DER CRL listing <see cref="Revoked"/>, signed by the authority.</summary>
    public byte[] Crl(DateTimeOffset? thisUpdate = null, DateTimeOffset? nextUpdate = null)
    {
        var builder = new CertificateRevocationListBuilder();
        foreach ((string serial, (DateTimeOffset time, X509RevocationReason reason)) in Revoked)
        {
            builder.AddEntry(Convert.FromHexString(serial), time, reason);
        }

        DateTimeOffset issued = thisUpdate ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        return builder.Build(authority, BigInteger.One, nextUpdate ?? issued.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, issued);
    }

    /// <summary>A certificate the authority issues for its own OCSP responder, with the OCSP-signing extended key usage when <paramref name="ocspSigning"/>.</summary>
    public X509Certificate2 IssueResponder(bool ocspSigning = true)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Broadside test OCSP responder", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        if (ocspSigning)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(OcspSigningUsage)], critical: false));
        }

        using X509Certificate2 issued = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(3), RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(rsa);
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        request.Content?.CopyTo(body, null, cancellationToken);
        return Answer(request, body.ToArray());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        return Answer(request, body);
    }

    private HttpResponseMessage Answer(HttpRequestMessage request, byte[] body)
    {
        lock (Requests)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
        }

        if (request.Method == HttpMethod.Get && request.RequestUri == CrlAddress)
        {
            return Reply(Crl(), "application/pkix-crl");
        }

        if (request.Method == HttpMethod.Post && request.RequestUri == OcspAddress && !OcspOffline)
        {
            Assert.Equal("application/ocsp-request", request.Content?.Headers.ContentType?.MediaType);
            (byte[] serial, byte[] nonce) = ReadRequest(body);
            return Reply(Ocsp(serial, null, null, null, nonce, corruptSignature: false), "application/ocsp-response");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Reply(byte[] data, string mediaType)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>The serial number and the nonce extension value of a one-certificate <c>OCSPRequest</c> (RFC 6960 §4.1.1).</summary>
    private static (byte[] Serial, byte[] Nonce) ReadRequest(byte[] body)
    {
        AsnReader tbs = new AsnReader(body, AsnEncodingRules.DER).ReadSequence().ReadSequence();
        AsnReader certificateId = tbs.ReadSequence().ReadSequence().ReadSequence();
        _ = certificateId.ReadSequence();
        _ = certificateId.ReadOctetString();
        _ = certificateId.ReadOctetString();
        byte[] serial = certificateId.ReadIntegerBytes().ToArray();
        byte[] nonce = [];
        if (tbs.HasData)
        {
            AsnReader extension = tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2)).ReadSequence().ReadSequence();
            Assert.Equal("1.3.6.1.5.5.7.48.1.2", extension.ReadObjectIdentifier());
            nonce = extension.ReadOctetString();
        }

        return (serial, nonce);
    }
}

/// <summary>Encodes RFC 6960 OCSP responses.</summary>
#pragma warning disable CA5350 // RFC 5019 §2.1.1 and RFC 6960 §4.2.2.3: CertID and byKey hashes are SHA-1.
internal static class OcspResponses
{
    /// <summary>
    /// A successful basic <c>OCSPResponse</c> about the certificate <paramref name="issuer"/> issued with <paramref name="serial"/>, signed by
    /// <paramref name="responder"/> (named by key hash, its certificate included when it is not the issuer), with a SHA-1 CertID.
    /// </summary>
    public static byte[] Build(
        X509Certificate2 issuer,
        byte[] serial,
        OcspStatus status,
        DateTimeOffset revocationTime,
        X509RevocationReason reason,
        DateTimeOffset producedAt,
        DateTimeOffset nextUpdate,
        X509Certificate2 responder,
        byte[]? nonce,
        bool corruptSignature)
    {
        var data = new AsnWriter(AsnEncodingRules.DER);
        using (data.PushSequence())
        {
            using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2)))
            {
                data.WriteOctetString(SHA1.HashData(responder.PublicKey.EncodedKeyValue.RawData));
            }

            data.WriteGeneralizedTime(producedAt, omitFractionalSeconds: true);
            using (data.PushSequence())
            using (data.PushSequence())
            {
                using (data.PushSequence())
                {
                    using (data.PushSequence())
                    {
                        data.WriteObjectIdentifier("1.3.14.3.2.26");
                        data.WriteNull();
                    }

                    data.WriteOctetString(SHA1.HashData(issuer.SubjectName.RawData));
                    data.WriteOctetString(SHA1.HashData(issuer.PublicKey.EncodedKeyValue.RawData));
                    data.WriteIntegerUnsigned(serial.AsSpan().TrimStart((byte)0));
                }

                switch (status)
                {
                    case OcspStatus.Good:
                        data.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                        break;
                    case OcspStatus.Revoked:
                        using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
                        {
                            data.WriteGeneralizedTime(revocationTime, omitFractionalSeconds: true);
                            using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                            {
                                data.WriteEnumeratedValue(reason);
                            }
                        }

                        break;
                    default:
                        data.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 2));
                        break;
                }

                data.WriteGeneralizedTime(producedAt.AddMinutes(-1), omitFractionalSeconds: true);
                using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    data.WriteGeneralizedTime(nextUpdate, omitFractionalSeconds: true);
                }
            }

            if (nonce is { Length: > 0 })
            {
                using (data.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
                using (data.PushSequence())
                using (data.PushSequence())
                {
                    data.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.2");
                    data.WriteOctetString(nonce);
                }
            }
        }

        byte[] tbs = data.Encode();
        using RSA key = responder.GetRSAPrivateKey()!;
        byte[] signature = key.SignData(tbs, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (corruptSignature)
        {
            signature[^1] ^= 0x01;
        }

        var basic = new AsnWriter(AsnEncodingRules.DER);
        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(tbs);
            using (basic.PushSequence())
            {
                basic.WriteObjectIdentifier("1.2.840.113549.1.1.11");
                basic.WriteNull();
            }

            basic.WriteBitString(signature);
            if (!ReferenceEquals(responder, issuer) && responder.Thumbprint != issuer.Thumbprint)
            {
                using (basic.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
                using (basic.PushSequence())
                {
                    basic.WriteEncodedValue(responder.RawData);
                }
            }
        }

        var response = new AsnWriter(AsnEncodingRules.DER);
        using (response.PushSequence())
        {
            response.WriteEnumeratedValue(OcspResponseStatus.Successful);
            using (response.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0)))
            using (response.PushSequence())
            {
                response.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.1");
                response.WriteOctetString(basic.Encode());
            }
        }

        return response.Encode();
    }

    private enum OcspResponseStatus
    {
        Successful = 0,
    }
}
#pragma warning restore CA5350
