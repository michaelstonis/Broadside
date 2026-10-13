using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Writing;

/// <summary>Decides what a full save of an opened document writes, and builds the <see cref="FileWriter"/> that writes it.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5. Every object the cross-reference information lists as in use is written, whether it sat in the file body or in
/// an object stream; the newest revision's copy wins, so the revisions of an incrementally updated file collapse into one (§7.5.6).
/// Objects that describe the old file's structure are dropped and their numbers freed: cross-reference streams (§7.5.8) and object
/// streams (§7.5.7), which the new file replaces with its own, and the linearization parameter dictionary and hint streams (Annex F,
/// F.3.6: a regenerated file is no longer linearized). Such an object is kept, as an ordinary object, when a kept object or the
/// trailer refers to it, which only a damaged file does. Objects added to the document since it was read are written like the
/// others; objects removed from it are not, and their numbers are freed with the generation raised by one (issue #145).
/// </para>
/// <para>
/// <see cref="CreateIncremental"/> plans an incremental update instead (§7.5.6), through the same <see cref="FileWriter"/>.
/// </para>
/// <para>
/// With <see cref="PdfSaveOptions.RemoveUnreachableObjects"/>, only the objects the trailer reaches through indirect references are
/// written (garbage collection), which drops the old file structure too, since nothing reaches it. Objects keep their numbers and
/// generations unless <see cref="PdfSaveOptions.ObjectNumbering"/> is <see cref="PdfObjectNumbering.Compact"/> or the numbering is
/// too large or too sparse to write (<see cref="IsCompact"/>); a renumbered file numbers its objects 1 to n, generation 0, in the
/// order of their old numbers, and every reference is written with its object's new number (§7.3.10).
/// </para>
/// <para>
/// An object that has not changed (<see cref="CosObject.IsDirty"/>), was read without any repair and holds no reference whose number
/// changes is copied byte for byte from the source; any other object is serialized, its stream data written as stored. Every object
/// is loaded first, so in strict mode a deviation throws before anything is written.
/// </para>
/// <para>
/// Encryption (§7.6) follows <see cref="EncryptionPlan"/>: when the source or the output is encrypted, nothing is copied (the source
/// bytes are ciphertext tied to the old key and object number) and every object goes through an <see cref="Security.ObjectEncryptor"/>
/// under its written number. New encryption drops the old encryption dictionary and writes its own as a new object, numbered above
/// every number the file lists or refers to. The object streams and the cross-reference stream a save adds are numbered the same
/// way, above every number written, whenever the file keeps its references as they are (issue #295).
/// </para>
/// <para>
/// An unfiltered stream that <see cref="PdfSaveOptions.StreamCompression"/> selects is written instead as a <c>FlateDecode</c> copy
/// when that is smaller (§7.4.4), under its written number, with its references renumbered and a direct <c>Length</c> (§7.3.8.2).
/// </para>
/// </remarks>
internal static class SavePlan
{
    /// <summary>
    /// The largest object number a saved file may keep: the limit ISO 32000-1 Annex C, Table C.1 gives for the number of indirect
    /// objects. A classic table lists every number up to the largest (§7.5.4), so a larger number in a damaged or hostile file would
    /// make the table gigabytes long; such a file is renumbered.
    /// </summary>
    public const int MaxObjectNumber = 8_388_607;

    /// <summary>
    /// The highest object number kept whatever the numbering's density. Above it, the highest number may be at most
    /// <see cref="MaxNumbersPerObject"/> times the number of objects: a classic table lists every number up to the highest (20 bytes
    /// each), so a tiny file with one object numbered in the millions would save to a file a hundred megabytes long (libFuzzer
    /// finding, issue #48). Such numberings are renumbered (issue #144).
    /// </summary>
    public const int MaxSparseObjectNumber = 1 << 20;

    /// <summary>The most object numbers per object above <see cref="MaxSparseObjectNumber"/>.</summary>
    public const int MaxNumbersPerObject = 16;

