using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;
using Broadside.Security;

namespace Broadside.Writing;

/// <summary>One indirect object to write: its number and generation, its value, and its source bytes when they may be copied.</summary>
/// <param name="Reference">The object's number and generation, kept from the source.</param>
/// <param name="Value">The object.</param>
/// <param name="Source">The bytes the object was read from, copied instead of serializing <paramref name="Value"/>; <see langword="null"/> to serialize.</param>
/// <param name="Compress">
/// Whether <paramref name="Value"/>, an unfiltered stream, is written compressed with the <c>FlateDecode</c> encoder when that makes it
/// smaller (issue #142); otherwise it is written as <paramref name="Source"/> says.
/// </param>
internal readonly record struct WriterObject(CosReference Reference, CosObject Value, ObjectSourceBytes? Source, bool Compress = false);

/// <summary>The file an incremental update is appended to, and what its cross-reference section continues (ISO 32000-2 §7.5.6).</summary>
/// <param name="HeaderOffset">The absolute offset of the file's <c>%PDF-</c> header, which every offset the update states is relative to (§7.5.2).</param>
/// <param name="PreviousSection">
/// The <c>Prev</c> entry: the offset of the file's newest cross-reference section, relative to the header. <see langword="null"/> for
/// a section that lists every object instead, because the file's own cross-reference information was rebuilt (issue #41).
/// </param>
/// <param name="Form">Whether the section is a classic table (§7.5.4) or a cross-reference stream (§7.5.8).</param>
/// <param name="Size">The previous trailer's <c>Size</c>; the update's is at least this.</param>
/// <param name="KeptEntries">For a section that lists every object, the file's entries for the objects the update does not write; empty otherwise.</param>
/// <param name="PreviousIdentifier">The second file identifier of the previous trailer, hashed into the new one (§14.4), or <see langword="null"/>.</param>
internal sealed record UpdateTarget(
    long HeaderOffset,
    long? PreviousSection,
    XrefSectionKind Form,
    int Size,
    IReadOnlyDictionary<int, XrefEntry> KeptEntries,
    CosString? PreviousIdentifier);

/// <summary>An object an incremental update wrote, and where its bytes are in the updated file.</summary>
/// <param name="Reference">The object's number and generation.</param>
/// <param name="Value">The object.</param>
/// <param name="Offset">The absolute offset of the first byte after <c>N G obj</c> and its end-of-line.</param>
/// <param name="Length">The number of bytes before the end-of-line and <c>endobj</c>.</param>
internal readonly record struct WrittenObject(CosReference Reference, CosObject Value, long Offset, int Length);

/// <summary>What an incremental update appended, once its last byte is written.</summary>
/// <param name="Bytes">The appended bytes: everything after the original file.</param>
/// <param name="Section">The update's cross-reference section, with absolute offsets in the updated file.</param>
/// <param name="ListsEverything">Whether the section lists every object (<see cref="UpdateTarget.PreviousSection"/> is <see langword="null"/>).</param>
/// <param name="Objects">The objects written.</param>
internal sealed record UpdateResult(ReadOnlyMemory<byte> Bytes, XrefSection Section, bool ListsEverything, IReadOnlyList<WrittenObject> Objects);

/// <summary>
/// Writes a complete PDF file: header, objects, cross-reference information and trailer, as a sequence of chunks that a synchronous
/// and an asynchronous caller write out the same way, so both produce the same bytes.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.2 (header with a binary comment line), §7.3.10 (<c>N G obj</c> … <c>endobj</c>), §7.3.8 (streams with a direct
/// <c>Length</c>), §7.5.4 (classic table, one subsection, 20-byte entries, free entries linked from object 0), §7.5.5 (trailer,
/// <c>startxref</c>, <c>%%EOF</c>), §7.5.7 (object streams), §7.5.8 (cross-reference stream with an entry for itself) and §14.4
/// (file identifiers).
/// </para>
/// <para>
/// An incremental update (§7.5.6, <see cref="UpdateTarget"/>) is written the same way after a copy of every byte of the source: no
/// header, the objects, then a cross-reference section that lists object 0 (the head of the free list), the objects written and the
/// numbers freed, in subsections, with a trailer whose <c>Prev</c> entry points at the previous section. Offsets stay relative to the
/// source's header. The second file identifier is then derived from the previous one and the update's own bytes.
/// </para>
/// <para>
/// With an <see cref="ObjectEncryptor"/> (§7.6), every object is serialized through it under the number it is written with,
/// compressed first and then encrypted; the encryption dictionary is written as it is and kept out of object streams (§7.5.7), and
/// the cross-reference stream and the trailer are never encrypted.
/// </para>
/// <para>
/// Output is deterministic (encrypted output aside: its IVs are random): the second file identifier is the first 16 bytes of the SHA-256 hash of everything written before the
/// cross-reference information, and the first is the source's or, for a file without one, the same value (§14.4: both are equal
/// when a file is first written). Nothing depends on the clock or a random source.
/// </para>
/// </remarks>
internal sealed partial class FileWriter
{
    /// <summary>How many bytes are gathered before a chunk is handed out.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>The most objects one object stream holds, to keep random access cheap (§7.5.7, NOTE 4).</summary>
    internal const int ObjectStreamCapacity = 100;

