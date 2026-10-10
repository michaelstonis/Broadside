namespace Broadside.Images;

/// <summary>The colour model an image codec reports for the samples it decoded, after its own colour transforms.</summary>
/// <remarks>
/// ISO 32000-2 §7.4.9: a JPEG 2000 image without a <c>ColorSpace</c> entry takes its colour space from the codestream's colour
/// specification, else from its channel count (1 Gray, 3 RGB, 4 CMYK). A codec reports the enumerated space it found here and the
/// ICC profile, if any, in <see cref="DecodedImage.IccProfile"/>. The image dictionary's <c>ColorSpace</c>, when present, wins.
/// </remarks>
public enum ImageColorModel
{
    /// <summary>The codec reports no colour model; the image dictionary's colour space applies.</summary>
    Unknown,

    /// <summary>One gray component.</summary>
    Gray,

    /// <summary>Three components: red, green, blue.</summary>
    Rgb,

    /// <summary>Four components: cyan, magenta, yellow, black.</summary>
    Cmyk,

    /// <summary>Three components: CIE L*, a*, b*.</summary>
    Lab,
}
