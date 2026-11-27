using Broadside.Layout.Flow;

namespace Broadside.Layout.Tests.Barcodes;

/// <summary>
/// Code 128 (ISO/IEC 15417) and GS1-128 (issue #631): the symbol character table, code sets A, B and C chosen for the shortest symbol,
/// the modulo 103 check character, and FNC1 with GS1 application identifiers. Every symbol is read back by <see cref="ReferenceDecoders"/>.
/// </summary>
public sealed class Code128EncoderTests
{
    [Fact]
    public void The_symbol_character_table_has_107_distinct_patterns_of_11_modules_with_even_bar_widths_and_a_13_module_stop()
    {
        string[] patterns = BarcodeCode128.Patterns;

        Assert.Equal(107, patterns.Length);
        Assert.Equal(107, patterns.Distinct(StringComparer.Ordinal).Count());
        for (int value = 0; value < 106; value++)
        {
            Assert.Equal(11, patterns[value].Sum(static c => c - '0'));
            Assert.Equal(0, (patterns[value][0] + patterns[value][2] + patterns[value][4] - (3 * '0')) % 2);
        }

        Assert.Equal("2331112", patterns[106]);
    }

    [Fact]
    public void Text_starts_in_code_set_B_and_the_check_character_is_the_weighted_sum_modulo_103()
    {
        Assert.Equal([104, 55, 73, 75, 73, 80, 69, 68, 73, 65], BarcodeCode128.Values(Input("Wikipedia")));

        Barcode barcode = Barcode.Code128("Wikipedia");

        // Start, nine characters, check (88: 3281 mod 103) and stop: 11 modules each, 13 for the stop.
        Assert.Equal((11 * 11) + 13, barcode.Columns);
        Assert.Equal(1, barcode.Rows);
        Assert.Equal(10, barcode.QuietZone);
        Assert.Equal("Wikipedia", barcode.HumanReadableText);
        Assert.Equal("2331112", string.Concat(ReferenceDecoders.Runs(Row(barcode)).TakeLast(7)));
        Assert.Equal("Wikipedia", ReferenceDecoders.Code128(Row(barcode)));
        Assert.Equal(BarcodeCode128.Patterns[88], string.Concat(ReferenceDecoders.Runs(Row(barcode)).SkipLast(7).TakeLast(6)));
    }

    [Fact]
    public void An_even_run_of_digits_is_code_set_C_two_digits_per_character()
    {
        Assert.Equal([105, 12, 34, 56, 78, 90], BarcodeCode128.Values(Input("1234567890")));
        Assert.Equal("1234567890", ReferenceDecoders.Code128(Row(Barcode.Code128("1234567890"))));
    }

    [Theory]
    [InlineData("12345", 5)]
    [InlineData("AB12345678", 8)]
    [InlineData("AB1234567C", 10)]
    [InlineData("\t", 2)]
    [InlineData("a\tb", 5)]
    [InlineData("\t\ta\t\t", 7)]
    [InlineData("X1Y", 4)]
    public void Code_sets_are_switched_and_shifted_to_make_the_shortest_symbol(string data, int characters)
    {
        Assert.Equal(characters, BarcodeCode128.Values(Input(data)).Count);
        Assert.Equal(data, ReferenceDecoders.Code128(Row(Barcode.Code128(data))));
    }

    [Fact]
    public void Control_characters_start_code_set_A()
    {
        Assert.Equal([103, 73], BarcodeCode128.Values(Input("\t")));
    }

    [Fact]
    public void Gs1128_starts_with_FNC1_and_runs_fixed_length_identifiers_together()
    {
        Barcode barcode = Barcode.Gs1128("(01)09501101530003(17)250101");

        Assert.Equal([105, 102, 1, 9, 50, 11, 1, 53, 0, 3, 17, 25, 1, 1], BarcodeCode128.Values(BarcodeLinear.Gs1("(01)09501101530003(17)250101")));
        Assert.Equal(BarcodeKind.Gs1128, barcode.Kind);
        Assert.Equal("(01)09501101530003(17)250101", barcode.HumanReadableText);
        Assert.Equal("\u001D010950110153000317250101", ReferenceDecoders.Code128(Row(barcode)));
    }

    [Fact]
    public void Gs1128_ends_a_variable_length_element_with_FNC1_unless_it_is_last()
    {
        Assert.Equal("\u001D2112345\u001D10ABC", ReferenceDecoders.Code128(Row(Barcode.Gs1128("(21)12345(10)ABC"))));
        Assert.Equal("\u001D0109501101530003" + "10ABC", ReferenceDecoders.Code128(Row(Barcode.Gs1128("(01)09501101530003(10)ABC"))));
    }

    [Theory]
    [InlineData("01)09501101530003", "is not '('")]
    [InlineData("(01", "not closed")]
    [InlineData("(1)5", "two to four digits")]
    [InlineData("(01)123", "14 characters long")]
    [InlineData("(310)123456", "have 4 digits")]
    [InlineData("(10)", "has no data")]
    [InlineData("(10)AB#", "'#'")]
    public void Gs1128_rejects_malformed_element_strings_with_the_reason(string data, string reason)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Barcode.Gs1128(data));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_outside_ASCII_and_empty_text_throw_naming_the_problem()
    {
        ArgumentException outside = Assert.Throws<ArgumentException>(() => Barcode.Code128("café"));
        Assert.Contains("U+00E9 at index 3", outside.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => Barcode.Code128(string.Empty));
        Assert.Throws<ArgumentNullException>(() => Barcode.Code128(null!));
    }

    internal static int[] Input(string data) => [.. data.Select(static c => (int)c)];

    internal static bool[] Row(Barcode barcode) => [.. Enumerable.Range(0, barcode.Columns).Select(column => barcode.IsDark(column, 0))];
}
