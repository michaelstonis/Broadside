namespace Broadside;

/// <summary>What applying a redaction does with an image that an area covers only in part.</summary>
/// <remarks>ISO 32000-2 §12.5.6.23 (Table 195), §8.9.5. An image wholly inside an area is always removed.</remarks>
public enum PdfRedactionImageMode
{
    /// <summary>
    /// The covered samples (and the same samples of the image's soft mask or mask) are to be set to the fill and the image written
    /// again as a new image XObject. Until sample blanking lands (issue #624, second part), a partly covered image is removed whole
    /// and a <c>RedactionImageRemovedWhole</c> diagnostic is recorded.
    /// </summary>
    BlankPixels,

    /// <summary>An image any part of which an area covers is removed whole.</summary>
    RemoveImage,
}
