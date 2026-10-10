namespace Broadside;

/// <summary>The page scaling a print dialog starts with: the viewer preference <c>PrintScaling</c>.</summary>
/// <remarks>ISO 32000-2 §12.2, Table 147 (PDF 1.6). The default, and the value an unrecognized name reads as, is <see cref="AppDefault"/>.</remarks>
public enum PdfPrintScaling
{
    /// <summary>No page scaling (<c>None</c>).</summary>
    None,

    /// <summary>The viewer's default print scaling (<c>AppDefault</c>, the default).</summary>
    AppDefault,
}
