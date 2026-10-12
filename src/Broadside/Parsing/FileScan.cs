using System.Buffers;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>An <c>N G obj</c> header found by scanning.</summary>
/// <param name="Number">The object number.</param>
/// <param name="Generation">The generation number.</param>
/// <param name="Offset">The absolute byte offset of the first digit of the object number.</param>
internal readonly record struct ScannedObject(int Number, int Generation, long Offset);

/// <summary>
/// The positions of the file-structure keywords in a file, found in one pass over its bytes: what the reader repairs a damaged
/// file from when its cross-reference information cannot be trusted.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.5 (repair is not specified; this follows pdf.js and PDFBox). The scan reads the file in windows through
/// <see cref="PdfSource.Read"/>, so it never holds the whole file, and looks for each keyword with a vectorized span search: it is
/// linear in the file length and allocates only for what it finds. It looks for indirect object headers (<c>N G obj</c>, preceded
/// by white-space, a delimiter or the start of the file), the <c>xref</c> and <c>trailer</c> keywords, and the names
/// <c>/XRef</c>, <c>/ObjStm</c> and <c>/Catalog</c>, which mark the cross-reference streams, object streams and catalogs whose
/// object headers precede them.
/// </para>
/// <para>
/// Matches inside stream data are found too, as in both reference engines; callers parse what they find and discard what does not
/// parse. The result is immutable and safe to share between threads.
/// </para>
/// </remarks>
internal sealed class FileScan
{
    /// <summary>How many bytes each window of the scan covers.</summary>
    private const int WindowLength = 64 * 1024;

    /// <summary>How many bytes before a window are kept so a header that straddles it is recognized.</summary>
    private const int LookBehind = 32;

    /// <summary>How many bytes after a window are kept so the delimiter after a keyword is seen.</summary>
    private const int LookAhead = 16;

    private readonly List<ScannedObject> _objects = [];
    private readonly List<long> _xrefKeywords = [];
    private readonly List<long> _trailerKeywords = [];
    private readonly List<long> _xrefStreamNames = [];
    private readonly List<long> _objectStreamNames = [];
    private readonly List<long> _catalogNames = [];

    private readonly Lazy<Dictionary<(int Number, int Generation), long>> _newest;

