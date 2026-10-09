using System.Buffers;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Filters;

/// <summary>
/// The filter extension point: filters are registered per engine through the options, never in a static registry, and a
/// registered filter replaces the managed default of its name or adds a new one. ISO 32000-2 §7.4.1; ADR 0001.
/// </summary>
public class FilterExtensionPointTests
{
    [Fact]
    public void A_decorator_registered_through_the_options_replaces_the_default_flate_filter()
    {
        var counting = new CountingFilter(new FlateDecodeFilter());
        var engine = new PdfEngine(new PdfOptions().UseFilter(counting));
        using PdfDocument document = engine.Open(Corpus.Bytes("flate-stream.pdf"));

        ReadOnlyMemory<byte> decoded = document.DecodeStream(CorpusFilterTests.ContentStream(document));

        Assert.Equal(1, counting.Calls);
        Assert.Equal("BT /F1 24 Tf 72 700 Td (FlateDecode) Tj ET", Encoding.Latin1.GetString(decoded.Span));
    }

    [Fact]
    public void Two_engines_in_one_process_keep_their_own_filters()
    {
        var counting = new CountingFilter(new FlateDecodeFilter());
        var withDecorator = new PdfEngine(new PdfOptions().UseFilter(counting));
        var withDefaults = new PdfEngine();

        using (PdfDocument document = withDefaults.Open(Corpus.Bytes("flate-stream.pdf")))
        {
            _ = document.DecodeStream(CorpusFilterTests.ContentStream(document));
        }

        using (PdfDocument document = PdfDocument.Open(Corpus.Bytes("filter-chain.pdf")))
        {
            _ = document.DecodeStream(CorpusFilterTests.ContentStream(document));
        }

        Assert.Equal(0, counting.Calls);
        using PdfDocument decorated = withDecorator.Open(Corpus.Bytes("filter-chain.pdf"));
        _ = decorated.DecodeStream(CorpusFilterTests.ContentStream(decorated));
        Assert.Equal(1, counting.Calls);
    }

    [Fact]
    public void Options_changed_after_the_engine_is_built_do_not_change_its_filters()
    {
        var options = new PdfOptions();
        var engine = new PdfEngine(options);
        var counting = new CountingFilter(new FlateDecodeFilter());
        options.UseFilter(counting);

        using PdfDocument document = engine.Open(Corpus.Bytes("flate-stream.pdf"));
        _ = document.DecodeStream(CorpusFilterTests.ContentStream(document));

        Assert.Equal(0, counting.Calls);
        Assert.Same(counting, Assert.Single(options.Filters));
    }

    [Fact]
    public void A_registered_filter_can_add_a_name_the_defaults_lack()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /ROT13Decode", "Uryyb"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().UseFilter(new Rot13Filter()));

        Assert.Equal("Hello", Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Fact]
    public void A_replacement_flate_filter_still_gets_the_predictor_applied_by_the_pipeline()
    {
        var counting = new CountingFilter(new FlateDecodeFilter());
        byte[] file = FilterTesting.FileWithStream(
            "/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 2 >>",
            FilterEncoders.Zlib([2, 1, 2, 2, 1, 1]));

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().UseFilter(counting));

        Assert.Equal<byte>([1, 2, 2, 3], decoded);
        Assert.Empty(codes);
        Assert.Equal(1, counting.Calls);
    }

    [Fact]
    public void A_filter_sees_its_parameters_and_the_stream_dictionary_and_can_decode_a_referenced_stream()
    {
        // The shape a JBIG2 codec needs: parameters naming a globals stream, decoded through the same pipeline (§7.4.7).
        var probe = new ProbeFilter();
        byte[] file = FilterTesting.FileWithStream(
            "/Filter /ProbeDecode /DecodeParms << /Globals 5 0 R >> /Width 7",
            "x"u8,
            "<< /Length 13 /Filter /ASCIIHexDecode >>\nstream\n676C6F62616C>\nendstream");

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().UseFilter(probe));

        Assert.Equal("global|7", Encoding.Latin1.GetString(decoded));
        Assert.Empty(codes);
    }

    [Fact]
    public void An_exception_thrown_by_a_filter_becomes_a_diagnostic_and_keeps_what_it_wrote()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /ThrowingDecode", "abc"u8);

        (byte[] decoded, string[] codes) = FilterTesting.DecodeWithCodes(file, new PdfOptions().UseFilter(new ThrowingFilter()));

        Assert.Equal("partial", Encoding.Latin1.GetString(decoded));
        Assert.Equal(["FilterFailed"], codes);
    }

    [Fact]
    public void Crypt_and_abbreviations_cannot_be_registered()
    {
        Assert.Throws<ArgumentException>(() => new PdfOptions().UseFilter(new NamedFilter("Crypt")));
        Assert.Throws<ArgumentException>(() => new PdfOptions().UseFilter(new NamedFilter("Fl")));
    }

    [Fact]
    public void A_stand_alone_context_reports_resolves_and_decodes_with_the_default_filters()
    {
        var context = new FilterContext();
        var stream = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("ASCIIHexDecode") }, "414>"u8.ToArray());

        Assert.Equal("A@", Encoding.Latin1.GetString(context.DecodeStream(stream).Span));
        Assert.Same(CosNull.Instance, context.Resolve(new CosReference(1, 0)));
        context.Report("Custom", DiagnosticSeverity.Warning, "Recorded.");
        Assert.Equal("Custom", Assert.Single(context.Diagnostics).Code);

        var strict = new FilterContext { ReadingMode = PdfReadingMode.Strict };
        Assert.Throws<DiagnosticException>(() => strict.Report("Custom", DiagnosticSeverity.Warning, "Thrown."));
    }

    private sealed class CountingFilter(IStreamFilter inner) : IStreamFilter
    {
        private int _calls;

        public int Calls => _calls;

        public CosName Name => inner.Name;

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            Interlocked.Increment(ref _calls);
            inner.Decode(encoded, output, context);
        }
    }

    private sealed class Rot13Filter : IStreamFilter
    {
        public CosName Name { get; } = new("ROT13Decode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            foreach (byte value in encoded.Span)
            {
                byte rotated = value switch
                {
                    >= (byte)'a' and <= (byte)'z' => (byte)('a' + ((value - 'a' + 13) % 26)),
                    >= (byte)'A' and <= (byte)'Z' => (byte)('A' + ((value - 'A' + 13) % 26)),
                    _ => value,
                };
                output.Write([rotated]);
            }
        }
    }

    private sealed class ProbeFilter : IStreamFilter
    {
        public CosName Name { get; } = new("ProbeDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            var globals = (CosStream)context.Resolve(context.Parameters![new CosName("Globals")]);
            var width = (CosInteger)context.Resolve(context.StreamDictionary[new CosName("Width")]);
            output.Write(context.DecodeStream(globals).Span);
            output.Write(Encoding.Latin1.GetBytes($"|{width.Value}"));
        }
    }

    private sealed class ThrowingFilter : IStreamFilter
    {
        public CosName Name { get; } = new("ThrowingDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            output.Write("partial"u8);
            throw new InvalidOperationException("Broken on purpose.");
        }
    }

    private sealed class NamedFilter(string name) : IStreamFilter
    {
        public CosName Name { get; } = new(name);

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context) => output.Write(encoded.Span);
    }
}
