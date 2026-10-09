using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// The decoded contents of one object stream: its data and, for each member, the object number and the byte range it occupies.
/// Built once per object stream and shared by every member load.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5.7, Table 16. The data starts with <c>N</c> pairs of integers, an object number and the offset of the object
/// relative to <c>First</c>; the objects follow. Objects need not be separated by white-space (NOTE 7), so each member is parsed
/// over exactly the bytes up to the next member's offset (the last one up to the end of the data).
/// </para>
/// <para>
/// Deviations repaired: fewer pairs than <c>N</c> (the pairs present are used); offsets out of order (members are bounded by the
/// next offset in sorted order); offsets past the end of the data (the member is dropped). A <c>First</c> or <c>N</c> that is not a
/// non-negative integer, or a <c>First</c> past the end of the data, makes every member unreadable.
/// </para>
/// </remarks>
internal sealed class ObjectStream
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int[] _numbers;
    private readonly int[] _starts;
    private readonly int[] _ends;

    private ObjectStream(CosReference reference, ReadOnlyMemory<byte> data, int[] numbers, int[] starts, int[] ends)
    {
        Reference = reference;
        _data = data;
        _numbers = numbers;
        _starts = starts;
        _ends = ends;
    }

    /// <summary>Gets the object stream's own reference.</summary>
    public CosReference Reference { get; }

    /// <summary>Gets the object numbers of the members, in header order.</summary>
    public IReadOnlyList<int> ObjectNumbers => _numbers;

    /// <summary>Gets a value indicating whether the object stream could be read; its members read as null, silently, when not.</summary>
    public bool IsReadable { get; private init; } = true;

    /// <summary>Creates the contents of an object stream that cannot be read, already reported: it has no members.</summary>
    /// <param name="reference">The object stream's reference.</param>
    /// <returns>The empty contents.</returns>
    public static ObjectStream Unreadable(CosReference reference) => new(reference, ReadOnlyMemory<byte>.Empty, [], [], []) { IsReadable = false };

    /// <summary>Reads the header of a decoded object stream.</summary>
    /// <param name="reference">The object stream's reference, for diagnostics.</param>
    /// <param name="data">The decoded stream data.</param>
    /// <param name="count">The resolved <c>N</c> entry.</param>
    /// <param name="first">The resolved <c>First</c> entry.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>The contents; <see cref="Unreadable"/> when <c>N</c> or <c>First</c> cannot be used.</returns>
    public static ObjectStream Read(CosReference reference, ReadOnlyMemory<byte> data, CosObject count, CosObject first, DiagnosticSink diagnostics)
    {
        if (count is not CosInteger { Value: >= 0 } n || first is not CosInteger { Value: >= 0 } firstOffset || firstOffset.Value > data.Length)
        {
            diagnostics.Report(
                DiagnosticCodes.ObjectStreamInvalid,
                DiagnosticSeverity.Error,
                "The object stream's N and First entries shall be non-negative integers, First within the data; its objects read as null.",
                objectReference: reference);
            return Unreadable(reference);
        }

        int dataStart = (int)firstOffset.Value;
        ReadOnlySpan<byte> header = data.Span[..dataStart];
        var lexer = new CosLexer(header);

        // Each pair takes at least four bytes ("1 0 "), so a hostile N cannot make the capacity exceed the header's size.
        int capacity = (int)Math.Min(n.Value, (header.Length / 4) + 1);
        var numbers = new List<int>(capacity);
        var starts = new List<int>(capacity);
        bool dropped = false;
        for (long index = 0; index < n.Value; index++)
        {
            if (!StructureTokens.TryReadUnsigned(ref lexer, out long number) || !StructureTokens.TryReadUnsigned(ref lexer, out long offset))
            {
                diagnostics.Report(
                    DiagnosticCodes.ObjectStreamHeaderInvalid,
                    DiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture, $"The object stream's header holds {index} pairs of object number and offset where N says {n.Value}; the pairs present are used."),
                    objectReference: reference);
                break;
            }

            if (number is 0 or > int.MaxValue || offset > data.Length - dataStart)
            {
                dropped = true;
                continue;
            }

            numbers.Add((int)number);
            starts.Add(dataStart + (int)offset);
        }

        if (dropped)
        {
            diagnostics.Report(
                DiagnosticCodes.ObjectStreamHeaderInvalid,
                DiagnosticSeverity.Warning,
                "A pair in the object stream's header has an invalid object number or an offset past the end of the data; that object is not read.",
                objectReference: reference);
        }

        int[] ends = Ends(starts, data.Length, out bool ordered);
        if (!ordered)
        {
            diagnostics.Report(
                DiagnosticCodes.ObjectStreamHeaderInvalid,
                DiagnosticSeverity.Warning,
                "The offsets in the object stream's header shall increase; each object is read up to the next offset in sorted order.",
                objectReference: reference);
        }

        return new ObjectStream(reference, data, [.. numbers], [.. starts], ends);
    }

    /// <summary>Parses the member <paramref name="member"/> the cross-reference stream places at <paramref name="index"/>.</summary>
    /// <param name="member">The member's reference (generation 0).</param>
    /// <param name="index">The index from the type 2 cross-reference entry (Table 18).</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <returns>The object, or <see cref="CosNull"/> when the object stream does not hold it.</returns>
    public CosObject Parse(CosReference member, int index, DiagnosticSink diagnostics) => Parse(member, index, diagnostics, out _);

    /// <summary>
    /// Parses the member <paramref name="member"/> the cross-reference stream places at <paramref name="index"/> and returns the bytes it
    /// was read from, so a writer can copy them (issue #44).
    /// </summary>
    /// <param name="member">The member's reference (generation 0).</param>
    /// <param name="index">The index from the type 2 cross-reference entry (Table 18).</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="memberBytes">
    /// The member's bytes without surrounding white-space, when it was read without any repair and is not a stream (which an object
    /// stream shall not hold); otherwise <see langword="null"/>.
    /// </param>
    /// <returns>The object, or <see cref="CosNull"/> when the object stream does not hold it.</returns>
    public CosObject Parse(CosReference member, int index, DiagnosticSink diagnostics, out ReadOnlyMemory<byte>? memberBytes)
    {
        memberBytes = null;
        if (!IsReadable)
        {
            return CosNull.Instance;
        }

        int position = index < _numbers.Length && _numbers[index] == member.ObjectNumber ? index : Array.IndexOf(_numbers, member.ObjectNumber);
        if (position < 0)
        {
            diagnostics.Report(
                DiagnosticCodes.ObjectStreamMemberMissing,
                DiagnosticSeverity.Error,
                string.Create(CultureInfo.InvariantCulture, $"The cross-reference entry places the object in object stream {Reference.ObjectNumber}, which does not hold it; read as null."),
                objectReference: member);
            return CosNull.Instance;
        }

        if (position != index)
        {
            diagnostics.Report(
                DiagnosticCodes.ObjectStreamIndexMismatch,
                DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture, $"The cross-reference entry gives index {index} in object stream {Reference.ObjectNumber}, but the object is at index {position}; it is read from there."),
                objectReference: member);
        }

        ReadOnlySpan<byte> bytes = _data.Span[_starts[position].._ends[position]];
        var repairs = new CosRepairLog(keepAll: true);
        var parser = new CosParser(bytes, repairs);
        CosObject value = parser.ParseObject();
        foreach (CosRepair repair in repairs.All)
        {
            diagnostics.Report(repair.Code, DiagnosticSeverity.Warning, repair.Message, objectReference: member);
        }

        if (value is CosStream)
        {
            diagnostics.Report(
                DiagnosticCodes.ObjectStreamMemberInvalid,
                DiagnosticSeverity.Warning,
                "A stream shall not be stored in an object stream (§7.5.7); it is read anyway.",
                objectReference: member);
        }
        else if (repairs.Count == 0)
        {
            memberBytes = Trim(_data[_starts[position].._ends[position]]);
        }

        return value;
    }

    /// <summary>Removes the white-space around a member's bytes (§7.2.3, Table 1).</summary>
    private static ReadOnlyMemory<byte> Trim(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> span = bytes.Span;
        int start = 0;
        while (start < span.Length && CosLexer.IsWhitespace(span[start]))
        {
            start++;
        }

        int end = span.Length;
        while (end > start && CosLexer.IsWhitespace(span[end - 1]))
        {
            end--;
        }

        return bytes[start..end];
    }

    /// <summary>Returns, for each start, the nearest greater start of another member, or the end of the data.</summary>
    private static int[] Ends(List<int> starts, int dataLength, out bool ordered)
    {
        ordered = true;
        for (int index = 1; index < starts.Count; index++)
        {
            ordered &= starts[index] >= starts[index - 1];
        }

        int[] ends = new int[starts.Count];
        int[] sorted = [.. starts];
        Array.Sort(sorted);
        for (int index = 0; index < starts.Count; index++)
        {
            int next = Array.BinarySearch(sorted, starts[index] + 1);
            next = next < 0 ? ~next : FirstAtOrAbove(sorted, next);
            ends[index] = next < sorted.Length ? sorted[next] : dataLength;
        }

        return ends;
    }

    /// <summary>Moves a binary-search hit back to the first element with the same value.</summary>
    private static int FirstAtOrAbove(int[] sorted, int found)
    {
        while (found > 0 && sorted[found - 1] == sorted[found])
        {
            found--;
        }

        return found;
    }
}
