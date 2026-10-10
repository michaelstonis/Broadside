using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Filters.Jbig2;

/// <summary>
/// The segments of one JBIG2 stream (the image's page stream, or its <c>JBIG2Globals</c> stream), in stream order. Parsing allocates
/// the list and its pooled arrays once per stream, nothing per segment.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.88 §7.2 (segment headers), Annex D.3 (embedded organisation) with D.1 (sequential layout), as ISO 32000-2 §7.4.7 requires.
/// Leniency: a file header (D.4) is skipped with <c>Jbig2FileHeaderPresent</c>, and one announcing the random-access organisation
/// (D.2) has its headers read first and the data parts after them; parsing stops after an end-of-file segment. Every field is
/// bounded by the bytes left: a header that does not fit or cannot be parsed ends the list (<c>Jbig2SegmentTruncated</c>,
/// <c>Jbig2SegmentHeaderInvalid</c>); a data part longer than the stream is cut (<c>Jbig2SegmentTruncated</c>).
/// </para>
/// <para>
/// The unknown data length 0xFFFFFFFF (§7.2.7) is resolved for immediate generic regions by finding the terminator their coding
/// uses (0x00 0x00 for MMR, 0xFF 0xAC otherwise) after the generic region data header, followed by the four-byte row count
/// (§7.4.6.4).
/// </para>
/// </remarks>
internal sealed class Jbig2SegmentList : IDisposable
{
    private static ReadOnlySpan<byte> FileHeaderId => [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    private Jbig2Segment[] _segments;
    private uint[] _referred;
    private int _referredCount;

    private Jbig2SegmentList()
    {
        _segments = ArrayPool<Jbig2Segment>.Shared.Rent(16);
        _referred = ArrayPool<uint>.Shared.Rent(16);
    }

    /// <summary>Gets the number of segments.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the segments in stream order.</summary>
    public ReadOnlySpan<Jbig2Segment> Segments => _segments.AsSpan(0, Count);

    /// <summary>Parses the segments of <paramref name="data"/>.</summary>
    /// <param name="data">The stream's decoded bytes.</param>
    /// <param name="reporter">Where deviations go.</param>
    /// <param name="source">"page" or "globals", for messages.</param>
    /// <returns>The list; dispose it to return its arrays.</returns>
    public static Jbig2SegmentList Parse(ReadOnlySpan<byte> data, Jbig2Reporter reporter, string source)
    {
        var list = new Jbig2SegmentList();
        int position = 0;
        bool sequential = true;
        if (data.StartsWith(FileHeaderId))
        {
            byte flags = data.Length > 8 ? data[8] : (byte)1;
            sequential = (flags & 1) != 0;
            position = Math.Min(data.Length, 9 + ((flags & 2) == 0 ? 4 : 0));
            reporter.Report(
                DiagnosticCodes.Jbig2FileHeaderPresent,
                DiagnosticSeverity.Warning,
                $"The JBIG2 {source} stream starts with a JBIG2 file header (ITU-T T.88 D.4), which ISO 32000-2 §7.4.7 does not allow; it is skipped{(sequential ? string.Empty : " and the segments are read in the random-access organisation it announces")}.");
        }

        if (sequential)
        {
            list.ParseSequential(data, position, reporter, source);
        }
        else
        {
            list.ParseRandomAccess(data, position, reporter, source);
        }

        return list;
    }

    /// <summary>The segment numbers <paramref name="segment"/> refers to (§7.2.5).</summary>
    public ReadOnlySpan<uint> ReferredTo(in Jbig2Segment segment) => _referred.AsSpan(segment.ReferredStart, segment.ReferredCount);

    /// <summary>Finds the last segment numbered <paramref name="number"/>.</summary>
    /// <param name="number">The segment number.</param>
    /// <param name="segment">The segment.</param>
    /// <returns><see langword="true"/> when there is one.</returns>
    public bool TryFind(uint number, out Jbig2Segment segment)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            if (_segments[i].Number == number)
            {
                segment = _segments[i];
                return true;
            }
        }

