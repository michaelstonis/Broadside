using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Broadside.Security.Cryptography;

namespace Broadside.Signatures;

/// <summary>
/// A client of an RFC 3161 time-stamp authority over HTTP, through the <see cref="HttpClient"/> it is given: it time-stamps
/// signatures (PAdES B-T, through <see cref="PdfSignatureOptions.TimestampClient"/>) and documents
/// (<see cref="PdfDocument.Timestamp"/>).
/// </summary>
/// <remarks>
/// <para>
/// RFC 3161 §2.4 and §3.4 (time-stamp protocol via HTTP), as updated by RFC 5816; ISO 32000-2 §12.8.3.3.1 (timestamp information as an
/// unsigned attribute), §12.8.3.4.3 b) (the PAdES signature timestamp) and §12.8.5 (document timestamps). A request is a
/// <c>TimeStampReq</c> of the hash of the data with <see cref="DigestAlgorithm"/>, a fresh random nonce, <see cref="Policy"/> when
/// set, and <c>certReq</c> true, so the token carries the authority's certificate; it is POSTed to <see cref="AuthorityUri"/> as
/// <c>application/timestamp-query</c>. The response must grant the request and hold a token whose message imprint, nonce and policy
/// are the request's; anything else throws <see cref="PdfTimestampException"/>, and nothing is written. The token's signature and
/// the authority's certificate path are not validated here: validating signatures does that.
/// </para>
/// <para>
/// The <see cref="HttpClient"/> is the transport: its handler decides how the request travels (proxies, authentication, retries), and
/// a test can give it a handler that answers in process. The client does not dispose it. The synchronous
/// <see cref="PdfDocument.Sign"/> and <see cref="PdfDocument.Timestamp"/> send the request with <see cref="HttpClient.Send(HttpRequestMessage, CancellationToken)"/>,
/// which the browser does not support: use the asynchronous methods there. A client may be shared between threads and documents.
/// </para>
/// </remarks>
public sealed class PdfTimestampClient
{
    /// <summary>The longest response read: a token with a long certificate chain is a few kilobytes.</summary>
    private const int MaximumResponseLength = 1 << 20;

    private readonly HttpClient _httpClient;
    private HashAlgorithmName _digestAlgorithm = HashAlgorithmName.SHA256;
    private string? _policy;
    private int _tokenSizeEstimate = 8192;

    /// <summary>Initializes a new instance of the <see cref="PdfTimestampClient"/> class.</summary>
    /// <param name="httpClient">The client requests go through; not disposed by this one.</param>
    /// <param name="authorityUri">The absolute URI of the time-stamp authority's HTTP endpoint.</param>
    /// <exception cref="ArgumentException"><paramref name="authorityUri"/> is not absolute.</exception>
    /// <remarks>RFC 3161 §3.4.</remarks>
    public PdfTimestampClient(HttpClient httpClient, Uri authorityUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(authorityUri);
        if (!authorityUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The time-stamp authority's URI must be absolute.", nameof(authorityUri));
        }

        _httpClient = httpClient;
        AuthorityUri = authorityUri;
    }

    /// <summary>Gets the time-stamp authority's HTTP endpoint.</summary>
    /// <remarks>RFC 3161 §3.4.</remarks>
    public Uri AuthorityUri { get; }

    /// <summary>
    /// Gets or sets the hash algorithm of the message imprint: SHA-256 (the default), SHA-384 or SHA-512, or a SHA-3 algorithm where
    /// signing supports it. The authority must accept it.
    /// </summary>
    /// <exception cref="NotSupportedException">The algorithm is not one signing supports.</exception>
    /// <remarks>RFC 3161 §2.4.1, <c>messageImprint</c>; ISO 32000-2 §12.8.3.1, Table 260.</remarks>
    public HashAlgorithmName DigestAlgorithm
    {
        get => _digestAlgorithm;
        set
        {
            CmsSignedData.RequireSupportedDigest(value);
            _digestAlgorithm = value;
        }
    }

    /// <summary>
    /// Gets or sets the object identifier of the policy to ask the authority to issue tokens under (<c>reqPolicy</c>), in dotted
    /// form; <see langword="null"/> (the default) accepts the authority's default policy.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a dotted object identifier.</exception>
    /// <remarks>RFC 3161 §2.4.1, <c>reqPolicy</c>; §2.4.2: a token under another policy is refused.</remarks>
    public string? Policy
    {
        get => _policy;
        set
        {
            if (value is not null)
            {
                try
                {
                    new AsnWriter(AsnEncodingRules.DER).WriteObjectIdentifier(value);
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException($"'{value}' is not a dotted object identifier.", nameof(value), exception);
                }
            }

            _policy = value;
        }
    }

