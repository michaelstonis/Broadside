using System.Text;

namespace Broadside.Layout.Flow;

/// <summary>A barcode symbology <see cref="Barcode"/> encodes.</summary>
public enum BarcodeKind
{
    /// <summary>Code 128 (ISO/IEC 15417).</summary>
    Code128,

    /// <summary>GS1-128: Code 128 with FNC1 and GS1 application identifiers.</summary>
    Gs1128,

    /// <summary>Code 39 (ISO/IEC 16388).</summary>
    Code39,

    /// <summary>EAN-13 (ISO/IEC 15420).</summary>
    Ean13,

    /// <summary>UPC-A (ISO/IEC 15420).</summary>
    UpcA,

    /// <summary>QR Code model 2 (ISO/IEC 18004).</summary>
    QrCode,

    /// <summary>Data Matrix ECC 200 (ISO/IEC 16022).</summary>
    DataMatrix,
}

/// <summary>The error correction level of a QR code: the share of codewords it can restore.</summary>
/// <remarks>ISO/IEC 18004 §5.3.8.</remarks>
public enum QrErrorCorrection
{
    /// <summary>Level L: about 7%.</summary>
    Low,

    /// <summary>Level M: about 15%.</summary>
    Medium,

    /// <summary>Level Q: about 25%.</summary>
    Quartile,

    /// <summary>Level H: about 30%.</summary>
    High,
}

/// <summary>
/// A barcode or two-dimensional symbol, encoded into its matrix of modules when it is created: <see cref="BarcodeCanvasExtensions.DrawBarcode"/>
/// paints it as vector rectangles on any canvas, and <see cref="ContainerBuilder.Barcode"/> places it in the document builder's flow.
/// </summary>
/// <remarks>
/// <para>
/// The factories validate and encode at once, and throw <see cref="ArgumentException"/> naming the character or length that cannot be
/// encoded. A linear symbol has one row of modules; a two-dimensional one has <see cref="Rows"/> rows. Row 0 is the top. A barcode is
/// immutable and safe to share across threads and documents.
/// </para>
/// <para>
/// The symbologies are ISO/IEC 15417 (Code 128), ISO/IEC 16388 (Code 39), ISO/IEC 15420 (EAN/UPC), ISO/IEC 18004 (QR Code) and ISO/IEC
/// 16022 (Data Matrix), with the GS1 General Specifications for GS1-128. Drawing needs only paths and fills (ISO 32000-2 §8.5.2, §8.5.3),
/// so no PDF version is raised.
/// </para>
/// </remarks>
public sealed class Barcode
{
    private readonly bool[] _modules;

    private Barcode(BarcodeKind kind, string data, bool[] modules, int columns, int rows, int quietZone, string? humanReadableText)
    {
        Kind = kind;
        Data = data;
        _modules = modules;
        Columns = columns;
        Rows = rows;
        QuietZone = quietZone;
        HumanReadableText = humanReadableText;
    }

    /// <summary>Gets the symbology.</summary>
    public BarcodeKind Kind { get; }

    /// <summary>Gets the data as given (a byte QR code's bytes read as Latin-1); EAN-13 and UPC-A include the check digit.</summary>
    public string Data { get; }

    /// <summary>Gets the width of the symbol in modules, without the quiet zone.</summary>
    public int Columns { get; }

    /// <summary>Gets the height of the symbol in modules: 1 for a linear symbol.</summary>
    public int Rows { get; }

    /// <summary>Gets the light margin the symbology requires on each side, in modules.</summary>
    /// <remarks>10 for Code 128, GS1-128 and Code 39, 11 for EAN-13 (its left side; the right needs 7), 9 for UPC-A, 4 for QR Code and 1 for Data Matrix.</remarks>
    public int QuietZone { get; }

    /// <summary>Gets the text printed under a linear symbol, or null for a two-dimensional one.</summary>
    public string? HumanReadableText { get; }

    /// <summary>Gets a value indicating whether the symbol is linear: one row of bars.</summary>
    public bool IsLinear => Rows == 1;

