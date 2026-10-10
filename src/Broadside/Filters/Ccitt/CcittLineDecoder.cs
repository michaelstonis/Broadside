namespace Broadside.Filters.Ccitt;

/// <summary>How decoding one coded line ended.</summary>
internal enum CcittLineStatus
{
    /// <summary>The line decoded to its last column.</summary>
    Ok,

    /// <summary>The line decoded, but a run or vertical position went past the last column and was cut there.</summary>
    RunTooLong,

    /// <summary>The bits at the decoding position start no valid code; the line is complete up to there and white after.</summary>
    InvalidCode,

    /// <summary>A vertical mode put a1 at or left of a0; the line is complete up to a0 and white after.</summary>
    BadVerticalPosition,

    /// <summary>An EOL came before the last column (left unconsumed); the line is complete up to there and white after.</summary>
    UnexpectedEol,

    /// <summary>The data ended inside the line; the line is complete up to there and white after.</summary>
    EndOfData,
}

/// <summary>What the uncompressed-mode pattern being painted ends with, after its white pixels (T.4 Table 5, T.6 Table 4).</summary>
internal enum CcittPatternEnd : byte
{
    /// <summary>Nothing: five white pixels, still in uncompressed mode (code 000001).</summary>
    None,

    /// <summary>A black pixel (codes 1, 01, 001, 0001, 00001).</summary>
    Black,

    /// <summary>The exit from uncompressed mode, with the tag bit naming the colour of the next run (codes 0000001T to 00000000001T).</summary>
    Exit,
}

