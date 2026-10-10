using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The LZW and Flate predictor functions, through the document API: hand-computed vectors for TIFF Predictor 2 and each PNG
/// algorithm. ISO 32000-2 §7.4.4.3 Table 8 and §7.4.4.4 Tables 9 and 10; PNG algorithms per ISO/IEC 15948 §9.
/// </summary>
public class PredictorTests
{
    /// <summary>Two rows of three 8-bit gray samples: 10 20 30 / 15 25 40.</summary>
    private static readonly byte[] Samples = [10, 20, 30, 15, 25, 40];

    public static TheoryData<byte[]> PngRows => new()
    {
        { [0, 10, 20, 30, 0, 15, 25, 40] },     // None
        { [1, 10, 10, 10, 1, 15, 10, 15] },     // Sub: minus the byte to the left
        { [2, 10, 20, 30, 2, 5, 5, 10] },       // Up: minus the byte above (0 above the first row)
        { [3, 10, 15, 20, 3, 10, 8, 13] },      // Average: minus floor((left + above) / 2)
        { [4, 10, 10, 10, 4, 5, 5, 10] },       // Paeth: minus the closest of left, above, upper-left to left + above - upper-left
        { [1, 10, 10, 10, 4, 5, 5, 10] },       // Each row carries its own tag
    };

    [Theory]
    [MemberData(nameof(PngRows))]
    public void Png_predicted_rows_decode_to_the_samples_whatever_predictor_10_to_15_is_declared(byte[] predicted)
    {
        foreach (int declared in (int[])[10, 12, 15])
        {
            byte[] file = FilterTesting.FileWithStream(
                $"/Filter /FlateDecode /DecodeParms << /Predictor {declared} /Columns 3 >>",
                FilterEncoders.Zlib(predicted));

            (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

            Assert.Equal(Samples, decoded);
            Assert.Empty(codes);
        }
    }

    [Fact]
    public void Png_sub_predicts_from_the_whole_pixel_to_the_left_when_a_pixel_is_several_bytes()
    {
        // Colors 2, 8 bits: the left neighbour of byte 2 is byte 0.
        byte[] file = FilterTesting.FileWithStream(
            "/Filter /FlateDecode /DecodeParms << /Predictor 11 /Colors 2 /Columns 2 >>",
            FilterEncoders.Zlib([1, 1, 2, 2, 2]));

        (byte[] decoded, _) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal<byte>([1, 2, 3, 4], decoded);
    }

    public static TheoryData<string, byte[], byte[]> TiffRows => new()
    {
        // 8 bits, 2 colors, 3 columns: each component minus the same component of the sample to its left, modulo 256.
        { "/Colors 2 /Columns 3", [10, 100, 2, 1, 3, 254], [10, 100, 12, 101, 15, 99] },
        // 16 bits, big-endian: 0100 0203 0001 differenced to 0100 0103 FDFE.
        { "/BitsPerComponent 16 /Columns 3", [0x01, 0x00, 0x01, 0x03, 0xFD, 0xFE], [0x01, 0x00, 0x02, 0x03, 0x00, 0x01] },
        // 1 bit, 8 columns: 1 1 0 0 1 0 1 1 differenced (XOR with the left bit) to 1 0 1 0 1 1 1 0.
        { "/BitsPerComponent 1 /Columns 8", [0xAE], [0xCB] },
        // 2 bits, 4 columns: 3 1 2 0 differenced modulo 4 to 3 2 1 2; two rows, each predicted on its own.
        { "/BitsPerComponent 2 /Columns 4", [0xE6, 0xE6], [0xD8, 0xD8] },
        // 4 bits, 3 columns: 15 1 8 differenced modulo 16 to 15 2 7; the padding nibble is left alone.
        { "/BitsPerComponent 4 /Columns 3", [0xF2, 0x75], [0xF1, 0x85] },
    };

    [Theory]
    [MemberData(nameof(TiffRows))]
    public void Tiff_predictor_2_adds_each_component_to_the_same_component_to_its_left(string parameters, byte[] predicted, byte[] expected)
    {
        byte[] file = FilterTesting.FileWithStream(
            $"/Filter /FlateDecode /DecodeParms << /Predictor 2 {parameters} >>",
            FilterEncoders.Zlib(predicted));

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal(expected, decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void Predictors_apply_after_lzw_too()
    {
        byte[] file = FilterTesting.FileWithStream(
            "/Filter /LZWDecode /DecodeParms << /Predictor 12 /Columns 3 /EarlyChange 0 >>",
            FilterEncoders.LzwEncode([2, 10, 20, 30, 2, 5, 5, 10], earlyChange: 0));

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal(Samples, decoded);
        Assert.Empty(codes);
    }

    public static TheoryData<string, byte[], byte[], string> MalformedPredictions => new()
    {
        { "/Predictor 7 /Columns 3", [2, 10, 20, 30], [2, 10, 20, 30], "PredictorInvalid" },
        { "/Predictor 12 /Columns 3", [9, 10, 20, 30], [10, 20, 30], "PredictorInvalid" },
        { "/Predictor 12 /Columns 3", [2, 10, 20, 30, 0, 5], [10, 20, 30, 5, 0, 0], "PredictorInvalid" },
        { "/Predictor 12 /Columns 0", [2, 10, 2, 5], [10, 15], "DecodeParmsInvalid" },
        { "/Predictor 12 /Columns 1 /BitsPerComponent 3", [2, 10, 2, 5], [10, 15], "DecodeParmsInvalid" },
        { "/Predictor 12 /Columns 100000000", [2, 10, 20, 30], [2, 10, 20, 30], "PredictorInvalid" },
        { "/Predictor 2 /Colors 4 /Columns 2", [1, 2, 3, 4, 5], [1, 2, 3, 4, 5], "PredictorInvalid" },
    };

    [Theory]
    [MemberData(nameof(MalformedPredictions))]
    public void Malformed_predictor_data_or_parameters_are_repaired_with_a_diagnostic(string parameters, byte[] predicted, byte[] expected, string code)
    {
        byte[] file = FilterTesting.FileWithStream($"/Filter /FlateDecode /DecodeParms << {parameters} >>", FilterEncoders.Zlib(predicted));

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal(expected, decoded);
        Assert.Equal([code], codes);
    }

    [Fact]
    public void Strict_mode_throws_for_an_unknown_predictor()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /FlateDecode /DecodeParms << /Predictor 3 >>", FilterEncoders.Zlib([1, 2, 3]));

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => FilterTesting.Decode(file, new PdfOptions().UseStrict()));

        Assert.Equal("PredictorInvalid", error.Diagnostic.Code);
    }
}
