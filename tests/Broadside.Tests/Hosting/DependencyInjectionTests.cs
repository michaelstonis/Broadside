using System.Reflection;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Broadside.Tests.Hosting;

/// <summary>
/// A hosted application registers Broadside once and gets the same behavior as the static entry point, with options bound
/// through the options pattern and diagnostics logged to its pipeline (spec #33 user stories 43 to 45; issue #46; ADR 0005).
/// </summary>
public class DependencyInjectionTests
{
    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void The_registered_engine_and_the_static_entry_point_open_documents_identically(string fileName)
    {
        Assert.SkipWhen(WellFormedCorpusTests.NeedsCrossReferenceStreams(fileName), "Needs cross-reference streams (#39).");
        using ServiceProvider services = new ServiceCollection().AddBroadside().BuildServiceProvider();

        using PdfDocument fromContainer = services.GetRequiredService<PdfEngine>().Open(Corpus.Bytes(fileName));
        using PdfDocument fromStatic = PdfDocument.Open(Corpus.Bytes(fileName));

        Assert.Equal(DocumentProjection.Of(fromStatic), DocumentProjection.Of(fromContainer));
        Assert.Equal(fromStatic.Pages.Count, fromContainer.Pages.Count);
        Assert.Empty(fromContainer.Diagnostics);
    }

    [Fact]
    public void The_registered_engine_and_the_static_entry_point_record_the_same_diagnostics_for_a_broken_file()
    {
        using ServiceProvider services = new ServiceCollection().AddBroadside().BuildServiceProvider();

        using PdfDocument fromContainer = services.GetRequiredService<PdfEngine>().Open(Corpus.Bytes("missing-endobj.pdf"));
        using PdfDocument fromStatic = PdfDocument.Open(Corpus.Bytes("missing-endobj.pdf"));

        Assert.Equal(DocumentProjection.Of(fromStatic), DocumentProjection.Of(fromContainer));
        Assert.Equal("MissingEndobj", Assert.Single(fromContainer.Diagnostics).Code);
    }

    [Fact]
    public void The_engine_is_one_singleton_even_when_registered_twice()
    {
        using ServiceProvider services = new ServiceCollection().AddBroadside().AddBroadside().BuildServiceProvider();

        PdfEngine engine = Assert.Single(services.GetServices<PdfEngine>());
        Assert.Same(engine, services.GetRequiredService<PdfEngine>());
    }

