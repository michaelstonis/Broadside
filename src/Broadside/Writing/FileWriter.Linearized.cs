using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Writing;

/// <summary>The linearized layout of a full save (ISO 32000-2 Annex F).</summary>
internal sealed partial class FileWriter
{
    /// <summary>The width every offset in the linearization parameter dictionary and the first-page trailer is padded to.</summary>
    private const int PaddedWidth = 10;

    /// <summary>The bytes of a cross-reference stream entry's offset field: the body is held in one array, so offsets fit in four.</summary>
    private const int LinearizedOffsetWidth = 4;

    private static readonly CosName SharedObjectTable = new("S");
    private static readonly CosName OutlineTable = new("O");

    /// <summary>The keys of a cross-reference stream's dictionary that describe the stream or the section rather than the document (§7.3.8.2, §7.5.8.2).</summary>
    private static readonly HashSet<CosName> SectionKeys =
    [
        KnownNames.Type, KnownNames.Length, KnownNames.W, KnownNames.Index, KnownNames.Size, KnownNames.Prev, KnownNames.XRefStm, KnownNames.ID,
        FilterNames.Filter, FilterNames.DecodeParms, FilterNames.F, new CosName("FFilter"), new CosName("FDecodeParms"), new CosName("DL"),
    ];

    /// <summary>
    /// Gets the layout of a linearized file, or <see langword="null"/> for an ordinary one. With a layout, the objects are in written
    /// order (<see cref="LinearizedLayout.Objects"/>) under their new numbers, and <see cref="Write"/> writes a linearized file.
    /// </summary>
    public LinearizedLayout? Linearized { get; init; }

    /// <summary>Gets a value indicating whether the primary hint stream is compressed with the <c>FlateDecode</c> encoder.</summary>
    public bool CompressHintStream { get; init; }

