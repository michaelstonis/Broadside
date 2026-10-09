using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Tests.Filters;
using Broadside.Tests.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Broadside.Tests.Document;

/// <summary>
/// The public diagnostic model (ADR 0005): severities, what strict mode throws, and that each repair is recorded once.
/// </summary>
public class DiagnosticModelTests
{
    [Fact]
    public void Severities_are_ordered_from_information_to_error()
    {
        Assert.True(DiagnosticSeverity.Information < DiagnosticSeverity.Warning);
        Assert.True(DiagnosticSeverity.Warning < DiagnosticSeverity.Error);
    }

    [Fact]
    public void Information_is_recorded_and_never_thrown_even_in_strict_mode()
    {
        byte[] file = FilterTesting.FileWithStream("/Filter /NotedDecode", "data"u8);
        using PdfDocument document = PdfDocument.Open(file, new PdfOptions().UseStrict().UseFilter(new NotingFilter()));

        ReadOnlyMemory<byte> decoded = document.DecodeStream((CosStream)document.Resolve(FilterTesting.StreamReference));

        Assert.Equal("data"u8.ToArray(), decoded.ToArray());
        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("FeatureNoted", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void Information_is_logged_at_the_information_level()
    {
        using var logs = new CapturingLoggerProvider();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddBroadside(static options => options.UseFilter(new NotingFilter()))
            .BuildServiceProvider();
        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(FilterTesting.FileWithStream("/Filter /NotedDecode", "data"u8));

        _ = document.DecodeStream((CosStream)document.Resolve(FilterTesting.StreamReference));

        Assert.Equal(LogLevel.Information, Assert.Single(logs.Entries).Level);
    }

    [Fact]
    public void Decoding_a_damaged_stream_twice_records_its_diagnostic_once()
    {
        using PdfDocument document = PdfDocument.Open(FilterTesting.FileWithStream("/Filter /ASCIIHexDecode", "4G1x42>"u8));
        var stream = (CosStream)document.Resolve(FilterTesting.StreamReference);

        _ = document.DecodeStream(stream);
        _ = document.DecodeStream(stream);

        Assert.Equal("FilterDataInvalid", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Strict_mode_throws_a_warning_every_time_it_is_met()
    {
        using PdfDocument document = PdfDocument.Open(
            FilterTesting.FileWithStream("/Filter /ASCIIHexDecode", "4G1x42>"u8),
            new PdfOptions().UseStrict());
        var stream = (CosStream)document.Resolve(FilterTesting.StreamReference);

        Assert.Equal("FilterDataInvalid", Assert.Throws<DiagnosticException>(() => document.DecodeStream(stream)).Diagnostic.Code);
        Assert.Equal("FilterDataInvalid", Assert.Throws<DiagnosticException>(() => document.DecodeStream(stream)).Diagnostic.Code);
        Assert.Single(document.Diagnostics);
    }

    /// <summary>A pass-through filter that notes a legal feature it does not support, the use the Information severity is for.</summary>
    private sealed class NotingFilter : IStreamFilter
    {
        public CosName Name { get; } = new("NotedDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            context.Report("FeatureNoted", DiagnosticSeverity.Information, "The stream uses a feature that is read and preserved but not supported.");
            output.Write(encoded.Span);
        }
    }
}