    /// <summary>Plans the save of an opened document.</summary>
    /// <param name="source">The document's file.</param>
    /// <param name="loader">The document's loader.</param>
    /// <param name="linearization">The document's linearization information, whose objects are dropped.</param>
    /// <param name="options">
    /// The cross-reference layout, the numbering, whether unreachable objects are dropped, and which streams are compressed and how hard.
    /// </param>
    /// <param name="encoders">The engine's encoders; streams are compressed with its <c>FlateDecode</c> encoder.</param>
    /// <param name="security">The document's security, or <see langword="null"/> for a document read as not encrypted.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="NotSupportedException">What <see cref="EncryptionPlan"/> refuses.</exception>
    public static FileWriter Create(PdfSource source, ObjectLoader loader, PdfLinearization? linearization, PdfSaveOptions options, StreamEncoderRegistry encoders, PdfSecurity? security)
    {
        CrossReference crossReference = loader.CrossReference;
        CosDictionary sourceTrailer = crossReference.Trailer;
        EncryptionPlan encryption = EncryptionPlan.Create(loader, security, options, incremental: false);

        var existing = new Dictionary<int, CosReference>();
        var freeGenerations = new Dictionary<int, int>();
        foreach ((int number, XrefEntry entry) in crossReference.Entries)
        {
            if (loader.RemovedObjects.TryGetValue(number, out int removed))
            {
                freeGenerations[number] = NextGeneration(removed);
                continue;
            }

            switch (entry.Kind)
            {
                case XrefEntryKind.InUse:
                    existing[number] = new CosReference(number, entry.Generation);
                    break;
                case XrefEntryKind.Compressed:
                    existing[number] = new CosReference(number, 0);
                    break;
                default:
                    freeGenerations[number] = entry.Generation;
                    break;
            }
        }

        foreach (int number in loader.AddedObjects.Keys)
        {
            existing[number] = new CosReference(number, 0);
        }

        // An encryption dictionary that is replaced or dropped is not written; its number is freed.
        if (encryption.ReplacesDictionary && sourceTrailer.TryGetValue(KnownNames.Encrypt, out CosObject? oldEncryption)
            && oldEncryption is CosReference oldReference && existing.TryGetValue(oldReference.ObjectNumber, out CosReference? listed) && listed.Equals(oldReference))
        {
            existing.Remove(oldReference.ObjectNumber);
            freeGenerations[oldReference.ObjectNumber] = NextGeneration(oldReference.Generation);
        }

        var trailer = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in sourceTrailer)
        {
            if (!entry.Key.Equals(KnownNames.Size) && !entry.Key.Equals(KnownNames.Prev) && !entry.Key.Equals(KnownNames.XRefStm) && !entry.Key.Equals(KnownNames.ID)
                && !(encryption.ReplacesDictionary && EncryptionPlan.IsEncryptionEntry(entry.Key)))
            {
                trailer[entry.Key] = entry.Value;
            }
        }

        List<(CosReference Reference, CosObject Value)> kept = options.RemoveUnreachableObjects
            ? LoadReachable(loader, trailer, existing)
            : LoadAll(loader, linearization, crossReference, sourceTrailer, existing);

        if (encryption.NewDictionary is { } dictionary)
        {
            // The new encryption dictionary: an indirect object numbered as the next added object would be, above every number the
            // file lists or refers to (§7.3.10, Table 15); a dangling reference in a damaged file must not come to reach it (fuzz
            // finding). The number is not taken: the document does not change.
            int number = loader.NextNumber;
            if (number == int.MaxValue)
            {
                throw new InvalidOperationException("The document has no object number left for the encryption dictionary.");
            }

            var reference = new CosReference(number, 0);
            kept.Add((reference, dictionary));
            trailer[KnownNames.Encrypt] = reference;
        }

