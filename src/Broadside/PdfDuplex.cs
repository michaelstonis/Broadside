namespace Broadside;

/// <summary>The paper handling a print dialog starts with: the viewer preference <c>Duplex</c>.</summary>
/// <remarks>ISO 32000-2 §12.2, Table 147 (PDF 1.7). The default is implementation dependent.</remarks>
public enum PdfDuplex
{
    /// <summary>Single-sided (<c>Simplex</c>).</summary>
    Simplex,

    /// <summary>Double-sided, flipping on the short edge of the sheet (<c>DuplexFlipShortEdge</c>).</summary>
    DuplexFlipShortEdge,

    /// <summary>Double-sided, flipping on the long edge of the sheet (<c>DuplexFlipLongEdge</c>).</summary>
    DuplexFlipLongEdge,
}
