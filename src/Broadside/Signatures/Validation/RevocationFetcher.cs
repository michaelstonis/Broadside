using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures.Validation;

/// <summary>
/// Fetches revocation data through the client the caller opted in with: an OCSP request to the responder a certificate's authority
/// information access names, or the CRL its distribution point names. At most <see cref="MaxRequests"/> requests per validation; CRLs
/// are fetched once per address. A failed request yields nothing.
/// </summary>
/// <remarks>
/// RFC 6960 §4.1 (OCSPRequest, with the nonce of §4.4.1 per RFC 8954 §2.1) and Appendix A.1 (HTTP POST,
/// <c>application/ocsp-request</c>); RFC 5280 §4.2.1.13 (CRL distribution points) and §4.2.2.1 (authority information access).
/// Only <c>http</c> and <c>https</c> addresses are used.
/// </remarks>
internal sealed class RevocationFetcher(HttpClient client)
{
    /// <summary>The most requests one validation sends.</summary>
    public const int MaxRequests = 32;

    private const int MaxOcspLength = 1024 * 1024;
    private const int MaxCrlLength = 16 * 1024 * 1024;
    private const string AuthorityInformationAccessOid = "1.3.6.1.5.5.7.1.1";
    private const string OcspAccessMethod = "1.3.6.1.5.5.7.48.1";
    private const string CrlDistributionPointsOid = "2.5.29.31";
    private const string Sha1Oid = "1.3.14.3.2.26";

    private readonly Dictionary<string, byte[]?> _crls = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private int _requests;

    /// <summary>Asks the OCSP responder <paramref name="certificate"/> names about it, and returns the response if one came back.</summary>
    public async ValueTask<byte[]?> FetchOcspAsync(X509Certificate2 certificate, X509Certificate2 issuer, bool asynchronous, CancellationToken cancellationToken)
    {
        foreach (Uri uri in OcspAddresses(certificate))
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(16);
            byte[] request = EncodeRequest(certificate, issuer, nonce);
            byte[]? response = await SendAsync(HttpMethod.Post, uri, request, MaxOcspLength, asynchronous, cancellationToken).ConfigureAwait(false);
            if (response is null || !OcspResponse.TryDecode(response, out OcspResponse? decoded))
            {
                continue;
            }

            // RFC 6960 §4.4.1: a nonce in the response must be the request's (a responder may leave it out, RFC 5019 §2.2.1).
            if (decoded!.Nonce is { } echoed && !echoed.AsSpan().SequenceEqual(NonceValue(nonce)) && !echoed.AsSpan().SequenceEqual(nonce))
            {
                continue;
            }

            return response;
        }

