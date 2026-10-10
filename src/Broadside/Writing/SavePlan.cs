using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Writing;

/// <summary>Decides what a full save of an opened document writes, and builds the <see cref="FileWriter"/> that writes it.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5. Every object the cross-reference information lists as in use is written under its own number and generation,
/// whether it sat in the file body or in an object stream; the newest revision's copy wins, so the revisions of an incrementally
/// updated file collapse into one (§7.5.6). Objects that describe the old file's structure are dropped and their numbers freed:
/// cross-reference streams (§7.5.8) and object streams (§7.5.7), which the new file replaces with its own, and the linearization
/// parameter dictionary and hint streams (Annex F, F.3.6: a regenerated file is no longer linearized). Such an object is kept,
/// as an ordinary object, when a kept object or the trailer refers to it, which only a damaged file does.
/// </para>
/// <para>
/// An object that has not changed (<see cref="CosObject.IsDirty"/>) and was read without any repair is copied byte for byte from
/// the source; any other object is serialized. Every object is loaded first, so in strict mode a deviation throws before anything
/// is written.
/// </para>
/// </remarks>
internal static class SavePlan
{
    /// <summary>
    /// The largest object number a saved file may use: the limit ISO 32000-1 Annex C, Table C.1 gives for the number of indirect
    /// objects. A classic table lists every number up to the largest (§7.5.4), so a larger number in a damaged or hostile file would
    /// make the table gigabytes long; such a file needs renumbering, which the full rewrite of a later phase does.
    /// </summary>
    public const int MaxObjectNumber = 8_388_607;

    /// <summary>
    /// The highest object number saved whatever the numbering's density. Above it, the highest number may be at most
    /// <see cref="MaxNumbersPerObject"/> times the number of objects: a classic table lists every number up to the highest (20 bytes
    /// each), so a tiny file with one object numbered in the millions would save to a file a hundred megabytes long (libFuzzer
    /// finding, issue #48). Such numberings need renumbering, which is not supported yet.
    /// </summary>
    public const int MaxSparseObjectNumber = 1 << 20;

    /// <summary>The most object numbers per object above <see cref="MaxSparseObjectNumber"/>.</summary>
    public const int MaxNumbersPerObject = 16;

    private static readonly CosName XRef = new("XRef");
    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName XRefStm = new("XRefStm");

    /// <summary>Plans the save of an opened document.</summary>
    /// <param name="source">The document's file.</param>
    /// <param name="loader">The document's loader.</param>
    /// <param name="linearization">The document's linearization information, whose objects are dropped.</param>
    /// <param name="layout">The cross-reference layout.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="NotSupportedException">
    /// The document is encrypted, uses object numbers above <see cref="MaxObjectNumber"/>, or numbers its objects far more sparsely
    /// than <see cref="MaxNumbersPerObject"/> numbers per object above <see cref="MaxSparseObjectNumber"/>.
    /// </exception>
    public static FileWriter Create(PdfSource source, ObjectLoader loader, PdfLinearization? linearization, PdfCrossReferenceLayout layout)
    {
        CrossReference crossReference = loader.CrossReference;
        CosDictionary sourceTrailer = crossReference.Trailer;
        if (sourceTrailer.ContainsKey(KnownNames.Encrypt))
        {
            throw new NotSupportedException(
                "Saving an encrypted document is not supported yet: its objects would be written with keys that no longer match, or as plain text.");
        }

        if (crossReference.Entries.Keys.DefaultIfEmpty().Max() > MaxObjectNumber)
        {
            throw new NotSupportedException(
                $"The document uses object numbers above {MaxObjectNumber}; saving it needs renumbering, which is not supported yet.");
        }

        int inUse = 0;
        int highest = 0;
        foreach (KeyValuePair<int, XrefEntry> entry in crossReference.Entries)
        {
            if (entry.Value.Kind != XrefEntryKind.Free)
            {
                inUse++;
                highest = Math.Max(highest, entry.Key);
            }
        }

        if (highest > MaxSparseObjectNumber && highest > (long)MaxNumbersPerObject * inUse)
        {
            throw new NotSupportedException(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"The document numbers {inUse} objects up to {highest}; saving a numbering that sparse needs renumbering, which is not supported yet."));
        }

        var candidates = new HashSet<int>();
        if (linearization is not null)
        {
            candidates.Add(linearization.Reference.ObjectNumber);
            AddHintStreams(linearization, crossReference, candidates);
        }

