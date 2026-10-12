namespace Broadside;

/// <summary>One page's entry in the page offset hint table: where the page's objects are and which shared objects it needs.</summary>
/// <remarks>ISO 32000-2 F.4.2, Table F.4.</remarks>
public sealed class PdfPageHint
{
    internal PdfPageHint(int firstObjectNumber, int objectCount, long offset, long length, IReadOnlyList<int> sharedObjects)
    {
        FirstObjectNumber = firstObjectNumber;
        ObjectCount = objectCount;
        Offset = offset;
        Length = length;
        SharedObjects = sharedObjects;
    }

    /// <summary>
    /// Gets the object number of the page's first object: the linearization dictionary's <c>O</c> for the first page, 1 for the
    /// second, then accumulated over the object counts of the pages before.
    /// </summary>
    /// <remarks>ISO 32000-2 Table F.4, item 1.</remarks>
    public int FirstObjectNumber { get; }

    /// <summary>Gets the number of objects of the page, the page object included.</summary>
    /// <remarks>ISO 32000-2 Table F.4, item 1.</remarks>
    public int ObjectCount { get; }

    /// <summary>Gets the absolute offset of the page's page object in the file.</summary>
    /// <remarks>ISO 32000-2 Table F.3, item 2, and Table F.4, item 2: the first page's location, then accumulated page lengths.</remarks>
    public long Offset { get; }

    /// <summary>Gets the length of the page in bytes, from its page object to the last byte of its last object.</summary>
    /// <remarks>ISO 32000-2 Table F.4, item 2.</remarks>
    public long Length { get; }

    /// <summary>Gets the shared object identifiers the page references: indexes into <see cref="PdfLinearizationHints.SharedObjects"/>.</summary>
    /// <remarks>ISO 32000-2 Table F.4, items 3 and 4. Empty for the first page in a conforming file.</remarks>
    public IReadOnlyList<int> SharedObjects { get; }
}
