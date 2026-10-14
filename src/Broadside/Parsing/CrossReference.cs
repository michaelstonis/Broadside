using Broadside.Caching;
using Broadside.IO;
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

/// <summary>
/// How many cross-reference stream entries one document may still read: one per byte of the file, and at least
/// <see cref="Minimum"/>. A classic table spends 20 bytes of the file per entry (§7.5.4), but a cross-reference stream's data is
/// filtered (§7.5.8), so Flate lets a few kilobytes describe millions of entries, each of which costs tens of bytes in the
/// cross-reference dictionaries: without a budget an 11 KB file made the reader allocate 2.3 GB (libFuzzer finding, issue #48).
/// A real file spends far more than a byte on each object it holds, so the budget only cuts entries no file needs.
/// </summary>
/// <remarks>ISO 32000-2 §7.5.8.2 and §7.5.8.3.</remarks>
internal sealed class XrefEntryBudget(long limit)
{
    /// <summary>The smallest budget, whatever the file length: 2^20 entries, enough for any small file's sparse numbering.</summary>
    public const long Minimum = 1 << 20;

    private long _remaining = limit;

    /// <summary>Returns the budget for one document read from <paramref name="source"/>.</summary>
    /// <param name="source">The file.</param>
    /// <returns>A new budget.</returns>
    public static XrefEntryBudget For(PdfSource source) => new(Math.Max(Minimum, source.Length));

    /// <summary>Spends one entry.</summary>
    /// <returns><see langword="false"/> when the budget is spent.</returns>
    public bool TryTake()
    {
        if (_remaining <= 0)
        {
            return false;
        }

        _remaining--;
        return true;
    }
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

    /// <summary>
    /// Gets a value indicating whether the section was rebuilt by scanning the file rather than read from it (issue #41,
    /// <see cref="CrossReferenceReconstructor"/>). Such a section is the only one, and its revision spans the whole file.
    /// </summary>
    public bool IsReconstructed { get; init; }
}

/// <summary>
/// The cross-reference information of a file: its sections, newest first, and the lookup they define together.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4, §7.5.5 and §7.5.6. Lookup is newest first, first write wins: an object number takes its entry from the most
/// recent section that lists it; a hybrid table's free entry gives way to its own <c>XRefStm</c> stream's entry (§7.5.8.4), which
/// otherwise comes after the table and before older sections. The trailer is the newest section's, with keys only an older trailer has filled in (a lenient
/// fallback; §7.5.6 says each update's trailer repeats them).
/// </para>
/// <para>
/// A linearized file read through range requests (issue #208, Annex G, G.2) starts <see cref="Deferred"/>: only its first-page
/// section (F.3.4) is read, and the rest of the chain, from the main section at the end of the file on, is read the first time a
/// lookup misses the first-page section and the hint tables cannot place the object (<see cref="Locator"/>), or anything needs every
/// section. The first-page section is the newest, so what it lists never changes when the rest arrives.
/// </para>
/// </remarks>
internal sealed class CrossReference
{
    private readonly Dictionary<int, XrefEntry> _merged = [];
    private readonly IReadOnlyList<XrefSection>? _sections;
    private readonly IReadOnlyList<XrefRevision>? _revisions;
    private readonly IReadOnlyList<CosName>? _inheritedTrailerKeys;
    private readonly XrefSection? _firstPage;
    private readonly Func<CrossReference>? _readComplete;
    private readonly OnceCache<bool, CrossReference>? _complete;
    private IObjectLocator? _locator;

