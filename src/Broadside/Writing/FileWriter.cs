using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Writing;

/// <summary>One indirect object to write: its number and generation, its value, and its source bytes when they may be copied.</summary>
/// <param name="Reference">The object's number and generation, kept from the source.</param>
/// <param name="Value">The object.</param>
/// <param name="Source">The bytes the object was read from, copied instead of serializing <paramref name="Value"/>; <see langword="null"/> to serialize.</param>
internal readonly record struct WriterObject(CosReference Reference, CosObject Value, ObjectSourceBytes? Source);

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
/// Output is deterministic: the second file identifier is the first 16 bytes of the SHA-256 hash of everything written before the
/// cross-reference information, and the first is the source's or, for a file without one, the same value (§14.4: both are equal
/// when a file is first written). Nothing depends on the clock or a random source.
/// </para>
/// </remarks>
internal sealed class FileWriter
{
    /// <summary>How many bytes are gathered before a chunk is handed out.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>The most objects one object stream holds, to keep random access cheap (§7.5.7, NOTE 4).</summary>
    private const int ObjectStreamCapacity = 100;

    /// <summary>The largest offset a classic cross-reference entry's ten digits can state (§7.5.4).</summary>
    private const long MaxTableOffset = 9_999_999_999;

    private static readonly PdfVersion StreamVersion = new(1, 5);
    private static readonly CosName XRef = new("XRef");
    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName FlateDecode = new("FlateDecode");
    private static readonly CosName W = new("W");
    private static readonly CosName N = new("N");
    private static readonly CosName First = new("First");
    private static readonly CosName Filter = new("Filter");
    private static readonly CosName DecodeParms = new("DecodeParms");
    private static readonly CosName Predictor = new("Predictor");
    private static readonly CosName Columns = new("Columns");

    private readonly PdfSource? _source;
    private readonly PdfVersion _version;
    private readonly IReadOnlyList<WriterObject> _objects;
    private readonly IReadOnlyDictionary<int, int> _freeGenerations;
    private readonly CosDictionary _trailer;
    private readonly CosString? _firstIdentifier;
    private readonly PdfCrossReferenceLayout _layout;

    /// <summary>Initializes a new instance of the <see cref="FileWriter"/> class.</summary>
    /// <param name="source">The file source bytes are copied from; <see langword="null"/> when no object has source bytes.</param>
    /// <param name="version">The version for the header; raised to 1.5 for a cross-reference stream.</param>
    /// <param name="objects">The objects to write, in ascending object number.</param>
    /// <param name="freeGenerations">For object numbers that are free, the generation their free entry carries; others default to 0.</param>
    /// <param name="trailer">The trailer entries to write besides <c>Size</c> and <c>ID</c>.</param>
    /// <param name="firstIdentifier">The first file identifier to keep, or <see langword="null"/> for a file written for the first time.</param>
    /// <param name="layout">The cross-reference layout.</param>
    public FileWriter(
        PdfSource? source,
        PdfVersion version,
        IReadOnlyList<WriterObject> objects,
        IReadOnlyDictionary<int, int> freeGenerations,
        CosDictionary trailer,
        CosString? firstIdentifier,
        PdfCrossReferenceLayout layout)
    {
        _source = source;
        _version = layout != PdfCrossReferenceLayout.Table && version < StreamVersion ? StreamVersion : version;
        _objects = objects;
        _freeGenerations = freeGenerations;
        _trailer = trailer;
        _firstIdentifier = firstIdentifier;
        _layout = layout;
    }

    /// <summary>Writes the file. Each chunk is valid until the next one is requested.</summary>
    /// <returns>The file's bytes, in order.</returns>
    public IEnumerable<ReadOnlyMemory<byte>> Write()
    {
        using var buffer = new PooledBufferWriter(ChunkSize * 2, Array.MaxLength);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = new Dictionary<int, XrefEntry>();
        long flushed = 0;

        WriteHeader(buffer);

        bool compress = _layout == PdfCrossReferenceLayout.StreamWithObjectStreams;
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

            entries[item.Reference.ObjectNumber] = new XrefEntry(XrefEntryKind.InUse, flushed + buffer.WrittenSpan.Length, item.Reference.Generation);
            WriteIndirectObject(buffer, item.Reference, item, _source);
            if (buffer.WrittenSpan.Length >= ChunkSize)
            {
                hash.AppendData(buffer.WrittenSpan);
                yield return buffer.WrittenMemory;
                flushed += buffer.WrittenSpan.Length;
                buffer.Clear();
            }
        }

        int nextNumber = maxNumber + 1;
        for (int start = 0; start < compressible.Count; start += ObjectStreamCapacity)
        {
            int container = nextNumber++;
            int count = Math.Min(ObjectStreamCapacity, compressible.Count - start);
            for (int index = 0; index < count; index++)
            {
                entries[compressible[start + index].Reference.ObjectNumber] = new XrefEntry(XrefEntryKind.Compressed, container, index);
            }

            CosStream objectStream = BuildObjectStream(compressible, start, count);
            var reference = new CosReference(container, 0);
            entries[container] = new XrefEntry(XrefEntryKind.InUse, flushed + buffer.WrittenSpan.Length, 0);
            WriteIndirectObject(buffer, reference, new WriterObject(reference, objectStream, null), source: null);
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

        if (_layout == PdfCrossReferenceLayout.Table)
        {
            WriteTable(buffer, entries, nextNumber, flushed, identifier);
        }
        else
        {
            WriteStream(buffer, entries, nextNumber, flushed, identifier);
        }

        yield return buffer.WrittenMemory;
    }

