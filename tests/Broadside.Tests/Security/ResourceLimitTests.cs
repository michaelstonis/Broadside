using System.Buffers;
using System.Text;
using Broadside.Content;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Tests.Colors;
using Broadside.Tests.Content;
using Broadside.Tests.Document;
using Broadside.Tests.Filters;
using Broadside.TestSupport;

namespace Broadside.Tests.Security;

/// <summary>
/// The resource limits of the threat model (docs/threat-model.md) that no other test reached: each test builds a hostile file that
/// runs into one limit through the public API, and checks that reading stops there with the limit's diagnostic instead of
/// recursing, allocating or decoding without bound.
/// </summary>
public sealed class ResourceLimitTests
{
    public static TheoryData<string> ExpandingFilters => ["/FlateDecode", "/LZWDecode", "/RunLengthDecode", "/ASCIIHexDecode", "/ASCII85Decode"];

    [Theory]
    [MemberData(nameof(ExpandingFilters))]
    public void Every_expanding_filter_stops_at_the_decoded_length_limit(string filter)
    {
        // ISO 32000-2 §7.4: a few bytes of compressed data may decode to far more; MaxDecodedLength bounds every filter, not only Flate.
        byte[] data = new byte[100_000];
        byte[] encoded = filter switch
        {
            "/FlateDecode" => FilterEncoders.Zlib(data),
            "/LZWDecode" => FilterEncoders.LzwEncode(data),
            "/RunLengthDecode" => FilterEncoders.RunLengthEncode(data),
            "/ASCIIHexDecode" => Encoding.Latin1.GetBytes(Convert.ToHexString(data) + ">"),
            _ => FilterEncoders.Ascii85Encode(data),
        };
        byte[] file = FilterTesting.FileWithStream($"/Filter {filter}", encoded);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().WithMaxDecodedLength(1000));

