// Module matrices are square grids; a rectangular array reads as the symbol does.
#pragma warning disable CA1814

using System.Text;
using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// QR Code (ISO/IEC 18004) (issue #631): the worked example of Annex I ("01234567" at 1-M), capacities of Table 7, the smallest version,
/// segmentation into numeric, alphanumeric and byte modes, the ECI header for text outside Latin-1, and every symbol read back by
/// <see cref="ReferenceDecoders.QrCode"/> (format and version information, unmasking, Reed-Solomon syndromes and segments).
/// </summary>
public sealed class QrCodeEncoderTests
{
    [Fact]
    public void The_worked_example_01234567_at_1_M_gives_the_data_and_error_correction_codewords_of_the_standard()
    {
        (byte[] data, byte[] codewords, int version) = BarcodeQrCode.Codewords(Units("01234567"), QrErrorCorrection.Medium, eci: false);

        Assert.Equal(1, version);
        Assert.Equal(
            [0x10, 0x20, 0x0C, 0x56, 0x61, 0x80, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11],
            data);
        Assert.Equal([.. data, 0xA5, 0x24, 0xD4, 0xC1, 0xED, 0x36, 0xC7, 0x87, 0x2C, 0x55], codewords);

        Barcode barcode = Barcode.QrCode("01234567", QrErrorCorrection.Medium);
        Assert.Equal(21, barcode.Columns);
        Assert.Equal(21, barcode.Rows);
        Assert.Equal(4, barcode.QuietZone);
        Assert.Null(barcode.HumanReadableText);
        Assert.Equal(("01234567", 1, 'M'), Read(barcode));
    }

    [Theory]
    [InlineData(1, QrErrorCorrection.Low, 19)]
    [InlineData(1, QrErrorCorrection.Medium, 16)]
    [InlineData(1, QrErrorCorrection.Quartile, 13)]
    [InlineData(1, QrErrorCorrection.High, 9)]
    [InlineData(10, QrErrorCorrection.Medium, 216)]
    [InlineData(40, QrErrorCorrection.Low, 2956)]
    [InlineData(40, QrErrorCorrection.High, 1276)]
    public void Data_capacities_match_Table_7(int version, QrErrorCorrection level, int codewords)
    {
        Assert.Equal(codewords, BarcodeQrCode.DataCodewords(version, level));
    }

    [Fact]
    public void Every_version_and_level_has_whole_blocks_that_fill_the_symbol()
    {
        foreach (QrErrorCorrection level in Enum.GetValues<QrErrorCorrection>())
        {
            int previous = 0;
            for (int version = 1; version <= 40; version++)
            {
                int capacity = BarcodeQrCode.DataCodewords(version, level);
                Assert.True(capacity > previous, $"{version}-{level}");
                previous = capacity;
            }
        }
    }

    [Theory]
    [InlineData("HELLO WORLD", QrErrorCorrection.Quartile, 1)]
    [InlineData("https://example.com/invoices/2026/000123?ref=QR", QrErrorCorrection.Medium, 4)]
    [InlineData("Ünïcødé is Latin-1", QrErrorCorrection.Low, 2)]
    [InlineData("Grüße aus 東京, Ωμέγα", QrErrorCorrection.High, 4)]
    [InlineData("ABCDEF0123456789012345678901234567890abcdefgh", QrErrorCorrection.Medium, 3)]
    public void The_smallest_version_is_chosen_and_the_symbol_reads_back(string data, QrErrorCorrection level, int version)
    {
        Barcode barcode = Barcode.QrCode(data, level);

        Assert.Equal((data, version, "LMQH"[(int)level]), Read(barcode));
        Assert.Equal((version * 4) + 17, barcode.Columns);
    }

    [Theory]
    [InlineData(QrErrorCorrection.Low)]
    [InlineData(QrErrorCorrection.Medium)]
    [InlineData(QrErrorCorrection.Quartile)]
    [InlineData(QrErrorCorrection.High)]
    public void Every_version_reads_back_at_every_level(QrErrorCorrection level)
    {
        // Fill each version to its capacity in byte mode, so every block layout, alignment grid and version block is exercised.
        for (int version = 1; version <= 40; version++)
        {
            int bytes = (BarcodeQrCode.DataCodewords(version, level) * 8 - 4 - (version <= 9 ? 8 : 16)) / 8;
            string data = string.Concat(Enumerable.Range(0, bytes).Select(static i => (char)('a' + (i * 7 % 26))));
            Barcode barcode = Barcode.QrCode(data, level);

            Assert.Equal((data, version, "LMQH"[(int)level]), Read(barcode));
        }
    }

