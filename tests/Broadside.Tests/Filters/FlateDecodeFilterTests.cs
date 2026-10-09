using System.IO.Compression;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>FlateDecode through the filter contract. ISO 32000-2 §7.4.4.1; RFC 1950 and RFC 1951.</summary>
public class FlateDecodeFilterTests
{
    private static readonly byte[] Data = FilterEncoders.SampleData(100_000, alphabet: 4);

    [Fact]
    public void Inflates_zlib_data()
    {
        (byte[] decoded, string[] codes) = FilterTesting.Run(new FlateDecodeFilter(), FilterEncoders.Zlib(Data));

        Assert.Equal(Data, decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void Ignores_a_wrong_checksum_and_bytes_after_the_final_block()
    {
        byte[] encoded = [.. FilterEncoders.Zlib(Data), 0x0D, 0x0A];
        encoded[^3] ^= 0xFF;

        (byte[] decoded, string[] codes) = FilterTesting.Run(new FlateDecodeFilter(), encoded);

        Assert.Equal(Data, decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void A_missing_checksum_is_not_a_deviation_worth_reporting()
    {
        byte[] encoded = FilterEncoders.Zlib(Data)[..^4];

        (byte[] decoded, string[] codes) = FilterTesting.Run(new FlateDecodeFilter(), encoded);

        Assert.Equal(Data, decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void Raw_deflate_data_without_a_zlib_header_is_inflated_with_a_diagnostic()
    {
        var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(Data);
        }

        (byte[] decoded, string[] codes) = FilterTesting.Run(new FlateDecodeFilter(), buffer.ToArray());

        Assert.Equal(Data, decoded);
        Assert.Equal(["FilterDataInvalid"], codes);
    }

    [Fact]
    public void Truncated_data_keeps_everything_inflated_with_a_diagnostic()
    {
        byte[] encoded = FilterEncoders.Zlib(Data);

        (byte[] decoded, string[] codes) = FilterTesting.Run(new FlateDecodeFilter(), encoded.AsSpan(0, encoded.Length / 2));

        Assert.InRange(decoded.Length, 10_000, Data.Length - 1);
        Assert.Equal(Data[..decoded.Length], decoded);
        Assert.Equal(["FilterDataTruncated"], codes);
    }

    [Fact]
    public void Corrupt_data_keeps_the_bytes_inflated_before_the_error_with_a_diagnostic()
    {
        // zlib header 78 01; a non-final stored block of 5 bytes (RFC 1951 §3.2.4); then a final block of the reserved type 11.
        byte[] encoded = [0x78, 0x01, 0x00, 0x05, 0x00, 0xFA, 0xFF, .. "hello"u8, 0x07];

        (byte[] decoded, string[] codes) = FilterTesting.Run(new FlateDecodeFilter(), encoded);

        // zlib reports the error in the same call that produced the last byte, which the BCL then withholds: at most one is lost.
        Assert.Equal("hell"u8.ToArray(), decoded[..4]);
        Assert.InRange(decoded.Length, 4, 5);
        Assert.Equal(["FilterDataInvalid"], codes);
    }

    [Fact]
    public void Strict_mode_throws_for_truncated_data()
    {
        byte[] encoded = FilterEncoders.Zlib(Data);

        DiagnosticException error = Assert.Throws<DiagnosticException>(
            () => FilterTesting.Run(new FlateDecodeFilter(), encoded.AsSpan(0, 100).ToArray(), mode: PdfReadingMode.Strict));

        Assert.Equal("FilterDataTruncated", error.Diagnostic.Code);
    }
}
