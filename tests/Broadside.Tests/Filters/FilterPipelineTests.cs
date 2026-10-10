using System.Text;
using Broadside.Diagnostics;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// How a document reads <c>Filter</c> and <c>DecodeParms</c> and runs the chain, including the repairs of deviations from Table 5.
/// ISO 32000-2 §7.3.8.2 Table 5, §7.4.1, §7.4.10, §8.9.7 Table 92.
/// </summary>
public class FilterPipelineTests
{
    [Fact]
    public void An_unknown_filter_leaves_the_stream_readable_as_raw_bytes_with_a_diagnostic()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /NoSuchDecode", "raw bytes"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("raw bytes", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterUnsupported"], codes);
    }

    [Fact]
    public void A_chain_stops_at_the_first_filter_it_cannot_run_keeping_what_was_decoded()
    {
        // ASCIIHex decodes to four bytes; no filter is registered under the second name.
        byte[] file = FilterTesting.FileWithStream("/Filter [/ASCIIHexDecode /NoSuchDecode]", "FFD8FFE0>"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal<byte>([0xFF, 0xD8, 0xFF, 0xE0], decoded);
        Assert.Equal(["FilterUnsupported"], codes);
    }

    [Fact]
    public void Strict_mode_throws_for_an_unknown_filter()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /NoSuchDecode", "raw"u8);

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => FilterTesting.Decode(file, new PdfOptions().UseStrict()));

        Assert.Equal("FilterUnsupported", error.Diagnostic.Code);
    }

    [Fact]
    public void Filters_apply_in_array_order_and_the_same_filter_may_appear_twice()
    {
        // "3431>" hex-decodes to "41>", which hex-decodes to "A".
        byte[] file = FilterTesting.FileWithStream("/Filter [/ASCIIHexDecode /ASCIIHexDecode]", "34313E>"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("A", Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Fact]
    public void Decode_parameters_align_with_the_filter_array_and_null_means_defaults()
    {
        byte[] predicted = FilterEncoders.Zlib([2, 1, 2, 2, 1, 1]);
        byte[] hex = Encoding.ASCII.GetBytes(Convert.ToHexString(predicted) + ">");
        byte[] file = FilterTesting.FileWithStream("/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null << /Predictor 12 /Columns 2 >>]", hex);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal<byte>([1, 2, 2, 3], decoded);
        Assert.Empty(codes);
    }

    [Fact]
    public void Indirect_filter_and_parameters_are_resolved()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter 5 0 R /DecodeParms 6 0 R", FilterEncoders.Zlib([2, 1, 2, 2, 1, 1]), "[/FlateDecode]", "[<< /Predictor 12 /Columns 2 >>]");

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal<byte>([1, 2, 2, 3], decoded);
        Assert.Empty(codes);
    }

    public static TheoryData<string, byte[], string> TableFiveDeviations => new()
    {
        // A single filter with a parameter array: the first element is used.
        { "/Filter /FlateDecode /DecodeParms [<< /Predictor 12 /Columns 2 >>]", [1, 2, 2, 3], "DecodeParmsInvalid" },
        // Two filters with one parameter dictionary: the dictionary is ignored, so Flate runs without the predictor.
        { "/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms << /Predictor 12 /Columns 2 >>", [2, 1, 2, 2, 1, 1], "DecodeParmsInvalid" },
        // An abbreviation outside an inline image: read as the full name.
        { "/Filter /Fl /DecodeParms << /Predictor 12 /Columns 2 >>", [1, 2, 2, 3], "FilterAbbreviationNotAllowed" },
        // A parameter array element that is not a dictionary: defaults.
        { "/Filter [/FlateDecode] /DecodeParms [42]", [2, 1, 2, 2, 1, 1], "DecodeParmsInvalid" },
    };

    [Theory]
    [MemberData(nameof(TableFiveDeviations))]
    public void Deviations_from_table_5_are_repaired_with_one_diagnostic(string entries, byte[] expected, string code)
    {
        byte[] flate = FilterEncoders.Zlib([2, 1, 2, 2, 1, 1]);
        byte[] data = entries.Contains("ASCIIHexDecode", StringComparison.Ordinal) ? Encoding.ASCII.GetBytes(Convert.ToHexString(flate) + ">") : flate;
        byte[] file = FilterTesting.FileWithStream(entries, data);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal(expected, decoded);
        Assert.Equal([code], codes);
    }

    // Member data, not inline data: the conformance checker cannot read inline strings that hold a closing bracket.
    public static TheoryData<string> NotNames => new() { "/Filter 42", "/Filter [42]" };

    public static TheoryData<string> EmptyStreams => new() { "/Filter /FlateDecode", "/Filter [/ASCII85Decode /LZWDecode]" };

    public static TheoryData<string> IdentityCryptFilters => new()
    {
        "/Filter /Crypt",
        "/Filter [/Crypt /ASCIIHexDecode] /DecodeParms [<< /Type /CryptFilterDecodeParms /Name /Identity >> null]",
    };

    [Theory]
    [MemberData(nameof(NotNames))]
    public void A_filter_entry_that_is_not_a_name_leaves_the_data_raw_with_a_diagnostic(string entries)
    {
        byte[] file = FilterTesting.FileWithStream(entries, "raw"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("raw", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterInvalid"], codes);
    }

    [Theory]
    [MemberData(nameof(EmptyStreams))]
    public void A_stream_with_no_data_decodes_to_nothing_without_a_diagnostic(string entries)
    {
        byte[] file = FilterTesting.FileWithStream(entries, []);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Empty(decoded);
        Assert.Empty(codes);
    }

    [Theory]
    [MemberData(nameof(IdentityCryptFilters))]
    public void The_identity_crypt_filter_passes_the_data_through(string entries)
    {
        byte[] file = FilterTesting.FileWithStream(entries, entries.Contains("ASCIIHex", StringComparison.Ordinal) ? "41>"u8 : "A"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("A", Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Fact]
    public void A_crypt_filter_the_security_handler_does_not_know_leaves_the_data_encrypted_with_a_diagnostic()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /Crypt /DecodeParms << /Name /StdCF >>", "secret"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("secret", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["CryptFilterUnsupported"], codes);
    }

    [Fact]
    public void A_crypt_filter_that_is_not_first_is_applied_where_it_is_with_a_diagnostic()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter [/ASCIIHexDecode /Crypt]", "41>"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("A", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["CryptFilterNotFirst"], codes);
    }

    [Fact]
    public void Output_beyond_the_decoded_length_limit_is_truncated_with_a_diagnostic()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /FlateDecode", FilterEncoders.Zlib(new byte[100_000]));

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().WithMaxDecodedStreamLength(1000));

        Assert.Equal(new byte[1000], decoded);
        Assert.Equal(["StreamDecodedLengthExceeded"], codes);
    }

    [Fact]
    public void Data_in_an_external_file_is_not_read_and_the_stream_bytes_decode_instead_with_a_diagnostic()
    {
        byte[] file = FilterTesting.FileWithStream("/F (data.bin) /Filter /ASCIIHexDecode", "41>"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file);

        Assert.Equal("A", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["StreamExternalFileUnsupported"], codes);
    }

    [Fact]
    public void Data_in_an_external_file_is_a_legal_unsupported_feature_that_strict_mode_does_not_reject()
    {
        // Table 5 allows F (PDF 1.2); not reading the file is a limit of this reader, not a deviation of the file, so the diagnostic
        // is Information (issue #41's severity rule). The veraPDF agreement check of issue #47 found it thrown in strict mode.
        byte[] file = FilterTesting.FileWithStream("/F (data.bin) /Filter /ASCIIHexDecode", "41>"u8);

        (byte[] decoded, PdfDocument document) = FilterTesting.Decode(file, new PdfOptions().UseStrict());
        using (document)
        {
            Assert.Equal("A", Encoding.Latin1.GetString(decoded));
            Assert.Equal(Broadside.Diagnostics.DiagnosticSeverity.Information, Assert.Single(document.Diagnostics).Severity);
        }
    }

    [Fact]
    public void The_maximum_decoded_length_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfOptions().WithMaxDecodedStreamLength(0));
        Assert.Equal(PdfOptions.DefaultMaxDecodedStreamLength, new PdfOptions().MaxDecodedStreamLength);
    }
}
