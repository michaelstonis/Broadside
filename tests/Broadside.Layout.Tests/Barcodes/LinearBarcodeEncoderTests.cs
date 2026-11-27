using Broadside.Layout.Flow;
using static Broadside.Layout.Tests.Barcodes.Code128EncoderTests;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// EAN-13 and UPC-A (ISO/IEC 15420) and Code 39 (ISO/IEC 16388) (issue #631): the number sets, the leading digit's parity, check digits
/// added or verified, the Code 39 character table and its modulo 43 check character, each symbol read back by <see cref="ReferenceDecoders"/>.
/// </summary>
public sealed class LinearBarcodeEncoderTests
{
    [Fact]
    public void Number_set_A_has_odd_parity_and_sets_B_and_C_derive_from_it()
    {
        for (int digit = 0; digit < 10; digit++)
        {
            string a = BarcodeLinear.SetA[digit];
            Assert.Equal(1, a.Count(static c => c == '1') % 2);
            Assert.Equal('0', a[0]);
            Assert.Equal('1', a[^1]);
        }

        Assert.Equal("1110010", BarcodeLinear.SetC(BarcodeLinear.SetA[0]));
        Assert.Equal("0100111", BarcodeLinear.SetB(BarcodeLinear.SetA[0]));
        Assert.Equal("0010111", BarcodeLinear.SetB(BarcodeLinear.SetA[9]));
    }

    [Fact]
    public void An_EAN_13_from_12_digits_gets_its_check_digit_and_95_modules()
    {
        Barcode barcode = Barcode.Ean13("400638133393");

        Assert.Equal("4006381333931", barcode.Data);
        Assert.Equal("4006381333931", barcode.HumanReadableText);
        Assert.Equal(95, barcode.Columns);
        Assert.Equal(11, barcode.QuietZone);
        Assert.Equal("4006381333931", ReferenceDecoders.Ean13(Row(barcode)));

        // The leading 4 sets the left half's parity to A B A A B B (ISO/IEC 15420 Table 3): the first digit, 0, is set A.
        Assert.Equal("101" + "0001101" + "0100111", string.Concat(Row(barcode).Take(17).Select(static d => d ? '1' : '0')));
    }

    [Theory]
    [InlineData("5901234123457")]
    [InlineData("9780201379624")]
    [InlineData("0000000000000")]
    public void An_EAN_13_with_a_correct_check_digit_reads_back(string digits)
    {
        Assert.Equal(digits, ReferenceDecoders.Ean13(Row(Barcode.Ean13(digits))));
    }

    [Fact]
    public void A_UPC_A_is_the_EAN_13_with_a_leading_zero()
    {
        Barcode barcode = Barcode.UpcA("03600029145");

        Assert.Equal("036000291452", barcode.Data);
        Assert.Equal(9, barcode.QuietZone);
        Assert.Equal("0036000291452", ReferenceDecoders.Ean13(Row(barcode)));
        Assert.Equal(Row(Barcode.Ean13("0036000291452")), Row(barcode));
    }

    [Theory]
    [InlineData("4006381333932", "should be 1")]
    [InlineData("40063813339", "got 11 characters")]
    [InlineData("40063813339A", "'A' at index 11")]
    public void A_wrong_EAN_13_throws_naming_the_problem(string digits, string reason)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Barcode.Ean13(digits));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_UPC_A_check_digit_throws()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Barcode.UpcA("036000291453"));

        Assert.Contains("should be 2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Code_39_table_has_three_wide_elements_per_character_two_of_them_bars_except_for_the_four_symbols()
    {
        Assert.Equal(44, BarcodeLinear.Code39Patterns.Length);
        Assert.Equal(44, BarcodeLinear.Code39Patterns.Distinct(StringComparer.Ordinal).Count());
        for (int index = 0; index < 44; index++)
        {
            string pattern = BarcodeLinear.Code39Patterns[index];
            Assert.Equal(3, pattern.Count(static c => c == '1'));
            int wideBars = pattern.Where(static (c, i) => i % 2 == 0 && c == '1').Count();
            bool symbol = index is >= 39 and <= 42;
            Assert.Equal(symbol ? 0 : 2, wideBars);
        }
    }

    [Fact]
    public void Code_39_frames_the_data_with_start_and_stop_and_reads_back()
    {
        Barcode barcode = Barcode.Code39("CODE 39");

        // Nine characters (start, seven, stop) of 15 modules, with a narrow space between them.
        Assert.Equal((9 * 15) + 8, barcode.Columns);
        Assert.Equal("CODE 39", barcode.HumanReadableText);
        Assert.Equal("CODE 39", ReferenceDecoders.Code39(Row(barcode), checkDigit: false));
    }

    [Fact]
    public void The_Code_39_check_character_is_the_sum_of_the_values_modulo_43()
    {
        // C 12 + O 24 + D 13 + E 14 + space 38 + 3 + 9 = 113; 113 mod 43 = 27, R.
        Barcode barcode = Barcode.Code39("CODE 39", checkDigit: true);

        Assert.Equal("CODE 39R", barcode.HumanReadableText);
        Assert.Equal("CODE 39R", ReferenceDecoders.Code39(Row(barcode), checkDigit: true));
    }

    [Fact]
    public void Code_39_has_no_lowercase_letters()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Barcode.Code39("Code"));

        Assert.Contains("'o'", error.Message, StringComparison.Ordinal);
        Assert.Contains("no lowercase letters", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Barcode.Code39("*"));
        Assert.Throws<ArgumentException>(() => Barcode.Code39(string.Empty));
    }
}
