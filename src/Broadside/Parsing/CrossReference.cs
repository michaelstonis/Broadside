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

    /// <summary>An object stored inside an object stream (§7.5.7), type 2 in a cross-reference stream (§7.5.8.3, Table 18).</summary>
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

    /// <summary>A cross-reference stream (§7.5.8).</summary>
    Stream,
}

/// <summary>One cross-reference section as read from the file: its entries and its trailer.</summary>
/// <remarks>ISO 32000-2 §7.5.4 and §7.5.5. Kept per section, never only merged, for incremental updates (#40) and repair (#41).</remarks>
/// <param name="Offset">The absolute byte offset of the section (the <c>xref</c> keyword, or the stream object).</param>
/// <param name="End">
/// The absolute byte offset just past the section's trailer dictionary; for a stream, just past the stream object's <c>endobj</c>.
/// </param>
/// <param name="Kind">Table or stream.</param>
/// <param name="Entries">The entries, by object number. Object number 0 is never present.</param>
/// <param name="Trailer">
/// The section's trailer dictionary. For a cross-reference stream, its dictionary without the entries that describe the stream
/// itself (Type, Length, W, Index, Filter, DecodeParms, DL, F, FFilter, FDecodeParms); for the stream a hybrid file's table names
/// through <c>XRefStm</c>, an empty dictionary, since the table's trailer is the trailer (§7.5.8.4).
/// </param>
internal sealed record XrefSection(long Offset, long End, XrefSectionKind Kind, IReadOnlyDictionary<int, XrefEntry> Entries, CosDictionary Trailer)
{
    /// <summary>
    /// Gets the absolute offset of the cross-reference stream this table's trailer names through <c>XRefStm</c> (§7.5.8.4), when it
    /// was read; that stream is the next section in <see cref="CrossReference.Sections"/>.
    /// </summary>
    public long? XRefStreamOffset { get; init; }

    /// <summary>Gets the cross-reference stream object of a <see cref="XrefSectionKind.Stream"/> section, as parsed.</summary>
    public CosStream? Stream { get; init; }
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
        Revisions = GroupRevisions(sections);
        var trailer = new OrderedDictionary<CosName, CosObject>();
        var inherited = new List<CosName>();
        for (int revision = Revisions.Count - 1; revision >= 0; revision--)
        {
            foreach (XrefSection section in Revisions[revision].Sections)
            {
                foreach ((int number, XrefEntry entry) in section.Entries)
                {
                    _merged.TryAdd(number, entry);
                }

                foreach (KeyValuePair<CosName, CosObject> entry in section.Trailer)
                {
                    if (trailer.TryAdd(entry.Key, entry.Value) && revision < Revisions.Count - 1)
                    {
                        inherited.Add(entry.Key);
                    }
                }
            }
        }

        Trailer = CosDictionary.FromOwnedEntries(trailer);
        InheritedTrailerKeys = inherited;
    }

    /// <summary>Gets the sections, newest first.</summary>
    public IReadOnlyList<XrefSection> Sections { get; }

    /// <summary>Gets the revisions the sections form, oldest first (§7.5.6).</summary>
    /// <remarks>
    /// <para>
    /// Each update appends one revision. A revision usually has one section; it has more when the file is linearized (the
    /// first-page section and the main section, F.3.4, form the original revision: the first-page section's <c>Prev</c> points
    /// forward, to a higher offset, which an appended update never does) or when a hybrid file's table names a cross-reference
    /// stream through <c>XRefStm</c> (§7.5.8.4: the stream belongs to its table's revision).
    /// </para>
    /// </remarks>
    public IReadOnlyList<XrefRevision> Revisions { get; }

    /// <summary>
    /// Gets the trailer keys the newest revision's trailer lacks and an older revision's trailer supplied (§7.5.6: an update's
    /// trailer shall repeat every entry of the previous one except <c>Prev</c>).
    /// </summary>
    public IReadOnlyList<CosName> InheritedTrailerKeys { get; }

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

    /// <summary>Splits the sections, newest first, into revisions, oldest first.</summary>
    private static List<XrefRevision> GroupRevisions(IReadOnlyList<XrefSection> sections)
    {
        var revisions = new List<XrefRevision>();
        var current = new List<XrefSection> { sections[0] };
        for (int index = 1; index < sections.Count; index++)
        {
            XrefSection section = sections[index];
            bool forward = section.Offset > current[0].Offset;
            bool namedStream = section.Offset == sections[index - 1].XRefStreamOffset;
            if (!forward && !namedStream)
            {
                revisions.Add(new XrefRevision(current));
                current = [];
            }

            current.Add(section);
        }

        revisions.Add(new XrefRevision(current));
        revisions.Reverse();
        return revisions;
    }
}

/// <summary>One revision of a file: the sections one save wrote. ISO 32000-2 §7.5.6, F.3.4.</summary>
/// <param name="Sections">The sections, in <c>Prev</c> chain order: the first is the one the revision's trailer is taken from.</param>
internal sealed record XrefRevision(IReadOnlyList<XrefSection> Sections)
{
    /// <summary>Gets the section the chain reaches first; its trailer is the revision's trailer.</summary>
    public XrefSection Newest => Sections[0];

    /// <summary>Gets the section stored last in the file; the revision ends with its trailer's <c>%%EOF</c> marker.</summary>
    public XrefSection LastInFile => Sections.MaxBy(section => section.Offset)!;

    /// <summary>Gets a value indicating whether the revision is a linearized file's first-page and main sections (F.3.4).</summary>
    public bool IsLinearizedPair => Sections.Count > 1 && Sections[1].Offset > Sections[0].Offset;
}
