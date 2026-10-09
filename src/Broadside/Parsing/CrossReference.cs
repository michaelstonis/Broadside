using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>The kind of a cross-reference entry.</summary>
/// <remarks>ISO 32000-2 §7.5.4 (free and in-use entries) and §7.5.8.3, Table 18 (compressed entries, type 2).</remarks>
internal enum XrefEntryKind : byte
{
    /// <summary>A free (deleted or never used) object number. A reference to it resolves to null (§7.3.10).</summary>
    Free,

    /// <summary>An object stored in the file body at a byte offset.</summary>
    InUse,

    /// <summary>An object stored inside an object stream (§7.5.7). Read by issue #39.</summary>
    Compressed,
}

/// <summary>One cross-reference entry.</summary>
/// <param name="Kind">Whether the entry is free, in use or compressed.</param>
/// <param name="Offset">
/// <see cref="XrefEntryKind.InUse"/>: the byte offset of the object, relative to the <c>%PDF-</c> header (§7.5.2).
/// <see cref="XrefEntryKind.Free"/>: the next free object number. <see cref="XrefEntryKind.Compressed"/>: the object number of the
/// object stream.
/// </param>
/// <param name="Generation">
/// <see cref="XrefEntryKind.InUse"/> and <see cref="XrefEntryKind.Free"/>: the generation number.
/// <see cref="XrefEntryKind.Compressed"/>: the index of the object inside the object stream.
/// </param>
internal readonly record struct XrefEntry(XrefEntryKind Kind, long Offset, int Generation)
{
    /// <summary>A free entry with no successor, what an absent or zero-offset entry reads as.</summary>
    public static readonly XrefEntry Free = new(XrefEntryKind.Free, 0, 0);
}

/// <summary>Whether a cross-reference section is a classic table (§7.5.4) or a cross-reference stream (§7.5.8).</summary>
internal enum XrefSectionKind : byte
{
    /// <summary>A classic <c>xref</c> table followed by <c>trailer</c>.</summary>
    Table,

    /// <summary>A cross-reference stream (issue #39).</summary>
    Stream,
}

/// <summary>One cross-reference section as read from the file: its entries and its trailer.</summary>
/// <remarks>ISO 32000-2 §7.5.4 and §7.5.5. Kept per section, never only merged, for incremental updates (#40) and repair (#41).</remarks>
/// <param name="Offset">The absolute byte offset of the section (the <c>xref</c> keyword, or the stream object).</param>
/// <param name="End">The absolute byte offset just past the section's trailer dictionary.</param>
/// <param name="Kind">Table or stream.</param>
/// <param name="Entries">The entries, by object number. Object number 0 is never present.</param>
/// <param name="Trailer">The section's trailer dictionary (for a stream, the stream dictionary).</param>
internal sealed record XrefSection(long Offset, long End, XrefSectionKind Kind, IReadOnlyDictionary<int, XrefEntry> Entries, CosDictionary Trailer)
{
    /// <summary>Gets the offset of the hybrid-file cross-reference stream this table's trailer names (§7.5.8.4), when issue #39 follows it.</summary>
    public long? XRefStreamOffset { get; init; }
}

/// <summary>
/// The cross-reference information of a file: its sections, newest first, and the lookup they define together.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.5.4, §7.5.5 and §7.5.6. Lookup is newest first, first write wins: an object number takes its entry from the most
/// recent section that lists it. The trailer is the newest section's, with keys only an older trailer has filled in (a lenient
/// fallback; §7.5.6 says each update's trailer repeats them).
/// </remarks>
internal sealed class CrossReference
{
    private readonly Dictionary<int, XrefEntry> _merged = [];

    /// <summary>Initializes a new instance of the <see cref="CrossReference"/> class.</summary>
    /// <param name="sections">The sections, newest first. At least one.</param>
    public CrossReference(IReadOnlyList<XrefSection> sections)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sections.Count);
        Sections = sections;
        var trailer = new OrderedDictionary<CosName, CosObject>();
        foreach (XrefSection section in sections)
        {
            foreach ((int number, XrefEntry entry) in section.Entries)
            {
                _merged.TryAdd(number, entry);
            }

            foreach (KeyValuePair<CosName, CosObject> entry in section.Trailer)
            {
                trailer.TryAdd(entry.Key, entry.Value);
            }
        }

        Trailer = CosDictionary.FromOwnedEntries(trailer);
    }

    /// <summary>Gets the sections, newest first.</summary>
    public IReadOnlyList<XrefSection> Sections { get; }

    /// <summary>Gets the merged trailer dictionary.</summary>
    public CosDictionary Trailer { get; }

    /// <summary>Finds the entry for an object number.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="entry">The newest entry, or <see cref="XrefEntry.Free"/> when no section lists the number.</param>
    /// <returns><see langword="true"/> when a section lists the number.</returns>
    public bool TryGetEntry(int objectNumber, out XrefEntry entry)
    {
        if (_merged.TryGetValue(objectNumber, out entry))
        {
            return true;
        }

        entry = XrefEntry.Free;
        return false;
    }
}