        Dictionary<CosReference, CosReference>? references = null;
        if (options.ObjectNumbering == PdfObjectNumbering.Compact || !IsCompact(Highest(kept, freeGenerations), kept.Count))
        {
            references = new Dictionary<CosReference, CosReference>(kept.Count);
            for (int index = 0; index < kept.Count; index++)
            {
                references[kept[index].Reference] = new CosReference(index + 1, 0);
            }

            freeGenerations.Clear();
        }
        else
        {
            var keptReferences = new HashSet<CosReference>(kept.Select(item => item.Reference));
            foreach (CosReference reference in existing.Values)
            {
                if (!keptReferences.Contains(reference))
                {
                    freeGenerations[reference.ObjectNumber] = NextGeneration(reference.Generation);
                }
            }

            if (options.RemoveUnreachableObjects)
            {
                // The numbers are kept, but a serialized object must not keep a reference to a dropped object (its number is free and
                // an update could reuse it), and an object whose bytes hold one is serialized rather than copied.
                references = keptReferences.ToDictionary(reference => reference);
            }
        }

        encryption.CheckWritable(kept, references);
        var objects = new List<WriterObject>(kept.Count);
        foreach ((CosReference reference, CosObject value) in kept)
        {
            // Source bytes of an encrypted file are ciphertext under the old key and number; an encrypted or decrypted file
            // serializes every object.
            ObjectSourceBytes? bytes = encryption.Encryptor is null
                && !value.IsDirty
                && loader.TryGetSourceBytes(reference, out ObjectSourceBytes found)
                && (references is null || KeepsReferences(value, references))
                    ? found
                    : null;
            bool compress = options.StreamCompression switch
            {
                PdfStreamCompression.AllStreams => IsCompressible(value),
                PdfStreamCompression.ChangedStreams => (value.IsDirty || loader.AddedObjects.ContainsKey(reference.ObjectNumber)) && IsCompressible(value),
                _ => false,
            };
            objects.Add(new WriterObject(references is null ? reference : references[reference], value, bytes, compress));
        }

        CosString? firstIdentifier = sourceTrailer.TryGetValue(KnownNames.ID, out CosObject? id)
            && loader.Resolve(id) is CosArray { Count: 2 } identifiers
            && loader.Resolve(identifiers[0]) is CosString first
            && loader.Resolve(identifiers[1]) is CosString
                ? first
                : null;

        PdfVersion version = loader.Header.Version ?? FileHeader.DefaultVersion;
        if (encryption.MinimumVersion is { } minimum && version < minimum)
        {
            version = minimum;
        }