    /// <summary>
    /// Writes a linearized file (Annex F, F.3): header, linearization parameter dictionary, first-page cross-reference section and
    /// trailer, part 4, the primary hint stream, parts 6 to 9 (object streams last), the main cross-reference section and trailer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The objects are written to memory first, because the file starts with what only the finished layout knows: the file length,
    /// the hint stream's place and length, the end of the first page and the offset of the main section (Table F.1), and the first-page
    /// section's offsets. Those values are padded to a fixed width, so the beginning of the file has the same length whatever they
    /// are and nothing has to be written twice. Hint table offsets leave the hint stream out (F.4.1), so the tables depend only on
    /// the objects.
    /// </para>
    /// <para>
    /// The first-page section lists the linearization dictionary, the first-page cross-reference stream (for a stream layout), part
    /// 4, part 6 and the hint stream, in one subsection with no free entries (F.3.4); its trailer holds the document's entries and a
    /// <c>Prev</c> pointing forward at the main section, and ends with a dummy <c>startxref 0</c>. The main section lists object 0
    /// and every object below the first-page section; its trailer holds only <c>Size</c>, and its <c>startxref</c> points at the
    /// first-page section (F.3.11). The second file identifier is derived from the objects' bytes (§14.4).
    /// </para>
    /// </remarks>
    private IEnumerable<ReadOnlyMemory<byte>> WriteLinearized(LinearizedLayout layout)
    {
        using var body = new PooledBufferWriter(ChunkSize * 2, Array.MaxLength);
        int uncompressed = _objects.Count - layout.CompressedCount;
        long[] starts = new long[uncompressed + layout.ObjectStreamCount + 1];
        for (int index = 0; index < uncompressed; index++)
        {
            starts[index] = body.WrittenSpan.Length;
            WriteIndirectObject(body, _objects[index].Reference, _objects[index], _references);
        }

        // Object streams: the part 9 objects nothing else needs, at the end of the body (F.3.1, §7.5.7).
        var compressedEntries = new Dictionary<int, XrefEntry>();
        var members = new List<WriterObject>(layout.CompressedCount);
        for (int index = uncompressed; index < _objects.Count; index++)
        {
            members.Add(_objects[index]);
        }

        for (int stream = 0; stream < layout.ObjectStreamCount; stream++)
        {
            int start = stream * ObjectStreamCapacity;
            int count = Math.Min(ObjectStreamCapacity, members.Count - start);
            var reference = new CosReference(layout.FirstObjectStreamNumber + stream, 0);
            for (int index = 0; index < count; index++)
            {
                compressedEntries[members[start + index].Reference.ObjectNumber] = new XrefEntry(XrefEntryKind.Compressed, reference.ObjectNumber, index);
            }

            starts[uncompressed + stream] = body.WrittenSpan.Length;
            WriteIndirectObject(body, reference, new WriterObject(reference, BuildObjectStream(members, start, count), null), references: null);
        }

        starts[^1] = body.WrittenSpan.Length;
        ReadOnlyMemory<byte> written = body.WrittenMemory;

        int firstPageStart = layout.OpenDocumentCount;
        int firstPageEnd = firstPageStart + layout.PageObjectCounts[0];
        long hintPosition = starts[firstPageStart];

        byte[] digest = SHA256.HashData(written.Span);
        var secondIdentifier = new CosString(digest.AsSpan(0, 16), hexadecimal: true);
        var identifier = new CosArray([_firstIdentifier ?? secondIdentifier, secondIdentifier]);

        // The beginning of the file has a fixed length; render it once with zeros to learn it.
        int firstSectionCount = layout.HintStreamNumber - layout.LinearizationNumber + 1;
        long[] firstSectionOffsets = new long[firstSectionCount];
        var placeholder = new LinearizationValues(0, 0, 0, 0, 0, 0);
        int prefixLength = WritePrefix(new ArrayBufferWriter<byte>(), layout, placeholder, firstSectionOffsets, identifier, out long firstSectionOffset);

        // The hint stream, from offsets that leave it out (F.4.1).
        CosStream hintStream = BuildHintStream(layout, starts, prefixLength);
        var hintReference = new CosReference(layout.HintStreamNumber, 0);
        ReadOnlyMemory<byte> hintObject;
        using (var hintBuffer = new PooledBufferWriter(ChunkSize, Array.MaxLength))
        {
            WriteIndirectObject(hintBuffer, hintReference, new WriterObject(hintReference, hintStream, null), references: null);
            hintObject = hintBuffer.WrittenSpan.ToArray();
        }

        long hintLength = hintObject.Length;

        long Offset(int index) => prefixLength + starts[index] + (starts[index] >= hintPosition ? hintLength : 0);

        // The main section: object 0, parts 7 to 9, the object streams, the main cross-reference stream, the compressed objects.
        long mainOffset = prefixLength + written.Length + hintLength;
        var mainEntries = new Dictionary<int, XrefEntry>(compressedEntries);
        for (int index = firstPageEnd; index < uncompressed; index++)
        {
            mainEntries[_objects[index].Reference.ObjectNumber] = new XrefEntry(XrefEntryKind.InUse, Offset(index), 0);
        }

        for (int stream = 0; stream < layout.ObjectStreamCount; stream++)
        {
            mainEntries[layout.FirstObjectStreamNumber + stream] = new XrefEntry(XrefEntryKind.InUse, Offset(uncompressed + stream), 0);
        }

        var main = new ArrayBufferWriter<byte>();
        long firstEntry = WriteMainSection(main, layout, mainEntries, mainOffset, firstSectionOffset);

        // The first-page section's entries, in number order: dictionary, [stream], part 4, part 6, hint stream.
        int slot = layout.FirstPageCrossReferenceNumber == 0 ? 1 : 2;
        for (int index = 0; index < firstPageEnd; index++)
        {
            firstSectionOffsets[slot++] = Offset(index);
        }

        firstSectionOffsets[slot] = prefixLength + hintPosition;
        var values = new LinearizationValues(
            FileLength: mainOffset + main.WrittenCount,
            HintOffset: prefixLength + hintPosition,
            HintLength: hintLength,
            FirstPageEnd: Offset(firstPageEnd),
            MainSection: firstEntry,
            MainSectionOffset: mainOffset);
        var prefix = new ArrayBufferWriter<byte>(prefixLength);
        if (WritePrefix(prefix, layout, values, firstSectionOffsets, identifier, out _) != prefixLength)
        {
            throw new InvalidOperationException("The beginning of a linearized file changed length between its two renderings.");
        }

        yield return prefix.WrittenMemory;
        foreach (ReadOnlyMemory<byte> chunk in Slice(written[..(int)hintPosition]))
        {
            yield return chunk;
        }

        yield return hintObject;
        foreach (ReadOnlyMemory<byte> chunk in Slice(written[(int)hintPosition..]))
        {
            yield return chunk;
        }

        yield return main.WrittenMemory;
    }

