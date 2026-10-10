using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>ASCIIHexDecode and ASCII85Decode through the filter contract. ISO 32000-2 §7.4.2 and §7.4.3.</summary>
public sealed class AsciiFilterTests
{
    [Theory]
    [InlineData("48656C6C6F>", "Hello")]
    [InlineData("48 65\n6c\t6C\r6F\f\0>", "Hello")]
    [InlineData("4>", "@")]
    [InlineData("486>", "H`")]
    [InlineData(">", "")]
    [InlineData("41>42", "A")]
    public void Ascii_hex_decodes_digit_pairs_ignores_white_space_and_pads_an_odd_final_digit(string encoded, string expected)
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new AsciiHexDecodeFilter(), Encoding.Latin1.GetBytes(encoded));

        Assert.Equal(expected, Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Fact]
    public void Ascii_hex_skips_invalid_characters_with_one_diagnostic()
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new AsciiHexDecodeFilter(), "4G1x42>"u8);

        Assert.Equal("AB", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterDataInvalid"], codes);
    }

    [Fact]
    public void Ascii_hex_without_the_end_marker_decodes_to_its_end_without_a_diagnostic()
    {
        // §7.4.2 says > "indicates EOD"; unlike §7.4.3 for ASCII85 it never requires the marker, so data that simply ends is not
        // a deviation. veraPDF passes such a stream (veraPDF corpus PDF_A-1b 6-1-2-t01-pass-a.pdf; issue #47).
        (byte[] decoded, string[] codes) = FilterTesting.Run(new AsciiHexDecodeFilter(), "414"u8);

        Assert.Equal<byte>([0x41, 0x40], decoded);
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData("9jqo^~>", "Man ")]
    [InlineData("9jqo^BlbD-BleB1DJ+*+F(f,q~>", "Man is distinguished")]
    [InlineData("z~>", "\0\0\0\0")]
    [InlineData("zz9jqo^~>", "\0\0\0\0\0\0\0\0Man ")]
    [InlineData("9jqo~>", "Man")]
    [InlineData("9jn~>", "Ma")]
    [InlineData("9`~>", "M")]
    [InlineData(" 9j\nqo^\t\0\r\f~>", "Man ")]
    [InlineData("~>", "")]
    [InlineData("s8W-!~>", "\xff\xff\xff\xff")]
    [InlineData("9jqo^~", "Man ")]
    [InlineData("<~9jqo^~>", "Man ")]
    public void Ascii85_decodes_groups_z_partial_groups_and_ignores_white_space(string encoded, string expected)
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new Ascii85DecodeFilter(), Encoding.Latin1.GetBytes(encoded));

        Assert.Equal(expected, Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData("9jqo^{~>", "Man ")]
    [InlineData("9jzqo^~>", "Man ")]
    [InlineData("s8W-\"~>", "\0\0\0\0")]
    [InlineData("9jqo^9~>", "Man ")]
    public void Ascii85_repairs_impossible_sequences_with_a_diagnostic(string encoded, string expected)
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new Ascii85DecodeFilter(), Encoding.Latin1.GetBytes(encoded));

        Assert.Equal(expected, Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterDataInvalid"], codes);
    }

    [Fact]
    public void Ascii85_without_the_end_marker_decodes_the_partial_group_with_a_diagnostic()
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new Ascii85DecodeFilter(), "9jqo^9jqo"u8);

        Assert.Equal("Man Man", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterDataTruncated"], codes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Both_filters_round_trip_the_encoders_of_the_corpus_generator(int extra)
    {
        byte[] data = [.. FilterEncoders.SampleData(10_000, alphabet: 200), .. new byte[extra]];

        (byte[] fromHex, string[] hexCodes) = FilterTesting.Run(new AsciiHexDecodeFilter(), Encoding.ASCII.GetBytes(Convert.ToHexString(data) + ">"));
        (byte[] from85, string[] codes85) = FilterTesting.Run(new Ascii85DecodeFilter(), FilterEncoders.Ascii85Encode(data));

        Assert.Equal(data, fromHex);
        Assert.Equal(data, from85);
        Assert.Empty(hexCodes);
        Assert.Empty(codes85);
    }

    [Fact]
    public void Strict_mode_throws_for_an_invalid_character()
    {
        DiagnosticException error = Assert.Throws<DiagnosticException>(
            () => FilterTesting.Run(new Ascii85DecodeFilter(), "9jqo^v~>"u8, mode: PdfReadingMode.Strict));

        Assert.Equal("FilterDataInvalid", error.Diagnostic.Code);
    }
}
