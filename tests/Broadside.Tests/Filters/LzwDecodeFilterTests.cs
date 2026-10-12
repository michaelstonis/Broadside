using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>LZWDecode through the filter contract. ISO 32000-2 §7.4.4.2 and §7.4.4.3 Table 8.</summary>
public sealed class LzwDecodeFilterTests
{
    [Fact]
    public void Decodes_the_packed_example_after_table_7()
    {
        // §7.4.4.2 Example 1 and Example 2: the codes of Table 7, 9 bits each, packed high-order bit first.
        byte[] encoded = Convert.FromHexString("800B6050220C0C8501");

        (byte[] decoded, string[] codes) = FilterTesting.Run(new LzwDecodeFilter(), encoded);

        Assert.Equal<byte>([45, 45, 45, 45, 45, 65, 45, 45, 45, 66], decoded);
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(1, "<< /EarlyChange 1 >>")]
    [InlineData(0, "<< /EarlyChange 0 >>")]
    public void Round_trips_data_that_grows_the_code_length_to_12_bits_and_clears_the_table(int earlyChange, string? parameters)
    {
        byte[] data = FilterEncoders.SampleData(60_000);

        (byte[] decoded, string[] codes) = FilterTesting.Run(new LzwDecodeFilter(), FilterEncoders.LzwEncode(data, earlyChange), parameters);

        Assert.Equal(data, decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void Early_change_matters_once_the_table_reaches_511_entries()
    {
        byte[] data = FilterEncoders.SampleData(5_000);
        byte[] encodedLate = FilterEncoders.LzwEncode(data, earlyChange: 0);

        (byte[] decoded, _) = FilterTesting.Run(new LzwDecodeFilter(), encodedLate);

        Assert.NotEqual(data, decoded);
    }

    [Fact]
    public void Data_without_the_end_of_data_code_decodes_to_its_end_with_a_diagnostic()
    {
        byte[] data = FilterEncoders.SampleData(2_000);
        byte[] encoded = FilterEncoders.LzwEncode(data);
        byte[] truncated = encoded[..(encoded.Length / 2)];

        (byte[] decoded, string[] codes) = FilterTesting.Run(new LzwDecodeFilter(), truncated);

        Assert.True(decoded.Length > 500);
        Assert.Equal(data[..decoded.Length], decoded);
        Assert.Equal(["FilterDataTruncated"], codes);
    }

    [Fact]
    public void A_code_not_yet_in_the_table_ends_the_data_with_a_diagnostic()
    {
        // Clear, 'A' (65), then code 300 while the next free entry is 258 (9-bit codes: 256, 65, 300, 257).
        byte[] encoded = Pack9(256, 65, 300, 257);

        (byte[] decoded, string[] codes) = FilterTesting.Run(new LzwDecodeFilter(), encoded);

        Assert.Equal<byte>([65], decoded);
        Assert.Equal(["FilterDataInvalid"], codes);
    }

    [Fact]
    public void An_early_change_other_than_0_or_1_is_read_as_1_with_a_diagnostic()
    {
        byte[] data = FilterEncoders.SampleData(3_000);

        (byte[] decoded, string[] codes) = FilterTesting.Run(new LzwDecodeFilter(), FilterEncoders.LzwEncode(data), "<< /EarlyChange 5 >>");

        Assert.Equal(data, decoded);
        Assert.Equal(["DecodeParmsInvalid"], codes);
    }

    [Fact]
    public void Strict_mode_throws_for_truncated_data()
    {
        byte[] encoded = FilterEncoders.LzwEncode(FilterEncoders.SampleData(100));

        DiagnosticException error = Assert.Throws<DiagnosticException>(
            () => FilterTesting.Run(new LzwDecodeFilter(), encoded.AsSpan(0, encoded.Length - 2).ToArray(), mode: PdfReadingMode.Strict));

        Assert.Equal("FilterDataTruncated", error.Diagnostic.Code);
    }

    private static byte[] Pack9(params int[] codes)
    {
        var output = new List<byte>();
        int accumulator = 0;
        int bits = 0;
        foreach (int code in codes)
        {
            accumulator = (accumulator << 9) | code;
            bits += 9;
            while (bits >= 8)
            {
                output.Add((byte)(accumulator >> (bits - 8)));
                bits -= 8;
            }
        }

        if (bits > 0)
        {
            output.Add((byte)(accumulator << (8 - bits)));
        }

        return [.. output];
    }
}