    /// <summary>Returns whether the module at <paramref name="column"/> and <paramref name="row"/> (row 0 at the top) is dark.</summary>
    /// <param name="column">The column, 0 to <see cref="Columns"/> - 1.</param>
    /// <param name="row">The row, 0 to <see cref="Rows"/> - 1.</param>
    /// <returns>True for a dark module (a bar), false for a light one (a space).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The column or row is outside the symbol.</exception>
    public bool IsDark(int column, int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        return _modules[(row * Columns) + column];
    }

    /// <summary>The modules, row by row from the top.</summary>
    internal ReadOnlySpan<bool> Modules => _modules;

    /// <summary>Encodes ASCII text (0 to 127) as Code 128, switching between code sets A, B and C to make the symbol as short as possible.</summary>
    /// <param name="data">The text.</param>
    /// <returns>The barcode.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty or holds a character outside ASCII.</exception>
    /// <remarks>ISO/IEC 15417.</remarks>
    public static Barcode Code128(string data)
    {
        NotEmpty(data);
        bool[] modules = BarcodeCode128.Encode(data);
        return Linear(BarcodeKind.Code128, data, modules, 10, data);
    }

    /// <summary>
    /// Encodes a GS1 element string as GS1-128, with each application identifier in parentheses, such as
    /// <c>(01)09501101530003(17)250101</c>: FNC1 starts the symbol and ends each variable-length element but the last.
    /// </summary>
    /// <param name="data">The element string.</param>
    /// <returns>The barcode; its human-readable text is the element string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> is empty, is not identifiers in parentheses each followed by data, holds a character outside the GS1 set,
    /// or gives an identifier of predefined length (such as (01), 14 digits) data of another length.
    /// </exception>
    /// <remarks>ISO/IEC 15417 and the GS1 General Specifications §5.4.</remarks>
    public static Barcode Gs1128(string data)
    {
        NotEmpty(data);
        bool[] modules = BarcodeCode128.Encode(BarcodeLinear.Gs1(data));
        return Linear(BarcodeKind.Gs1128, data, modules, 10, data);
    }

    /// <summary>Encodes Code 39: digits, capital letters, space and <c>- . $ / + %</c>, between <c>*</c> start and stop characters.</summary>
    /// <param name="data">The text.</param>
    /// <param name="checkDigit">Whether to append the modulo 43 check character.</param>
    /// <returns>The barcode.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty or holds a character Code 39 has not, such as a lowercase letter.</exception>
    /// <remarks>ISO/IEC 16388, with wide elements three modules wide.</remarks>
    public static Barcode Code39(string data, bool checkDigit = false)
    {
        NotEmpty(data);
        bool[] modules = BarcodeLinear.Code39(data, checkDigit, out string encoded);
        return Linear(BarcodeKind.Code39, data, modules, 10, encoded);
    }

    /// <summary>Encodes an EAN-13 from 12 digits, adding the check digit, or from 13, verifying it.</summary>
    /// <param name="digits">The digits.</param>
    /// <returns>The barcode, whose <see cref="Data"/> is the 13 digits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="digits"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="digits"/> is not 12 or 13 digits, or its check digit is wrong.</exception>
    /// <remarks>ISO/IEC 15420.</remarks>
    public static Barcode Ean13(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        string full = BarcodeLinear.EanDigits(digits, 12, "EAN-13");
        return Linear(BarcodeKind.Ean13, full, BarcodeLinear.Ean13(full), 11, full);
    }

    /// <summary>Encodes a UPC-A from 11 digits, adding the check digit, or from 12, verifying it.</summary>
    /// <param name="digits">The digits.</param>
    /// <returns>The barcode, whose <see cref="Data"/> is the 12 digits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="digits"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="digits"/> is not 11 or 12 digits, or its check digit is wrong.</exception>
    /// <remarks>ISO/IEC 15420: the EAN-13 with a leading 0.</remarks>
    public static Barcode UpcA(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        string full = BarcodeLinear.EanDigits(digits, 11, "UPC-A");
        return Linear(BarcodeKind.UpcA, full, BarcodeLinear.Ean13("0" + full), 9, full);
    }