    /// <summary>The largest offset a classic cross-reference entry's ten digits can state (§7.5.4).</summary>
    private const long MaxTableOffset = 9_999_999_999;

    /// <summary>The most bytes of the source one chunk of an incremental update's copy holds.</summary>
    private const int PrefixChunkSize = 1 << 20;

    /// <summary>What a compressed stream's dictionary grows by: <c> /Filter /FlateDecode</c>.</summary>
    private static readonly int FilterEntryLength = " /Filter /FlateDecode".Length;

    private readonly PdfSource? _source;
    private readonly IReadOnlyList<WriterObject> _objects;
    private readonly IReadOnlyDictionary<int, int> _freeGenerations;
    private readonly CosDictionary _trailer;
    private readonly CosString? _firstIdentifier;
    private readonly PdfCrossReferenceLayout _layout;
    private readonly IStreamEncoder _flate;
    private readonly CompressionLevel _compressionLevel;
    private readonly IReadOnlyDictionary<CosReference, CosReference>? _references;
    private readonly UpdateTarget? _update;
    private readonly ObjectEncryptor? _encryption;

    /// <summary>Initializes a new instance of the <see cref="FileWriter"/> class.</summary>
    /// <param name="source">The file source bytes are copied from; <see langword="null"/> when no object has source bytes.</param>
    /// <param name="version">The version for the header (<see cref="Version"/>); the caller raises it for the features the file uses (<see cref="VersionPlan"/>).</param>
    /// <param name="objects">The objects to write, in ascending object number.</param>
    /// <param name="freeGenerations">For object numbers that are free, the generation their free entry carries; others default to 0.</param>
    /// <param name="trailer">The trailer entries to write besides <c>Size</c> and <c>ID</c>.</param>
    /// <param name="firstIdentifier">The first file identifier to keep, or <see langword="null"/> for a file written for the first time.</param>
    /// <param name="layout">The cross-reference layout.</param>
    /// <param name="flate">The <c>FlateDecode</c> encoder for object streams, the cross-reference stream and compressed streams.</param>
    /// <param name="compressionLevel">How hard <paramref name="flate"/> compresses.</param>
    /// <param name="references">
    /// For a renumbered file, the number each reference in a serialized object, the trailer included, is written with; a reference
    /// it does not hold is written as <c>null</c>. <see langword="null"/> writes references as they are.
    /// </param>
    /// <param name="update">
    /// For an incremental update, the file it is appended to (<paramref name="source"/>, copied whole) and the section it continues;
    /// <paramref name="freeGenerations"/> then holds only the numbers the update frees, unless the section lists every object.
    /// </param>
    /// <param name="encryption">
    /// For a file whose objects are encrypted or decrypted on the way out (§7.6), what each written object becomes under its written
    /// number; every object is then serialized, never copied, and the encryption dictionary stays out of object streams.
    /// <see langword="null"/> writes objects as they are.
    /// </param>
    public FileWriter(
        PdfSource? source,
        PdfVersion version,
        IReadOnlyList<WriterObject> objects,
        IReadOnlyDictionary<int, int> freeGenerations,
        CosDictionary trailer,
        CosString? firstIdentifier,
        PdfCrossReferenceLayout layout,
        IStreamEncoder flate,
        CompressionLevel compressionLevel,
        IReadOnlyDictionary<CosReference, CosReference>? references = null,
        UpdateTarget? update = null,
        ObjectEncryptor? encryption = null)
    {
        _encryption = encryption;
        _source = source;
        Version = version;
        _objects = objects;
        _freeGenerations = freeGenerations;
        _trailer = trailer;
        _firstIdentifier = firstIdentifier;
        _layout = layout;
        _flate = flate;
        _compressionLevel = compressionLevel;
        _references = references;
        _update = update;
    }