        return new FileWriter(
            source,
            version,
            objects,
            freeGenerations,
            trailer,
            encryption.FirstIdentifier ?? firstIdentifier,
            options.CrossReferenceLayout,
            encoders.Flate,
            options.CompressionLevel,
            references,
            update: null,
            encryption.Encryptor)
        {
            // A renumbered file (references is not null) writes a dangling reference as null; one whose references are written as
            // they are keeps it, so the streams the save adds skip its number.
            StreamNumbers = references is null && options.CrossReferenceLayout != PdfCrossReferenceLayout.Table ? StreamNumbers(loader) : null,
        };
    }

    /// <summary>Plans an incremental update of an opened document (§7.5.6).</summary>
    /// <param name="source">The document's file, which the update is appended to.</param>
    /// <param name="loader">The document's loader.</param>
    /// <param name="options">Which of the written streams are compressed, and how hard.</param>
    /// <param name="encoders">The engine's encoders; streams are compressed with its <c>FlateDecode</c> encoder.</param>
    /// <param name="security">The document's security, or <see langword="null"/> for a document read as not encrypted.</param>
    /// <returns>The writer.</returns>
    /// <exception cref="NotSupportedException">What <see cref="EncryptionPlan"/> refuses.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.5.6: "A cross-reference section for an incremental update shall contain entries only for objects that have been
    /// changed, replaced, or deleted." The update writes every loaded object that is dirty, every added object, and a free entry for
    /// every removed one, with its generation raised by one (H.7.4). Nothing is loaded to decide that: an object nobody loaded has
    /// not changed. (An update that writes a cross-reference stream reads every object once, to number the stream away from every
    /// dangling reference: issue #295.) The written objects are serialized; a written stream is compressed per
    /// <see cref="PdfSaveOptions.StreamCompression"/>.
    /// </para>
    /// <para>
    /// The section takes the form of the file's newest one, a stream after a stream and a table after a table or a hybrid file's
    /// table (§7.5.8.4), and its trailer repeats the newest trailer's entries except <c>Prev</c>, <c>XRefStm</c> and <c>Size</c> (and a
    /// damaged <c>Encrypt</c> entry the document was read without, when the save removes encryption: issue #283), with
    /// <c>Prev</c> pointing at that section. A file whose cross-reference information had to be rebuilt (issue #41) has no section to
    /// point at: its update lists every object instead, so readers no longer need to rebuild it, and it writes again every object
    /// read with a repair, so appending bytes cannot change how it reads (an object cut off by the end of the file would run on).
    /// </para>
    /// </remarks>
    public static FileWriter CreateIncremental(PdfSource source, ObjectLoader loader, PdfSaveOptions options, StreamEncoderRegistry encoders, PdfSecurity? security)
    {
        CrossReference crossReference = loader.CrossReference;
        CosDictionary sourceTrailer = crossReference.Trailer;
        EncryptionPlan encryption = EncryptionPlan.Create(loader, security, options, incremental: true);

        var objects = new List<WriterObject>();
        var kept = new Dictionary<int, XrefEntry>();
        var freeGenerations = new Dictionary<int, int>();
        bool listsEverything = crossReference.Sections[0].IsReconstructed;
        foreach ((int number, XrefEntry entry) in crossReference.Entries)
        {
            if (loader.RemovedObjects.TryGetValue(number, out int removed))
            {
                freeGenerations[number] = NextGeneration(removed);
                continue;
            }

            if (entry.Kind == XrefEntryKind.Free)
            {
                if (listsEverything)
                {
                    freeGenerations[number] = entry.Generation;
                }

                continue;
            }

            var reference = new CosReference(number, entry.Kind == XrefEntryKind.InUse ? entry.Generation : 0);
            if ((loader.TryGetLoaded(reference, out CosObject? value) && value.IsDirty)
                || (listsEverything && RereadsDifferently(loader, reference, out value)))
            {
                objects.Add(new WriterObject(reference, value, null, options.StreamCompression != PdfStreamCompression.None && IsCompressible(value)));
            }
            else if (listsEverything)
            {
                kept[number] = entry;
            }
        }

        foreach ((int number, CosObject value) in loader.AddedObjects)
        {
            objects.Add(new WriterObject(new CosReference(number, 0), value, null, options.StreamCompression != PdfStreamCompression.None && IsCompressible(value)));
        }

        objects.Sort((left, right) => left.Reference.ObjectNumber.CompareTo(right.Reference.ObjectNumber));

        var trailer = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in sourceTrailer)
        {
            if (!entry.Key.Equals(KnownNames.Size) && !entry.Key.Equals(KnownNames.Prev) && !entry.Key.Equals(KnownNames.XRefStm) && !entry.Key.Equals(KnownNames.ID)
                && !(encryption.ReplacesDictionary && EncryptionPlan.IsEncryptionEntry(entry.Key)))
            {
                trailer[entry.Key] = entry.Value;
            }
        }

        CosString? firstIdentifier = null;
        CosString? previousIdentifier = null;
        if (sourceTrailer.TryGetValue(KnownNames.ID, out CosObject? id)
            && loader.Resolve(id) is CosArray { Count: 2 } identifiers
            && loader.Resolve(identifiers[0]) is CosString first
            && loader.Resolve(identifiers[1]) is CosString second)
        {
            firstIdentifier = first;
            previousIdentifier = second;
        }

        XrefSectionKind form = listsEverything
            ? kept.Values.Any(entry => entry.Kind == XrefEntryKind.Compressed) ? XrefSectionKind.Stream : XrefSectionKind.Table
            : crossReference.Sections[0].Kind;
        // Above every number the file lists, whatever its Size says (a new cross-reference stream must not take a used number).
        int size = sourceTrailer.TryGetValue(KnownNames.Size, out CosObject? sizeEntry) && sizeEntry is CosInteger { Value: > 0 and <= MaxObjectNumber } count
            ? (int)count.Value
            : 0;
        foreach (int number in crossReference.Entries.Keys)
        {
            size = Math.Max(size, number + 1);
        }
        var target = new UpdateTarget(
            loader.Header.Offset,
            listsEverything ? null : crossReference.Sections[0].Offset - loader.Header.Offset,
            form,
            size,
            kept,
            previousIdentifier);

        return new FileWriter(
            source,
            loader.Header.Version ?? FileHeader.DefaultVersion,
            objects,
            freeGenerations,
            trailer,
            encryption.FirstIdentifier ?? firstIdentifier,
            form == XrefSectionKind.Stream ? PdfCrossReferenceLayout.Stream : PdfCrossReferenceLayout.Table,
            encoders.Flate,
            options.CompressionLevel,
            references: null,
            target,
            encryption.Encryptor)
        {
            StreamNumbers = form == XrefSectionKind.Stream ? StreamNumbers(loader) : null,
        };
    }

    /// <summary>
    /// The numbers of the streams a save adds, through the document's allocator (<see cref="ObjectLoader.NextNumberFrom"/>), so none
    /// takes the number of a dangling reference (§7.3.10, issue #295). The numbers references name are found now (reading every
    /// object, once per document), so in strict mode a deviation throws before anything is written.
    /// </summary>
    private static Func<int, int> StreamNumbers(ObjectLoader loader)
    {
        _ = loader.NextNumber;
        return loader.NextNumberFrom;
    }

    /// <summary>
    /// Whether an object of a file whose cross-reference information was rebuilt must be written again for the update to read the
    /// same: an object read with a repair, such as an unterminated object at the end of the file, which would run on into the
    /// appended bytes. Loads the object; in strict mode a deviation throws before anything is written.
    /// </summary>
    private static bool RereadsDifferently(ObjectLoader loader, CosReference reference, out CosObject value)
    {
        value = loader.Load(reference);
        return !loader.TryGetSourceBytes(reference, out _);
    }

    /// <summary>The generation a freed number's entry carries: one more than the freed object's, at most 65535 (§7.5.4).</summary>
    private static int NextGeneration(int generation) => Math.Min(generation + 1, CosReference.MaxGeneration);

    /// <summary>
    /// Whether a save may compress an object: a stream without filters (no <c>Filter</c>, no <c>DecodeParms</c>, its data in the
    /// file rather than an external one, §7.3.8.2 Table 5) that is not a metadata stream (§14.3.2), which is left readable to tools
    /// that scan files for XMP packets.
    /// </summary>
    private static bool IsCompressible(CosObject value) =>
        value is CosStream stream
        && !stream.Dictionary.ContainsKey(FilterNames.Filter)
        && !stream.Dictionary.ContainsKey(FilterNames.DecodeParms)
        && !stream.Dictionary.ContainsKey(FilterNames.F)
        && !(stream.Dictionary.TryGetValue(KnownNames.Type, out CosObject? type) && FileAndLayerNames.Metadata.Equals(type));

    /// <summary>
    /// Whether a file whose highest object number is <paramref name="highest"/> and which holds <paramref name="count"/> objects can
    /// keep its numbers: a classic table lists every number up to the highest (§7.5.4), so the highest may not pass
    /// <see cref="MaxObjectNumber"/>, and above <see cref="MaxSparseObjectNumber"/> it may be at most
    /// <see cref="MaxNumbersPerObject"/> times the number of objects.
    /// </summary>
    private static bool IsCompact(int highest, int count) =>
        highest <= MaxObjectNumber && (highest <= MaxSparseObjectNumber || highest <= (long)MaxNumbersPerObject * count);

    /// <summary>The highest number the written cross-reference information would list when the numbers are kept.</summary>
    private static int Highest(List<(CosReference Reference, CosObject Value)> kept, Dictionary<int, int> freeGenerations)
    {
        int highest = 0;
        foreach (int number in freeGenerations.Keys)
        {
            highest = Math.Max(highest, number);
        }

        foreach ((CosReference reference, _) in kept)
        {
            highest = Math.Max(highest, reference.ObjectNumber);
        }

        return highest;
    }

    /// <summary>
    /// Loads every object the cross-reference information lists as in use, in ascending number, except the old file's structure
    /// (cross-reference and object streams, the linearization dictionary and hint streams) when nothing kept refers to it.
    /// </summary>
    private static List<(CosReference Reference, CosObject Value)> LoadAll(
        ObjectLoader loader,
        PdfLinearization? linearization,
        CrossReference crossReference,
        CosDictionary sourceTrailer,
        Dictionary<int, CosReference> existing)
    {
        var candidates = new HashSet<int>();
        if (linearization is not null)
        {
            candidates.Add(linearization.Reference.ObjectNumber);
            AddHintStreams(linearization, crossReference, candidates);
        }

        var loaded = new List<(CosReference Reference, CosObject Value)>(existing.Count);
        foreach ((int number, CosReference reference) in existing.OrderBy(pair => pair.Key))
        {
            CosObject value = loader.Load(reference);
            if (IsFileStructure(value))
            {
                candidates.Add(number);
            }

            loaded.Add((reference, value));
        }

        HashSet<CosReference> referenced = FindReferenced(sourceTrailer, loaded, candidates);
        loaded.RemoveAll(item => candidates.Contains(item.Reference.ObjectNumber) && !referenced.Contains(item.Reference));
        return loaded;
    }

    /// <summary>
    /// Loads the objects the trailer reaches through indirect references, in ascending number: garbage collection. A stream's
    /// <c>Length</c> is not followed, because a serialized stream is written with a direct one (§7.3.8.2); a reference to a number
    /// the file does not hold, or under another generation, reaches nothing (§7.3.10).
    /// </summary>
    private static List<(CosReference Reference, CosObject Value)> LoadReachable(ObjectLoader loader, CosDictionary trailer, Dictionary<int, CosReference> existing)
    {
        var loaded = new Dictionary<CosReference, CosObject>();
        var pending = new Stack<CosObject>();
        pending.Push(trailer);
        while (pending.TryPop(out CosObject? value))
        {
            switch (value)
            {
                case CosReference reference:
                    if (existing.TryGetValue(reference.ObjectNumber, out CosReference? live) && live.Equals(reference) && !loaded.ContainsKey(live))
                    {
                        CosObject target = loader.Load(live);
                        loaded[live] = target;
                        pending.Push(target);
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
                    foreach (KeyValuePair<CosName, CosObject> entry in stream.Dictionary)
                    {
                        if (!entry.Key.Equals(KnownNames.Length))
                        {
                            pending.Push(entry.Value);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        return [.. loaded.OrderBy(pair => pair.Key.ObjectNumber).Select(pair => (pair.Key, pair.Value))];
    }

    /// <summary>Whether every reference <paramref name="value"/> holds is written unchanged, so its source bytes stay right.</summary>
    private static bool KeepsReferences(CosObject value, Dictionary<CosReference, CosReference> references)
    {
        var pending = new Stack<CosObject>();
        pending.Push(value);
        while (pending.TryPop(out CosObject? item))
        {
            switch (item)
            {
                case CosReference reference:
                    if (!references.TryGetValue(reference, out CosReference? written) || !written.Equals(reference))
                    {
                        return false;
                    }

                    break;
                case CosArray array:
                    foreach (CosObject element in array)
                    {
                        pending.Push(element);
                    }

                    break;
                case CosDictionary dictionary:
                    foreach (CosObject element in dictionary.Values)
                    {
                        pending.Push(element);
                    }

                    break;
                case CosStream stream:
                    pending.Push(stream.Dictionary);
                    break;
                default:
                    break;
            }
        }

        return true;
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
        && (KnownNames.XRef.Equals(type) || KnownNames.ObjStm.Equals(type));

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
