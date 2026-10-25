namespace Broadside.Images;

/// <summary>What a platform image decoder does beyond the shared rule: codings it decodes, and segments of the data it would misread.</summary>
/// <remarks>ITU-T T.81 Annex B (frames and marker segments); the Exif APP1 segment; ICC.1 Annex B.4 (APP2 <c>ICC_PROFILE</c>).</remarks>
[Flags]
public enum PlatformImageFeatures
{
    /// <summary>Baseline and progressive Huffman-coded data only, read without a colour profile or orientation.</summary>
    None = 0,

    /// <summary>Also decodes arithmetic-coded frames (SOF9, SOF10); without it they are never offered.</summary>
    ArithmeticCoding = 1,

    /// <summary>
    /// Rotates or flips the image as an Exif orientation tag says. The core zero-fills the payload of every APP1 Exif segment before
    /// handing the data over, so the samples keep the image's own orientation (ISO 32000-2 §8.9.4 maps rows to the unit square).
    /// </summary>
    AppliesExifOrientation = 2,

    /// <summary>
    /// Converts an embedded ICC profile's colours even when asked not to. The core zero-fills the payload of every APP2
    /// <c>ICC_PROFILE</c> segment first: the image dictionary's <c>ColorSpace</c> gives the samples their meaning (ISO 32000-2 §8.9.5.1).
    /// </summary>
    ConvertsEmbeddedProfiles = 4,
}