    /// <summary>
    /// Gets the numbers of the object streams and the cross-reference stream the writer adds (§7.5.7, §7.5.8): given the lowest
    /// number one may take (one above every number written or freed), the number it takes. A file whose references are written as
    /// they are numbers them through the document's allocator (<see cref="ObjectLoader.NextNumberFrom"/>), so none takes the number
    /// of a dangling reference, which must stay null (§7.3.10, issue #295). <see langword="null"/> takes the lowest number: a
    /// renumbered file writes every reference to an object it does not hold as <c>null</c>, so no reference can reach them.
    /// </summary>
    public Func<int, int>? StreamNumbers { get; init; }

    /// <summary>Gets or sets the version the header states (§7.5.2); an incremental update writes no header.</summary>
    public PdfVersion Version { get; set; }

    /// <summary>Gets what an incremental update appended, once <see cref="Write"/> has been enumerated to the end; otherwise <see langword="null"/>.</summary>
    public UpdateResult? Update { get; private set; }

    /// <summary>Writes the file. Each chunk is valid until the next one is requested.</summary>
    /// <returns>The file's bytes, in order.</returns>
    public IEnumerable<ReadOnlyMemory<byte>> Write()
    {
        if (Linearized is { } layout)
        {
            foreach (ReadOnlyMemory<byte> chunk in WriteLinearized(layout))
            {
                yield return chunk;
            }

            yield break;
        }

        if (_update is null)
        {
            foreach (ReadOnlyMemory<byte> chunk in WriteFrom(0))
            {
                yield return chunk;
            }

            yield break;
        }

        if (_source is null)
        {
            throw new InvalidOperationException("An incremental update was asked for without the file it is appended to.");
        }

        // §7.5.6: the original bytes, every one of them, unchanged.
        long length = _source.Length;
        long position = 0;
        while (position < length)
        {
            ReadOnlyMemory<byte> window = _source.GetWindow(position, ChunkSize);
            if (window.IsEmpty)
            {
                throw new InvalidOperationException("The source file ended before its length.");
            }

            window = window[..(int)Math.Min(window.Length, Math.Min(length - position, PrefixChunkSize))];
            yield return window;
            position += window.Length;
        }

        var appended = new ArrayBufferWriter<byte>(ChunkSize);
        foreach (ReadOnlyMemory<byte> chunk in WriteFrom(length))
        {
            appended.Write(chunk.Span);
            yield return chunk;
        }

        Update = Update! with { Bytes = appended.WrittenMemory };
    }