    /// <summary>Initializes a new instance of the <see cref="CrossReference"/> class.</summary>
    /// <param name="sections">The sections, newest first. At least one.</param>
    public CrossReference(IReadOnlyList<XrefSection> sections)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sections.Count);
        _sections = sections;
        _revisions = GroupRevisions(sections);
        IReadOnlyList<XrefRevision> revisions = _revisions;
        var trailer = new OrderedDictionary<CosName, CosObject>();
        var inherited = new List<CosName>();
        for (int revision = revisions.Count - 1; revision >= 0; revision--)
        {
            IReadOnlyList<XrefSection> revisionSections = revisions[revision].Sections;
            for (int index = 0; index < revisionSections.Count; index++)
            {
                XrefSection section = revisionSections[index];
                XrefSection? hybridStream = section.XRefStreamOffset is { } streamOffset
                    && index + 1 < revisionSections.Count
                    && revisionSections[index + 1].Offset == streamOffset
                        ? revisionSections[index + 1]
                        : null;
                foreach ((int number, XrefEntry entry) in section.Entries)
                {
                    // §7.5.8.4: a free table entry is the pre-1.5 marker of an object the section's XRefStm stream defines, and
                    // "a PDF reader shall look in the cross-reference stream first"; an in-use table entry still wins.
                    XrefEntry effective = entry.Kind == XrefEntryKind.Free && hybridStream is not null
                        && hybridStream.Entries.TryGetValue(number, out XrefEntry fromStream)
                            ? fromStream
                            : entry;
                    _merged.TryAdd(number, effective);
                }

                foreach (KeyValuePair<CosName, CosObject> entry in section.Trailer)
                {
                    if (trailer.TryAdd(entry.Key, entry.Value) && revision < revisions.Count - 1)
                    {
                        inherited.Add(entry.Key);
                    }
                }
            }
        }

        Trailer = CosDictionary.FromOwnedEntries(trailer);
        _inheritedTrailerKeys = inherited;
    }

    private CrossReference(XrefSection firstPage, Func<CrossReference> readComplete)
    {
        _firstPage = firstPage;
        _readComplete = readComplete;
        _complete = new();
        foreach ((int number, XrefEntry entry) in firstPage.Entries)
        {
            _merged.TryAdd(number, entry);
        }

        var trailer = new OrderedDictionary<CosName, CosObject>();
        foreach (KeyValuePair<CosName, CosObject> entry in firstPage.Trailer)
        {
            trailer.TryAdd(entry.Key, entry.Value);
        }

        Trailer = CosDictionary.FromOwnedEntries(trailer);
    }

    /// <summary>
    /// Gets a value indicating whether only the first-page section has been read so far (<see cref="Deferred"/>); every other
    /// member reads the rest of the chain on first use.
    /// </summary>
    public bool IsDeferred => _firstPage is not null && !_complete!.TryGet(true, out _);

    /// <summary>
    /// Gets the first-page section of a linearized file (F.3.4): the section a deferred read started from, else the newest section
    /// of the original revision when that revision is a linearized pair; <see langword="null"/> otherwise.
    /// </summary>
    public XrefSection? FirstPageSection => _firstPage ?? (Revisions[0].IsLinearizedPair ? Revisions[0].Newest : null);

    /// <summary>
    /// Gets or sets what places an object the first-page section does not list without reading the rest of the chain: the hint
    /// tables of a linearized file (F.4). Only consulted while <see cref="IsDeferred"/>.
    /// </summary>
    public IObjectLocator? Locator
    {
        get => Volatile.Read(ref _locator);
        set => Volatile.Write(ref _locator, value);
    }

    /// <summary>Gets the sections, newest first.</summary>
    public IReadOnlyList<XrefSection> Sections => _sections ?? Complete.Sections;

    /// <summary>Gets the revisions the sections form, oldest first (§7.5.6).</summary>
    /// <remarks>
    /// <para>
    /// Each update appends one revision. A revision usually has one section; it has more when the file is linearized (the
    /// first-page section and the main section, F.3.4, form the original revision: the first-page section's <c>Prev</c> points
    /// forward, to a higher offset, which an appended update never does) or when a hybrid file's table names a cross-reference
    /// stream through <c>XRefStm</c> (§7.5.8.4: the stream belongs to its table's revision).
    /// </para>
    /// </remarks>
    public IReadOnlyList<XrefRevision> Revisions => _revisions ?? Complete.Revisions;

    /// <summary>
    /// Gets the trailer keys the newest revision's trailer lacks and an older revision's trailer supplied (§7.5.6: an update's
    /// trailer shall repeat every entry of the previous one except <c>Prev</c>).
    /// </summary>
    public IReadOnlyList<CosName> InheritedTrailerKeys => _inheritedTrailerKeys ?? Complete.InheritedTrailerKeys;

    /// <summary>Gets the merged lookup: for every object number a section lists, the entry <see cref="TryGetEntry"/> returns.</summary>
    public IReadOnlyDictionary<int, XrefEntry> Entries => _firstPage is null ? _merged : Complete.Entries;

    /// <summary>Gets the merged trailer dictionary.</summary>
    /// <remarks>
    /// For a <see cref="Deferred"/> read, the first-page trailer's entries, which F.3.4 says hold everything needed to display the
    /// document; the main trailer "should not contain any entries other than Size" (F.3.11) and is not merged in.
    /// </remarks>
    public CosDictionary Trailer { get; }

    /// <summary>The whole chain, read on first use for a deferred read; a lookup made while it is being read finds the first-page section only.</summary>
    private CrossReference Complete => _complete!.GetOrCreate(
        true,
        this,
        static (_, self) => new Created<CrossReference>(self._readComplete!()),
        static (_, self) => new CrossReference([self._firstPage!]));

    /// <summary>
    /// Starts the cross-reference information of a linearized file from its first-page section alone (Annex G, G.2); the rest of the
    /// chain is read by <paramref name="readComplete"/> when first needed, and must start with <paramref name="firstPage"/>.
    /// </summary>
    /// <param name="firstPage">The first-page section (F.3.4).</param>
    /// <param name="readComplete">Reads every section, the first-page section first.</param>
    /// <returns>The cross-reference information.</returns>
    public static CrossReference Deferred(XrefSection firstPage, Func<CrossReference> readComplete) => new(firstPage, readComplete);

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
        if (_firstPage is null)
        {
            return false;
        }

        if (_complete!.TryGet(true, out CrossReference? complete))
        {
            return complete.TryGetEntry(objectNumber, out entry);
        }

        return Locator?.TryLocate(objectNumber, out entry) == true || Complete.TryGetEntry(objectNumber, out entry);
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

/// <summary>Places an object without the cross-reference section that lists it: a linearized file's hint tables (F.4).</summary>
internal interface IObjectLocator
{
    /// <summary>Finds where the object numbered <paramref name="objectNumber"/> (generation 0) is.</summary>
    /// <param name="objectNumber">The object number.</param>
    /// <param name="entry">An in-use entry for the object, its offset relative to the header.</param>
    /// <returns><see langword="false"/> when the object cannot be placed this way: the cross-reference section decides.</returns>
    bool TryLocate(int objectNumber, out XrefEntry entry);
}