    /// <summary>Whether an object may be stored in an object stream (§7.5.7).</summary>
    private static bool CanBeCompressed(WriterObject item) =>
        item.Reference.Generation == 0 && item.Value is not (CosStream or CosReference);

    /// <summary>Writes <c>%PDF-n.m</c> and a comment line of four bytes above 127, marking the file as binary (§7.5.2).</summary>
    private void WriteHeader(IBufferWriter<byte> buffer)
    {
        buffer.Write("%PDF-"u8);
        WriteAscii(buffer, _version.ToString());
        buffer.Write("\n%"u8);
        buffer.Write<byte>([0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
    }

    /// <summary>Writes <c>N G obj</c>, the object's bytes, and <c>endobj</c> (§7.3.10).</summary>
    private static void WriteIndirectObject(PooledBufferWriter buffer, CosReference reference, WriterObject item, PdfSource? source)
    {
        WriteInteger(buffer, reference.ObjectNumber);
        buffer.Write(" "u8);
        WriteInteger(buffer, reference.Generation);
        buffer.Write(" obj\n"u8);
        WriteBody(buffer, item, source);
        buffer.Write("\nendobj\n"u8);
    }

    /// <summary>Writes an object's bytes: copied from the source when it has them, serialized otherwise.</summary>
    private static void WriteBody(PooledBufferWriter buffer, WriterObject item, PdfSource? source)
    {
        if (item.Source is not { } bytes)
        {
            item.Value.WriteTo(buffer);
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
            WriteBody(bodies, item, _source);
        }

        header.Write("\n"u8);
        var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(header.WrittenSpan);
            zlib.Write(bodies.WrittenSpan);
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = ObjStm,
            [N] = new CosInteger(count),
            [First] = new CosInteger(header.WrittenCount),
            [Filter] = FlateDecode,
        };
        return new CosStream(dictionary, data.ToArray());
    }

    /// <summary>Writes a classic cross-reference table and trailer (§7.5.4, §7.5.5).</summary>
    private void WriteTable(PooledBufferWriter buffer, Dictionary<int, XrefEntry> entries, int size, long xrefOffset, CosArray identifier)
    {
        XrefEntry[] table = Complete(entries, size);
        buffer.Write("xref\n0 "u8);
        WriteInteger(buffer, size);
        buffer.Write("\n"u8);
        foreach (XrefEntry entry in table)
        {
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

        CosDictionary trailer = TrailerDictionary(size, identifier);
        buffer.Write("trailer\n"u8);
        trailer.WriteTo(buffer);
        WriteEnd(buffer, xrefOffset);
    }

    /// <summary>Writes a cross-reference stream that lists itself, then the end of the file (§7.5.8).</summary>
    private void WriteStream(PooledBufferWriter buffer, Dictionary<int, XrefEntry> entries, int number, long offset, CosArray identifier)
    {
        int size = number + 1;
        entries[number] = new XrefEntry(XrefEntryKind.InUse, offset, 0);
        XrefEntry[] table = Complete(entries, size);

        long largest = 0;
        foreach (XrefEntry entry in table)
        {
            largest = Math.Max(largest, entry.Offset);
        }

        int middle = 1;
        while (middle < 8 && largest >> (8 * middle) != 0)
        {
            middle++;
        }

        int columns = 1 + middle + 2;
        byte[] rows = new byte[size * (columns + 1)];
        byte[] previous = new byte[columns];
        byte[] row = new byte[columns];
        Span<byte> field = stackalloc byte[8];
        for (int index = 0; index < size; index++)
        {
            XrefEntry entry = table[index];
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
            int at = index * (columns + 1);
            rows[at] = 2;
            for (int column = 0; column < columns; column++)
            {
                rows[at + 1 + column] = (byte)(row[column] - previous[column]);
            }

            (previous, row) = (row, previous);
        }

        var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(rows);
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = XRef,
        };
        foreach (KeyValuePair<CosName, CosObject> entry in TrailerDictionary(size, identifier))
        {
            dictionary[entry.Key] = entry.Value;
        }

        dictionary[W] = new CosArray([new CosInteger(1), new CosInteger(middle), new CosInteger(2)]);
        dictionary[Filter] = FlateDecode;
        dictionary[DecodeParms] = new CosDictionary { [Predictor] = new CosInteger(12), [Columns] = new CosInteger(columns) };

        var reference = new CosReference(number, 0);
        WriteIndirectObject(buffer, reference, new WriterObject(reference, new CosStream(dictionary, data.ToArray()), null), source: null);
        WriteEnd(buffer, offset);
    }

    /// <summary>The trailer: <c>Size</c>, the given entries, then <c>ID</c> (§7.5.5, Table 15).</summary>
    private CosDictionary TrailerDictionary(int size, CosArray identifier)
    {
        var trailer = new CosDictionary { [KnownNames.Size] = new CosInteger(size) };
        foreach (KeyValuePair<CosName, CosObject> entry in _trailer)
        {
            trailer[entry.Key] = entry.Value;
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