    /// <summary>
    /// Writes everything after the source's bytes: the whole file (header included) when <paramref name="begin"/> is 0, else an
    /// incremental update appended at <paramref name="begin"/>.
    /// </summary>
    private IEnumerable<ReadOnlyMemory<byte>> WriteFrom(long begin)
    {
        using var buffer = new PooledBufferWriter(ChunkSize * 2, Array.MaxLength);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new Dictionary<int, XrefEntry>();
        var written = new List<WrittenObject>();
        long flushed = begin;
        long origin = _update?.HeaderOffset ?? 0;

        if (_update is null)
        {
            WriteHeader(buffer);
        }
        else
        {
            if (_update.PreviousIdentifier is { } previous)
            {
                hash.AppendData(previous.Bytes);
            }

            Span<byte> last = stackalloc byte[1];
            if (begin > 0 && _source!.Read(begin - 1, last) == 1 && last[0] is not ((byte)'\n' or (byte)'\r'))
            {
                buffer.Write("\n"u8);
            }
        }

        bool compress = _update is null && _layout == PdfCrossReferenceLayout.StreamWithObjectStreams;
        var compressible = new List<WriterObject>();
        int maxNumber = _freeGenerations.Count == 0 ? 0 : _freeGenerations.Keys.Max();
        foreach (WriterObject item in _objects)
        {
            maxNumber = Math.Max(maxNumber, item.Reference.ObjectNumber);
            if (compress && CanBeCompressed(item))
            {
                compressible.Add(item);
                continue;
            }

            entries[item.Reference.ObjectNumber] = new XrefEntry(XrefEntryKind.InUse, flushed + buffer.WrittenSpan.Length - origin, item.Reference.Generation);
            (int bodyStart, int bodyEnd) = WriteIndirectObject(buffer, item.Reference, item, _references);
            if (_update is not null)
            {
                written.Add(new WrittenObject(item.Reference, item.Value, flushed + bodyStart, bodyEnd - bodyStart));
            }

            if (buffer.WrittenSpan.Length >= ChunkSize)
            {
                hash.AppendData(buffer.WrittenSpan);
                yield return buffer.WrittenMemory;
                flushed += buffer.WrittenSpan.Length;
                buffer.Clear();
            }
        }

        if (_update is not null)
        {
            foreach (int number in _update.KeptEntries.Keys)
            {
                maxNumber = Math.Max(maxNumber, number);
            }

            maxNumber = Math.Max(maxNumber, _update.Size - 1);
        }

        int nextNumber = maxNumber + 1;
        for (int start = 0; start < compressible.Count; start += ObjectStreamCapacity)
        {
            int container = StreamNumber(nextNumber);
            nextNumber = container + 1;
            int count = Math.Min(ObjectStreamCapacity, compressible.Count - start);
            for (int index = 0; index < count; index++)
            {
                entries[compressible[start + index].Reference.ObjectNumber] = new XrefEntry(XrefEntryKind.Compressed, container, index);
            }

            CosStream objectStream = BuildObjectStream(compressible, start, count);
            var reference = new CosReference(container, 0);
            entries[container] = new XrefEntry(XrefEntryKind.InUse, flushed + buffer.WrittenSpan.Length - origin, 0);
            WriteIndirectObject(buffer, reference, new WriterObject(reference, objectStream, null), references: null);
            if (buffer.WrittenSpan.Length >= ChunkSize)
            {
                hash.AppendData(buffer.WrittenSpan);
                yield return buffer.WrittenMemory;
                flushed += buffer.WrittenSpan.Length;
                buffer.Clear();
            }
        }

        hash.AppendData(buffer.WrittenSpan);
        if (buffer.WrittenSpan.Length > 0)
        {
            yield return buffer.WrittenMemory;
            flushed += buffer.WrittenSpan.Length;
            buffer.Clear();
        }

        byte[] digest = hash.GetHashAndReset();
        var secondIdentifier = new CosString(digest.AsSpan(0, 16), hexadecimal: true);
        var identifier = new CosArray([_firstIdentifier ?? secondIdentifier, secondIdentifier]);

        XrefSection section = _layout == PdfCrossReferenceLayout.Table
            ? WriteTable(buffer, entries, nextNumber, flushed, origin, identifier)
            : WriteStream(buffer, entries, StreamNumber(nextNumber), flushed, origin, identifier);
        if (_update is not null)
        {
            Update = new UpdateResult(ReadOnlyMemory<byte>.Empty, section, _update.PreviousSection is null, written);
        }

        yield return buffer.WrittenMemory;
    }

    /// <summary>The number of a stream the writer adds, at or above <paramref name="lowest"/>, through <see cref="StreamNumbers"/>.</summary>
    private int StreamNumber(int lowest)
    {
        int number = StreamNumbers?.Invoke(lowest) ?? lowest;
        if (number < lowest || number == int.MaxValue)
        {
            throw new InvalidOperationException("The document has no object number left for the streams the save adds.");
        }

        return number;
    }

    /// <summary>Whether an object may be stored in an object stream (§7.5.7): not a stream, and not the encryption dictionary.</summary>
    private bool CanBeCompressed(WriterObject item) =>
        item.Reference.Generation == 0 && item.Value is not (CosStream or CosReference) && !ReferenceEquals(item.Value, _encryption?.EncryptionDictionary);

