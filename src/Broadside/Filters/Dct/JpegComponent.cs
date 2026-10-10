using System.Buffers;

namespace Broadside.Filters.Dct;

/// <summary>
/// One image component of a frame: its frame-header parameters, geometry, the tables bound to it, and the per-image buffers it
/// decodes into (quantized coefficients, then IDCT samples).
/// </summary>
/// <remarks>ITU-T T.81 §A.1.1 (component dimensions), §A.2 (MCUs and block order), §B.2.2 Table B.2 (Ci, Hi, Vi, Tqi).</remarks>
internal sealed class JpegComponent
{
    /// <summary>The quantization values latched at the first scan of the component, natural order.</summary>
    public readonly int[] Quantization = new int[64];

    /// <summary>Gets or sets the component identifier Ci.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the horizontal sampling factor Hi (1 for a frame of one component).</summary>
    public int H { get; set; }

    /// <summary>Gets or sets the vertical sampling factor Vi (1 for a frame of one component).</summary>
    public int V { get; set; }

    /// <summary>Gets or sets the quantization table destination Tqi.</summary>
    public int QuantizationTable { get; set; }

    /// <summary>Gets or sets a value indicating whether <see cref="Quantization"/> holds the table.</summary>
    public bool QuantizationLatched { get; set; }

    /// <summary>Gets or sets a value indicating whether a scan has covered the component.</summary>
    public bool Scanned { get; set; }

    /// <summary>Gets or sets the number of blocks in a row of a non-interleaved scan: ceil(xi / 8).</summary>
    public int BlocksWide { get; set; }

    /// <summary>Gets or sets the number of block rows of a non-interleaved scan: ceil(yi / 8).</summary>
    public int BlocksHigh { get; set; }

    /// <summary>Gets or sets the number of blocks in a row padded to whole MCUs: MCUs per row x Hi.</summary>
    public int BlocksPerLine { get; set; }

    /// <summary>Gets or sets the number of block rows padded to whole MCUs: MCU rows x Vi.</summary>
    public int BlockRows { get; set; }

    /// <summary>Gets or sets the DC predictor (§F.2.1.3.1).</summary>
    public int Predictor { get; set; }

    /// <summary>Gets or sets the DC table bound by the current scan.</summary>
    public HuffmanTable? DcTable { get; set; }

    /// <summary>Gets or sets the AC table bound by the current scan.</summary>
    public HuffmanTable? AcTable { get; set; }

    /// <summary>Gets the quantized coefficients of <see cref="StoredRows"/> block rows from <see cref="FirstStoredRow"/>, 64 per block, natural order.</summary>
    public short[]? Coefficients { get; private set; }

    /// <summary>Gets the number of block rows <see cref="Coefficients"/> holds.</summary>
    public int StoredRows { get; private set; }

    /// <summary>Gets or sets the first block row <see cref="Coefficients"/> holds.</summary>
    public int FirstStoredRow { get; set; }

    /// <summary>Gets the IDCT samples of one MCU row: <see cref="StripStride"/> x 8 Vi bytes.</summary>
    public byte[]? Strip { get; private set; }

    /// <summary>Gets the width of <see cref="Strip"/>: <see cref="BlocksPerLine"/> x 8.</summary>
    public int StripStride => BlocksPerLine * 8;

    /// <summary>Gets a row the component's samples are replicated into to the image width, when it is subsampled horizontally.</summary>
    public byte[]? Upsampled { get; private set; }

    /// <summary>Returns the 64 coefficients of a block.</summary>
    /// <param name="row">The block row.</param>
    /// <param name="column">The block column.</param>
    /// <returns>The block.</returns>
    public Span<short> Block(int row, int column) =>
        Coefficients.AsSpan((((row - FirstStoredRow) * BlocksPerLine) + column) * 64, 64);

    /// <summary>Rents the buffers for an image: coefficients for <paramref name="rows"/> block rows, the strip, the upsampling row.</summary>
    /// <param name="rows">The block rows to store.</param>
    /// <param name="imageWidth">The image width X.</param>
    /// <param name="upsample">Whether the component is subsampled horizontally.</param>
    public void Allocate(int rows, int imageWidth, bool upsample)
    {
        StoredRows = rows;
        FirstStoredRow = 0;
        int count = BlocksPerLine * rows * 64;
        Coefficients = ArrayPool<short>.Shared.Rent(count);
        Coefficients.AsSpan(0, count).Clear();
        Strip = ArrayPool<byte>.Shared.Rent(StripStride * V * 8);
        Upsampled = upsample ? ArrayPool<byte>.Shared.Rent(imageWidth) : null;
    }

    /// <summary>Zeroes the stored coefficients.</summary>
    public void ClearCoefficients() => Coefficients.AsSpan(0, BlocksPerLine * StoredRows * 64).Clear();

    /// <summary>Returns the per-image buffers and forgets the per-image state.</summary>
    public void Release()
    {
        if (Coefficients is not null)
        {
            ArrayPool<short>.Shared.Return(Coefficients);
            Coefficients = null;
        }

        if (Strip is not null)
        {
            ArrayPool<byte>.Shared.Return(Strip);
            Strip = null;
        }

        if (Upsampled is not null)
        {
            ArrayPool<byte>.Shared.Return(Upsampled);
            Upsampled = null;
        }

        StoredRows = 0;
        QuantizationLatched = false;
        Scanned = false;
        DcTable = null;
        AcTable = null;
        Predictor = 0;
    }
}