    /// <summary>
    /// Gets or sets how many bytes to reserve in the signature's <c>Contents</c> for the token, which is only known once it arrives,
    /// after the byte range is fixed: 8192 by default, enough for an authority certificate and its issuers. A token that does not
    /// fit throws <see cref="InvalidOperationException"/> and nothing is written; raise this for an authority with a long chain.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255, <c>Contents</c>: "Space for the Contents value shall be allocated before the message digest is computed".</remarks>
    public int TokenSizeEstimate
    {
        get => _tokenSizeEstimate;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _tokenSizeEstimate = value;
        }
    }

    /// <summary>Sets <see cref="DigestAlgorithm"/>.</summary>
    /// <param name="digestAlgorithm">The message imprint's hash algorithm.</param>
    /// <returns>This client.</returns>
    /// <remarks>RFC 3161 §2.4.1.</remarks>
    public PdfTimestampClient WithDigestAlgorithm(HashAlgorithmName digestAlgorithm)
    {
        DigestAlgorithm = digestAlgorithm;
        return this;
    }

    /// <summary>Sets <see cref="Policy"/>.</summary>
    /// <param name="policy">The policy's object identifier, or <see langword="null"/>.</param>
    /// <returns>This client.</returns>
    /// <remarks>RFC 3161 §2.4.1.</remarks>
    public PdfTimestampClient WithPolicy(string? policy)
    {
        Policy = policy;
        return this;
    }

    /// <summary>Sets <see cref="TokenSizeEstimate"/>.</summary>
    /// <param name="tokenSizeEstimate">The bytes to reserve for the token.</param>
    /// <returns>This client.</returns>
    /// <remarks>ISO 32000-2 §12.8.1, Table 255.</remarks>
    public PdfTimestampClient WithTokenSizeEstimate(int tokenSizeEstimate)
    {
        TokenSizeEstimate = tokenSizeEstimate;
        return this;
    }

    /// <summary>Asks the authority for a token over the hash <paramref name="imprint"/>, sending the request synchronously.</summary>
    /// <param name="digestAlgorithm">The algorithm <paramref name="imprint"/> was computed with.</param>
    /// <param name="imprint">The hash of the data to time-stamp.</param>
    /// <returns>The checked DER-encoded <c>TimeStampToken</c>.</returns>
    /// <exception cref="PdfTimestampException">The request failed or the token does not answer it.</exception>
    internal byte[] RequestToken(HashAlgorithmName digestAlgorithm, byte[] imprint)
    {
        Request request = CreateRequest(digestAlgorithm, imprint);
        byte[] response;
        try
        {
            using HttpRequestMessage message = request.ToMessage(AuthorityUri);
            using HttpResponseMessage answer = _httpClient.Send(message, HttpCompletionOption.ResponseHeadersRead);
            RequireSuccess(answer);
            using Stream body = answer.Content.ReadAsStream();
            response = ReadBody(body);
        }
        catch (HttpRequestException exception)
        {
            throw TransportFailure(exception);
        }
        catch (TaskCanceledException exception)
        {
            throw TransportFailure(exception);
        }

        return request.Read(response);
    }

    /// <summary>Asks the authority for a token over the hash <paramref name="imprint"/>.</summary>
    /// <param name="digestAlgorithm">The algorithm <paramref name="imprint"/> was computed with.</param>
    /// <param name="imprint">The hash of the data to time-stamp.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The checked DER-encoded <c>TimeStampToken</c>.</returns>
    /// <exception cref="PdfTimestampException">The request failed or the token does not answer it.</exception>
    internal async Task<byte[]> RequestTokenAsync(HashAlgorithmName digestAlgorithm, byte[] imprint, CancellationToken cancellationToken)
    {
        Request request = CreateRequest(digestAlgorithm, imprint);
        byte[] response;
        try
        {
            using HttpRequestMessage message = request.ToMessage(AuthorityUri);
            using HttpResponseMessage answer = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            RequireSuccess(answer);
            Stream body = await answer.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                byte[] chunk = new byte[16 * 1024];
                int read;
                while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    Append(buffer, chunk.AsSpan(0, read));
                }

                response = buffer.ToArray();
            }
        }
        catch (HttpRequestException exception)
        {
            throw TransportFailure(exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw TransportFailure(exception);
        }

        return request.Read(response);
    }

    private Request CreateRequest(HashAlgorithmName digestAlgorithm, byte[] imprint)
    {
        string digestOid = CmsSignedData.DigestOid(digestAlgorithm);
        byte[] nonce = TimestampProtocol.CreateNonce();
        string? policy = _policy;
        return new Request(TimestampProtocol.EncodeRequest(digestOid, imprint, nonce, policy, requestCertificates: true), digestOid, imprint, nonce, policy);
    }

    /// <summary>The hash of <paramref name="data"/> with <see cref="DigestAlgorithm"/>, what a request over it carries.</summary>
    internal byte[] Hash(ReadOnlySpan<byte> data) => IncrementalDigest.HashData(_digestAlgorithm, data);

    private void RequireSuccess(HttpResponseMessage answer)
    {
        if (!answer.IsSuccessStatusCode)
        {
            throw new PdfTimestampException(
                $"The time-stamp authority at {AuthorityUri} answered HTTP {(int)answer.StatusCode} {answer.ReasonPhrase}, not a time-stamp response (RFC 3161 §3.4).");
        }
    }

    private PdfTimestampException TransportFailure(Exception exception) =>
        new($"The request to the time-stamp authority at {AuthorityUri} failed: {exception.Message}", exception);

    private static byte[] ReadBody(Stream body)
    {
        using var buffer = new MemoryStream();
        Span<byte> chunk = stackalloc byte[4096];
        int read;
        while ((read = body.Read(chunk)) > 0)
        {
            Append(buffer, chunk[..read]);
        }

        return buffer.ToArray();
    }

    private static void Append(MemoryStream buffer, ReadOnlySpan<byte> chunk)
    {
        if (buffer.Length + chunk.Length > MaximumResponseLength)
        {
            throw new PdfTimestampException($"The time-stamp authority's response is longer than {MaximumResponseLength} bytes; a time-stamp response is a few kilobytes.");
        }

        buffer.Write(chunk);
    }

    /// <summary>A request with what its response is checked against.</summary>
    private sealed record Request(byte[] Body, string DigestOid, byte[] Imprint, byte[] Nonce, string? Policy)
    {
        public HttpRequestMessage ToMessage(Uri uri)
        {
            var content = new ByteArrayContent(Body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
            var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/timestamp-reply"));
            return message;
        }

        public byte[] Read(byte[] response) => TimestampProtocol.ReadResponse(response, DigestOid, Imprint, Nonce, Policy, requestedCertificates: true);
    }
}
