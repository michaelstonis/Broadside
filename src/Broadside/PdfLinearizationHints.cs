namespace Broadside;

/// <summary>The page offset and shared object hint tables of a linearized file, decoded.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.4.1 to F.4.3. Offsets are absolute byte offsets from the start of the file (the same base as
/// <see cref="Diagnostics.Diagnostic.Offset"/>), already corrected for the primary hint stream, which the tables' own positions
/// leave out (F.4.1). Following qpdf, which is validated against Acrobat, an offset equal to the hint stream's is corrected too:
/// otherwise a first page stored right after the hint stream would be placed on it.
/// </para>
/// <para>
/// The content-stream offset and length items of the page offset table (Table F.4 items 6 and 7) and the fractional positions of
/// shared object references (item 5) are not exposed: Acrobat writes placeholder values there. The thumbnail, outline, thread,
/// named destination, interactive form, information dictionary, logical structure, page label, renditions and embedded file hint
/// tables (F.4.4 to F.4.7) are not read.
/// </para>
/// </remarks>
public sealed class PdfLinearizationHints
{
    internal PdfLinearizationHints(IReadOnlyList<PdfPageHint> pages, IReadOnlyList<PdfSharedObjectHint> sharedObjects)
    {
        Pages = pages;
        SharedObjects = sharedObjects;
    }

    /// <summary>Gets one entry per page, in the order of the page offset hint table: the first page (the linearization dictionary's <c>O</c>) first.</summary>
    /// <remarks>ISO 32000-2 F.4.2, Tables F.3 and F.4.</remarks>
    public IReadOnlyList<PdfPageHint> Pages { get; }

    /// <summary>
    /// Gets the shared object groups, in table order: the groups stored with the first page, then those in the shared objects
    /// section. <see cref="PdfPageHint.SharedObjects"/> holds indexes into this list.
    /// </summary>
    /// <remarks>ISO 32000-2 F.4.3, Tables F.5 and F.6.</remarks>
    public IReadOnlyList<PdfSharedObjectHint> SharedObjects { get; }
}
