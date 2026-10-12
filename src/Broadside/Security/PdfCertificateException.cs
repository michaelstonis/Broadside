using System.Globalization;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.Xml;
using System.Text;

namespace Broadside.Security;

/// <summary>Why a document encrypted for certificate recipients did not open with the certificates given.</summary>
/// <remarks>ISO 32000-2 §7.6.5.1.</remarks>
public enum PdfCertificateFailure
{
    /// <summary>No certificate was given: open the document with <see cref="PdfCertificateCredentials"/> for one of its recipients.</summary>
    Required,

    /// <summary>None of the certificates given is a recipient of the document.</summary>
    NoMatchingRecipient,

    /// <summary>A certificate given is a recipient, but its private key is not available or does not decrypt the enveloped data.</summary>
    PrivateKeyUnavailable,
}

/// <summary>The exception thrown when a document encrypted for certificate recipients cannot be opened with the certificates given.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.5.1: only the listed recipients can open the document. <see cref="Recipients"/> names them (issuer and serial
/// number, or subject key identifier, RFC 5652 §6.2), so a caller can find the matching certificate, in a store for instance, and try
/// again.
/// </remarks>
public sealed class PdfCertificateException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfCertificateException"/> class.</summary>
    /// <param name="failure">Why the certificates did not open the document.</param>
    /// <param name="recipients">The recipients the document is encrypted for.</param>
    public PdfCertificateException(PdfCertificateFailure failure, IEnumerable<SubjectIdentifier> recipients)
        : this(failure, (recipients ?? throw new ArgumentNullException(nameof(recipients))).ToArray(), innerException: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateException"/> class.</summary>
    public PdfCertificateException()
        : this(PdfCertificateFailure.Required, Array.Empty<SubjectIdentifier>(), innerException: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PdfCertificateException(string? message)
        : this(message, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PdfCertificateException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Failure = PdfCertificateFailure.NoMatchingRecipient;
        Recipients = [];
    }

    /// <summary>Initializes a new instance of the <see cref="PdfCertificateException"/> class with the cause.</summary>
    internal PdfCertificateException(PdfCertificateFailure failure, SubjectIdentifier[] recipients, Exception? innerException)
        : base(Describe(failure, recipients, innerException), innerException)
    {
        Failure = failure;
        Recipients = recipients;
    }

    /// <summary>Gets why the certificates did not open the document.</summary>
    public PdfCertificateFailure Failure { get; }

    /// <summary>Gets the recipients of the document's recipient lists, in the order they appear.</summary>
    public IReadOnlyList<SubjectIdentifier> Recipients { get; }

    private static string Describe(PdfCertificateFailure failure, SubjectIdentifier[] recipients, Exception? innerException)
    {
        var message = new StringBuilder(failure switch
        {
            PdfCertificateFailure.Required => "The document is encrypted for certificate recipients; open it with the certificate and private key of one of them.",
            PdfCertificateFailure.NoMatchingRecipient => "None of the certificates given is a recipient of the document.",
            _ => "A certificate given is a recipient of the document, but its private key is not available or does not decrypt the enveloped data.",
        });
        if (innerException is not null)
        {
            message.Append(' ').Append(innerException.Message);
        }

        if (recipients.Length > 0)
        {
            message.Append(" Recipients: ");
            for (int index = 0; index < recipients.Length; index++)
            {
                message.Append(index == 0 ? string.Empty : "; ").Append(Describe(recipients[index]));
            }

            message.Append('.');
        }

        return message.ToString();
    }

    private static string Describe(SubjectIdentifier recipient) => recipient.Value switch
    {
        X509IssuerSerial serial => string.Create(CultureInfo.InvariantCulture, $"issuer '{serial.IssuerName}', serial number {serial.SerialNumber}"),
        string keyIdentifier => $"subject key identifier {keyIdentifier}",
        _ => recipient.Type.ToString(),
    };
}