        return null;
    }

    /// <summary>Downloads the CRLs at <paramref name="certificate"/>'s distribution points.</summary>
    public async ValueTask<List<byte[]>> FetchCrlsAsync(X509Certificate2 certificate, bool asynchronous, CancellationToken cancellationToken)
    {
        var lists = new List<byte[]>();
        foreach (Uri uri in CrlAddresses(certificate))
        {
            byte[]? crl;
            bool cached;
            lock (_lock)
            {
                cached = _crls.TryGetValue(uri.AbsoluteUri, out crl);
            }

            if (!cached)
            {
                crl = await SendAsync(HttpMethod.Get, uri, null, MaxCrlLength, asynchronous, cancellationToken).ConfigureAwait(false);
                lock (_lock)
                {
                    _crls[uri.AbsoluteUri] = crl;
                }
            }

            if (crl is not null)
            {
                lists.Add(crl);
            }
        }

        return lists;
    }

    private ValueTask<byte[]?> SendAsync(HttpMethod method, Uri uri, byte[]? body, int maxLength, bool asynchronous, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_requests >= MaxRequests)
            {
                return ValueTask.FromResult<byte[]?>(null);
            }

            _requests++;
        }

        return asynchronous
            ? new ValueTask<byte[]?>(DownloadAsync(method, uri, body, maxLength, cancellationToken))
            : ValueTask.FromResult(FetchOverNetwork(method, uri, body, maxLength, cancellationToken));
    }

    private byte[]? FetchOverNetwork(HttpMethod method, Uri uri, byte[]? body, int maxLength, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage message = CreateMessage(method, uri, body);
            using HttpResponseMessage answer = client.Send(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!answer.IsSuccessStatusCode || answer.Content.Headers.ContentLength > maxLength)
            {
                return null;
            }

            using Stream stream = answer.Content.ReadAsStream(cancellationToken);
            using var buffer = new MemoryStream();
            Span<byte> chunk = stackalloc byte[4096];
            int read;
            while ((read = stream.Read(chunk)) > 0)
            {
                if (buffer.Length + read > maxLength)
                {
                    return null;
                }

                buffer.Write(chunk[..read]);
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private async Task<byte[]?> DownloadAsync(HttpMethod method, Uri uri, byte[]? body, int maxLength, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage message = CreateMessage(method, uri, body);
            using HttpResponseMessage answer = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!answer.IsSuccessStatusCode || answer.Content.Headers.ContentLength > maxLength)
            {
                return null;
            }

            Stream stream = await answer.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[16 * 1024];
                int read;
                while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > maxLength)
                    {
                        return null;
                    }

                    await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                return buffer.ToArray();
            }
        }
        catch (Exception exception) when (IsTransportFailure(exception, cancellationToken))
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateMessage(HttpMethod method, Uri uri, byte[]? body)
    {
        var message = new HttpRequestMessage(method, uri);
        if (body is not null)
        {
            message.Content = new ByteArrayContent(body);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/ocsp-response"));
        }

        return message;
    }

    /// <summary>A failed request (refused, broken off, timed out) rather than the caller's cancellation.</summary>
    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>The OCSP responders the authority information access extension names (RFC 5280 §4.2.2.1).</summary>
    private static List<Uri> OcspAddresses(X509Certificate2 certificate)
    {
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != AuthorityInformationAccessOid)
            {
                continue;
            }

            var addresses = new List<Uri>();
            try
            {
                AsnReader access = new AsnReader(extension.RawData, AsnEncodingRules.BER).ReadSequence();
                while (access.HasData)
                {
                    AsnReader description = access.ReadSequence();
                    string method = description.ReadObjectIdentifier();
                    if (method == OcspAccessMethod && ReadUri(description) is { } uri)
                    {
                        addresses.Add(uri);
                    }
                }
            }
            catch (AsnContentException)
            {
                // An unreadable extension names no responder.
            }

            return addresses;
        }

        return [];
    }

    /// <summary>The full-name URIs of the CRL distribution points extension (RFC 5280 §4.2.1.13).</summary>
    private static List<Uri> CrlAddresses(X509Certificate2 certificate)
    {
        var addresses = new List<Uri>();
        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != CrlDistributionPointsOid)
            {
                continue;
            }

            try
            {
                AsnReader points = new AsnReader(extension.RawData, AsnEncodingRules.BER).ReadSequence();
                while (points.HasData)
                {
                    AsnReader point = points.ReadSequence();
                    if (!point.HasData || !point.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    {
                        continue;
                    }

                    AsnReader name = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
                    if (!name.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    {
                        continue;
                    }

                    AsnReader fullName = name.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
                    while (fullName.HasData)
                    {
                        if (ReadUri(fullName) is { } uri)
                        {
                            addresses.Add(uri);
                        }
                    }
                }
            }
            catch (AsnContentException)
            {
                // An unreadable extension names no list.
            }
        }

        return addresses;
    }

    /// <summary>Reads one <c>GeneralName</c>, returning it when it is an <c>http</c> or <c>https</c> uniformResourceIdentifier ([6]).</summary>
    private static Uri? ReadUri(AsnReader reader)
    {
        var uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
        if (!reader.PeekTag().HasSameClassAndValue(uriTag))
        {
            _ = reader.ReadEncodedValue();
            return null;
        }

        string text = reader.ReadCharacterString(UniversalTagNumber.IA5String, uriTag);
        return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;
    }

    /// <summary>An <c>OCSPRequest</c> for one certificate with a SHA-1 <c>CertID</c> (RFC 5019 §2.1.1) and a nonce, unsigned.</summary>
    private static byte[] EncodeRequest(X509Certificate2 certificate, X509Certificate2 issuer, byte[] nonce)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            using (writer.PushSequence())
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(Sha1Oid);
                    writer.WriteNull();
                }

                writer.WriteOctetString(IncrementalDigest.HashData(HashAlgorithmName.SHA1, certificate.IssuerName.RawData));
                writer.WriteOctetString(IncrementalDigest.HashData(HashAlgorithmName.SHA1, issuer.PublicKey.EncodedKeyValue.RawData));
                writer.WriteIntegerUnsigned(OcspResponse.TrimLeadingZeros(certificate.SerialNumberBytes).Span);
            }

            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2)))
            using (writer.PushSequence())
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(OcspResponse.NonceOid);
                writer.WriteOctetString(NonceValue(nonce));
            }
        }

        return writer.Encode();
    }

    /// <summary>The nonce extension's value: the nonce as an OCTET STRING (RFC 8954 §2.1).</summary>
    private static byte[] NonceValue(byte[] nonce)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteOctetString(nonce);
        return writer.Encode();
    }
}
