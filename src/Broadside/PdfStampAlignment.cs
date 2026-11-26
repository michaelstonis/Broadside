namespace Broadside;

/// <summary>Where a <see cref="PdfStamp"/> goes along one direction of the page's box, as the page is viewed.</summary>
/// <remarks>ISO 32000-2 §14.11.2 (page boundaries).</remarks>
public enum PdfStampAlignment
{
    /// <summary>At the start: the left edge across, the top edge down.</summary>
    Start,

    /// <summary>In the middle (the default).</summary>
    Center,

    /// <summary>At the end: the right edge across, the bottom edge down.</summary>
    End,
}
