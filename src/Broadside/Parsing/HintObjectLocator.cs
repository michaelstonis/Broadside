using System.Globalization;
using Broadside.Caching;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>
/// Places the objects of a linearized file's pages and shared object groups from its hint tables, without its main
/// cross-reference section: each page offset hint table entry and each shared object group names a run of consecutively numbered
/// objects and the byte range they occupy, and each object of the run is found in that range by its <c>N 0 obj</c> header.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.3.1 (objects of the second group are numbered from 1, all with generation 0), F.3.8 (a page's objects are grouped
/// together, its page object first), F.4.2 (Table F.4 items 1 and 2: each page's object count and length, accumulated from the
/// first page's) and F.4.3 (Table F.6: each shared group's object count and length). Annex G, G.4: with the hints, a reader asks
/// for one page's byte range and its shared objects' instead of the cross-reference section first.
/// </para>
/// <para>
/// Hints are a guide, not the file (F.3.6: they "shall be derived from the document", and nothing checks them): an object is placed
/// only when its exact header is found inside the range the hints give for it, in object number order, at the start of the
/// range or after an <c>endobj</c>; otherwise the
/// cross-reference section decides. Compressed objects (F.4.1: hints locate their object stream) are never found this way. A
/// range is searched once, on the first lookup that falls in it, and the search is a pure function of the file's bytes, so a
/// search a remote read interrupts (issue #207) is simply repeated.
/// </para>
/// </remarks>
internal sealed class HintObjectLocator : IObjectLocator
{
    /// <summary>The longest hinted range searched; a longer one (a page with a large image, say) is left to the cross-reference section.</summary>
    private const long MaxRangeLength = 16L << 20;

    private readonly PdfSource _source;
    private readonly long _headerOffset;
    private readonly Func<PdfLinearizationHints?> _hints;
    private readonly OnceCache<bool, HintedRange[]> _ranges = new();
    private readonly OnceCache<int, long[]> _offsets = new();

    /// <summary>Initializes a new instance of the <see cref="HintObjectLocator"/> class.</summary>
    /// <param name="source">The file.</param>
    /// <param name="headerOffset">The absolute offset of the header, which cross-reference offsets count from.</param>
    /// <param name="hints">Reads the hint tables; <see langword="null"/> when they cannot be read.</param>
    public HintObjectLocator(PdfSource source, long headerOffset, Func<PdfLinearizationHints?> hints)
    {
        _source = source;
        _headerOffset = headerOffset;
        _hints = hints;
    }

    /// <inheritdoc/>
    public bool TryLocate(int objectNumber, out XrefEntry entry)
    {
        entry = XrefEntry.Free;
        HintedRange[] ranges = _ranges.GetOrCreate(true, this, static (_, self) => new Created<HintedRange[]>(self.ReadRanges()), static (_, _) => []);
        int index = Find(ranges, objectNumber);
        if (index < 0)
        {
            return false;
        }

        long[] offsets = _offsets.GetOrCreate(index, (Locator: this, Range: ranges[index]), static (_, state) => new Created<long[]>(state.Locator.Search(state.Range)), static (_, _) => []);
        int position = objectNumber - ranges[index].FirstObjectNumber;
        if (position >= offsets.Length || offsets[position] < 0)
        {
            return false;
        }

        entry = new XrefEntry(XrefEntryKind.InUse, offsets[position] - _headerOffset, 0);
        return true;
    }

    /// <summary>The run whose numbers include <paramref name="objectNumber"/>: the last run starting at or before it, if it reaches it.</summary>
    private static int Find(HintedRange[] ranges, int objectNumber)
    {
        int low = 0;
        int high = ranges.Length - 1;
        int found = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (ranges[middle].FirstObjectNumber <= objectNumber)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found >= 0 && objectNumber < (long)ranges[found].FirstObjectNumber + ranges[found].ObjectCount ? found : -1;
    }

