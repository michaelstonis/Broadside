namespace Broadside.GoldenImages;

/// <summary>A backend's render of a corpus page does not match its golden image (<see cref="GoldenCorpus.AssertMatchesReference"/>).</summary>
public sealed class GoldenImageMismatchException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="GoldenImageMismatchException"/> class.</summary>
    public GoldenImageMismatchException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GoldenImageMismatchException"/> class.</summary>
    /// <param name="message">What differs and where the received and diff images are.</param>
    public GoldenImageMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GoldenImageMismatchException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public GoldenImageMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
