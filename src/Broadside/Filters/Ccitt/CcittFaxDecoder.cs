namespace Broadside.Filters.Ccitt;

/// <summary>The <c>CCITTFaxDecode</c> parameters, validated. ISO 32000-2 §7.4.6, Table 11.</summary>
/// <param name="K">Negative: pure two-dimensional (Group 4); 0: one-dimensional; positive: mixed, a tag bit before every line.</param>
/// <param name="EndOfLine">Whether EOLs are required before every line (they are accepted anyway).</param>
/// <param name="EncodedByteAlign">Whether every line (or the EOL before it) is aligned to a byte boundary.</param>
/// <param name="Columns">The width in pixels, at least 1.</param>
/// <param name="Rows">The height in rows; 0 when unknown.</param>
/// <param name="EndOfBlock">Whether the data is expected to end with EOFB or RTC (always recognized anyway).</param>
/// <param name="BlackIs1">Whether black pixels decode to 1 bits.</param>
/// <param name="DamagedRowsBeforeError">How many damaged rows in a row are tolerated (replaced) when EndOfLine is true and K &gt;= 0.</param>
internal readonly record struct CcittParameters(
    int K = 0,
    bool EndOfLine = false,
    bool EncodedByteAlign = false,
    int Columns = 1728,
    int Rows = 0,
    bool EndOfBlock = true,
    bool BlackIs1 = false,
    int DamagedRowsBeforeError = 0)
{
    /// <summary>Gets the number of bytes in one decoded row: <c>ceil(Columns / 8)</c>.</summary>
    public int RowBytes => (int)(((long)Columns + 7) >> 3);
}

/// <summary>What went wrong while decoding, for the filter to report once per kind.</summary>
[Flags]
internal enum CcittIssues
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>EndOfLine is true but a line had no EOL before it.</summary>
    MissingEol = 1 << 0,

    /// <summary>A run went past the last column and was cut.</summary>
    RunTooLong = 1 << 1,

    /// <summary>A damaged row was replaced, within DamagedRowsBeforeError.</summary>
    DamagedRowReplaced = 1 << 2,

    /// <summary>A damaged row beyond DamagedRowsBeforeError was kept as far as it decoded; decoding went on at the next EOL.</summary>
    DamagedRowResynchronized = 1 << 3,

    /// <summary>A damaged row with no EOL to resynchronize at: decoding stopped after it.</summary>
    DamagedData = 1 << 4,

    /// <summary>The data ended inside a row.</summary>
    Truncated = 1 << 5,

    /// <summary>The data ended before Rows rows (no EOFB or RTC).</summary>
    FewerRows = 1 << 6,

    /// <summary>Uncompressed mode was used; the rest of that row is white.</summary>
    Uncompressed = 1 << 7,
}

