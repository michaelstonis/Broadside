namespace Broadside;

/// <summary>A page boundary, named by its key in the page object: what the viewer preferences <c>ViewArea</c>, <c>ViewClip</c>, <c>PrintArea</c> and <c>PrintClip</c> select.</summary>
/// <remarks>ISO 32000-2 §12.2, Table 147 (PDF 1.4, deprecated in PDF 2.0), and §14.11.2. The default is <see cref="CropBox"/>.</remarks>
public enum PdfPageBoundary
{
    /// <summary>The crop box (the default).</summary>
    CropBox,

    /// <summary>The media box.</summary>
    MediaBox,

    /// <summary>The bleed box.</summary>
    BleedBox,

    /// <summary>The trim box.</summary>
    TrimBox,

    /// <summary>The art box.</summary>
    ArtBox,
}
