namespace Broadside.Images;

/// <summary>The transform code of a JPEG Adobe APP14 marker: how the file says its components relate to the colors they encode.</summary>
/// <remarks>
/// ISO 32000-2 §7.4.8, Table 13 (<c>ColorTransform</c>) and Adobe Technical Note #5116 §18. The values are the marker's own byte, so
/// a file with a code the note does not define yields a value that is none of these (the decoder records a diagnostic). A file without
/// an APP14 marker has no transform code at all, which <see cref="DecodedImage.AdobeTransform"/> reports as <see langword="null"/>.
/// </remarks>
public enum AdobeColorTransform
{
    /// <summary>No transform: three components are RGB, four are CMYK.</summary>
    /// <remarks>Adobe Technical Note #5116 §18: code 0, "Unknown (RGB or CMYK)".</remarks>
    Unknown = 0,

    /// <summary>Three components are YCbCr.</summary>
    /// <remarks>Adobe Technical Note #5116 §18: code 1.</remarks>
    YCbCr = 1,

    /// <summary>Four components are YCCK: YCbCr plus black, to be converted to CMYK.</summary>
    /// <remarks>Adobe Technical Note #5116 §18: code 2.</remarks>
    YCCK = 2,
}
