namespace Broadside;

/// <summary>How an explicit destination positions and magnifies its page: the view name in its array.</summary>
/// <remarks>ISO 32000-2 §12.3.2.2, Table 149.</remarks>
public enum PdfDestinationView
{
    /// <summary>The array names no view this library knows, or none at all; the destination is not valid.</summary>
    Unknown = 0,

    /// <summary><c>[page /XYZ left top zoom]</c>: (left, top) at the upper-left corner of the window, magnified by zoom; a null keeps the current value (PDF 1.0).</summary>
    Xyz,

    /// <summary><c>[page /Fit]</c>: the whole page fits the window (PDF 1.0).</summary>
    Fit,

    /// <summary><c>[page /FitH top]</c>: top at the top edge, the page width fits the window (PDF 1.0).</summary>
    FitH,

    /// <summary><c>[page /FitV left]</c>: left at the left edge, the page height fits the window (PDF 1.0).</summary>
    FitV,

    /// <summary><c>[page /FitR left bottom right top]</c>: the rectangle fits the window (PDF 1.0).</summary>
    FitR,

    /// <summary><c>[page /FitB]</c>: the page's bounding box fits the window (PDF 1.1).</summary>
    FitB,

    /// <summary><c>[page /FitBH top]</c>: top at the top edge, the bounding box width fits the window (PDF 1.1).</summary>
    FitBH,

    /// <summary><c>[page /FitBV left]</c>: left at the left edge, the bounding box height fits the window (PDF 1.1).</summary>
    FitBV,
}