    [Fact]
    public void The_fluent_delegate_configures_the_registered_engine()
    {
        using ServiceProvider services = new ServiceCollection().AddBroadside(static options => options.UseStrict()).BuildServiceProvider();

        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(TestPdf.OnePage(string.Empty));

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.Pages.Count);
        Assert.Equal("PageMediaBoxMissing", error.Diagnostic.Code);
    }

    [Fact]
    public void Configuration_delegates_and_bound_configuration_apply_in_registration_order_and_post_configuration_last()
    {
        IConfiguration strictSection = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Broadside:ReadingMode"] = "Strict" })
            .Build()
            .GetSection("Broadside");

        Assert.Equal(PdfReadingMode.Strict, ReadingModeOf(services => services.AddBroadside(static o => o.UseLenient()).Configure<PdfOptions>(strictSection)));
        Assert.Equal(PdfReadingMode.Lenient, ReadingModeOf(services => services.Configure<PdfOptions>(strictSection).AddBroadside(static o => o.UseLenient())));
        Assert.Equal(PdfReadingMode.Lenient, ReadingModeOf(services => services.AddBroadside(static o => o.UseStrict()).AddBroadside(static o => o.UseLenient())));
        Assert.Equal(
            PdfReadingMode.Lenient,
            ReadingModeOf(services => services.PostConfigure<PdfOptions>(static o => o.UseLenient()).AddBroadside(static o => o.UseStrict())));
    }

    [Fact]
    public void The_engine_reads_its_options_once_so_later_changes_do_not_affect_it()
    {
        using ServiceProvider services = new ServiceCollection().AddBroadside().BuildServiceProvider();
        PdfEngine engine = services.GetRequiredService<PdfEngine>();

        services.GetRequiredService<IOptions<PdfOptions>>().Value.UseStrict();
        using PdfDocument document = engine.Open(TestPdf.OnePage(string.Empty));

        Assert.Single(document.Pages);
        Assert.Equal("PageMediaBoxMissing", Assert.Single(document.Diagnostics).Code);
    }

    [Fact]
    public void Engines_in_two_containers_and_the_static_entry_point_do_not_interfere()
    {
        using ServiceProvider strict = new ServiceCollection().AddBroadside(static o => o.UseStrict()).BuildServiceProvider();
        using ServiceProvider lenient = new ServiceCollection().AddBroadside().BuildServiceProvider();
        byte[] file = TestPdf.OnePage(string.Empty);

        using PdfDocument fromStrict = strict.GetRequiredService<PdfEngine>().Open(file);
        using PdfDocument fromLenient = lenient.GetRequiredService<PdfEngine>().Open(file);
        using PdfDocument fromStatic = PdfDocument.Open(file);

        Assert.Throws<DiagnosticException>(() => fromStrict.Pages.Count);
        Assert.Single(fromLenient.Pages);
        Assert.Single(fromStatic.Pages);
    }

    [Fact]
    public void Each_diagnostic_is_also_logged_to_the_hosts_pipeline()
    {
        using var logs = new CapturingLoggerProvider();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddBroadside()
            .BuildServiceProvider();

        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(Corpus.Bytes("missing-endobj.pdf"));
        _ = document.Pages.Count;

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        LogEntry entry = Assert.Single(logs.Entries);
        Assert.Equal("Broadside.PdfDocument", entry.Category);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(new EventId(1, "Diagnostic"), entry.EventId);
        Assert.Equal("MissingEndobj", entry.State["Code"]);
        Assert.Equal(DiagnosticSeverity.Warning, entry.State["Severity"]);
        Assert.Equal(new CosReference(3, 0), entry.State["ObjectReference"]);
        Assert.Equal(diagnostic.Offset, entry.State["Offset"]);
        Assert.Equal(diagnostic.Message, entry.State["Message"]);
        Assert.Contains("MissingEndobj", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_logs_the_deviation_before_throwing_it()
    {
        using var logs = new CapturingLoggerProvider();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddBroadside(static options => options.UseStrict())
            .BuildServiceProvider();
        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(TestPdf.OnePage(string.Empty));

        DiagnosticException error = Assert.Throws<DiagnosticException>(() => document.Pages.Count);

        LogEntry entry = Assert.Single(logs.Entries);
        Assert.Equal("PageMediaBoxMissing", entry.State["Code"]);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(error.Diagnostic.Code, entry.State["Code"]);
    }

    [Fact]
    public void The_fluent_path_logs_through_a_logger_factory_set_on_the_options()
    {
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage(string.Empty), new PdfOptions().WithLoggerFactory(loggerFactory));
        _ = document.Pages.Count;

        LogEntry entry = Assert.Single(logs.Entries);
        Assert.Equal("PageMediaBoxMissing", entry.State["Code"]);
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    [Fact]
    public void A_logger_factory_set_on_the_options_takes_precedence_over_the_containers()
    {
        using var containerLogs = new CapturingLoggerProvider();
        using var optionsLogs = new CapturingLoggerProvider();
        using var optionsLoggerFactory = LoggerFactory.Create(logging => logging.AddProvider(optionsLogs));
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(containerLogs))
            .AddBroadside(options => options.WithLoggerFactory(optionsLoggerFactory))
            .BuildServiceProvider();

        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(TestPdf.OnePage(string.Empty));
        _ = document.Pages.Count;

        Assert.Single(optionsLogs.Entries);
        Assert.Empty(containerLogs.Entries);
    }

    [Fact]
    public void Logging_below_the_enabled_level_records_the_diagnostic_on_the_document_only()
    {
        using var logs = new CapturingLoggerProvider();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Critical))
            .AddBroadside()
            .BuildServiceProvider();

        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(TestPdf.OnePage(string.Empty));

        Assert.Single(document.Pages);
        Assert.Single(document.Diagnostics);
        Assert.Empty(logs.Entries);
    }

    [Fact]
    public void The_core_references_only_the_base_library_and_the_Microsoft_abstractions_for_injection_options_and_logging()
    {
        string[] allowedExtensions =
        [
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Primitives",
        ];

        AssemblyName[] references = typeof(PdfEngine).Assembly.GetReferencedAssemblies();

        Assert.All(references, reference => Assert.True(
            reference.Name is "System" or "netstandard" or "mscorlib"
                || reference.Name!.StartsWith("System.", StringComparison.Ordinal)
                || allowedExtensions.Contains(reference.Name),
            $"The core references {reference.Name}, which ADR 0001 does not allow."));
        Assert.Contains(references, static reference => reference.Name == "Microsoft.Extensions.Logging.Abstractions");
    }

    private static PdfReadingMode ReadingModeOf(Action<IServiceCollection> register)
    {
        var collection = new ServiceCollection();
        register(collection);
        using ServiceProvider services = collection.BuildServiceProvider();
        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(TestPdf.OnePage(string.Empty));
        try
        {
            _ = document.Pages.Count;
            return PdfReadingMode.Lenient;
        }
        catch (DiagnosticException)
        {
            return PdfReadingMode.Strict;
        }
    }
}