/// <summary>
/// Frames coded lines with the <c>CCITTFaxDecode</c> parameters (ISO 32000-2 §7.4.6, Table 11) on top of
/// <see cref="CcittLineDecoder"/>: EOLs, tag bits, byte alignment, EOFB/RTC, row limits and damaged rows. Call
/// <see cref="MoveNext"/>, then <see cref="WriteRow"/>, until it returns <see langword="false"/>. Allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.4.6; ITU-T T.4 §4.1.3-4.1.4, §4.2.2-4.2.4 (EOL, fill, tag bit, RTC); ITU-T T.6 §2.4.1 (EOFB). Before every line:
/// stop at the row limit; stop at EOFB/RTC whatever EndOfBlock says (Adobe's decoder checks for it always); align first when there
/// are no EOLs (pad bits come before the line data); accept an EOL (with fill) whether or not EndOfLine asks for one, and align
/// only when it is missing (so fill that makes the EOL end on a byte boundary is read as fill); read the tag bit when K &gt; 0;
/// stop when only zero bits remain.
/// </para>
/// <para>
/// A damaged row (an invalid code, a misplaced vertical mode, an early EOL, uncompressed mode) is replaced by the previous row (or
/// white after another damaged row) when EndOfLine is true, K &gt;= 0 and DamagedRowsBeforeError allows; otherwise it keeps what
/// decoded, white after. Decoding then resumes at the next EOL, or stops when the data has none.
/// </para>
/// </remarks>
internal ref struct CcittFaxDecoder
{
    private CcittLineDecoder _line;
    private readonly CcittParameters _parameters;
    private readonly int _limit;
    private bool _eolMode;
    private bool _ended;
    private bool _pending;
    private bool _previousDamaged;
    private int _consecutiveDamaged;

    /// <summary>Initializes a new instance of the <see cref="CcittFaxDecoder"/> struct.</summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="rowLimit">The most rows to decode (the image's Height, a size limit), or <see cref="int.MaxValue"/>.</param>
    /// <param name="first">Work memory of <see cref="CcittLineDecoder.WorkLength"/> ints.</param>
    /// <param name="second">More work memory of <see cref="CcittLineDecoder.WorkLength"/> ints.</param>
    public CcittFaxDecoder(ReadOnlySpan<byte> data, CcittParameters parameters, int rowLimit, Span<int> first, Span<int> second)
    {
        _line = new CcittLineDecoder(data, parameters.Columns, first, second);
        _parameters = parameters;
        _limit = !parameters.EndOfBlock && parameters.Rows > 0 ? Math.Min(parameters.Rows, rowLimit) : rowLimit;
        _eolMode = parameters.EndOfLine;
        Issues = CcittIssues.None;
        RowsDecoded = 0;
        DamagedRows = 0;
        FirstIssueRow = -1;
        EndOfBlockFound = false;
    }

    /// <summary>Gets what went wrong so far.</summary>
    public CcittIssues Issues { get; private set; }

    /// <summary>Gets the number of rows decoded so far.</summary>
    public int RowsDecoded { get; private set; }

    /// <summary>Gets the number of damaged rows so far.</summary>
    public int DamagedRows { get; private set; }

    /// <summary>Gets the index of the first row with an issue, or -1.</summary>
    public int FirstIssueRow { get; private set; }

    /// <summary>Gets a value indicating whether the data ended with EOFB or RTC.</summary>
    public bool EndOfBlockFound { get; private set; }

    /// <summary>Gets the number of bytes read, the last partly read byte included.</summary>
    public readonly int BytesConsumed => _line.BytesConsumed;

    /// <summary>Decodes the next row.</summary>
    /// <returns><see langword="false"/> when there are no more rows.</returns>
    public bool MoveNext()
    {
        if (_pending)
        {
            _line.UseCurrentAsReference();
            _pending = false;
        }

        if (_ended || RowsDecoded >= _limit)
        {
            Finish();
            return false;
        }

        bool tagged = _parameters.K > 0;
        long start = _line.BitPosition;
        if (_line.TrySkipEol() && _line.IsEolAhead(tagged))
        {
            ConsumeEndOfBlock(tagged);
            return false;
        }

        _line.BitPosition = start;
        if (_line.OnlyZerosLeft)
        {
            Finish();
            return false;
        }

        if (_parameters.EncodedByteAlign && !_eolMode)
        {
            _line.AlignToByte();
        }

        bool eol = _line.TrySkipEol();
        if (!eol && _eolMode)
        {
            if (_parameters.EncodedByteAlign)
            {
                _line.AlignToByte();
            }

            Note(CcittIssues.MissingEol);
            if (RowsDecoded == 0)
            {
                // The data does not have the EOLs the parameters promise: read it without them (libtiff does the same).
                _eolMode = false;
            }
        }

        bool twoDimensional = _parameters.K < 0 || (tagged && _line.ReadTagBit() == 0);
        CcittLineStatus status = twoDimensional ? _line.Decode2DLine() : _line.Decode1DLine();
        switch (status)
        {
            case CcittLineStatus.Ok:
                _consecutiveDamaged = 0;
                _previousDamaged = false;
                break;
            case CcittLineStatus.RunTooLong:
                Note(CcittIssues.RunTooLong);
                _consecutiveDamaged = 0;
                _previousDamaged = false;
                break;
            case CcittLineStatus.EndOfData:
                Note(CcittIssues.Truncated);
                _ended = true;
                break;
            default:
                Damaged(status);
                break;
        }

        RowsDecoded++;
        _pending = true;
        return true;
    }

    /// <summary>Writes the row <see cref="MoveNext"/> decoded: <see cref="CcittParameters.RowBytes"/> bytes in the PDF 1-bit layout.</summary>
    /// <param name="row">The destination.</param>
    public readonly void WriteRow(Span<byte> row) => _line.PackCurrent(row, _parameters.BlackIs1);

    private void Damaged(CcittLineStatus status)
    {
        // Uncompressed mode is legal data this decoder does not read: reported on its own, not as damage.
        bool unsupported = status == CcittLineStatus.Uncompressed;
        Note(unsupported ? CcittIssues.Uncompressed : CcittIssues.None);
        DamagedRows++;
        if (!_eolMode)
        {
            // Nothing to resynchronize at: keep the partial row and stop.
            Note(unsupported ? CcittIssues.None : CcittIssues.DamagedData);
            _ended = true;
            return;
        }

        _consecutiveDamaged++;
        bool tolerated = _parameters.EndOfLine && _parameters.K >= 0 && _consecutiveDamaged <= _parameters.DamagedRowsBeforeError;
        if (tolerated)
        {
            Note(unsupported ? CcittIssues.None : CcittIssues.DamagedRowReplaced);
            if (_previousDamaged)
            {
                _line.ClearCurrent();
            }
            else
            {
                _line.RepeatReference();
            }
        }
        else
        {
            Note(unsupported ? CcittIssues.None : CcittIssues.DamagedRowResynchronized);
        }

        _previousDamaged = true;
        if (!_line.SeekEol())
        {
            _ended = true;
        }
    }

    private void ConsumeEndOfBlock(bool tagged)
    {
        // EOFB is two EOLs (T.6 §2.4.1.1); RTC six, with a tag bit after each when K > 0 (T.4 §4.1.4, §4.2.4).
        do
        {
            if (tagged)
            {
                _line.ReadTagBit();
            }
        }
        while (_line.TrySkipEol());

        EndOfBlockFound = true;
        _ended = true;
        Finish();
    }

    private void Finish()
    {
        _ended = true;
        if (_parameters.Rows > 0 && RowsDecoded < _parameters.Rows && RowsDecoded < _limit && !(EndOfBlockFound && _parameters.EndOfBlock))
        {
            Note(CcittIssues.FewerRows);
        }
    }

    private void Note(CcittIssues issue)
    {
        if (issue == CcittIssues.None)
        {
            return;
        }

        if (Issues == CcittIssues.None)
        {
            FirstIssueRow = RowsDecoded;
        }

        Issues |= issue;
    }
}
