using Broadside.Objects;

namespace Broadside.Images;

/// <summary>An alternate image: another version of an image, for printing or for another optional content state.</summary>
/// <remarks>
/// ISO 32000-2 §8.9.5.4, Table 89. Read from the base image's <c>Alternates</c> array (PDF 1.3). Which alternate to use is the
/// viewer's choice (the selection rules of §8.9.5.4 depend on the output device and optional content), so none is selected here.
/// </remarks>
public sealed class PdfAlternateImage
{
    internal PdfAlternateImage(CosDictionary dictionary, PdfImage image, bool defaultForPrinting, CosDictionary? optionalContent)
    {
        Dictionary = dictionary;
        Image = image;
        DefaultForPrinting = defaultForPrinting;
        OptionalContent = optionalContent;
    }

    /// <summary>Gets the alternate image dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.4, Table 89.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the alternate image (<c>Image</c>).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.4, Table 89 (required).</remarks>
    public PdfImage Image { get; }

    /// <summary>Gets a value indicating whether this alternate is the one to print (<c>DefaultForPrinting</c>, default false).</summary>
    /// <remarks>ISO 32000-2 §8.9.5.4, Table 89.</remarks>
    public bool DefaultForPrinting { get; }

    /// <summary>Gets the optional content group or membership dictionary that selects this alternate (<c>OC</c>, PDF 1.5), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §8.9.5.4, Table 89, and §8.11.</remarks>
    public CosDictionary? OptionalContent { get; }
}
