using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Broadside.Tests.Hosting;

/// <summary>One log entry as a host's logging pipeline receives it, with the structured values the message template names.</summary>
internal sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message, IReadOnlyDictionary<string, object?> State);

/// <summary>A logger provider that keeps every entry it is given, so a test can assert what reached the pipeline.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = new Dictionary<string, object?>();
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (KeyValuePair<string, object?> pair in pairs)
                {
                    values[pair.Key] = pair.Value;
                }
            }

            entries.Enqueue(new LogEntry(category, logLevel, eventId, formatter(state, exception), values));
        }
    }
}
