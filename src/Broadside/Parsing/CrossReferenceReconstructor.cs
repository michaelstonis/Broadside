using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// Rebuilds the cross-reference information of a file whose own cannot be read, from a <see cref="FileScan"/> of its bytes.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.4 and §7.5.5 define the cross-reference table and trailer; §7.5 does not define repair, so this follows pdf.js
/// and PDFBox, keeping what each gets right:
/// </para>
/// <list type="number">
/// <item>Every <c>N G obj</c> header becomes an in-use entry; when a number occurs more than once, the copy furthest into the file
/// wins, since an incremental update appends newer copies (§7.5.6).</item>
/// <item>Every object stream (<c>/Type /ObjStm</c>) is decoded and its members become compressed entries (§7.5.7), unless a copy of
/// the member stored later in the file body supersedes it; without this, a file whose cross-reference stream is damaged loses every
/// compressed object (a pdf.js limitation).</item>
/// <item>The trailer is the newest <c>trailer</c> dictionary or cross-reference stream dictionary whose <c>Root</c> resolves to a
/// catalog with a <c>Pages</c> dictionary (both engines take the oldest, which loses updates). <c>Encrypt</c> and <c>ID</c> come
/// from any trailer that has them when the chosen one does not (PDFBox's fallback loses them, so encrypted files fail).</item>
/// <item>With no usable trailer, the catalog is the newest object of type <c>/Catalog</c> whose <c>Pages</c> resolves, and a
/// <see cref="DiagnosticCodes.TrailerMissing"/> or <see cref="DiagnosticCodes.RootMissing"/> diagnostic says so.</item>
/// </list>
/// <para>The result has one synthetic section, marked <see cref="XrefSection.IsReconstructed"/>, that spans the whole file.</para>
/// </remarks>
internal static class CrossReferenceReconstructor
{
    private static readonly CosName XRef = new("XRef");
    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName N = new("N");
    private static readonly CosName First = new("First");
    private static readonly CosName XRefStm = new("XRefStm");

    /// <summary>The cross-reference stream entries that describe the stream, not the file (§7.5.8.2, Table 17).</summary>
    private static readonly CosName[] StreamOnlyKeys =
    [
        KnownNames.Type, KnownNames.Length, new("W"), new("Index"), new("Filter"), new("DecodeParms"), new("DL"), new("F"),
        new("FFilter"), new("FDecodeParms"),
    ];