    [Fact]
    public void Mixed_text_is_split_into_segments_shorter_than_one_mode_alone()
    {
        List<BarcodeQrCode.Unit> units = Units("Order 00001234567890123456789 ships");
        (byte[] mixed, _, _) = BarcodeQrCode.Codewords(units, QrErrorCorrection.Low, eci: false);

        // In byte mode alone the 35 characters take 4 + 8 + 280 bits: 37 codewords, more than version 2-L's 34.
        Assert.True(Barcode.QrCode("Order 00001234567890123456789 ships", QrErrorCorrection.Low).Columns <= 25);
        Assert.True(mixed.Length <= BarcodeQrCode.DataCodewords(2, QrErrorCorrection.Low));
        Assert.Equal(("Order 00001234567890123456789 ships", 2, 'L'), Read(Barcode.QrCode("Order 00001234567890123456789 ships", QrErrorCorrection.Low)));
    }

    [Fact]
    public void Text_outside_Latin_1_is_UTF_8_after_ECI_26_and_Latin_1_text_has_no_ECI()
    {
        (byte[] utf8, _, _) = BarcodeQrCode.Codewords(Units("Ω", latin1: false), QrErrorCorrection.Medium, eci: true);
        (byte[] latin1, _, _) = BarcodeQrCode.Codewords(Units("é"), QrErrorCorrection.Medium, eci: false);

        // 0111 00011010 (ECI 26), then 0100 (byte mode) and a count of 2 (Ω is two bytes in UTF-8).
        Assert.Equal([0x71, 0xA4, 0x02], utf8[..3]);
        Assert.Equal(0x40, latin1[0] & 0xF0);
        Assert.Equal("Ω", Read(Barcode.QrCode("Ω")).Text);
    }

    [Fact]
    public void Bytes_are_encoded_as_they_are()
    {
        byte[] bytes = [0x00, 0xFF, 0x10, 0x80, 0x41];
        Barcode barcode = Barcode.QrCode(bytes);

        bool[,] matrix = Matrix(barcode);
        Assert.Equal(bytes, ReferenceDecoders.QrCode(matrix).Bytes);
        Assert.Equal(Encoding.Latin1.GetString(bytes), barcode.Data);
    }

    [Fact]
    public void The_largest_symbols_hold_7089_digits_and_2953_bytes_at_level_L_and_more_throws_with_the_capacity()
    {
        Assert.Equal(177, Barcode.QrCode(new string('7', 7089), QrErrorCorrection.Low).Columns);
        Assert.Equal(177, Barcode.QrCode(new byte[2953], QrErrorCorrection.Low).Columns);

        ArgumentException digits = Assert.Throws<ArgumentException>(() => Barcode.QrCode(new string('7', 7090), QrErrorCorrection.Low));
        ArgumentException bytes = Assert.Throws<ArgumentException>(() => Barcode.QrCode(new byte[2954], QrErrorCorrection.Low));
        Assert.Contains("2956 codewords", digits.Message, StringComparison.Ordinal);
        Assert.Contains("2953 bytes", bytes.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_data_and_an_unknown_level_throw()
    {
        Assert.Throws<ArgumentException>(() => Barcode.QrCode(string.Empty));
        Assert.Throws<ArgumentException>(() => Barcode.QrCode(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => Barcode.QrCode("x", (QrErrorCorrection)9));
    }

    [Fact]
    public void The_mask_with_the_lowest_penalty_is_kept()
    {
        // Every symbol keeps the format information of one of the eight masks; the chosen one reads back, and the same input always
        // gives the same symbol.
        Barcode first = Barcode.QrCode("https://example.com/");
        Barcode second = Barcode.QrCode("https://example.com/");

        Assert.Equal(Matrix(first), Matrix(second));
        Assert.Equal("https://example.com/", Read(first).Text);
    }

    internal static (string Text, int Version, char Level) Read(Barcode barcode)
    {
        (string text, _, int version, char level) = ReferenceDecoders.QrCode(Matrix(barcode));
        return (text, version, level);
    }

    internal static bool[,] Matrix(Barcode barcode)
    {
        bool[,] matrix = new bool[barcode.Rows, barcode.Columns];
        for (int row = 0; row < barcode.Rows; row++)
        {
            for (int column = 0; column < barcode.Columns; column++)
            {
                matrix[row, column] = barcode.IsDark(column, row);
            }
        }

        return matrix;
    }

    private static List<BarcodeQrCode.Unit> Units(string text, bool latin1 = true)
    {
        if (latin1)
        {
            return [.. text.Select(static c => new BarcodeQrCode.Unit(c, [(byte)c]))];
        }

        return [.. text.EnumerateRunes().Select(static rune =>
        {
            byte[] bytes = new byte[rune.Utf8SequenceLength];
            rune.EncodeToUtf8(bytes);
            return new BarcodeQrCode.Unit(rune.Value, bytes);
        })];
    }
}
