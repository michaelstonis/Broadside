using Broadside;
using Broadside.Diagnostics;
using Broadside.Samples;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

Sample.Start("Reading: dependency injection and logging");

RegisterAndResolve();
BindFromConfiguration();
LogWithoutAContainer();
Console.WriteLine("OK");

static void RegisterAndResolve()
{
    using var counter = new CountingLoggerProvider();

    #region register
    var services = new ServiceCollection();
    services.AddLogging(logging => logging.AddConsole());
    services.AddBroadside(options => options.WithMaxDecodedLength(256L << 20));
    #endregion
    services.AddLogging(logging => logging.AddProvider(counter));

    using ServiceProvider provider = services.BuildServiceProvider();

    #region resolve
    PdfEngine engine = provider.GetRequiredService<PdfEngine>();
    using PdfDocument document = engine.Open("startxref-wrong.pdf"); // the repair is logged as a warning, and recorded
    #endregion
    Sample.Expect(document.Diagnostics.Count == 1 && counter.Warnings == 1, "the repair is logged");
}

static void BindFromConfiguration()
{
    #region configuration
    IConfiguration configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Broadside:ReadingMode"] = "Strict" })
        .Build();

    var services = new ServiceCollection();
    services.AddSingleton(configuration);
    services.AddBroadside();
    services.AddOptions<PdfOptions>().BindConfiguration("Broadside");
    #endregion

    using ServiceProvider provider = services.BuildServiceProvider();
    PdfEngine engine = provider.GetRequiredService<PdfEngine>();
    bool strict = false;
    try
    {
        engine.Open("startxref-wrong.pdf").Dispose();
    }
    catch (DiagnosticException)
    {
        strict = true;
    }

    Sample.Expect(strict, "the bound engine reads strictly");
}

static void LogWithoutAContainer()
{
    #region logger-factory
    using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    PdfOptions options = new PdfOptions().UseLoggerFactory(loggerFactory);
    using PdfDocument document = PdfDocument.Open("startxref-wrong.pdf", options);
    #endregion
}

/// <summary>Counts the warnings logged, so the sample can check that the repair reached the log.</summary>
internal sealed class CountingLoggerProvider : ILoggerProvider
{
    private int _warnings;

    public int Warnings => _warnings;

    public ILogger CreateLogger(string categoryName) =>
        categoryName == "Broadside.PdfDocument" ? new Counter(this) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Counter(CountingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref owner._warnings);
            }
        }
    }
}
