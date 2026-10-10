namespace Broadside;

/// <summary>One group of adjacent objects in the shared object hint table.</summary>
/// <remarks>ISO 32000-2 F.4.3, Table F.6.</remarks>
public sealed class PdfSharedObjectHint
{
    internal PdfSharedObjectHint(int firstObjectNumber, int objectCount, long offset, long length)
    {
        FirstObjectNumber = firstObjectNumber;
        ObjectCount = objectCount;
        Offset = offset;
        Length = length;
    }

    /// <summary>Gets the object number of the group's first object; the others follow it in number order.</summary>
    /// <remarks>ISO 32000-2 Table F.5, item 1, and Table F.6, item 4.</remarks>
    public int FirstObjectNumber { get; }

    /// <summary>Gets the number of objects in the group.</summary>
    /// <remarks>ISO 32000-2 Table F.6, item 4.</remarks>
    public int ObjectCount { get; }

    /// <summary>Gets the absolute offset of the group's first object in the file.</summary>
    /// <remarks>ISO 32000-2 Table F.5, item 2, and Table F.6, item 1 (accumulated group lengths).</remarks>
    public long Offset { get; }

    /// <summary>Gets the length of the group in bytes.</summary>
    /// <remarks>ISO 32000-2 Table F.6, item 1.</remarks>
    public long Length { get; }
}