    /// <summary>Rebuilds the cross-reference information of <paramref name="source"/>.</summary>
    /// <param name="source">The file.</param>
    /// <param name="header">The header; offsets in the result are relative to it, like any entry's.</param>
    /// <param name="scan">The scan of the file.</param>
    /// <param name="streams">The filter pipeline object streams are decoded with.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="reportMissingTrailer">
    /// <see langword="false"/> when the caller already reported why the trailer is unusable, so only a file with no trailer at all
    /// adds a diagnostic.
    /// </param>
    /// <returns>The cross-reference information, or <see langword="null"/> when the file holds no catalog.</returns>
    public static CrossReference? Reconstruct(
        PdfSource source,
        FileHeader header,
        FileScan scan,
        StreamDecoder streams,
        DiagnosticSink diagnostics,
        bool reportMissingTrailer = true)
    {
        // 1. File-body objects: the last copy of each number wins.
        var entries = new Dictionary<int, XrefEntry>();
        var bodyOffsets = new Dictionary<int, long>();
        foreach (ScannedObject found in scan.Objects)
        {
            if (found.Offset < header.Offset)
            {
                continue;
            }

            entries[found.Number] = new XrefEntry(XrefEntryKind.InUse, found.Offset - header.Offset, found.Generation);
            bodyOffsets[found.Number] = found.Offset;
        }

        ObjectLoader loader = LoaderOver(source, header, entries, new CosDictionary(), streams, diagnostics);

        // 2. Members of object streams, unless a later file-body copy supersedes them.
        if (AddObjectStreamMembers(scan, loader, entries, bodyOffsets, streams, diagnostics))
        {
            loader = LoaderOver(source, header, entries, new CosDictionary(), streams, diagnostics);
        }

        // 3. The trailer: the newest candidate whose Root leads to a catalog with a page tree.
        List<(long Offset, CosDictionary Trailer)> trailers = TrailerCandidates(source, scan, loader);
        OrderedDictionary<CosName, CosObject>? chosen = null;
        foreach ((long _, CosDictionary candidate) in trailers)
        {
            if (candidate.TryGetValue(KnownNames.Root, out CosObject? root) && IsCatalog(loader, loader.Resolve(root)))
            {
                chosen = Without(candidate, KnownNames.Prev, XRefStm, KnownNames.Size);
                break;
            }
        }

        if (chosen is null)
        {
            if (FindCatalog(scan, loader, entries) is not { } catalog)
            {
                return null;
            }

            if (trailers.Count == 0 || reportMissingTrailer)
            {
                diagnostics.Report(
                    trailers.Count == 0 ? DiagnosticCodes.TrailerMissing : DiagnosticCodes.RootMissing,
                    DiagnosticSeverity.Warning,
                    trailers.Count == 0
                        ? string.Create(CultureInfo.InvariantCulture, $"The file has no trailer dictionary; the catalog is object {catalog.ObjectNumber} {catalog.Generation}, found by scanning the file.")
                        : string.Create(CultureInfo.InvariantCulture, $"No trailer's Root entry leads to the catalog; the catalog is object {catalog.ObjectNumber} {catalog.Generation}, found by scanning the file."));
            }

            chosen = new OrderedDictionary<CosName, CosObject> { [KnownNames.Root] = catalog };
        }

        foreach (CosName key in (ReadOnlySpan<CosName>)[KnownNames.Encrypt, KnownNames.ID, KnownNames.Info])
        {
            if (!chosen.ContainsKey(key) && trailers.FirstOrDefault(candidate => candidate.Trailer.ContainsKey(key)).Trailer is { } other)
            {
                chosen.Add(key, other[key]);
            }
        }

        chosen.Insert(0, KnownNames.Size, new CosInteger(entries.Count == 0 ? 1 : entries.Keys.Max() + 1L));
        CosDictionary trailer = CosDictionary.FromOwnedEntries(chosen);
        var section = new XrefSection(source.Length, source.Length, XrefSectionKind.Table, entries, trailer) { IsReconstructed = true };
        return new CrossReference([section]);
    }

    /// <summary>A loader over a provisional cross-reference, to read objects while the final one is being built.</summary>
    private static ObjectLoader LoaderOver(
        PdfSource source,
        FileHeader header,
        Dictionary<int, XrefEntry> entries,
        CosDictionary trailer,
        StreamDecoder streams,
        DiagnosticSink diagnostics)
    {
        var section = new XrefSection(source.Length, source.Length, XrefSectionKind.Table, entries, trailer)
        {
            IsReconstructed = true,
        };
        return new ObjectLoader(source, header, new CrossReference([section]), diagnostics, new ObjectLoaderHooks(), streams);
    }