    private static IEnumerable<ReadOnlyMemory<byte>> Slice(ReadOnlyMemory<byte> data)
    {
        for (int start = 0; start < data.Length; start += ChunkSize)
        {
            yield return data.Slice(start, Math.Min(ChunkSize, data.Length - start));
        }
    }

    /// <summary>Builds the primary hint stream (F.3.6): the page offset, shared object and outline hint tables, offsets leaving the stream out.</summary>
    private CosStream BuildHintStream(LinearizedLayout layout, long[] starts, int prefixLength)
    {
        int pages = layout.PageObjectCounts.Count;
        long[] pageLengths = new long[pages];
        int index = layout.OpenDocumentCount;
        for (int page = 0; page < pages; page++)
        {
            int end = index + layout.PageObjectCounts[page];
            pageLengths[page] = starts[end] - starts[index];
            index = end;
        }

        int firstPageStart = layout.OpenDocumentCount;
        int sharedStart = index;
        var sharedLengths = new List<long>(layout.PageObjectCounts[0] + layout.SharedCount);
        for (int item = firstPageStart; item < firstPageStart + layout.PageObjectCounts[0]; item++)
        {
            sharedLengths.Add(starts[item + 1] - starts[item]);
        }

        for (int item = sharedStart; item < sharedStart + layout.SharedCount; item++)
        {
            sharedLengths.Add(starts[item + 1] - starts[item]);
        }

        (int, long, int, long)? outlines = layout.Outlines is { } range
            ? (_objects[range.Start].Reference.ObjectNumber, prefixLength + starts[range.Start], range.Count, starts[range.Start + range.Count] - starts[range.Start])
            : null;
        var hints = new LinearizationHints(
            prefixLength + starts[firstPageStart],
            layout.PageObjectCounts,
            pageLengths,
            layout.SharedIdentifiers,
            layout.PageObjectCounts[0],
            sharedLengths,
            layout.SharedCount > 0 ? _objects[sharedStart].Reference.ObjectNumber : 0,
            layout.SharedCount > 0 ? prefixLength + starts[sharedStart] : 0,
            outlines);
        byte[] data = HintTableWriter.Write(hints, out int sharedTable, out int? outlineTable);

        var dictionary = new CosDictionary { [SharedObjectTable] = new CosInteger(sharedTable) };
        if (outlineTable is { } outline)
        {
            dictionary[OutlineTable] = new CosInteger(outline);
        }

        if (CompressHintStream)
        {
            dictionary[FilterNames.Filter] = FilterNames.FlateDecode;
            data = Deflate(data);
        }

        return new CosStream(dictionary, data);
    }