    /// <summary>Writes <c>%PDF-n.m</c> and a comment line of four bytes above 127, marking the file as binary (§7.5.2).</summary>
    private void WriteHeader(IBufferWriter<byte> buffer)
    {
        buffer.Write("%PDF-"u8);
        WriteAscii(buffer, Version.ToString());
        buffer.Write("\n%"u8);
        buffer.Write<byte>([0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
    }

    /// <summary>Writes <c>N G obj</c>, the object's bytes, and <c>endobj</c> (§7.3.10).</summary>
    /// <returns>Where in <paramref name="buffer"/> the object's bytes start and end.</returns>
    private (int Start, int End) WriteIndirectObject(PooledBufferWriter buffer, CosReference reference, WriterObject item, IReadOnlyDictionary<CosReference, CosReference>? references)
    {
        WriteInteger(buffer, reference.ObjectNumber);
        buffer.Write(" "u8);
        WriteInteger(buffer, reference.Generation);
        buffer.Write(" obj\n"u8);
        int start = buffer.WrittenSpan.Length;
        if (_encryption is not null)
        {
            // Compressed first, then encrypted (§7.6.2: encryption applies to the stream's encoded data); never copied.
            CosObject value = item.Compress && item.Value is CosStream unfiltered && Compressed(unfiltered) is { } compressed ? compressed : item.Value;
            CosWriter.Write(_encryption.Encrypt(value, reference), buffer, references);
        }
        else if (!item.Compress || item.Value is not CosStream stream || !TryWriteCompressed(buffer, stream, references))
        {
            WriteBody(buffer, item, _source, references);
        }

        int end = buffer.WrittenSpan.Length;
        buffer.Write("\nendobj\n"u8);
        return (start, end);
    }

    /// <summary>
    /// Writes an unfiltered stream compressed with the <c>FlateDecode</c> encoder (§7.4.4): a copy of its dictionary with a
    /// <c>Filter</c> entry, and the encoded data. The document's own stream is not changed. Writes nothing and returns
    /// <see langword="false"/> when compressing would not make the object smaller. The copy is written through
    /// <paramref name="references"/>, so a renumbered file renumbers its references, and with a direct <c>Length</c> for the encoded
    /// data (§7.3.8.2).
    /// </summary>
    private bool TryWriteCompressed(PooledBufferWriter buffer, CosStream stream, IReadOnlyDictionary<CosReference, CosReference>? references)
    {
        ReadOnlyMemory<byte> data = stream.EncodedData;
        using var encoded = new PooledBufferWriter(Math.Max(256, data.Length / 2), Array.MaxLength);
        _flate.Encode(data.Span, encoded, new StreamEncoderContext { StreamDictionary = stream.Dictionary, CompressionLevel = _compressionLevel });
        if (encoded.WrittenSpan.Length + FilterEntryLength >= data.Length)
        {
            return false;
        }

        CosWriter.Write(new CosStream(WithFlateFilter(stream.Dictionary), encoded.WrittenMemory), buffer, references);
        return true;
    }

    /// <summary>
    /// The <c>FlateDecode</c> copy of an unfiltered stream (§7.4.4), for a file whose streams are encrypted after compression;
    /// <see langword="null"/> when compressing would not make the object smaller.
    /// </summary>
    private CosStream? Compressed(CosStream stream)
    {
        ReadOnlyMemory<byte> data = stream.EncodedData;
        var encoded = new ArrayBufferWriter<byte>(Math.Max(256, data.Length / 2));
        _flate.Encode(data.Span, encoded, new StreamEncoderContext { StreamDictionary = stream.Dictionary, CompressionLevel = _compressionLevel });
        return encoded.WrittenCount + FilterEntryLength >= data.Length ? null : new CosStream(WithFlateFilter(stream.Dictionary), encoded.WrittenMemory);
    }

    /// <summary>A copy of a stream dictionary with <c>Filter</c> <c>FlateDecode</c>.</summary>
    private static CosDictionary WithFlateFilter(CosDictionary source)
    {
        var dictionary = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            dictionary[entry.Key] = entry.Value;
        }

        dictionary[FilterNames.Filter] = FilterNames.FlateDecode;
        return dictionary;
    }

    /// <summary>Writes an object's bytes: copied from the source when it has them, serialized (with its references renumbered) otherwise.</summary>
    private static void WriteBody(PooledBufferWriter buffer, WriterObject item, PdfSource? source, IReadOnlyDictionary<CosReference, CosReference>? references)
    {
        if (item.Source is not { } bytes)
        {
            CosWriter.Write(item.Value, buffer, references);
            return;
        }

        if (!bytes.IsInFile)
        {
            buffer.Write(bytes.Member.Span);
            return;
        }

        if (source is null)
        {
            throw new InvalidOperationException("Object bytes in a file were given without the file.");
        }

        Span<byte> target = buffer.GetSpan(bytes.Length)[..bytes.Length];
        int read = source.Read(bytes.FileOffset, target);
        if (read != bytes.Length)
        {
            throw new InvalidOperationException("The source file ended before the bytes of an object it holds.");
        }

        buffer.Advance(read);
    }

    /// <summary>Builds an object stream holding <paramref name="count"/> objects from <paramref name="start"/> (§7.5.7, Table 16).</summary>
    private CosStream BuildObjectStream(List<WriterObject> objects, int start, int count)
    {
        using var bodies = new PooledBufferWriter(ChunkSize, Array.MaxLength);
        var header = new ArrayBufferWriter<byte>();
        for (int index = 0; index < count; index++)
        {
            WriterObject item = objects[start + index];
            if (index > 0)
            {
                header.Write(" "u8);
                bodies.Write("\n"u8);
            }

            WriteInteger(header, item.Reference.ObjectNumber);
            header.Write(" "u8);
            WriteInteger(header, bodies.WrittenSpan.Length);
            WriteBody(bodies, item, _source, _references);
        }

        header.Write("\n"u8);
        int first = header.WrittenCount;
        header.Write(bodies.WrittenSpan);
        byte[] data = Deflate(header.WrittenSpan);

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = KnownNames.ObjStm,
            [KnownNames.N] = new CosInteger(count),
            [KnownNames.First] = new CosInteger(first),
            [FilterNames.Filter] = FilterNames.FlateDecode,
        };
        return new CosStream(dictionary, data);
    }