        Assert.Equal(new byte[1000], decoded);
        Assert.Equal(["StreamDecodedLengthExceeded"], codes);
    }

    [Fact]
    public void Operands_beyond_the_operand_limit_are_dropped_with_a_diagnostic()
    {
        // ISO 32000-2 §7.8.2: an operator takes a handful of operands; a stream of millions before one operator must not be held.
        string content = string.Join(' ', Enumerable.Repeat("1", 100)) + " 0 0 m 1 1 l S";
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(content));

        document.Pages[0].ProcessContent(new RecordingProcessor(), new ContentOptions().WithMaxOperands(16));

        Assert.Contains("ContentOperandOverflow", ContentPdf.Codes(document.Diagnostics));
    }

    [Fact]
    public void Colour_spaces_nested_deeper_than_the_limit_stop_with_a_diagnostic()
    {
        // ISO 32000-2 §8.6: Indexed, Separation, DeviceN and Pattern spaces name other spaces; the nesting is cut at a fixed depth.
        string space = "/DeviceGray";
        for (int level = 0; level < 12; level++)
        {
            space = $"[/Indexed {space} 0 <00>]";
        }

        byte[] file = ColorTesting.Page($"<< /ColorSpace << /X {space} >> >>", "/X cs 0 0 10 10 re f");

        (List<Paint> paints, string[] codes) = ColorTesting.Run(file);

        Assert.Single(paints);
        Assert.Contains("ColorSpaceTooDeep", codes);
    }

    [Fact]
    public void A_DeviceN_space_with_more_colourants_than_a_colour_holds_is_painted_through_its_alternate()
    {
        // ISO 32000-2 §8.6.6.5 sets no colourant limit (Table 73 notes 32 as an implementation limit); colours hold at most 32.
        string names = string.Join(' ', Enumerable.Range(0, 33).Select(static index => $"/C{index}"));
        string domain = string.Join(' ', Enumerable.Repeat("0 1", 33));
        string pops = string.Join(' ', Enumerable.Repeat("pop", 32));
        string function = $"{{{pops}}}";
        byte[] file = ColorTesting.Page(
            "<< /ColorSpace << /X [/DeviceN [" + names + "] /DeviceGray 5 0 R] >> >>",
            "/X cs 0 0 10 10 re f",
            $"<< /FunctionType 4 /Domain [{domain}] /Range [0 1] /Length {function.Length} >>\nstream\n{function}\nendstream");

        (List<Paint> paints, string[] codes) = ColorTesting.Run(file);

        Assert.Single(paints);
        Assert.Contains("ColorComponentLimitExceeded", codes);
    }

    [Fact]
    public void A_page_tree_deeper_than_the_limit_is_cut_with_a_diagnostic()
    {
        // ISO 32000-2 §7.7.3.2: intermediate nodes may nest; a chain of hundreds is cut rather than walked without bound.
        const int Levels = 300;
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>" };
        for (int level = 0; level < Levels; level++)
        {
            int number = level + 2;
            string parent = level == 0 ? string.Empty : $"/Parent {number - 1} 0 R";
            objects.Add($"<< /Type /Pages /Kids [{number + 1} 0 R] /Count 1 {parent} >>");
        }

        objects.Add($"<< /Type /Page /Parent {Levels + 1} 0 R /MediaBox [0 0 612 792] >>");

        using PdfDocument document = PdfDocument.Open(new TestPdf().Build([.. objects]));

        Assert.Empty(document.Pages);
        Assert.Contains("PageTreeTooDeep", document.Diagnostics.Select(static diagnostic => (string)diagnostic.Code));
    }

    [Fact]
    public void Loads_that_need_other_loads_more_than_the_limit_deep_read_as_null_with_a_diagnostic()
    {
        // ISO 32000-2 §7.3.8.2: a stream's Length may be an indirect object; each Length here is another stream, 40 deep.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
        };
        for (int index = 0; index < 40; index++)
        {
            objects.Add($"<< /Length {objects.Count + 2} 0 R >>\nstream\nx\nendstream");
        }

        objects.Add("1");

        using PdfDocument document = PdfDocument.Open(new TestPdf().Build([.. objects]));
        _ = document.Resolve(new CosReference(4, 0));

        Assert.Contains("ReferenceChainTooDeep", document.Diagnostics.Select(static diagnostic => (string)diagnostic.Code));
    }

    [Fact]
    public void Streams_whose_lengths_refer_to_each_other_are_read_without_recursing()
    {
        // ISO 32000-2 §7.3.8.2: loading object 4 needs its Length, object 5, whose Length is object 4 again.
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            "<< /Length 5 0 R >>\nstream\nabc\nendstream",
            "<< /Length 4 0 R >>\nstream\nabc\nendstream");

        using PdfDocument document = PdfDocument.Open(file);
        var stream = Assert.IsType<CosStream>(document.Resolve(new CosReference(4, 0)));

        Assert.Equal("abc", Encoding.Latin1.GetString(document.DecodeStream(stream).Span));
        Assert.Contains("StreamLengthInvalid", document.Diagnostics.Select(static diagnostic => (string)diagnostic.Code));
    }

    [Fact]
    public void Streams_that_need_other_streams_decoded_more_than_the_limit_deep_decode_to_nothing_with_a_diagnostic()
    {
        // ISO 32000-2 §7.4: a filter may decode another stream of the document (FilterContext.DecodeStream, as JBIG2Globals does);
        // a third-party filter that follows a chain of twelve such streams is cut at the nesting limit.
        var more = new List<string>();
        for (int index = 0; index < 12; index++)
        {
            more.Add($"<< /Length 1 /Filter /ChainDecode /DecodeParms << /Next {6 + index} 0 R >> >>\nstream\nx\nendstream");
        }

        more.Add("<< /Length 1 >>\nstream\ny\nendstream");
        byte[] file = FilterTesting.FileWithStream("/Filter /ChainDecode /DecodeParms << /Next 5 0 R >>", "x"u8, [.. more]);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().UseFilter(new ChainFilter()));

        Assert.Empty(decoded);
        Assert.Contains("StreamDecodingTooDeep", codes);
    }

    [Fact]
    public void A_JPEG_2000_codestream_with_more_tiles_than_the_limit_is_not_decoded()
    {
        // ITU-T T.800 A.5.1: SIZ gives the image and tile sizes; a 300 x 300 image of 1 x 1 tiles declares 90,000 tiles.
        byte[] codestream = (byte[])JpxSamples.WorkedExample.Clone();
        WriteBigEndian(codestream, 8, 300);   // Xsiz
        WriteBigEndian(codestream, 12, 300);  // Ysiz
        WriteBigEndian(codestream, 24, 1);    // XTsiz
        WriteBigEndian(codestream, 28, 1);    // YTsiz

        (byte[] decoded, string[] codes) = JpxTesting.Decode(codestream);

        Assert.Empty(decoded);
        Assert.Contains("JpxLimitExceeded", codes);
    }

    /// <summary>A third-party filter whose output is the decoded data of the stream its <c>Next</c> parameter names.</summary>
    private sealed class ChainFilter : IStreamFilter
    {
        public CosName Name { get; } = new("ChainDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            if (context.Parameters is { } parameters && parameters.TryGetValue(new CosName("Next"), out CosObject? next) && context.Resolve(next) is CosStream stream)
            {
                output.Write(context.DecodeStream(stream).Span);
            }
            else
            {
                output.Write(encoded.Span);
            }
        }
    }

    private static void WriteBigEndian(byte[] data, int offset, int value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset), value);
}