    /// <summary>
    /// Every page's run but the first page's (whose objects the first-page section lists, F.3.4) and every shared object group, by
    /// first object number.
    /// </summary>
    private HintedRange[] ReadRanges()
    {
        if (_hints() is not { } hints)
        {
            return [];
        }

        var ranges = new List<HintedRange>(hints.Pages.Count + hints.SharedObjects.Count);
        for (int page = 1; page < hints.Pages.Count; page++)
        {
            PdfPageHint hint = hints.Pages[page];
            ranges.Add(new HintedRange(hint.FirstObjectNumber, hint.ObjectCount, hint.Offset, hint.Length));
        }

        foreach (PdfSharedObjectHint group in hints.SharedObjects)
        {
            ranges.Add(new HintedRange(group.FirstObjectNumber, group.ObjectCount, group.Offset, group.Length));
        }

        ranges.RemoveAll(range => range.ObjectCount <= 0 || range.Length <= 0);
        ranges.Sort(static (left, right) => left.FirstObjectNumber.CompareTo(right.FirstObjectNumber));
        return [.. ranges];
    }

    /// <summary>
    /// Finds each object of <paramref name="range"/> in its bytes, in number order: <c>N 0 obj</c> at the start of the range or after
    /// white-space, and followed by a delimiter or white-space. An object not found reads as -1.
    /// </summary>
    private long[] Search(HintedRange range)
    {
        var offsets = new long[range.ObjectCount];
        Array.Fill(offsets, -1);
        if (range.Offset < _headerOffset || range.Length > MaxRangeLength || range.Offset >= _source.Length)
        {
            return offsets;
        }

        int length = (int)Math.Min(range.Length, _source.Length - range.Offset);
        ReadOnlySpan<byte> bytes = _source.GetWindow(range.Offset, length).Span;
        bytes = bytes[..Math.Min(bytes.Length, length)];
        Span<byte> header = stackalloc byte[32];
        int position = 0;
        for (int index = 0; index < offsets.Length; index++)
        {
            int written = Encoding(range.FirstObjectNumber + index, header);
            ReadOnlySpan<byte> pattern = header[..written];
            for (int from = position; from < bytes.Length;)
            {
                int found = bytes[from..].IndexOf(pattern);
                if (found < 0)
                {
                    break;
                }

                int start = from + found;
                int end = start + pattern.Length;
                if (FollowsObject(bytes, start, first: index == 0) && (end == bytes.Length || !CosLexer.IsRegular(bytes[end])))
                {
                    offsets[index] = range.Offset + start;
                    position = end;
                    break;
                }

                from = start + 1;
            }
        }

        return offsets;
    }

    /// <summary>
    /// Whether a header found at <paramref name="start"/> stands where an object can begin: at the start of the range for the run's
    /// first object, else after white-space that follows the previous object's <c>endobj</c>, so text inside a stream is not taken
    /// for a header.
    /// </summary>
    private static bool FollowsObject(ReadOnlySpan<byte> bytes, int start, bool first)
    {
        if (start == 0)
        {
            return first;
        }

        if (!CosLexer.IsWhitespace(bytes[start - 1]))
        {
            return false;
        }

        int end = start - 1;
        while (end > 0 && CosLexer.IsWhitespace(bytes[end - 1]))
        {
            end--;
        }

        return bytes[..end].EndsWith("endobj"u8);
    }

    /// <summary>Writes <c>N 0 obj</c> for the object number.</summary>
    private static int Encoding(long objectNumber, Span<byte> destination)
    {
        objectNumber.TryFormat(destination, out int written, provider: CultureInfo.InvariantCulture);
        " 0 obj"u8.CopyTo(destination[written..]);
        return written + " 0 obj"u8.Length;
    }

    /// <summary>A run of consecutively numbered objects and the absolute byte range they occupy.</summary>
    private readonly record struct HintedRange(int FirstObjectNumber, int ObjectCount, long Offset, long Length);
}