    /// <summary>
    /// Writes a classic cross-reference table and trailer (§7.5.4, §7.5.5), at absolute offset <paramref name="position"/>: one
    /// subsection per run of consecutive numbers <see cref="Listing"/> gives.
    /// </summary>
    /// <returns>The section as a reader would read it, with absolute offsets.</returns>
    private XrefSection WriteTable(PooledBufferWriter buffer, Dictionary<int, XrefEntry> entries, int size, long position, long origin, CosArray identifier)
    {
        List<(int Number, XrefEntry Entry)> listing = Listing(entries, size);
        buffer.Write("xref\n"u8);
        for (int run = 0; run < listing.Count;)
        {
            int end = run + 1;
            while (end < listing.Count && listing[end].Number == listing[end - 1].Number + 1)
            {
                end++;
            }

            WriteInteger(buffer, listing[run].Number);
            buffer.Write(" "u8);
            WriteInteger(buffer, end - run);
            buffer.Write("\n"u8);
            for (int index = run; index < end; index++)
            {
                XrefEntry entry = listing[index].Entry;
                if (entry.Offset > MaxTableOffset)
                {
                    throw new InvalidOperationException(
                        "The file is too large for a classic cross-reference table, whose offsets have ten digits; save with PdfCrossReferenceLayout.Stream.");
                }

                Span<byte> line = buffer.GetSpan(20);
                entry.Offset.TryFormat(line, out _, "D10", CultureInfo.InvariantCulture);
                line[10] = (byte)' ';
                entry.Generation.TryFormat(line[11..], out _, "D5", CultureInfo.InvariantCulture);
                line[16] = (byte)' ';
                line[17] = entry.Kind == XrefEntryKind.Free ? (byte)'f' : (byte)'n';
                line[18] = (byte)'\r';
                line[19] = (byte)'\n';
                buffer.Advance(20);
            }

            run = end;
        }

        CosDictionary trailer = TrailerDictionary(size, identifier);
        buffer.Write("trailer\n"u8);
        CosWriter.Write(trailer, buffer, _references);
        long sectionEnd = position + buffer.WrittenSpan.Length;
        WriteEnd(buffer, position - origin);
        return new XrefSection(position, sectionEnd, XrefSectionKind.Table, SectionEntries(listing), trailer);
    }

