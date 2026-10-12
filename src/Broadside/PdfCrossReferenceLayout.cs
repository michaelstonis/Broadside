namespace Broadside;

/// <summary>How a saved file stores its cross-reference information and its objects.</summary>
/// <remarks>
/// <para>ISO 32000-2 §7.5.4 (cross-reference table), §7.5.7 (object streams) and §7.5.8 (cross-reference streams).</para>
/// <para>
/// The supported combinations, and the minimum PDF version each needs (the header is raised to it when the document's own version is
/// lower; ADR 0003):
/// </para>
/// <list type="table">
/// <listheader><term>Layout</term><description>Cross-reference / object streams / minimum version</description></listheader>
/// <item><term><see cref="Table"/></term><description>Classic table / none / 1.0</description></item>
/// <item><term><see cref="Stream"/></term><description>Cross-reference stream / none / 1.5</description></item>
/// <item><term><see cref="StreamWithObjectStreams"/></term><description>Cross-reference stream / yes / 1.5</description></item>
/// </list>
/// <para>
/// A classic table with object streams is possible only as a hybrid file (§7.5.8.4), which is meant for readers older than
/// PDF 1.5 and is not written.
/// </para>
/// </remarks>
public enum PdfCrossReferenceLayout
{
    /// <summary>
    /// A classic cross-reference table and every object at the top level of the file body (§7.5.4). Readable by every PDF reader.
    /// Offsets are limited to ten digits, so the file must be smaller than 10 GB.
    /// </summary>
    Table,

    /// <summary>A cross-reference stream (§7.5.8) and every object at the top level of the file body. PDF 1.5.</summary>
    Stream,

    /// <summary>
    /// A cross-reference stream (§7.5.8), with every object that may be stored in an object stream stored in one (§7.5.7): neither
    /// streams nor objects with a generation other than 0 nor objects that are only an indirect reference. The most compact layout.
    /// PDF 1.5.
    /// </summary>
    StreamWithObjectStreams,
}
