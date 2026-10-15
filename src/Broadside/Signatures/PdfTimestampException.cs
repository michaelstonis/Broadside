namespace Broadside.Signatures;

/// <summary>
/// The exception thrown when a time-stamp authority does not issue a usable token: the HTTP request fails, the authority answers with
/// a failure status, or the token it returns does not match the request (another message imprint, nonce or policy).
/// </summary>
/// <remarks>
/// RFC 3161 §2.4.2 (the response and its status) and §2.4.1/§2.4.2 (the requester checks that the token's imprint and nonce are the
/// request's); ISO 32000-2 §12.8.3.3.1 and §12.8.5. Nothing has been written when it is thrown, and the document is left as it was.
/// </remarks>
public sealed class PdfTimestampException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfTimestampException"/> class.</summary>
    public PdfTimestampException()
        : this("The time-stamp authority did not issue a usable time-stamp token.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfTimestampException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PdfTimestampException(string? message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfTimestampException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PdfTimestampException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfTimestampException"/> class for a response with a failure status.</summary>
    /// <param name="message">The message.</param>
    /// <param name="status">The response's <c>PKIStatus</c>.</param>
    /// <param name="failureInfo">The response's <c>PKIFailureInfo</c> bits (bit n of the BIT STRING is <c>1 &lt;&lt; n</c>), or <see langword="null"/>.</param>
    internal PdfTimestampException(string message, int status, int? failureInfo)
        : base(message)
    {
        AuthorityStatus = status;
        FailureInfo = failureInfo;
    }

    /// <summary>
    /// Gets the <c>PKIStatus</c> the authority answered with (RFC 3161 §2.4.2: 2 rejection, 3 waiting, 4 revocationWarning,
    /// 5 revocationNotification), or <see langword="null"/> when the failure is not a status (a transport error or a mismatching token).
    /// </summary>
    /// <remarks>RFC 3161 §2.4.2, <c>PKIStatus</c>.</remarks>
    public int? AuthorityStatus { get; }

    /// <summary>
    /// Gets the <c>PKIFailureInfo</c> bits the authority gave with a failure status, bit n of the BIT STRING as <c>1 &lt;&lt; n</c>
    /// (0 badAlg, 2 badRequest, 5 badDataFormat, 14 timeNotAvailable, 15 unacceptedPolicy, 16 unacceptedExtension,
    /// 17 addInfoNotAvailable, 25 systemFailure), or <see langword="null"/>.
    /// </summary>
    /// <remarks>RFC 3161 §2.4.2, <c>PKIFailureInfo</c>.</remarks>
    public int? FailureInfo { get; }
}
