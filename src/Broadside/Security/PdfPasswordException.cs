namespace Broadside.Security;

/// <summary>Why an encrypted document did not open with the password given.</summary>
/// <remarks>ISO 32000-2 §7.6.4.1.</remarks>
public enum PdfPasswordFailure
{
    /// <summary>No password, or the empty one, was given and the document has a user password: ask for one.</summary>
    Required,

    /// <summary>The password given is neither the user nor the owner password.</summary>
    Incorrect,
}

/// <summary>The exception thrown when an encrypted document cannot be opened with the password given.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.4.1: a reader first tries the default (empty) user password and, when that fails, should prompt for a password.
/// <see cref="Failure"/> tells the two cases apart, so a caller can prompt on <see cref="PdfPasswordFailure.Required"/> and report a
/// typing mistake on <see cref="PdfPasswordFailure.Incorrect"/>.
/// </remarks>
public sealed class PdfPasswordException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class.</summary>
    /// <param name="failure">Why the password did not open the document.</param>
    public PdfPasswordException(PdfPasswordFailure failure)
        : base(failure == PdfPasswordFailure.Required
            ? "The document is encrypted with a user password; open it with the user or the owner password."
            : "The password is neither the document's user password nor its owner password.") => Failure = failure;

    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class.</summary>
    public PdfPasswordException()
        : this(PdfPasswordFailure.Required)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PdfPasswordException(string? message)
        : this(message, null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfPasswordException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PdfPasswordException(string? message, Exception? innerException)
        : base(message, innerException) => Failure = PdfPasswordFailure.Incorrect;

    /// <summary>Gets why the password did not open the document.</summary>
    public PdfPasswordFailure Failure { get; }
}

/// <summary>What about a document's encryption this engine cannot handle.</summary>
public enum PdfEncryptionNotSupportedReason
{
    /// <summary>No registered security handler implements the encryption dictionary's <c>Filter</c> or <c>SubFilter</c>.</summary>
    Handler,

    /// <summary>The handler does not implement the algorithm, revision or crypt filter method the document uses.</summary>
    Algorithm,

    /// <summary>The algorithm is not available on this platform.</summary>
    Platform,
}

/// <summary>The exception thrown when a document is encrypted in a way this engine cannot decrypt.</summary>
/// <remarks>
/// ISO 32000-2 §7.6.2 (Table 20: only the handler a document names, or one implementing its <c>SubFilter</c>, may open it) and §7.6.6
/// (Table 25: a reader that meets an unknown crypt filter method "shall report that the file is encrypted with an unsupported
/// algorithm"). Distinct from <see cref="PdfPasswordException"/>: no password will open the document with this engine.
/// </remarks>
public sealed class PdfEncryptionNotSupportedException : NotSupportedException
{
    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionNotSupportedException"/> class.</summary>
    /// <param name="reason">What is not supported.</param>
    /// <param name="message">The message.</param>
    public PdfEncryptionNotSupportedException(PdfEncryptionNotSupportedReason reason, string? message)
        : base(message) => Reason = reason;

    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionNotSupportedException"/> class.</summary>
    public PdfEncryptionNotSupportedException()
        : this(PdfEncryptionNotSupportedReason.Algorithm, "The document's encryption is not supported.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionNotSupportedException"/> class.</summary>
    /// <param name="message">The message.</param>
    public PdfEncryptionNotSupportedException(string? message)
        : this(PdfEncryptionNotSupportedReason.Algorithm, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionNotSupportedException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public PdfEncryptionNotSupportedException(string? message, Exception? innerException)
        : base(message, innerException) => Reason = PdfEncryptionNotSupportedReason.Algorithm;

    /// <summary>Gets what is not supported.</summary>
    public PdfEncryptionNotSupportedReason Reason { get; }
}
