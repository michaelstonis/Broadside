using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>CCITTFaxDecode through the filter contract. ISO 32000-2 §7.4.6, Table 11; ITU-T T.4 §4; ITU-T T.6 §2.</summary>
public class CcittFaxDecodeFilterTests
{
    private static readonly CcittFaxDecodeFilter Filter = new();

    [Fact]
    public void An_all_white_line_of_the_default_1728_columns_coded_one_dimensionally_decodes_to_white_bytes()
    {
        // T.4 Tables 3a and 2: make-up 1728 (010011011) + white terminating 0 (00110101).
        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, [0x4D, 0x9A, 0x80]);

        Assert.Equal(Enumerable.Repeat((byte)0xFF, 216), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void An_all_white_line_coded_two_dimensionally_against_a_white_reference_is_the_single_bit_of_V0()
    {
        // T.4 §4.2.1.3.4: a1 = b1 = the imaginary element after the last column; V(0) is 1. Eight such lines are one 0xFF byte.
        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, [0xFF], "<< /K -1 /Columns 16 /Rows 8 /EndOfBlock false >>");

        Assert.Equal(Enumerable.Repeat((byte)0xFF, 16), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void A_line_that_starts_black_begins_with_a_white_run_of_zero()
    {
        // T.4 §4.1.1: white 0 (00110101), black 3 (10), white 5 (1100): 0011 0101 1011 00|00
        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, [0x35, 0xB0], "<< /Columns 8 /Rows 1 >>");

        Assert.Equal<byte>([0b0001_1111], decoded);
        Assert.Empty(codes);
    }

    public static TheoryData<string, int, int, string> Modes => new()
    {
        // Encoding (K, EndOfLine, EncodedByteAlign, EndOfBlock), columns, rows, extra parameters.
        { "0 False False False", 203, 40, "/Rows 40 /EndOfBlock false" },
        { "0 True True True", 203, 40, "/EndOfLine true /EncodedByteAlign true" },
        { "0 True False True", 1728, 24, "/EndOfLine true" },
        { "0 False True False", 77, 33, "/EncodedByteAlign true /EndOfBlock false /Rows 33" },
        { "0 False False True", 64, 16, string.Empty },
        { "4 True False True", 2600, 30, "/K 4 /EndOfLine true /Rows 30" },
        { "2 True True True", 300, 25, "/K 2 /EndOfLine true /EncodedByteAlign true" },
        { "1 True False False", 81, 26, "/K 1 /EndOfLine true /EndOfBlock false /Rows 26" },
        { "-1 False False True", 1728, 40, "/K -1" },
        { "-1 False False False", 1728, 40, "/K -1 /EndOfBlock false /Rows 40" },
        { "-1 False True True", 333, 21, "/K -1 /EncodedByteAlign true" },
        { "-1 True False True", 100, 20, "/K -1 /EndOfLine true" },
        { "-1 True True False", 99, 20, "/K -1 /EndOfLine true /EncodedByteAlign true /EndOfBlock false /Rows 20" },
        { "-1 False False True", 5300, 12, "/K -1" },
        { "-1 False False True", 1, 9, "/K -1" },
        { "0 False False True", 1, 9, string.Empty },
    };

    [Theory]
    [MemberData(nameof(Modes))]
    public void Every_mode_and_framing_decodes_the_bitmap_the_encoder_was_given(string encoding, int columns, int rows, string parameters)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(columns, rows);
        byte[] encoded = CcittEncoder.Encode(bitmap, ParseEncoding(encoding));

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, encoded, $"<< /Columns {columns} {parameters} >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void BlackIs1_makes_black_pixels_1_bits_and_the_pad_bits_0()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(45, 16);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1));

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, encoded, "<< /K -1 /Columns 45 /BlackIs1 true >>");