    /// <summary>Decodes every object stream the scan found and registers its members as compressed entries (§7.5.7).</summary>
    /// <returns><see langword="true"/> when an entry was added or changed.</returns>
    private static bool AddObjectStreamMembers(
        FileScan scan,
        ObjectLoader loader,
        Dictionary<int, XrefEntry> entries,
        Dictionary<int, long> bodyOffsets,
        StreamDecoder streams,
        DiagnosticSink diagnostics)
    {
        var containers = new SortedSet<(long Offset, int Number)>();
        foreach (long name in scan.ObjectStreamNames)
        {
            // Only the copy of each container the entries settled on (the last in the file) is read.
            if (scan.TryFindObjectBefore(name, out ScannedObject container)
                && bodyOffsets.TryGetValue(container.Number, out long offset)
                && offset == container.Offset)
            {
                containers.Add((container.Offset, container.Number));
            }
        }

        var memberOffsets = new Dictionary<int, long>();
        bool changed = false;
        foreach ((long containerOffset, int number) in containers)
        {
            XrefEntry entry = entries[number];
            var reference = new CosReference(number, entry.Generation);
            if (loader.Load(reference) is not CosStream stream
                || !stream.Dictionary.TryGetValue(KnownNames.Type, out CosObject? type)
                || !ObjStm.Equals(type))
            {
                continue;
            }

            CosObject count = loader.Resolve(stream.Dictionary.TryGetValue(N, out CosObject? n) ? n : null);
            CosObject first = loader.Resolve(stream.Dictionary.TryGetValue(First, out CosObject? f) ? f : null);
            ObjectStream objectStream = ObjectStream.Read(new CosReference(number, 0), streams.Decode(stream), count, first, diagnostics);
            IReadOnlyList<int> members = objectStream.ObjectNumbers;
            for (int index = 0; index < members.Count; index++)
            {
                int member = members[index];
                bool supersededByBody = bodyOffsets.TryGetValue(member, out long bodyOffset) && bodyOffset > containerOffset;
                bool supersededByStream = memberOffsets.TryGetValue(member, out long otherContainer) && otherContainer > containerOffset;
                if (member != number && !supersededByBody && !supersededByStream)
                {
                    entries[member] = new XrefEntry(XrefEntryKind.Compressed, number, index);
                    memberOffsets[member] = containerOffset;
                    changed = true;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Collects the dictionaries after <c>trailer</c> keywords and of cross-reference streams, newest (furthest into the file) first.
    /// </summary>
    private static List<(long Offset, CosDictionary Trailer)> TrailerCandidates(PdfSource source, FileScan scan, ObjectLoader loader)
    {
        var candidates = new List<(long Offset, CosDictionary Trailer)>();
        foreach (long keyword in scan.TrailerKeywords)
        {
            ReadOnlySpan<byte> window = source.GetWindow(keyword + "trailer"u8.Length).Span;
            if (new CosParser(window, new CosRepairLog()).ParseObject() is CosDictionary trailer)
            {
                candidates.Add((keyword, trailer));
            }
        }

        var seen = new HashSet<long>();
        foreach (long name in scan.XrefStreamNames)
        {
            if (scan.TryFindObjectBefore(name, out ScannedObject header)
                && seen.Add(header.Offset)
                && loader.CrossReference.TryGetEntry(header.Number, out XrefEntry entry)
                && entry.Kind == XrefEntryKind.InUse
                && entry.Offset + loader.Header.Offset == header.Offset
                && loader.Load(new CosReference(header.Number, header.Generation)) is CosStream stream
                && stream.Dictionary.TryGetValue(KnownNames.Type, out CosObject? type)
                && XRef.Equals(type))
            {
                candidates.Add((header.Offset, CosDictionary.FromOwnedEntries(Without(stream.Dictionary, StreamOnlyKeys))));
            }
        }

        candidates.Sort(static (left, right) => right.Offset.CompareTo(left.Offset));
        return candidates;
    }

    /// <summary>Finds the newest catalog: an object of type <c>/Catalog</c> whose <c>Pages</c> resolves to a dictionary (§7.7.2).</summary>
    private static CosReference? FindCatalog(FileScan scan, ObjectLoader loader, Dictionary<int, XrefEntry> entries)
    {
        CosReference? best = null;
        long bestOffset = -1;
        foreach (long name in scan.CatalogNames)
        {
            if (scan.TryFindObjectBefore(name, out ScannedObject header) && header.Offset > bestOffset)
            {
                var reference = new CosReference(header.Number, header.Generation);
                if (IsCatalog(loader, loader.Load(reference)))
                {
                    best = reference;
                    bestOffset = header.Offset;
                }
            }
        }

        if (best is not null)
        {
            return best;
        }

        // The catalog may be compressed, where the scan cannot see its type: look at the members of object streams, newest number first.
        foreach (int number in entries.Where(static entry => entry.Value.Kind == XrefEntryKind.Compressed).Select(static entry => entry.Key).OrderDescending())
        {
            var reference = new CosReference(number, 0);
            if (loader.Load(reference) is CosDictionary dictionary
                && dictionary.TryGetValue(KnownNames.Type, out CosObject? type)
                && KnownNames.Catalog.Equals(type)
                && IsCatalog(loader, dictionary))
            {
                return reference;
            }
        }

        return null;
    }

    private static bool IsCatalog(ObjectLoader loader, CosObject value) =>
        value is CosDictionary catalog
        && catalog.TryGetValue(KnownNames.Pages, out CosObject? pages)
        && loader.Resolve(pages) is CosDictionary;

    /// <summary>Copies the entries of <paramref name="dictionary"/> but <paramref name="keys"/>, for a dictionary that reads as parsed (ADR 0004).</summary>
    private static OrderedDictionary<CosName, CosObject> Without(CosDictionary dictionary, params ReadOnlySpan<CosName> keys)
    {
        var copy = new OrderedDictionary<CosName, CosObject>();
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            if (!keys.Contains(entry.Key))
            {
                copy.Add(entry.Key, entry.Value);
            }
        }

        return copy;
    }
}