/// <summary>
/// The PDF-independent core of the fax decoders: decodes one coded line at a time into changing elements, one-dimensionally (T.4
/// §4.1) or two-dimensionally against the reference line (T.4 §4.2.1.3, T.6 §2.2), and handles EOLs, tag bits and alignment. The
/// <c>CCITTFaxDecode</c> filter frames lines with its parameters on top of it; JBIG2's MMR decoding (T.88 §6.2.6) uses it with T.6
/// framing (<see cref="MmrDecoder"/>). Allocates nothing: the caller supplies the two changing-element arrays.
/// </summary>
/// <remarks>
/// <para>
/// ITU-T T.4 §4.1-4.2, ITU-T T.6 §2.2-2.4. A line is a strictly increasing list of the positions where the colour changes, starting
/// from white: even entries change to black, odd ones back to white, so the black runs are [ch[2k], ch[2k + 1]) and an odd count
/// means black to the end. The reference line keeps three sentinels equal to Columns after its entries (the imaginary changing
/// elements of T.4 §4.2.1.3.4 b). Statuses are returned, never thrown, so each user maps them to its own diagnostics.
/// </para>
/// <para>
/// Uncompressed mode (T.4 §4.2.1.3.3 Note 2 and Table 5; T.6 §2.3.1 and Table 4): the extension code with xxx = 111 (in a mode code
/// of a 2-D line, or 000000001111 in place of a run of a 1-D line) paints pixels one by one from the codes of Table 5 until an exit
/// code, whose tag bit names the colour of the run that follows in the basic scheme. A pattern that reaches the last column carries
/// on at the start of the next line (T.6 §2.3.1: the end of one line and the start of the next "are concatenated to one pattern"),
/// so the decoder keeps the mode and the rest of the pattern across lines; an EOL, a new block or damage ends the mode.
/// </para>
/// </remarks>
internal ref struct CcittLineDecoder
{
    private const int Sentinels = 3;
    private const int RunCap = 1 << 30;
    private const int UncompressedExtension = 0b111;
    private const int EnterUncompressed = -2;
    private const int LongestUncompressedZeros = 10;

    private CcittBitReader _reader;
    private Span<int> _reference;
    private Span<int> _current;
    private int _referenceCount;
    private int _currentCount;
    private readonly int _columns;
    private readonly bool _allowUncompressed;
    private bool _tooLong;
    private bool _uncompressed;
    private int _patternWhites;
    private CcittPatternEnd _patternEnd;
    private bool _exitTag;

    /// <summary>Initializes a new instance of the <see cref="CcittLineDecoder"/> struct, with an all-white reference line.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="columns">The number of pixels in a line, at least 1.</param>
    /// <param name="first">Work memory of at least <see cref="WorkLength"/> ints.</param>
    /// <param name="second">More work memory of at least <see cref="WorkLength"/> ints.</param>
    /// <param name="allowUncompressed">Whether uncompressed mode is decoded (T.4, T.6) or its entrance code is invalid (JBIG2 MMR, T.88 §6.2.6).</param>
    public CcittLineDecoder(ReadOnlySpan<byte> data, int columns, Span<int> first, Span<int> second, bool allowUncompressed = true)
    {
        _allowUncompressed = allowUncompressed;
        _reader = new CcittBitReader(data);
        _columns = columns;
        _reference = first[..WorkLength(columns)];
        _current = second[..WorkLength(columns)];
        _referenceCount = 0;
        _currentCount = 0;
        _tooLong = false;
        _uncompressed = false;
        _patternWhites = 0;
        _patternEnd = CcittPatternEnd.None;
        _exitTag = false;
        SetReferenceWhite();
        _current[..Sentinels].Fill(columns);
    }

    /// <summary>Gets or sets the absolute bit position in the data.</summary>
    public long BitPosition
    {
        readonly get => _reader.Position;
        set => _reader.Position = value;
    }

    /// <summary>Gets the number of bytes read, the last partly read byte included.</summary>
    public readonly int BytesConsumed => _reader.BytesConsumed;

    /// <summary>Gets a value indicating whether only zero bits (padding) are left.</summary>
    public readonly bool OnlyZerosLeft => _reader.OnlyZerosLeft;

    /// <summary>Gets the changing elements of the line decoded last (without sentinels).</summary>
    public readonly ReadOnlySpan<int> Current => _current[.._currentCount];

    /// <summary>The number of ints each work array needs for <paramref name="columns"/> columns.</summary>
    /// <param name="columns">The number of columns.</param>
    /// <returns>The length.</returns>
    public static int WorkLength(int columns) => columns + 1 + Sentinels;

    /// <summary>Moves to the next byte boundary.</summary>
    public void AlignToByte() => _reader.AlignToByte();

    /// <summary>Reads a tag bit (T.4 §4.2.2: 1 = the next line is one-dimensional).</summary>
    /// <returns>The bit; 0 past the end.</returns>
    public int ReadTagBit() => _reader.ReadBit();

    /// <summary>Consumes fill and an EOL (11 or more zeros, then a 1) when they come next. T.4 §4.1.3, §4.1.4.</summary>
    /// <returns><see langword="true"/> when an EOL was consumed.</returns>
    public bool TrySkipEol()
    {
        long zeros = _reader.CountZeros(long.MaxValue);
        if (zeros >= CcittCodes.EolLength - 1 && zeros < _reader.BitsLeft)
        {
            _reader.Skip((int)Math.Min(zeros + 1, int.MaxValue));
            EndUncompressed();
            return true;
        }

        return false;
    }

    /// <summary>Tells whether fill and an EOL come next (after a tag bit when <paramref name="afterTagBit"/>), consuming nothing.</summary>
    /// <param name="afterTagBit">Whether a tag bit comes first (the EOLs of RTC for K &gt; 0, T.4 §4.2.4).</param>
    /// <returns><see langword="true"/> when an EOL follows.</returns>
    public readonly bool IsEolAhead(bool afterTagBit)
    {
        CcittBitReader reader = _reader;
        if (afterTagBit)
        {
            reader.Skip(1);
        }

        long zeros = reader.CountZeros(long.MaxValue);
        return zeros >= CcittCodes.EolLength - 1 && zeros < reader.BitsLeft;
    }

    /// <summary>Moves to the start of the next EOL (its fill zeros), for resynchronizing after a damaged line.</summary>
    /// <returns><see langword="false"/> when the data ends first (the position is then at the end).</returns>
    public bool SeekEol()
    {
        while (true)
        {
            long zeros = _reader.CountZeros(long.MaxValue);
            if (zeros >= _reader.BitsLeft)
            {
                _reader.Position += _reader.BitsLeft;
                return false;
            }

            if (zeros >= CcittCodes.EolLength - 1)
            {
                return true;
            }

            _reader.Position += zeros + 1;
        }
    }

    /// <summary>Makes the reference line all white (the first line of a block, T.4 §4.2.1.3.4, T.6 §2.2.2).</summary>
    public void SetReferenceWhite()
    {
        _referenceCount = 0;
        _reference[..Sentinels].Fill(_columns);
        EndUncompressed();
    }

    /// <summary>Makes the line decoded last the reference line for the next one.</summary>
    public void UseCurrentAsReference()
    {
        Span<int> swap = _reference;
        _reference = _current;
        _current = swap;
        _referenceCount = _currentCount;
    }

    /// <summary>Replaces the line decoded last with the reference line (the previous line), which stays the reference.</summary>
    public void RepeatReference()
    {
        _reference[..(_referenceCount + Sentinels)].CopyTo(_current);
        _currentCount = _referenceCount;
    }

    /// <summary>Replaces the line decoded last with a white line.</summary>
    public void ClearCurrent()
    {
        _currentCount = 0;
        _current[..Sentinels].Fill(_columns);
    }

    /// <summary>Decodes a one-dimensionally coded line: alternating white and black runs from white. T.4 §4.1.</summary>
    /// <returns>How the line ended.</returns>
    public CcittLineStatus Decode1DLine()
    {
        StartLine();
        int position = 0;
        bool black = false;
        if (_uncompressed && !ContinueUncompressed(ref position, ref black, out CcittLineStatus carried))
        {
            return EndLine(carried, position);
        }

        while (position < _columns)
        {
            int run = ReadRun(black, out CcittLineStatus status);
            if (run == EnterUncompressed)
            {
                if (!StartUncompressed(ref position, ref black, out status))
                {
                    return EndLine(status, position);
                }

                continue;
            }

            if (run < 0)
            {
                return EndLine(status, position);
            }

            position = (int)Math.Min((long)position + run, RunCap);
            Push(position);
            black = !black;
        }

        return EndLine(CcittLineStatus.Ok, position);
    }

    /// <summary>Decodes a two-dimensionally coded line against the reference line. T.4 §4.2.1.3, Figure 7; T.6 §2.2.</summary>
    /// <returns>How the line ended.</returns>
    public CcittLineStatus Decode2DLine()
    {
        StartLine();
        ReadOnlySpan<int> reference = _reference;
        int columns = _columns;
        int a0 = -1;
        bool black = false;
        int j = 0;
        if (_uncompressed)
        {
            int position = 0;
            if (!ContinueUncompressed(ref position, ref black, out CcittLineStatus carried))
            {
                return EndLine(carried, position);
            }

            a0 = position;
            j = black ? 1 : 0;
        }

        while (a0 < columns)
        {
            // b1: the first changing element of the reference line right of a0 whose colour is opposite to a0's (index parity).
            while (reference[j] <= a0 && reference[j] < columns)
            {
                j += 2;
            }

            int b1 = reference[j];
            int b2 = reference[j + 1];
            int entry = CcittCodes.Modes[_reader.Peek(7)];
            var mode = (CcittMode)(entry & 0xF);
            int length = entry >> 4;
            if (mode == CcittMode.None)
            {
                return EndLine(ZeroBitsStatus(), Math.Max(a0, 0));
            }

            if (length > _reader.BitsLeft)
            {
                return EndLine(CcittLineStatus.EndOfData, Math.Max(a0, 0));
            }

            _reader.Skip(length);
            switch (mode)
            {
                case CcittMode.Pass:
                    a0 = b2;
                    break;
                case CcittMode.Horizontal:
                    int start = Math.Max(a0, 0);
                    int first = ReadRun(black, out CcittLineStatus status);
                    if (first < 0)
                    {
                        return EndLine(first == EnterUncompressed ? CcittLineStatus.InvalidCode : status, start);
                    }

                    int a1 = (int)Math.Min((long)start + first, RunCap);
                    int second = ReadRun(!black, out status);
                    if (second < 0)
                    {
                        Push(a1);
                        return EndLine(second == EnterUncompressed ? CcittLineStatus.InvalidCode : status, Math.Min(a1, columns));
                    }

                    int a2 = (int)Math.Min((long)a1 + second, RunCap);
                    Push(a1);
                    Push(a2);
                    a0 = Math.Min(a2, columns);
                    break;
                case CcittMode.Extension:
                    if (CcittCodes.ExtensionBits > _reader.BitsLeft)
                    {
                        return EndLine(CcittLineStatus.EndOfData, Math.Max(a0, 0));
                    }

                    int extension = _reader.Peek(CcittCodes.ExtensionBits);
                    _reader.Skip(CcittCodes.ExtensionBits);
                    if (extension != UncompressedExtension || !_allowUncompressed)
                    {
                        return EndLine(CcittLineStatus.InvalidCode, Math.Max(a0, 0));
                    }

                    int at = Math.Max(a0, 0);
                    if (!StartUncompressed(ref at, ref black, out CcittLineStatus uncompressed))
                    {
                        return EndLine(uncompressed, at);
                    }

                    // Back in the basic scheme at the pixel after the pattern, with the colour the tag bit named; b1 is searched
                    // again from the start of the reference line with the parity of that colour.
                    a0 = at;
                    j = black ? 1 : 0;
                    break;
                default:
                    int vertical = b1 + VerticalOffset(mode);
                    if (vertical < 0 || vertical <= a0)
                    {
                        return EndLine(CcittLineStatus.BadVerticalPosition, Math.Max(a0, 0));
                    }

                    Push(vertical);
                    a0 = Math.Min(vertical, columns);
                    black = !black;
                    j = j > 0 ? j - 1 : j + 1;
                    break;
            }
        }

        return EndLine(CcittLineStatus.Ok, columns);
    }

    /// <summary>
    /// Writes the line decoded last as packed pixels: <c>ceil(Columns / 8)</c> bytes, MSB first, white = 1 and black = 0 unless
    /// <paramref name="blackIsOne"/>; the pad bits after the last column are white.
    /// </summary>
    /// <param name="row">The destination, at least <c>ceil(Columns / 8)</c> bytes.</param>
    /// <param name="blackIsOne">Whether black pixels are 1 bits (BlackIs1, ISO 32000-2 Table 11; JBIG2, T.88 §6.2.6).</param>
    public readonly void PackCurrent(Span<byte> row, bool blackIsOne)
    {
        int columns = _columns;
        int bytes = (columns + 7) >> 3;
        byte white = blackIsOne ? (byte)0x00 : (byte)0xFF;
        row = row[..bytes];
        row.Fill(white);
        ReadOnlySpan<int> changes = _current[.._currentCount];
        for (int i = 0; i < changes.Length; i += 2)
        {
            int start = changes[i];
            int end = i + 1 < changes.Length ? changes[i + 1] : columns;
            Invert(row, start, end, (byte)~white);
        }
    }

    private static void Invert(Span<byte> row, int start, int end, byte black)
    {
        if (start >= end)
        {
            return;
        }

        int first = start >> 3;
        int last = (end - 1) >> 3;
        byte firstMask = (byte)(0xFF >> (start & 7));
        byte lastMask = (byte)(0xFF << (7 - ((end - 1) & 7)));
        if (first == last)
        {
            row[first] ^= (byte)(firstMask & lastMask);
            return;
        }

        row[first] ^= firstMask;
        row[(first + 1)..last].Fill(black);
        row[last] ^= lastMask;
    }

    /// <summary>Enters uncompressed mode at <paramref name="position"/> and paints until the exit code or the end of the line.</summary>
    /// <returns><see langword="false"/> when the line ends here: filled while still in uncompressed mode (Ok) or damaged.</returns>
    private bool StartUncompressed(ref int position, ref bool black, out CcittLineStatus status)
    {
        _uncompressed = true;
        _patternWhites = 0;
        _patternEnd = CcittPatternEnd.None;
        return ContinueUncompressed(ref position, ref black, out status);
    }

    /// <summary>
    /// Paints uncompressed-mode pixels from <paramref name="position"/>: the rest of the pattern carried over from the line before,
    /// then pattern after pattern. On the exit code the colour of the next run is pushed and <paramref name="black"/> set to it.
    /// </summary>
    /// <returns><see langword="true"/> after the exit code; <see langword="false"/> when the line is full (Ok, the mode carries on) or damaged.</returns>
    private bool ContinueUncompressed(ref int position, ref bool black, out CcittLineStatus status)
    {
        while (true)
        {
            if (_patternWhites == 0 && _patternEnd == CcittPatternEnd.None && !ReadPattern(out status))
            {
                EndUncompressed();
                return false;
            }

            while (_patternWhites > 0)
            {
                if (position >= _columns)
                {
                    status = CcittLineStatus.Ok;
                    return false;
                }

                Paint(position++, black: false);
                _patternWhites--;
            }

            switch (_patternEnd)
            {
                case CcittPatternEnd.Black:
                    if (position >= _columns)
                    {
                        status = CcittLineStatus.Ok;
                        return false;
                    }

                    Paint(position++, black: true);
                    _patternEnd = CcittPatternEnd.None;
                    break;
                case CcittPatternEnd.Exit:
                    EndUncompressed();
                    black = _exitTag;
                    if (position < _columns)
                    {
                        Paint(position, black);
                    }

                    status = CcittLineStatus.Ok;
                    return true;
                default:
                    if (position >= _columns)
                    {
                        status = CcittLineStatus.Ok;
                        return false;
                    }

                    break;
            }
        }
    }

    /// <summary>Reads one uncompressed-mode code word of T.4 Table 5 into the pattern state.</summary>
    /// <returns><see langword="false"/> with the status when the bits are no code word (an EOL, the end of the data).</returns>
    private bool ReadPattern(out CcittLineStatus status)
    {
        long zeros = _reader.CountZeros(LongestUncompressedZeros + 1);
        if (zeros > LongestUncompressedZeros)
        {
            status = ZeroBitsStatus();
            return false;
        }

        // 1, 01, ..., 00001: zeros whites and a black; 000001: five whites; 0000001T to 00000000001T: zeros - 6 whites and the exit.
        int length = (int)zeros + 1 + (zeros > 5 ? 1 : 0);
        if (length > _reader.BitsLeft)
        {
            status = CcittLineStatus.EndOfData;
            return false;
        }

        _reader.Skip((int)zeros + 1);
        if (zeros <= 4)
        {
            _patternWhites = (int)zeros;
            _patternEnd = CcittPatternEnd.Black;
        }
        else if (zeros == 5)
        {
            _patternWhites = 5;
            _patternEnd = CcittPatternEnd.None;
        }
        else
        {
            _patternWhites = (int)zeros - 6;
            _patternEnd = CcittPatternEnd.Exit;
            _exitTag = _reader.ReadBit() == 1;
        }

        status = CcittLineStatus.Ok;
        return true;
    }

    /// <summary>Makes pixel <paramref name="position"/> the given colour: a changing element when it differs from the colour so far.</summary>
    private void Paint(int position, bool black)
    {
        if (((_currentCount & 1) == 1) != black)
        {
            Push(position);
        }
    }

    private void EndUncompressed()
    {
        _uncompressed = false;
        _patternWhites = 0;
        _patternEnd = CcittPatternEnd.None;
    }

    private static int VerticalOffset(CcittMode mode) => mode switch
    {
        CcittMode.VerticalRight1 => 1,
        CcittMode.VerticalRight2 => 2,
        CcittMode.VerticalRight3 => 3,
        CcittMode.VerticalLeft1 => -1,
        CcittMode.VerticalLeft2 => -2,
        CcittMode.VerticalLeft3 => -3,
        _ => 0,
    };

    private void StartLine()
    {
        _currentCount = 0;
        _tooLong = false;
    }

    /// <summary>
    /// Reads one run: any number of make-up codes and one terminating code. Returns the run; <see cref="EnterUncompressed"/> after
    /// the 1-D extension code of uncompressed mode (consumed); or -1 with the status when no run could be read (an EOL or zero fill,
    /// another extension code, an invalid code, the end of the data).
    /// </summary>
    private int ReadRun(bool black, out CcittLineStatus status)
    {
        ushort[] table = black ? CcittCodes.Black : CcittCodes.White;
        int bits = black ? 13 : 12;
        int total = 0;
        while (true)
        {
            int entry = table[_reader.Peek(bits)];
            int length = entry >> 12;
            int run = entry & 0xFFF;
            if (length == 0 || run == CcittCodes.EolRun)
            {
                status = ZeroBitsStatus();
                return -1;
            }

            if (length > _reader.BitsLeft)
            {
                status = CcittLineStatus.EndOfData;
                return -1;
            }

            _reader.Skip(length);
            if (run == CcittCodes.ExtensionRun)
            {
                if (CcittCodes.ExtensionBits > _reader.BitsLeft)
                {
                    status = CcittLineStatus.EndOfData;
                    return -1;
                }

                int extension = _reader.Peek(CcittCodes.ExtensionBits);
                _reader.Skip(CcittCodes.ExtensionBits);
                status = CcittLineStatus.InvalidCode;
                return extension == UncompressedExtension && _allowUncompressed ? EnterUncompressed : -1;
            }

            total = Math.Min(total + run, RunCap);
            if (run < 64)
            {
                status = CcittLineStatus.Ok;
                return total;
            }
        }
    }

    /// <summary>What a run of leading zeros at the decoding position is: an EOL (with fill), the end of the data, or an invalid code.</summary>
    private readonly CcittLineStatus ZeroBitsStatus()
    {
        long zeros = _reader.CountZeros(long.MaxValue);
        return zeros >= _reader.BitsLeft ? CcittLineStatus.EndOfData
            : zeros >= CcittCodes.EolLength - 1 ? CcittLineStatus.UnexpectedEol
            : CcittLineStatus.InvalidCode;
    }

    /// <summary>Adds a changing element at <paramref name="position"/> (cut at Columns); one at the last element's position cancels it.</summary>
    private void Push(int position)
    {
        if (position > _columns)
        {
            _tooLong = true;
            position = _columns;
        }

        if (_currentCount > 0 && _current[_currentCount - 1] == position)
        {
            _currentCount--;
        }
        else if (_currentCount < _current.Length - Sentinels)
        {
            _current[_currentCount++] = position;
        }
    }

    /// <summary>Ends the line: white from <paramref name="position"/> when it ended early, elements at Columns dropped, sentinels added.</summary>
    private CcittLineStatus EndLine(CcittLineStatus status, int position)
    {
        if (status != CcittLineStatus.Ok && (_currentCount & 1) == 1)
        {
            Push(Math.Min(position, _columns));
        }

        if (status != CcittLineStatus.Ok)
        {
            EndUncompressed();
        }

        while (_currentCount > 0 && _current[_currentCount - 1] >= _columns)
        {
            _currentCount--;
        }

        _current.Slice(_currentCount, Sentinels).Fill(_columns);
        return status == CcittLineStatus.Ok && _tooLong ? CcittLineStatus.RunTooLong : status;
    }
}
