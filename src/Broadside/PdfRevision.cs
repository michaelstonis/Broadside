using Broadside.Objects;

namespace Broadside;

/// <summary>
/// One revision of a document: the original file or one incremental update appended to it, as the bytes from the start of the file
/// to that revision's end-of-file marker.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.6. Updates are appended and leave earlier bytes intact, so revision k is exactly the first <see cref="Length"/>
/// bytes of the file: that prefix is the complete file as it was after the k-th save. The range ends just past the <c>%%EOF</c>
/// marker that terminates the revision's trailer, including the end-of-line marker after it when there is one.
/// </para>
/// <para>
/// A linearized file's first-page and main cross-reference sections (Annex F, F.3.4) form one revision, the original; a hybrid
/// file's cross-reference stream belongs to the revision of the table that names it (§7.5.8.4).
/// </para>
/// </remarks>
public sealed class PdfRevision
{
    internal PdfRevision(int index, long length, CosDictionary trailer)
    {
        Index = index;
        Length = length;
        Trailer = trailer;
    }

    /// <summary>Gets the zero-based position of the revision: 0 for the original file, 1 for the first update, and so on.</summary>
    public int Index { get; }

    /// <summary>
    /// Gets the length of the revision in bytes. The revision is the bytes from offset 0 of the file (not of the <c>%PDF-</c> header)
    /// up to, not including, this offset.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.5 and §7.5.6: each trailer is terminated by its own <c>%%EOF</c> marker.</remarks>
    public long Length { get; }

    /// <summary>Gets the revision's own trailer dictionary, as stored in the file (for a linearized original, the first-page trailer).</summary>
    /// <remarks>ISO 32000-2 §7.5.5, Table 15, and §7.5.6.</remarks>
    public CosDictionary Trailer { get; }
}