    /// <summary>
    /// Writes the beginning of the file: header, linearization parameter dictionary (Table F.1) and first-page cross-reference section
    /// and trailer (F.3.4). Its length does not depend on <paramref name="values"/> or <paramref name="offsets"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    private int WritePrefix(
        ArrayBufferWriter<byte> buffer,
        LinearizedLayout layout,
        LinearizationValues values,
        long[] offsets,
        CosArray identifier,
        out long sectionOffset)
    {
        WriteHeader(buffer);
        offsets[0] = buffer.WrittenCount;
        WriteInteger(buffer, layout.LinearizationNumber);
        buffer.Write(" 0 obj\n<< /Linearized 1 /L "u8);
        WritePadded(buffer, values.FileLength);
        buffer.Write(" /H [ "u8);
        WritePadded(buffer, values.HintOffset);
        buffer.Write(" "u8);
        WritePadded(buffer, values.HintLength);
        buffer.Write(" ] /O "u8);
        WriteInteger(buffer, layout.FirstPageObjectNumber);
        buffer.Write(" /E "u8);
        WritePadded(buffer, values.FirstPageEnd);
        buffer.Write(" /N "u8);
        WriteInteger(buffer, layout.PageObjectCounts.Count);
        buffer.Write(" /T "u8);
        WritePadded(buffer, values.MainSection);
        buffer.Write(" >>\nendobj\n"u8);

        sectionOffset = buffer.WrittenCount;
        int count = offsets.Length;
        int size = layout.HintStreamNumber + 1;
        if (layout.FirstPageCrossReferenceNumber == 0)
        {
            buffer.Write("xref\n"u8);
            WriteInteger(buffer, layout.LinearizationNumber);
            buffer.Write(" "u8);
            WriteInteger(buffer, count);
            buffer.Write("\n"u8);
            foreach (long offset in offsets)
            {
                WriteTableEntry(buffer, new XrefEntry(XrefEntryKind.InUse, offset, 0));
            }

            buffer.Write("trailer\n"u8);
            WriteFirstPageTrailer(buffer, size, identifier, values.MainSectionOffset, entries: null);
        }
        else
        {
            offsets[1] = sectionOffset;
            int columns = 1 + LinearizedOffsetWidth + 2;
            byte[] rows = new byte[count * columns];
            for (int index = 0; index < count; index++)
            {
                Span<byte> row = rows.AsSpan(index * columns, columns);
                row[0] = 1;
                BinaryPrimitives.WriteUInt32BigEndian(row[1..], (uint)offsets[index]);
            }

            WriteInteger(buffer, layout.FirstPageCrossReferenceNumber);
            buffer.Write(" 0 obj\n"u8);
            var entries = new CosDictionary
            {
                [KnownNames.Type] = KnownNames.XRef,
                [KnownNames.Index] = new CosArray([new CosInteger(layout.LinearizationNumber), new CosInteger(count)]),
                [KnownNames.W] = new CosArray([new CosInteger(1), new CosInteger(LinearizedOffsetWidth), new CosInteger(2)]),
                [KnownNames.Length] = new CosInteger(rows.Length),
            };
            WriteFirstPageTrailer(buffer, size, identifier, values.MainSectionOffset, entries);
            buffer.Write("\nstream\n"u8);
            buffer.Write(rows);
            buffer.Write("\nendstream\nendobj\n"u8);
        }

        buffer.Write("startxref\n0\n%%EOF\n"u8);
        return buffer.WrittenCount;
    }

    /// <summary>
    /// Writes the first-page trailer (F.3.4), or the first-page cross-reference stream's dictionary with <paramref name="entries"/>:
    /// <c>Size</c> for both sections, the document's trailer entries, <c>ID</c>, and <c>Prev</c> padded to a fixed width.
    /// </summary>
    private void WriteFirstPageTrailer(ArrayBufferWriter<byte> buffer, int size, CosArray identifier, long previous, CosDictionary? entries)
    {
        // The document's trailer entries, without any that describe a cross-reference section: a damaged source's trailer may carry
        // a stream's W or Filter, which must not reach the first-page cross-reference stream.
        var dictionary = new CosDictionary { [KnownNames.Size] = new CosInteger(size) };
        foreach (KeyValuePair<CosName, CosObject> entry in _trailer)
        {
            if (!SectionKeys.Contains(entry.Key))
            {
                dictionary[entry.Key] = entry.Value;
            }
        }

        if (entries is not null)
        {
            foreach (KeyValuePair<CosName, CosObject> entry in entries)
            {
                dictionary[entry.Key] = entry.Value;
            }
        }

        dictionary[KnownNames.ID] = identifier;
        buffer.Write("<<"u8);
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            buffer.Write(" "u8);
            CosWriter.Write(entry.Key, buffer);
            buffer.Write(" "u8);
            CosWriter.Write(entry.Value, buffer, _references);
        }

        buffer.Write(" /Prev "u8);
        WritePadded(buffer, previous);
        buffer.Write(" >>"u8);
        if (entries is null)
        {
            buffer.Write("\n"u8);
        }
    }

    /// <summary>
    /// Writes the main cross-reference section and trailer (F.3.11): a table or a cross-reference stream for object 0 and every number
    /// below the first-page section, with a trailer holding only <c>Size</c>, then <c>startxref</c> with the first-page section's
    /// offset and <c>%%EOF</c>.
    /// </summary>
    /// <returns>The <c>T</c> entry (Table F.1): the offset of the white-space before the entry for object 0, or of the stream object.</returns>
    private long WriteMainSection(ArrayBufferWriter<byte> buffer, LinearizedLayout layout, Dictionary<int, XrefEntry> entries, long position, long firstSection)
    {
        int size = layout.MainSectionSize;
        long firstEntry;
        if (layout.MainCrossReferenceNumber == 0)
        {
            buffer.Write("xref\n0 "u8);
            WriteInteger(buffer, size);
            firstEntry = position + buffer.WrittenCount;
            buffer.Write("\n"u8);
            WriteTableEntry(buffer, new XrefEntry(XrefEntryKind.Free, 0, CosReference.MaxGeneration));
            for (int number = 1; number < size; number++)
            {
                WriteTableEntry(buffer, entries[number]);
            }

            buffer.Write("trailer\n<< /Size "u8);
            WriteInteger(buffer, size);
            buffer.Write(" >>\n"u8);
        }
        else
        {
            firstEntry = position;
            entries[layout.MainCrossReferenceNumber] = new XrefEntry(XrefEntryKind.InUse, position, 0);
            var reference = new CosReference(layout.MainCrossReferenceNumber, 0);
            CosStream stream = BuildCrossReferenceStream(entries, size);
            using var objectBuffer = new PooledBufferWriter(ChunkSize, Array.MaxLength);
            WriteIndirectObject(objectBuffer, reference, new WriterObject(reference, stream, null), references: null);
            buffer.Write(objectBuffer.WrittenSpan);
        }

        buffer.Write("startxref\n"u8);
        WriteInteger(buffer, firstSection);
        buffer.Write("\n%%EOF\n"u8);
        return firstEntry;
    }

    /// <summary>
    /// Builds a cross-reference stream for numbers 0 to <paramref name="size"/> - 1 (§7.5.8): object 0 free, the others from
    /// <paramref name="entries"/>, <c>FlateDecode</c> with the PNG Up predictor, a trailer of <c>Size</c> only.
    /// </summary>
    private CosStream BuildCrossReferenceStream(Dictionary<int, XrefEntry> entries, int size)
    {
        long largest = entries.Values.Max(entry => entry.Offset);
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
        for (int number = 0; number < size; number++)
        {
            XrefEntry entry = number == 0 ? new XrefEntry(XrefEntryKind.Free, 0, CosReference.MaxGeneration) : entries[number];
            row[0] = entry.Kind switch
            {
                XrefEntryKind.Free => 0,
                XrefEntryKind.InUse => 1,
                _ => 2,
            };
            BinaryPrimitives.WriteInt64BigEndian(field, entry.Offset);
            field[(8 - middle)..].CopyTo(row.AsSpan(1, middle));
            BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(1 + middle, 2), (ushort)entry.Generation);
            int offset = number * (columns + 1);
            rows[offset] = 2;
            for (int column = 0; column < columns; column++)
            {
                rows[offset + 1 + column] = (byte)(row[column] - previous[column]);
            }

            (previous, row) = (row, previous);
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = KnownNames.XRef,
            [KnownNames.Size] = new CosInteger(size),
            [KnownNames.W] = new CosArray([new CosInteger(1), new CosInteger(middle), new CosInteger(2)]),
            [FilterNames.Filter] = FilterNames.FlateDecode,
            [FilterNames.DecodeParms] = new CosDictionary { [FilterNames.Predictor] = new CosInteger(12), [FilterNames.Columns] = new CosInteger(columns) },
        };
        return new CosStream(dictionary, Deflate(rows));
    }

    /// <summary>Writes one 20-byte classic cross-reference entry (§7.5.4).</summary>
    private static void WriteTableEntry(ArrayBufferWriter<byte> buffer, XrefEntry entry)
    {
        if (entry.Offset > MaxTableOffset)
        {
            throw new InvalidOperationException("The file is too large for a classic cross-reference table, whose offsets have ten digits.");
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

    /// <summary>Writes an integer followed by spaces up to <see cref="PaddedWidth"/> characters.</summary>
    private static void WritePadded(ArrayBufferWriter<byte> buffer, long value)
    {
        Span<byte> span = buffer.GetSpan(PaddedWidth)[..PaddedWidth];
        if (!value.TryFormat(span, out int written, default, CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException("The file is too large to linearize.");
        }

        span[written..].Fill((byte)' ');
        buffer.Advance(PaddedWidth);
    }

    /// <summary>The values of the linearization parameter dictionary and first-page trailer that depend on the finished layout.</summary>
    private readonly record struct LinearizationValues(long FileLength, long HintOffset, long HintLength, long FirstPageEnd, long MainSection, long MainSectionOffset);
}