    /// <summary>Writes a cross-reference stream that lists itself, then the end of the file (§7.5.8).</summary>
    /// <returns>The section as a reader would read it, with absolute offsets.</returns>
    private XrefSection WriteStream(PooledBufferWriter buffer, Dictionary<int, XrefEntry> entries, int number, long position, long origin, CosArray identifier)
    {
        int size = number + 1;
        entries[number] = new XrefEntry(XrefEntryKind.InUse, position - origin, 0);
        List<(int Number, XrefEntry Entry)> listing = Listing(entries, size);

        long largest = 0;
        foreach ((_, XrefEntry entry) in listing)
        {
            largest = Math.Max(largest, entry.Offset);
        }

        int middle = 1;
        while (middle < 8 && largest >> (8 * middle) != 0)
        {
            middle++;
        }

        int columns = 1 + middle + 2;
        byte[] rows = new byte[listing.Count * (columns + 1)];
        byte[] previous = new byte[columns];
        byte[] row = new byte[columns];
        Span<byte> field = stackalloc byte[8];
        var index = new List<CosObject>();
        for (int at = 0; at < listing.Count; at++)
        {
            (int listed, XrefEntry entry) = listing[at];
            if (at == 0 || listed != listing[at - 1].Number + 1)
            {
                index.Add(new CosInteger(listed));
                index.Add(new CosInteger(0));
            }

            index[^1] = new CosInteger(((CosInteger)index[^1]).Value + 1);
            row[0] = entry.Kind switch
            {
                XrefEntryKind.Free => 0,
                XrefEntryKind.InUse => 1,
                _ => 2,
            };
            BinaryPrimitives.WriteInt64BigEndian(field, entry.Offset);
            field[(8 - middle)..].CopyTo(row.AsSpan(1, middle));
            BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(1 + middle, 2), (ushort)entry.Generation);

            // PNG Up predictor (§7.4.4.4, Predictor 12): each byte minus the byte above it.
            int offset = at * (columns + 1);
            rows[offset] = 2;
            for (int column = 0; column < columns; column++)
            {
                rows[offset + 1 + column] = (byte)(row[column] - previous[column]);
            }

            (previous, row) = (row, previous);
        }

        byte[] data = Deflate(rows);

