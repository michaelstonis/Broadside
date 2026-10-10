using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>RunLengthDecode through the filter contract. ISO 32000-2 §7.4.5.</summary>
public sealed class RunLengthDecodeFilterTests
{
    public static TheoryData<byte[], string> WellFormed => new()
    {
        { [0xFD, (byte)'a', 0x00, (byte)'b', 0x80], "aaaab" },
        { [0x02, (byte)'x', (byte)'y', (byte)'z', 0x81, (byte)'-', 0x80], "xyz" + new string('-', 128) },
        { [0x80], string.Empty },
        { [0x00, (byte)'q', 0x80, 0x00, (byte)'r'], "q" },
    };

    [Theory]
    [MemberData(nameof(WellFormed))]
    public void Decodes_literal_and_repeated_runs_up_to_the_end_of_data_byte(byte[] encoded, string expected)
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new RunLengthDecodeFilter(), encoded);

        Assert.Equal(expected, Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Fact]
    public void Round_trips_the_encoder_of_the_corpus_generator()
    {
        byte[] data = [.. FilterEncoders.SampleData(5_000, alphabet: 3), .. new byte[300], .. FilterEncoders.SampleData(5_000, alphabet: 250)];

        (byte[] decoded, string[] codes) = FilterTesting.Run(new RunLengthDecodeFilter(), FilterEncoders.RunLengthEncode(data));

        Assert.Equal(data, decoded);
        Assert.Empty(codes);
    }

    public static TheoryData<byte[], string> EndingEarly => new()
    {
        { [0x00, (byte)'a'], "a" },
        { [0x03, (byte)'a', (byte)'b'], "ab" },
        { [0x00, (byte)'a', 0xFE], "a" },
    };

    [Theory]
    [MemberData(nameof(EndingEarly))]
    public void Data_that_ends_early_keeps_what_is_available_with_a_diagnostic(byte[] encoded, string expected)
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new RunLengthDecodeFilter(), encoded);

        Assert.Equal(expected, Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterDataTruncated"], codes);
    }

    [Fact]
    public void Strict_mode_throws_when_the_end_of_data_byte_is_missing()
    {
        DiagnosticException error = Assert.Throws<DiagnosticException>(
            () => FilterTesting.Run(new RunLengthDecodeFilter(), [0x00, (byte)'a'], mode: PdfReadingMode.Strict));

        Assert.Equal("FilterDataTruncated", error.Diagnostic.Code);
    }
}
