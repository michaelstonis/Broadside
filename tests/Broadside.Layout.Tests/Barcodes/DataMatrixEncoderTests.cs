// Module matrices are square grids; a rectangular array reads as the symbol does.
#pragma warning disable CA1814

using Broadside.Layout.Flow;
using static Broadside.Layout.Tests.Barcodes.QrCodeEncoderTests;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// Data Matrix ECC 200 (ISO/IEC 16022) (issue #631): the worked example "123456" (codewords 142 164 186 and error correction 114 25 5 88
/// 102), ASCII encodation, pad randomizing, the square symbol table, and every symbol size read back by
/// <see cref="ReferenceDecoders.DataMatrix"/> (finders, placement, Reed-Solomon syndromes per interleaved block).
/// </summary>
public sealed class DataMatrixEncoderTests
{
    [Fact]
    public void The_worked_example_123456_gives_the_codewords_of_the_standard()
    {
        List<byte> data = BarcodeDataMatrix.Ascii("123456"u8);
        byte[] codewords = BarcodeDataMatrix.Codewords(data, 3, 5, 1);

        Assert.Equal([142, 164, 186], data);
        Assert.Equal([142, 164, 186, 114, 25, 5, 88, 102], codewords);
        Barcode barcode = Barcode.DataMatrix("123456");
        Assert.Equal(10, barcode.Columns);
        Assert.Equal(10, barcode.Rows);
        Assert.Equal(1, barcode.QuietZone);
        Assert.Equal("123456", Read(barcode));
    }

    [Fact]
    public void Pads_after_the_first_are_randomized_by_position()
    {
        // "A" is 66; the pad at position 2 is 129, and the one at position 3 is 129 + (149 * 3 mod 253) + 1 - 254 = 70.
        Assert.Equal([66, 129, 70], BarcodeDataMatrix.Codewords(BarcodeDataMatrix.Ascii("A"u8), 3, 5, 1)[..3]);
    }

    [Fact]
    public void Characters_above_127_take_an_upper_shift()
    {
        Assert.Equal([235, 0xEA - 127], BarcodeDataMatrix.Ascii([0xEA]));
        Assert.Equal("é", Read(Barcode.DataMatrix("é")));
    }

    [Fact]
    public void Each_square_size_is_the_smallest_for_its_capacity_and_reads_back()
    {
        foreach ((int size, int region, int regions, int data, int ecc, int blocks) in BarcodeDataMatrix.Symbols)
        {
            Assert.Equal(size, regions * (region + 2));
            string digits = string.Concat(Enumerable.Range(0, data * 2).Select(static i => (char)('0' + (i * 3 % 10))));
            Barcode barcode = Barcode.DataMatrix(digits);

            Assert.Equal(size, barcode.Columns);
            Assert.Equal(digits, ReferenceDecoders.DataMatrix(Matrix(barcode), region, regions, data, ecc, blocks));
        }
    }

    [Theory]
    [InlineData("Hello, Data Matrix!")]
    [InlineData("Grüße aus 東京")]
    [InlineData("A1B2C3 0123456789 the quick brown fox")]
    public void Text_reads_back(string text)
    {
        Assert.Equal(text, Read(Barcode.DataMatrix(text)));
    }

    [Fact]
    public void The_largest_symbol_holds_3116_digits_and_more_throws_with_the_capacity()
    {
        Assert.Equal(144, Barcode.DataMatrix(new string('5', 3116)).Columns);

        ArgumentException error = Assert.Throws<ArgumentException>(() => Barcode.DataMatrix(new string('5', 3117)));
        Assert.Contains("holds 1558", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Barcode.DataMatrix(string.Empty));
    }

    private static string Read(Barcode barcode)
    {
        (_, int region, int regions, int data, int ecc, int blocks) = BarcodeDataMatrix.Symbols.Single(symbol => symbol.Size == barcode.Columns);
        return ReferenceDecoders.DataMatrix(Matrix(barcode), region, regions, data, ecc, blocks);
    }
}