        Assert.Equal(CcittEncoder.Pack(bitmap, blackIs1: true), decoded);
        Assert.Equal(0, decoded[5] & 0b0000_0111);
        Assert.Empty(codes);
    }

    [Fact]
    public void Tag_bits_not_K_decide_which_lines_are_one_dimensional()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(120, 30);
        byte[] encoded = CcittEncoder.Encode(bitmap, new CcittEncoding(K: 3, EndOfLine: true));

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, encoded, "<< /K 8 /Columns 120 /EndOfLine true >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void With_EndOfBlock_false_decoding_stops_after_Rows_rows_and_ignores_what_follows()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(64, 10);
        byte[] encoded = [.. CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1, EndOfBlock: false)), 0x45, 0x49, 0x20, 0x51];

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, encoded, "<< /K -1 /Columns 64 /Rows 10 /EndOfBlock false >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void EOFB_or_RTC_ends_the_data_even_when_EndOfBlock_is_false(int k)
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(50, 12);
        byte[] encoded = [.. CcittEncoder.Encode(bitmap, new CcittEncoding(K: k, EndOfLine: k > 0)), 0xFF, 0xFF, 0xFF];

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, encoded, $"<< /K {k} /Columns 50 /EndOfBlock false >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void Byte_aligned_lines_may_put_their_EOL_on_the_byte_boundary_after_padding_the_line()
    {
        // The other EncodedByteAlign form (xpdf): each line's data is padded to a byte, then the EOL starts on the boundary.
        bool[][] bitmap = CcittEncoder.SampleBitmap(70, 12);
        string bits = string.Concat(bitmap.Select(row => Pad("000000000001" + CcittEncoder.Encode1D(row))));

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /K 0 /Columns 70 /EndOfLine true /EncodedByteAlign true >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void EOLs_are_accepted_when_EndOfLine_is_false_and_their_absence_is_reported_when_it_is_true()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(90, 16);
        byte[] withEols = CcittEncoder.Encode(bitmap, new CcittEncoding(K: 0, EndOfLine: true));
        byte[] withoutEols = CcittEncoder.Encode(bitmap, new CcittEncoding(K: 0));

        (byte[] decodedWith, string[] codesWith) = FilterTesting.Run(Filter, withEols, "<< /Columns 90 >>");
        (byte[] decodedWithout, string[] codesWithout) = FilterTesting.Run(Filter, withoutEols, "<< /Columns 90 /EndOfLine true >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decodedWith);
        Assert.Empty(codesWith);
        Assert.Equal(CcittEncoder.Pack(bitmap), decodedWithout);
        Assert.Equal(["CcittMissingEol"], codesWithout);
    }

    [Fact]
    public void Within_DamagedRowsBeforeError_a_damaged_row_is_replaced_by_the_previous_row_and_a_second_one_by_white()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(40, 8);
        string bits = string.Concat(bitmap.Select((row, y) => "000000000001" + (y is 3 or 4 ? "0000000000111" : CcittEncoder.Encode1D(row))));

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /Columns 40 /EndOfLine true /DamagedRowsBeforeError 2 /Rows 8 >>");

        bool[][] expected = [.. bitmap];
        expected[3] = bitmap[2];
        expected[4] = new bool[40];
        Assert.Equal(CcittEncoder.Pack(expected), decoded);
        Assert.Equal(["CcittDamagedRowReplaced"], codes);
    }

    [Fact]
    public void Beyond_DamagedRowsBeforeError_a_damaged_row_keeps_what_decoded_and_decoding_resumes_at_the_next_EOL()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(40, 8);
        bool[] damaged = [.. Enumerable.Repeat(true, 10), .. new bool[30]];
        string bits = string.Concat(bitmap.Select((row, y) =>
            "000000000001" + (y == 5 ? CcittEncoder.RunCodes(0, false) + CcittEncoder.RunCodes(10, true) + "0000000000111" : CcittEncoder.Encode1D(row))));

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /Columns 40 /EndOfLine true /Rows 8 >>");

        bool[][] expected = [.. bitmap];
        expected[5] = damaged;
        Assert.Equal(CcittEncoder.Pack(expected), decoded);
        Assert.Equal(["CcittDataInvalid"], codes);
    }

    [Fact]
    public void A_damaged_Group_4_row_has_no_EOL_to_resume_at_so_decoding_stops_after_it()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(32, 6);
        string bits = string.Concat(bitmap.Take(3).Select((row, y) => CcittEncoder.Encode2D(row, y == 0 ? new bool[32] : bitmap[y - 1]))) + "0000001000" + "1111";

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /K -1 /Columns 32 /Rows 6 >>");

        Assert.Equal([.. CcittEncoder.Pack(bitmap[..3]), 0xFF, 0xFF, 0xFF, 0xFF], decoded);
        Assert.Equal(["CcittDataInvalid"], codes);
    }

    [Fact]
    public void Data_that_ends_inside_a_row_keeps_the_rows_before_and_the_start_of_that_row()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(64, 4);
        bitmap[3] = [.. Enumerable.Repeat(true, 20), .. new bool[44]];
        string bits = string.Concat(bitmap.Take(3).Select(CcittEncoder.Encode1D)) + CcittEncoder.RunCodes(0, false) + CcittEncoder.RunCodes(20, true);

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /Columns 64 /Rows 4 /EndOfBlock false >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Equal(["FilterDataTruncated"], codes);
    }

    [Fact]
    public void Fewer_rows_than_Rows_without_EOFB_are_reported_as_truncated()
    {
        bool[][] bitmap = CcittEncoder.SampleBitmap(64, 5);

        (byte[] decoded, string[] codes) = FilterTesting.Run(
            Filter, CcittEncoder.Encode(bitmap, new CcittEncoding(K: -1, EndOfBlock: false)), "<< /K -1 /Columns 64 /Rows 9 /EndOfBlock false >>");

        Assert.Equal(CcittEncoder.Pack(bitmap), decoded);
        Assert.Equal(["FilterDataTruncated"], codes);
    }

    [Fact]
    public void Runs_past_the_last_column_are_cut_there()
    {
        // White 10 then black 10 on an 16-column line.
        string bits = CcittEncoder.RunCodes(10, false) + CcittEncoder.RunCodes(10, true);

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /Columns 16 /Rows 1 >>");

        Assert.Equal<byte>([0xFF, 0b1100_0000], decoded);
        Assert.Equal(["CcittRunTooLong"], codes);
    }

    [Fact]
    public void Uncompressed_mode_is_not_decoded_and_is_reported_as_unsupported()
    {
        // A 2-D line: V0 is not possible on a white reference with a black start, so enter uncompressed mode (0000001111).
        string bits = "0000001111" + "1111";

        (byte[] decoded, string[] codes) = FilterTesting.Run(Filter, Pack(bits), "<< /K -1 /Columns 8 /Rows 1 >>");

        Assert.Equal<byte>([0xFF], decoded);
        Assert.Equal(["CcittUncompressedMode"], codes);
    }

    public static TheoryData<string, string> InvalidParameters => new()
    {
        { "<< /K 0.0 >>", "K" },
        { "<< /K (x) >>", "K" },
        { "<< /EndOfBlock 1 >>", "EndOfBlock" },
        { "<< /BlackIs1 /true >>", "BlackIs1" },
        { "<< /Columns 0 >>", "Columns" },
        { "<< /Rows -3 >>", "Rows" },
        { "<< /DamagedRowsBeforeError 2.0 >>", "DamagedRowsBeforeError" },
    };

    [Theory]
    [MemberData(nameof(InvalidParameters))]
    public void Invalid_parameters_take_their_defaults_with_a_diagnostic(string parameters, string key)
    {
        var context = new FilterContext { Parameters = (CosDictionary)CosObject.Parse(System.Text.Encoding.Latin1.GetBytes(parameters)) };
        Filter.Decode(new byte[] { 0x4D, 0x9A, 0x80 }, new System.Buffers.ArrayBufferWriter<byte>(), context);

        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Equal("DecodeParmsInvalid", diagnostic.Code);
        Assert.Contains(key, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_throws_on_damaged_data()
    {
        DiagnosticException error = Assert.Throws<DiagnosticException>(
            () => FilterTesting.Run(Filter, Pack("0000001000"), "<< /K -1 /Columns 8 >>", PdfReadingMode.Strict));

        Assert.Equal("CcittDataInvalid", error.Diagnostic.Code);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void Damaged_data_in_every_mode_decodes_leniently_to_whole_rows_without_an_exception(string encoding, int columns, int rows, string parameters)
    {
        byte[] encoded = CcittEncoder.Encode(CcittEncoder.SampleBitmap(columns, rows), ParseEncoding(encoding));
        uint state = (uint)(columns * 31 + rows);
        for (int trial = 0; trial < 60; trial++)
        {
            byte[] damaged = (byte[])encoded.Clone();
            for (int flip = 0; flip <= trial % 4; flip++)
            {
                state = (state * 1664525) + 1013904223;
                int bit = (int)((state >> 8) % (uint)(damaged.Length * 8));
                damaged[bit >> 3] ^= (byte)(0x80 >> (bit & 7));
            }

            if (trial % 5 == 4)
            {
                damaged = damaged[..(int)((state >> 4) % (uint)damaged.Length)];
            }

            (byte[] decoded, _) = FilterTesting.Run(Filter, damaged, $"<< /Columns {columns} {parameters} /DamagedRowsBeforeError {trial % 3} >>");

            Assert.Equal(0, decoded.Length % ((columns + 7) / 8));
        }
    }

    private static string Pad(string bits) => bits.PadRight((bits.Length + 7) / 8 * 8, '0');

    private static byte[] Pack(string bits)
    {
        bits = Pad(bits);
        byte[] bytes = new byte[bits.Length / 8];
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        return bytes;
    }

    private static CcittEncoding ParseEncoding(string text)
    {
        string[] parts = text.Split(' ');
        return new CcittEncoding(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), bool.Parse(parts[1]), bool.Parse(parts[2]), bool.Parse(parts[3]));
    }
}
