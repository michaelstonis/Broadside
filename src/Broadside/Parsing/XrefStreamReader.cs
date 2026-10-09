using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Reads one cross-reference stream section: the stream object at an offset, its dictionary and its binary entries.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.8. The stream is read before any object can be loaded, so its <c>Length</c> is used only when direct (an
/// indirect one makes the parser find the data end at <c>endstream</c>), its <c>Filter</c> and <c>DecodeParms</c> shall be direct
/// (§7.5.8.2), and it is never decrypted (§7.6.2). The data is decoded under the engine's decoded-length limit; entries are read
/// only as far as the data goes, so a hostile <c>Index</c> or <c>Size</c> cannot make the reader allocate more than the data holds.
/// </para>
/// <para>
/// Deviations repaired: a missing or wrong <c>Type</c> is read anyway; a malformed <c>Index</c> is replaced by its default
/// <c>[0 Size]</c>; data shorter than the entries ends the section where the data ends; entries for numbers at or beyond
/// <c>Size</c> are kept (Table 15 says they "shall be ignored", which would lose objects real files have); a type 1 entry with
/// offset 0 reads as free, as in a classic table. A <c>W</c> entry that cannot be read makes the section unreadable.
/// </para>
/// </remarks>
internal static class XrefStreamReader
{
    /// <summary>The widest field this reader accepts, in bytes: offsets and object numbers fit in a <see cref="long"/>.</summary>
    public const int MaxFieldWidth = 8;

    private static readonly CosName XRef = new("XRef");
    private static readonly CosName W = new("W");
    private static readonly CosName Index = new("Index");

    /// <summary>The keys of a cross-reference stream's dictionary that describe the stream, not the file (Table 5 and Table 17).</summary>
    private static readonly CosName[] StreamOnlyKeys =
    [
        KnownNames.Type, KnownNames.Length, W, Index, FilterNames.Filter, FilterNames.DecodeParms, FilterNames.DL, FilterNames.F,
        new("FFilter"), new("FDecodeParms"),
    ];

    /// <summary>Reads the cross-reference stream whose <c>N G obj</c> header is at <paramref name="offset"/>.</summary>
    /// <param name="source">The file.</param>
    /// <param name="offset">The absolute offset of the stream object.</param>
    /// <param name="streams">The document's filter pipeline.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>
    /// The section, whose <see cref="XrefSection.Trailer"/> is the stream dictionary without the entries that describe the stream
    /// itself; <see langword="null"/> when no cross-reference stream can be read there.
    /// </returns>
    public static XrefSection? Read(PdfSource source, long offset, StreamDecoder streams, DiagnosticSink diagnostics)
    {
        if (!TryParseStreamObject(source, offset, diagnostics, out CosStream? stream, out CosReference? reference, out long end))
        {
            return null;
        }

        CosDictionary dictionary = stream.Dictionary;
        if (!dictionary.TryGetValue(KnownNames.Type, out CosObject? type) || !XRef.Equals(type))
        {
            Report(diagnostics, DiagnosticCodes.XrefStreamTypeInvalid, DiagnosticSeverity.Warning, "The cross-reference stream's Type entry shall be /XRef; the stream is read as one.", offset, reference);
        }

        if (!TryReadWidths(dictionary, out int[] widths))
        {
            Report(
                diagnostics,
                DiagnosticCodes.XrefStreamWidthsInvalid,
                DiagnosticSeverity.Error,
                $"The cross-reference stream's W entry shall be an array of three direct integers from 0 to {MaxFieldWidth} with a positive sum; the section cannot be read.",
                offset,
                reference);
            return null;
        }

        ReadOnlySpan<byte> data = streams.Decode(stream).Span;
        int entryWidth = widths[0] + widths[1] + widths[2];
        long? size = ReadSize(dictionary, offset, reference, diagnostics);
        List<(long First, long Count)> subsections = ReadIndex(dictionary, size ?? (data.Length / entryWidth), offset, reference, diagnostics);
        var entries = new Dictionary<int, XrefEntry>();
        ReadEntries(data, widths, subsections, size, entries, offset, reference, diagnostics);
        return new XrefSection(offset, end, XrefSectionKind.Stream, entries, Trailer(dictionary)) { Stream = stream };
    }

    /// <summary>Returns the trailer a cross-reference stream stands for: its dictionary without the stream-only entries.</summary>
    private static CosDictionary Trailer(CosDictionary dictionary)
    {
        var trailer = new OrderedDictionary<CosName, CosObject>();
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            if (Array.IndexOf(StreamOnlyKeys, entry.Key) < 0)
            {
                trailer.Add(entry.Key, entry.Value);
            }
        }

        return CosDictionary.FromOwnedEntries(trailer);
    }

    /// <summary>Parses <c>N G obj</c> stream <c>endobj</c> at <paramref name="offset"/> (§7.3.10).</summary>
    private static bool TryParseStreamObject(
        PdfSource source,
        long offset,
        DiagnosticSink diagnostics,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CosStream? stream,
        out CosReference? reference,
        out long end)
    {
        stream = null;
        reference = null;
        end = offset;
        ReadOnlySpan<byte> window = source.GetWindow(offset).Span;
        var lexer = new CosLexer(window);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long number)
            || !StructureTokens.TryReadUnsigned(ref lexer, out long generation)
            || !StructureTokens.TryReadKeyword(ref lexer, "obj"u8)
            || number is 0 or > int.MaxValue
            || generation > CosReference.MaxGeneration)
        {
            Report(diagnostics, DiagnosticCodes.XrefSectionInvalid, DiagnosticSeverity.Error, "Expected the xref keyword or the header of a cross-reference stream object.", offset, null);
            return false;
        }

        reference = new CosReference((int)number, (int)generation);
        var repairs = new CosRepairLog(keepAll: true);
        var parser = new CosParser(window, repairs, lexer.Position);
        CosObject value = parser.ParseObject();
        foreach (CosRepair repair in repairs.All)
        {
            diagnostics.Report(repair.Code, DiagnosticSeverity.Warning, repair.Message, offset + repair.Offset, reference);
        }

        if (value is not CosStream parsed)
        {
            Report(diagnostics, DiagnosticCodes.XrefSectionInvalid, DiagnosticSeverity.Error, "The object at the cross-reference offset is not a stream.", offset, reference);
            return false;
        }

        lexer.Position = parser.Position;
        CosToken endobj = lexer.Next();
        if (StructureTokens.IsKeyword(window, endobj, "endobj"u8))
        {
            end = offset + endobj.End;
        }
        else
        {
            diagnostics.Report(DiagnosticCodes.MissingEndobj, DiagnosticSeverity.Warning, "The object is not followed by the endobj keyword; it ends here.", offset + endobj.Start, reference);
            end = offset + parser.Position;
        }

        stream = parsed;
        return true;
    }

    /// <summary>Reads <c>W</c> (Table 17): three direct integers, each 0 to <see cref="MaxFieldWidth"/>, at least one positive.</summary>
    private static bool TryReadWidths(CosDictionary dictionary, out int[] widths)
    {
        widths = new int[3];
        if (!dictionary.TryGetValue(W, out CosObject? entry) || entry is not CosArray { Count: 3 } array)
        {
            return false;
        }

        for (int index = 0; index < 3; index++)
        {
            if (array[index] is not CosInteger { Value: >= 0 and <= MaxFieldWidth } width)
            {
                return false;
            }

            widths[index] = (int)width.Value;
        }

        return widths[0] + widths[1] + widths[2] > 0;
    }

    /// <summary>Reads <c>Size</c> (Table 15 and Table 17): a direct, non-negative integer.</summary>
    private static long? ReadSize(CosDictionary dictionary, long offset, CosReference? reference, DiagnosticSink diagnostics)
    {
        if (dictionary.TryGetValue(KnownNames.Size, out CosObject? entry) && entry is CosInteger { Value: >= 0 } size)
        {
            return size.Value;
        }

        Report(
            diagnostics,
            DiagnosticCodes.XrefStreamSizeInvalid,
            DiagnosticSeverity.Warning,
            "The cross-reference stream's Size entry shall be a direct, non-negative integer; the entries are read as far as the data goes.",
            offset,
            reference);
        return null;
    }

    /// <summary>Reads <c>Index</c> (Table 17): pairs of first object number and count; default <c>[0 Size]</c>.</summary>
    private static List<(long First, long Count)> ReadIndex(CosDictionary dictionary, long defaultCount, long offset, CosReference? reference, DiagnosticSink diagnostics)
    {
        var subsections = new List<(long First, long Count)>();
        if (!dictionary.TryGetValue(Index, out CosObject? entry))
        {
            subsections.Add((0, defaultCount));
            return subsections;
        }

        if (entry is CosArray array && array.Count % 2 == 0)
        {
            for (int index = 0; index < array.Count; index += 2)
            {
                if (array[index] is not CosInteger { Value: >= 0 } first || array[index + 1] is not CosInteger { Value: >= 0 } count)
                {
                    subsections.Clear();
                    break;
                }

                subsections.Add((first.Value, count.Value));
            }

            if (subsections.Count > 0 || array.Count == 0)
            {
                return subsections;
            }
        }

        Report(
            diagnostics,
            DiagnosticCodes.XrefStreamIndexInvalid,
            DiagnosticSeverity.Warning,
            "The cross-reference stream's Index entry shall be an array of pairs of direct, non-negative integers; the default [0 Size] is used.",
            offset,
            reference);
        subsections.Add((0, defaultCount));
        return subsections;
    }

    /// <summary>Reads the entries (Table 18), one per object number of each subsection, until the data ends.</summary>
    private static void ReadEntries(
        ReadOnlySpan<byte> data,
        int[] widths,
        List<(long First, long Count)> subsections,
        long? size,
        Dictionary<int, XrefEntry> entries,
        long offset,
        CosReference? reference,
        DiagnosticSink diagnostics)
    {
        int entryWidth = widths[0] + widths[1] + widths[2];
        int position = 0;
        bool beyondSize = false;
        foreach ((long first, long count) in subsections)
        {
            for (long index = 0; index < count; index++)
            {
                if (data.Length - position < entryWidth)
                {
                    Report(
                        diagnostics,
                        DiagnosticCodes.XrefStreamDataTruncated,
                        DiagnosticSeverity.Warning,
                        "The cross-reference stream's data ends before the last entry its Index and W entries describe; the entries after it are missing.",
                        offset,
                        reference);
                    return;
                }

                ReadOnlySpan<byte> row = data.Slice(position, entryWidth);
                position += entryWidth;
                long number = first + index;
                if (number is 0 or > int.MaxValue)
                {
                    // §7.5.4: object number 0 is the head of the free list and never names an object.
                    continue;
                }

                beyondSize |= number >= size;
                string? problem = DecodeEntry(row, widths, out XrefEntry entry);
                if (problem is not null)
                {
                    string what = problem == DiagnosticCodes.XrefEntryOffsetInvalid ? "is in use at offset 0, where the header is" : "has a field out of range";
                    Report(
                        diagnostics,
                        problem,
                        DiagnosticSeverity.Error,
                        string.Create(CultureInfo.InvariantCulture, $"The cross-reference stream entry for object {number} {what}; the object is read as free."),
                        offset,
                        reference);
                }

                entries.TryAdd((int)number, entry);
            }
        }

        if (beyondSize)
        {
            Report(
                diagnostics,
                DiagnosticCodes.XrefStreamSizeInvalid,
                DiagnosticSeverity.Warning,
                "The cross-reference stream has entries for object numbers at or beyond its Size entry; they are kept.",
                offset,
                reference);
        }
    }

    /// <summary>Decodes one row (Table 18): fields big-endian, absent fields take their defaults, an unknown type is the null object.</summary>
    /// <returns>The diagnostic code of the problem when the row is unusable (the entry is then free), else <see langword="null"/>.</returns>
    private static string? DecodeEntry(ReadOnlySpan<byte> row, int[] widths, out XrefEntry entry)
    {
        long type = widths[0] == 0 ? 1 : Field(row[..widths[0]]);
        long field2 = Field(row.Slice(widths[0], widths[1]));
        long field3 = Field(row.Slice(widths[0] + widths[1], widths[2]));
        entry = XrefEntry.Free;
        switch (type)
        {
            case 0:
                entry = new XrefEntry(XrefEntryKind.Free, Math.Max(field2, 0), (int)Math.Clamp(field3, 0, CosReference.MaxGeneration));
                return null;
            case 1 when field2 == 0:
                return DiagnosticCodes.XrefEntryOffsetInvalid;
            case 1 when field2 > 0 && field3 is >= 0 and <= CosReference.MaxGeneration:
                entry = new XrefEntry(XrefEntryKind.InUse, field2, (int)field3);
                return null;
            case 2 when field2 is > 0 and <= int.MaxValue && field3 is >= 0 and <= int.MaxValue:
                entry = new XrefEntry(XrefEntryKind.Compressed, field2, (int)field3);
                return null;
            case 1 or 2:
                return DiagnosticCodes.XrefEntryInvalid;
            default:
                // Table 18: "Any other value shall be interpreted as a reference to the null object", which a free entry reads as.
                return null;
        }
    }

    /// <summary>Reads a big-endian unsigned field; one wider than 63 bits reads as negative, which every caller rejects.</summary>
    private static long Field(ReadOnlySpan<byte> bytes)
    {
        long value = 0;
        foreach (byte b in bytes)
        {
            value = (value << 8) | b;
        }

        return value;
    }

    private static void Report(DiagnosticSink diagnostics, string code, DiagnosticSeverity severity, string message, long offset, CosReference? reference) =>
        diagnostics.Report(code, severity, message, offset, reference);
}