    /// <summary>Encodes text as a QR code at error correction level M (see <see cref="QrCode(string, QrErrorCorrection)"/>).</summary>
    /// <param name="data">The text.</param>
    /// <returns>The barcode, at error correction level M.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or too long for version 40 (the message gives the capacity).</exception>
    /// <remarks>ISO/IEC 18004.</remarks>
    public static Barcode QrCode(string data) => QrCode(data, QrErrorCorrection.Medium);

    /// <summary>
    /// Encodes text as a QR code of the smallest version that holds it at <paramref name="level"/>, in numeric, alphanumeric and byte
    /// segments chosen to make it shortest. Latin-1 text is written as bytes; other text as UTF-8 after an ECI header.
    /// </summary>
    /// <param name="data">The text.</param>
    /// <param name="level">The error correction level.</param>
    /// <returns>The barcode.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or too long for version 40 (the message gives the capacity).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a defined level.</exception>
    /// <remarks>ISO/IEC 18004: all eight masks are tried and the one with the lowest penalty kept (§7.8.3); ECI 000026 is UTF-8 (§7.4.2).</remarks>
    public static Barcode QrCode(string data, QrErrorCorrection level)
    {
        NotEmpty(data);
        Level(level);
        (bool[] modules, int size) = BarcodeQrCode.Encode(data, level);
        return new Barcode(BarcodeKind.QrCode, data, modules, size, size, 4, null);
    }

    /// <summary>Encodes bytes as a QR code of the smallest version that holds them at level M, with no ECI.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The barcode; its <see cref="Data"/> is the bytes read as Latin-1.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or too long for version 40.</exception>
    /// <remarks>ISO/IEC 18004.</remarks>
    public static Barcode QrCode(ReadOnlySpan<byte> data) => QrCode(data, QrErrorCorrection.Medium);

    /// <summary>Encodes bytes as a QR code of the smallest version that holds them at <paramref name="level"/>, with no ECI.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="level">The error correction level.</param>
    /// <returns>The barcode; its <see cref="Data"/> is the bytes read as Latin-1.</returns>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or too long for version 40.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a defined level.</exception>
    /// <remarks>ISO/IEC 18004.</remarks>
    public static Barcode QrCode(ReadOnlySpan<byte> data, QrErrorCorrection level)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("The data is empty; a symbol needs at least one byte.", nameof(data));
        }

        Level(level);
        (bool[] modules, int size) = BarcodeQrCode.Encode(data, level);
        return new Barcode(BarcodeKind.QrCode, Encoding.Latin1.GetString(data), modules, size, size, 4, null);
    }

    /// <summary>Encodes text as the smallest square Data Matrix ECC 200 symbol that holds it, in ASCII encodation.</summary>
    /// <param name="data">The text: Latin-1 as it is, other text as UTF-8 after an ECI.</param>
    /// <returns>The barcode.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> is empty, or too long for the 144 by 144 symbol (the message gives the capacity).</exception>
    /// <remarks>ISO/IEC 16022: digit pairs take one codeword each.</remarks>
    public static Barcode DataMatrix(string data)
    {
        NotEmpty(data);
        (bool[] modules, int size) = BarcodeDataMatrix.Encode(data);
        return new Barcode(BarcodeKind.DataMatrix, data, modules, size, size, 1, null);
    }

    /// <summary>The name of the symbology as people say it, such as "QR code" or "EAN-13 barcode".</summary>
    internal string KindName => Kind switch
    {
        BarcodeKind.Code128 => "Code 128 barcode",
        BarcodeKind.Gs1128 => "GS1-128 barcode",
        BarcodeKind.Code39 => "Code 39 barcode",
        BarcodeKind.Ean13 => "EAN-13 barcode",
        BarcodeKind.UpcA => "UPC-A barcode",
        BarcodeKind.QrCode => "QR code",
        _ => "Data Matrix code",
    };

    private static Barcode Linear(BarcodeKind kind, string data, bool[] modules, int quietZone, string text) =>
        new(kind, data, modules, modules.Length, 1, quietZone, text);

    private static void NotEmpty(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("The data is empty; a symbol needs at least one character.", nameof(data));
        }
    }

    private static void Level(QrErrorCorrection level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown error correction level.");
        }
    }
}