    private FileScan() => _newest = new(IndexNewest, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the object headers, in file order.</summary>
    public IReadOnlyList<ScannedObject> Objects => _objects;

    /// <summary>Gets the offsets of the <c>xref</c> keywords that start classic cross-reference sections, in file order.</summary>
    public IReadOnlyList<long> XrefKeywords => _xrefKeywords;

    /// <summary>Gets the offsets of the <c>trailer</c> keywords, in file order.</summary>
    public IReadOnlyList<long> TrailerKeywords => _trailerKeywords;

    /// <summary>Gets the offsets of the name <c>/XRef</c> (a cross-reference stream's type), in file order.</summary>
    public IReadOnlyList<long> XrefStreamNames => _xrefStreamNames;

    /// <summary>Gets the offsets of the name <c>/ObjStm</c> (an object stream's type), in file order.</summary>
    public IReadOnlyList<long> ObjectStreamNames => _objectStreamNames;

    /// <summary>Gets the offsets of the name <c>/Catalog</c>, in file order.</summary>
    public IReadOnlyList<long> CatalogNames => _catalogNames;

    /// <summary>Scans the whole of <paramref name="source"/>.</summary>
    /// <param name="source">The file.</param>
    /// <returns>The scan.</returns>
    public static FileScan Run(PdfSource source) => Run(source, 0, source.Length);

    /// <summary>Scans the bytes of <paramref name="source"/> in [<paramref name="start"/>, <paramref name="end"/>).</summary>
    /// <param name="source">The file.</param>
    /// <param name="start">The first offset a match may start at.</param>
    /// <param name="end">The offset no match may start at or after.</param>
    /// <returns>The scan.</returns>
    public static FileScan Run(PdfSource source, long start, long end)
    {
        var scan = new FileScan();
        start = Math.Max(0, start);
        end = Math.Min(end, source.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(LookBehind + WindowLength + LookAhead);
        try
        {
            for (long windowStart = start; windowStart < end; windowStart += WindowLength)
            {
                long bufferStart = Math.Max(0, windowStart - LookBehind);
                int read = source.Read(bufferStart, buffer.AsSpan(0, (int)(windowStart - bufferStart) + WindowLength + LookAhead));
                int first = (int)(windowStart - bufferStart);
                int last = (int)Math.Min(end - bufferStart, first + WindowLength);
                scan.ScanWindow(buffer.AsSpan(0, read), bufferStart, first, last);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return scan;
    }

    /// <summary>Returns the object header that most closely precedes <paramref name="offset"/>, the object a name at it is in.</summary>
    /// <param name="offset">An absolute offset.</param>
    /// <param name="found">The header.</param>
    /// <returns><see langword="true"/> when a header precedes the offset.</returns>
    public bool TryFindObjectBefore(long offset, out ScannedObject found)
    {
        int low = 0;
        int high = _objects.Count - 1;
        int best = -1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            if (_objects[middle].Offset < offset)
            {
                best = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        found = best >= 0 ? _objects[best] : default;
        return best >= 0;
    }

    /// <summary>
    /// Finds the header of object <paramref name="number"/> <paramref name="generation"/> closest to <paramref name="near"/>, or the
    /// last one in the file when <paramref name="near"/> is <see langword="null"/> (an incremental update appends newer copies).
    /// </summary>
    /// <param name="number">The object number.</param>
    /// <param name="generation">The generation number.</param>
    /// <param name="near">The offset to stay close to, or <see langword="null"/>.</param>
    /// <param name="offset">The header's absolute offset.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryFindObject(int number, int generation, long? near, out long offset)
    {
        if (near is null)
        {
            return _newest.Value.TryGetValue((number, generation), out offset);
        }

        offset = -1;
        long distance = long.MaxValue;
        foreach (ScannedObject header in _objects)
        {
            if (header.Number != number || header.Generation != generation)
            {
                continue;
            }

            long candidate = Math.Abs(header.Offset - near.Value);
            if (offset < 0 || candidate < distance)
            {
                offset = header.Offset;
                distance = candidate;
            }
        }

        return offset >= 0;
    }

    /// <summary>Indexes the last header of each object number and generation, so lookups after a full scan are not linear.</summary>
    private Dictionary<(int Number, int Generation), long> IndexNewest()
    {
        var newest = new Dictionary<(int Number, int Generation), long>();
        foreach (ScannedObject header in _objects)
        {
            newest[(header.Number, header.Generation)] = header.Offset;
        }

        return newest;
    }

    private static bool IsWhiteSpace(byte value) => value is 0 or (byte)'\t' or (byte)'\n' or (byte)'\f' or (byte)'\r' or (byte)' ';

    private static bool IsDelimiter(byte value) =>
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static bool IsBoundary(ReadOnlySpan<byte> buffer, int position) =>
        position < 0 || position >= buffer.Length || IsWhiteSpace(buffer[position]) || IsDelimiter(buffer[position]);

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    /// <summary>Scans matches that start in [<paramref name="first"/>, <paramref name="last"/>) of <paramref name="buffer"/>.</summary>
    private void ScanWindow(ReadOnlySpan<byte> buffer, long bufferStart, int first, int last)
    {
        foreach (int position in Matches(buffer, first, last, "obj"u8))
        {
            if (IsBoundary(buffer, position + 3) && TryReadHeaderBefore(buffer, position, out int number, out int generation, out int start))
            {
                _objects.Add(new ScannedObject(number, generation, bufferStart + start));
            }
        }

        AddKeywords(buffer, bufferStart, first, last, "xref"u8, _xrefKeywords, requireWhiteSpaceBefore: true);
        AddKeywords(buffer, bufferStart, first, last, "trailer"u8, _trailerKeywords, requireWhiteSpaceBefore: false);
        AddKeywords(buffer, bufferStart, first, last, "/XRef"u8, _xrefStreamNames, requireWhiteSpaceBefore: false);
        AddKeywords(buffer, bufferStart, first, last, "/ObjStm"u8, _objectStreamNames, requireWhiteSpaceBefore: false);
        AddKeywords(buffer, bufferStart, first, last, "/Catalog"u8, _catalogNames, requireWhiteSpaceBefore: false);
    }

    private static void AddKeywords(
        ReadOnlySpan<byte> buffer,
        long bufferStart,
        int first,
        int last,
        ReadOnlySpan<byte> keyword,
        List<long> found,
        bool requireWhiteSpaceBefore)
    {
        bool isName = keyword[0] == (byte)'/';
        foreach (int position in Matches(buffer, first, last, keyword))
        {
            bool before = requireWhiteSpaceBefore
                ? position == 0 && bufferStart == 0 || position > 0 && IsWhiteSpace(buffer[position - 1])
                : isName || IsBoundary(buffer, position - 1) || position == 0;
            if (before && IsBoundary(buffer, position + keyword.Length))
            {
                found.Add(bufferStart + position);
            }
        }
    }

    /// <summary>
    /// Reads <c>N G</c> backwards from the <c>obj</c> at <paramref name="keyword"/>: white-space, the generation (1-5 digits),
    /// white-space, the object number (1-10 digits), then white-space, a delimiter or the start of the buffer.
    /// </summary>
    private static bool TryReadHeaderBefore(ReadOnlySpan<byte> buffer, int keyword, out int number, out int generation, out int start)
    {
        number = 0;
        generation = 0;
        start = 0;
        int position = keyword - 1;
        if (position < 0 || !IsWhiteSpace(buffer[position]))
        {
            return false;
        }

        while (position >= 0 && IsWhiteSpace(buffer[position]))
        {
            position--;
        }

        int generationEnd = position + 1;
        while (position >= 0 && IsDigit(buffer[position]))
        {
            position--;
        }

        int generationStart = position + 1;
        if (generationEnd - generationStart is < 1 or > 5 || position < 0 || !IsWhiteSpace(buffer[position]))
        {
            return false;
        }

        while (position >= 0 && IsWhiteSpace(buffer[position]))
        {
            position--;
        }

        int numberEnd = position + 1;
        while (position >= 0 && IsDigit(buffer[position]))
        {
            position--;
        }

        int numberStart = position + 1;
        if (numberEnd - numberStart is < 1 or > 10 || (position >= 0 && !IsWhiteSpace(buffer[position]) && !IsDelimiter(buffer[position])))
        {
            return false;
        }

        long parsedNumber = Parse(buffer[numberStart..numberEnd]);
        long parsedGeneration = Parse(buffer[generationStart..generationEnd]);
        if (parsedNumber is 0 or > int.MaxValue || parsedGeneration > CosReference.MaxGeneration)
        {
            return false;
        }

        number = (int)parsedNumber;
        generation = (int)parsedGeneration;
        start = numberStart;
        return true;
    }

    private static long Parse(ReadOnlySpan<byte> digits)
    {
        long value = 0;
        foreach (byte digit in digits)
        {
            value = (value * 10) + (digit - '0');
        }

        return value;
    }

    /// <summary>Enumerates the positions in [<paramref name="first"/>, <paramref name="last"/>) where <paramref name="value"/> starts.</summary>
    private static MatchEnumerator Matches(ReadOnlySpan<byte> buffer, int first, int last, ReadOnlySpan<byte> value) => new(buffer, first, last, value);

    private ref struct MatchEnumerator(ReadOnlySpan<byte> buffer, int first, int last, ReadOnlySpan<byte> value)
    {
        private readonly ReadOnlySpan<byte> _buffer = buffer;
        private readonly ReadOnlySpan<byte> _value = value;
        private int _next = first;

        public int Current { get; private set; }

        public readonly MatchEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_next >= last || _next >= _buffer.Length)
            {
                return false;
            }

            int found = _buffer[_next..].IndexOf(_value);
            if (found < 0 || _next + found >= last)
            {
                _next = last;
                return false;
            }

            Current = _next + found;
            _next = Current + 1;
            return true;
        }
    }
}