        var loaded = new List<(CosReference Reference, CosObject Value)>();
        var freeGenerations = new Dictionary<int, int>();
        foreach ((int number, XrefEntry entry) in crossReference.Entries.OrderBy(pair => pair.Key))
        {
            CosReference reference;
            switch (entry.Kind)
            {
                case XrefEntryKind.InUse:
                    reference = new CosReference(number, entry.Generation);
                    break;
                case XrefEntryKind.Compressed:
                    reference = new CosReference(number, 0);
                    break;
                default:
                    freeGenerations[number] = entry.Generation;
                    continue;
            }

            CosObject value = loader.Load(reference);
            if (IsFileStructure(value))
            {
                candidates.Add(number);
            }

            loaded.Add((reference, value));
        }

        HashSet<CosReference> referenced = FindReferenced(sourceTrailer, loaded, candidates);
        var objects = new List<WriterObject>(loaded.Count);
        foreach ((CosReference reference, CosObject value) in loaded)
        {
            if (candidates.Contains(reference.ObjectNumber) && !referenced.Contains(reference))
            {
                freeGenerations[reference.ObjectNumber] = Math.Min(reference.Generation + 1, CosReference.MaxGeneration);
                continue;
            }

            ObjectSourceBytes? bytes = !value.IsDirty && loader.TryGetSourceBytes(reference, out ObjectSourceBytes found) ? found : null;
            objects.Add(new WriterObject(reference, value, bytes));
        }

        var trailer = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in sourceTrailer)
        {
            if (!entry.Key.Equals(KnownNames.Size) && !entry.Key.Equals(KnownNames.Prev) && !entry.Key.Equals(XRefStm) && !entry.Key.Equals(KnownNames.ID))
            {
                trailer[entry.Key] = entry.Value;
            }
        }

        CosString? firstIdentifier = sourceTrailer.TryGetValue(KnownNames.ID, out CosObject? id)
            && loader.Resolve(id) is CosArray { Count: 2 } identifiers
            && loader.Resolve(identifiers[0]) is CosString first
            && loader.Resolve(identifiers[1]) is CosString
                ? first
                : null;

        return new FileWriter(
            source,
            loader.Header.Version ?? FileHeader.DefaultVersion,
            objects,
            freeGenerations,
            trailer,
            firstIdentifier,
            layout);
    }

    /// <summary>
    /// Finds the references the trailer and the objects that are kept contain. A file-structure object is dropped only when nothing
    /// kept refers to it: in a well-formed file nothing does, and in a damaged one dropping it would change what the document shows.
    /// A candidate something refers to is kept, and what it refers to is followed in turn.
    /// </summary>
    private static HashSet<CosReference> FindReferenced(CosDictionary trailer, List<(CosReference Reference, CosObject Value)> loaded, HashSet<int> candidates)
    {
        var referenced = new HashSet<CosReference>();
        var candidateValues = new Dictionary<CosReference, CosObject>();
        var pending = new Stack<CosObject>();
        pending.Push(trailer);
        foreach ((CosReference reference, CosObject value) in loaded)
        {
            if (candidates.Contains(reference.ObjectNumber))
            {
                candidateValues[reference] = value;
            }
            else
            {
                pending.Push(value);
            }
        }

        while (pending.TryPop(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference:
                    if (referenced.Add(reference) && candidateValues.TryGetValue(reference, out CosObject? kept))
                    {
                        pending.Push(kept);
                    }

                    break;
                case CosArray array:
                    foreach (CosObject item in array)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosDictionary dictionary:
                    foreach (CosObject item in dictionary.Values)
                    {
                        pending.Push(item);
                    }

                    break;
                case CosStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                default:
                    break;
            }
        }

        return referenced;
    }

    /// <summary>Whether an object is a cross-reference stream or an object stream, which the new file replaces.</summary>
    private static bool IsFileStructure(CosObject value) =>
        value is CosStream stream
        && stream.Dictionary.TryGetValue(KnownNames.Type, out CosObject? type)
        && (XRef.Equals(type) || ObjStm.Equals(type));

    /// <summary>Adds the objects at the hint stream offsets the linearization dictionary's <c>H</c> entry gives (F.3.3, Table F.1).</summary>
    private static void AddHintStreams(PdfLinearization linearization, CrossReference crossReference, HashSet<int> candidates)
    {
        if (!linearization.Dictionary.TryGetValue(PdfLinearization.Names.H, out CosObject? h) || h is not CosArray hints)
        {
            return;
        }

        var offsets = new HashSet<long>();
        for (int index = 0; index < hints.Count; index += 2)
        {
            if (hints[index] is CosInteger offset)
            {
                offsets.Add(offset.Value);
            }
        }

        foreach ((int number, XrefEntry entry) in crossReference.Entries)
        {
            if (entry.Kind == XrefEntryKind.InUse && offsets.Contains(entry.Offset))
            {
                candidates.Add(number);
            }
        }
    }
}