        CosDictionary trailer = TrailerDictionary(size, identifier);
        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = KnownNames.XRef,
        };
        foreach (KeyValuePair<CosName, CosObject> entry in trailer)
        {
            dictionary[entry.Key] = entry.Value;
        }

        dictionary[KnownNames.W] = new CosArray([new CosInteger(1), new CosInteger(middle), new CosInteger(2)]);

        // Index defaults to [0 Size] (§7.5.8.2, Table 17), which a complete file's one subsection is.
        if (index.Count != 2 || listing[0].Number != 0 || listing.Count != size)
        {
            dictionary[KnownNames.Index] = new CosArray(index);
        }

        dictionary[FilterNames.Filter] = FilterNames.FlateDecode;
        dictionary[FilterNames.DecodeParms] = new CosDictionary { [FilterNames.Predictor] = new CosInteger(12), [FilterNames.Columns] = new CosInteger(columns) };

        var reference = new CosReference(number, 0);
        var stream = new CosStream(dictionary, data);
        (_, int end) = WriteIndirectObject(buffer, reference, new WriterObject(reference, stream, null), _references);
        long sectionEnd = position + end + "\nendobj".Length;
        WriteEnd(buffer, position - origin);
        return new XrefSection(position, sectionEnd, XrefSectionKind.Stream, SectionEntries(listing), trailer) { Stream = stream };
    }

    /// <summary>
    /// The entries a cross-reference section lists, in ascending number. A complete file, and an update to a file whose
    /// cross-reference information was rebuilt, list every number below <paramref name="size"/> (<see cref="Complete"/>). Any other
    /// update lists object 0, the head of the free list, then the objects written and the numbers freed (§7.5.6, H.7.4): the freed
    /// numbers link from 0 in ascending order back to 0, each with the generation a reuse would take.
    /// </summary>
    private List<(int Number, XrefEntry Entry)> Listing(Dictionary<int, XrefEntry> entries, int size)
    {
        var listing = new List<(int Number, XrefEntry Entry)>();
        if (_update is not { PreviousSection: not null })
        {
            if (_update is not null)
            {
                foreach ((int number, XrefEntry entry) in _update.KeptEntries)
                {
                    entries.TryAdd(number, entry);
                }
            }

            XrefEntry[] table = Complete(entries, size);
            for (int number = 0; number < table.Length; number++)
            {
                listing.Add((number, table[number]));
            }

            return listing;
        }

        int[] freed = [.. _freeGenerations.Keys.Order()];
        listing.Add((0, new XrefEntry(XrefEntryKind.Free, freed.Length > 0 ? freed[0] : 0, CosReference.MaxGeneration)));
        for (int index = 0; index < freed.Length; index++)
        {
            int next = index + 1 < freed.Length ? freed[index + 1] : 0;
            listing.Add((freed[index], new XrefEntry(XrefEntryKind.Free, next, _freeGenerations[freed[index]])));
        }

        foreach ((int number, XrefEntry entry) in entries)
        {
            listing.Add((number, entry));
        }

        listing.Sort((left, right) => left.Number.CompareTo(right.Number));
        return listing;
    }

    /// <summary>The entries of a written section as <see cref="XrefSection.Entries"/> holds them: every listed number but 0.</summary>
    private static Dictionary<int, XrefEntry> SectionEntries(List<(int Number, XrefEntry Entry)> listing)
    {
        var entries = new Dictionary<int, XrefEntry>(listing.Count);
        foreach ((int number, XrefEntry entry) in listing)
        {
            if (number != 0)
            {
                entries[number] = entry;
            }
        }

        return entries;
    }

    /// <summary>The trailer: <c>Size</c>, the given entries, an update's <c>Prev</c>, then <c>ID</c> (§7.5.5, Table 15; §7.5.6).</summary>
    private CosDictionary TrailerDictionary(int size, CosArray identifier)
    {
        var trailer = new CosDictionary { [KnownNames.Size] = new CosInteger(size) };
        foreach (KeyValuePair<CosName, CosObject> entry in _trailer)
        {
            trailer[entry.Key] = entry.Value;
        }

        if (_update?.PreviousSection is { } previous)
        {
            trailer[KnownNames.Prev] = new CosInteger(previous);
        }

        trailer[KnownNames.ID] = identifier;
        return trailer;
    }

    /// <summary>
    /// Fills in a free entry for every number below <paramref name="size"/> that holds no object, and links the free entries into a
    /// list from object 0, which has generation 65535 (§7.5.4).
    /// </summary>
    private XrefEntry[] Complete(Dictionary<int, XrefEntry> entries, int size)
    {
        var table = new XrefEntry[size];
        int lastFree = 0;
        for (int number = size - 1; number >= 0; number--)
        {
            if (number > 0 && entries.TryGetValue(number, out XrefEntry entry))
            {
                table[number] = entry;
                continue;
            }

            int generation = number == 0 ? CosReference.MaxGeneration : _freeGenerations.GetValueOrDefault(number);
            table[number] = new XrefEntry(XrefEntryKind.Free, lastFree, generation);
            lastFree = number;
        }

        return table;
    }

    /// <summary>
    /// The data of a <c>FlateDecode</c> stream (§7.4.4) holding <paramref name="data"/>, written by the engine's <c>FlateDecode</c>
    /// encoder at the save's compression level.
    /// </summary>
    private byte[] Deflate(ReadOnlySpan<byte> data)
    {
        var output = new ArrayBufferWriter<byte>(Math.Max(256, data.Length / 2));
        _flate.Encode(data, output, new StreamEncoderContext { CompressionLevel = _compressionLevel });
        return output.WrittenSpan.ToArray();
    }

    /// <summary>Writes <c>startxref</c>, the offset of the cross-reference information, and <c>%%EOF</c> (§7.5.5).</summary>
    private static void WriteEnd(IBufferWriter<byte> buffer, long xrefOffset)
    {
        buffer.Write("\nstartxref\n"u8);
        WriteInteger(buffer, xrefOffset);
        buffer.Write("\n%%EOF\n"u8);
    }

    private static void WriteInteger(IBufferWriter<byte> buffer, long value)
    {
        Span<byte> span = buffer.GetSpan(20);
        value.TryFormat(span, out int written, default, CultureInfo.InvariantCulture);
        buffer.Advance(written);
    }

    private static void WriteAscii(IBufferWriter<byte> buffer, string text)
    {
        Span<byte> span = buffer.GetSpan(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            span[index] = (byte)text[index];
        }

        buffer.Advance(text.Length);
    }
}