        segment = default;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ArrayPool<Jbig2Segment>.Shared.Return(_segments);
        ArrayPool<uint>.Shared.Return(_referred);
        _segments = [];
        _referred = [];
        Count = 0;
    }

    private static string Describe(string source, int position) => string.Create(CultureInfo.InvariantCulture, $"the JBIG2 {source} stream at offset {position}");

    /// <summary>D.1: each header immediately followed by its data part.</summary>
    private void ParseSequential(ReadOnlySpan<byte> data, int position, Jbig2Reporter reporter, string source)
    {
        while (position < data.Length)
        {
            int start = position;
            if (!TryReadHeader(data, ref position, reporter, source, out Jbig2Segment segment, out long length))
            {
                return;
            }

            segment = WithData(data, segment, position, length, reporter, source, start);
            Add(segment);
            position = segment.DataStart + segment.DataLength;
            if (segment.Type == Jbig2SegmentType.EndOfFile)
            {
                return;
            }
        }
    }

    /// <summary>D.2: every header up to the end-of-file segment, then the data parts in the same order.</summary>
    private void ParseRandomAccess(ReadOnlySpan<byte> data, int position, Jbig2Reporter reporter, string source)
    {
        long[] lengths = ArrayPool<long>.Shared.Rent(16);
        try
        {
            int headers = 0;
            while (position < data.Length)
            {
                int start = position;
                if (!TryReadHeader(data, ref position, reporter, source, out Jbig2Segment segment, out long length))
                {
                    break;
                }

                if (headers == lengths.Length)
                {
                    long[] larger = ArrayPool<long>.Shared.Rent(headers * 2);
                    lengths.AsSpan(0, headers).CopyTo(larger);
                    ArrayPool<long>.Shared.Return(lengths);
                    lengths = larger;
                }

                lengths[headers++] = length;
                Add(segment with { DataStart = start });
                if (segment.Type == Jbig2SegmentType.EndOfFile)
                {
                    break;
                }
            }

            for (int i = 0; i < Count; i++)
            {
                int headerStart = _segments[i].DataStart;
                _segments[i] = WithData(data, _segments[i], position, lengths[i], reporter, source, headerStart);
                position = _segments[i].DataStart + _segments[i].DataLength;
            }
        }
        finally
        {
            ArrayPool<long>.Shared.Return(lengths);
        }
    }

    /// <summary>Places the data part at <paramref name="position"/>, resolving an unknown length and cutting one past the end.</summary>
    private static Jbig2Segment WithData(ReadOnlySpan<byte> data, Jbig2Segment segment, int position, long length, Jbig2Reporter reporter, string source, int headerStart)
    {
        int available = data.Length - position;
        bool unknown = false;
        if (length == uint.MaxValue)
        {
            if (FindUnknownLength(data[position..]) is int found)
            {
                length = found;
                unknown = true;
            }
            else
            {
                reporter.Report(
                    DiagnosticCodes.Jbig2SegmentTruncated,
                    DiagnosticSeverity.Error,
                    $"The generic region segment at {Describe(source, headerStart)} has an unknown length and no end marker with a row count (ITU-T T.88 §7.4.6.4); its data runs to the end of the stream.");
                length = available;
            }
        }

        if (length > available)
        {
            reporter.Report(
                DiagnosticCodes.Jbig2SegmentTruncated,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The segment {segment.Number} at {Describe(source, headerStart)} declares {length} data bytes but only {available} remain; what is present is decoded."));
            length = available;
        }

        return segment with { DataStart = position, DataLength = (int)length, UnknownLength = unknown };
    }

    /// <summary>
    /// §7.2.7 and §7.4.6.4: the length of an immediate generic region's data part whose header says 0xFFFFFFFF, up to and including its
    /// terminator and row count; <see langword="null"/> when the terminator is not found.
    /// </summary>
    private static int? FindUnknownLength(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
        {
            return null;
        }

        byte flags = data[17];
        bool mmr = (flags & 1) != 0;
        int scan = 18 + (mmr ? 0 : ((flags >> 1) & 3) == 0 ? 8 : 2);
        byte first = mmr ? (byte)0x00 : (byte)0xFF;
        byte second = mmr ? (byte)0x00 : (byte)0xAC;
        for (int i = scan; i + 6 <= data.Length; i++)
        {
            if (data[i] == first && data[i + 1] == second)
            {
                return i + 6;
            }
        }

        return null;
    }

    /// <summary>Reads one segment header (§7.2.1-7.2.7); <paramref name="length"/> is the declared data length (0xFFFFFFFF = unknown).</summary>
    private bool TryReadHeader(ReadOnlySpan<byte> data, ref int position, Jbig2Reporter reporter, string source, out Jbig2Segment segment, out long length)
    {
        int start = position;
        segment = default;
        length = 0;
        if (data.Length - position < 6)
        {
            return Truncated(reporter, source, start);
        }

        uint number = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
        byte flags = data[position + 4];
        int type = flags & 0x3F;
        bool longPage = (flags & 0x40) != 0;
        position += 5;
        int countField = data[position] >> 5;
        long count;
        if (countField <= 4)
        {
            count = countField;
            position++;
        }
        else if (countField == 7)
        {
            if (data.Length - position < 4)
            {
                return Truncated(reporter, source, start);
            }

            count = BinaryPrimitives.ReadUInt32BigEndian(data[position..]) & 0x1FFFFFFF;
            position += 4;
            long retain = (count + 8) / 8;
            if (data.Length - position < retain)
            {
                return Truncated(reporter, source, start);
            }

            position += (int)retain;
        }
        else
        {
            reporter.Report(
                DiagnosticCodes.Jbig2SegmentHeaderInvalid,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The segment header at {Describe(source, start)} has the referred-to segment count {countField}, which ITU-T T.88 §7.2.4 does not allow; it and the rest of the stream are ignored."));
            return false;
        }

        int size = number <= 256 ? 1 : number <= 65536 ? 2 : 4;
        int tail = (longPage ? 4 : 1) + 4;
        if (data.Length - position < tail || count > (data.Length - position - tail) / size)
        {
            return Truncated(reporter, source, start);
        }

        int referredStart = _referredCount;
        for (int i = 0; i < count; i++)
        {
            uint referred = size switch
            {
                1 => data[position],
                2 => BinaryPrimitives.ReadUInt16BigEndian(data[position..]),
                _ => BinaryPrimitives.ReadUInt32BigEndian(data[position..]),
            };
            position += size;
            AddReferred(referred);
        }

        uint page = longPage ? BinaryPrimitives.ReadUInt32BigEndian(data[position..]) : data[position];
        position += longPage ? 4 : 1;
        length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
        position += 4;
        if (length == uint.MaxValue && type != Jbig2SegmentType.ImmediateGenericRegion)
        {
            reporter.Report(
                DiagnosticCodes.Jbig2SegmentHeaderInvalid,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The segment {number} at {Describe(source, start)} has type {type} and an unknown data length, which ITU-T T.88 §7.2.7 allows only for immediate generic regions; it and the rest of the stream are ignored."));
            _referredCount = referredStart;
            return false;
        }

        segment = new Jbig2Segment(number, type, page, position, 0, referredStart, (int)count, false);
        return true;
    }

    private static bool Truncated(Jbig2Reporter reporter, string source, int start)
    {
        reporter.Report(
            DiagnosticCodes.Jbig2SegmentTruncated,
            DiagnosticSeverity.Error,
            $"The segment header at {Describe(source, start)} runs past the end of the stream; the bytes from there are ignored.");
        return false;
    }

    private void Add(in Jbig2Segment segment)
    {
        if (Count == _segments.Length)
        {
            Jbig2Segment[] larger = ArrayPool<Jbig2Segment>.Shared.Rent(Count * 2);
            _segments.AsSpan(0, Count).CopyTo(larger);
            ArrayPool<Jbig2Segment>.Shared.Return(_segments);
            _segments = larger;
        }

        _segments[Count++] = segment;
    }

    private void AddReferred(uint number)
    {
        if (_referredCount == _referred.Length)
        {
            uint[] larger = ArrayPool<uint>.Shared.Rent(_referredCount * 2);
            _referred.AsSpan(0, _referredCount).CopyTo(larger);
            ArrayPool<uint>.Shared.Return(_referred);
            _referred = larger;
        }

        _referred[_referredCount++] = number;
    }
}
