using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace DKNet.StaticData.App.TestSupport;

/// <summary>
/// Captures log entries written through <see cref="ILogger"/> during a scenario, so a step can assert on
/// observable log output instead of on internal call order. Registered as an additional <see cref="ILoggerProvider"/>
/// in a test host — it does not replace the console/other providers already configured, and the host's log filter
/// rules apply to it as to every other provider. Each entry keeps its structured values and the values of every scope
/// active when it was written (the request's trace id among them), so a test can tell what an entry carries without
/// depending on its exact wording.
/// </summary>
public sealed class TestLogCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<string> Messages => [.. _entries.Select(e => e.Message)];

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    public void Clear() => _entries.Clear();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, TestLogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = new List<KeyValuePair<string, object?>>();
            Collect(state, values);
            owner._scopes.ForEachScope((scope, list) => Collect(scope, list), values);
            owner._entries.Enqueue(new LogEntry(category, logLevel, eventId, formatter(state, exception), exception, values));
        }

        private static void Collect(object? source, List<KeyValuePair<string, object?>> values)
        {
            if (source is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                values.AddRange(pairs.Where(p => p.Key != "{OriginalFormat}"));
            }
            else if (source is not null)
            {
                values.Add(new KeyValuePair<string, object?>("Scope", source));
            }
        }
    }
}

/// <summary>One captured log entry: its message, level, structured values and the values of its active scopes.</summary>
public sealed record LogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> Values)
{
    /// <summary>
    /// Whether the entry names <paramref name="value"/>: one of its values (or scope values) is exactly that text, or
    /// its message contains it.
    /// </summary>
    public bool Names(string value) =>
        Message.Contains(value, StringComparison.Ordinal) ||
        Values.Any(v => string.Equals(Text(v.Value), value, StringComparison.Ordinal));

    /// <summary>
    /// Whether <paramref name="text"/> appears anywhere in the entry — its message, any value, any scope value or its
    /// exception's message. The strict check for "this entry never holds that text".
    /// </summary>
    public bool Holds(string text) =>
        Message.Contains(text, StringComparison.Ordinal) ||
        Values.Any(v => Text(v.Value)?.Contains(text, StringComparison.Ordinal) == true) ||
        Exception?.ToString().Contains(text, StringComparison.Ordinal) == true;

    /// <summary>Every text the entry carries: its message, then each value and scope value as text.</summary>
    public IEnumerable<string> Texts() =>
        new[] { Message }.Concat(Values.Select(v => Text(v.Value)).OfType<string>());

    private static string? Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);
}
