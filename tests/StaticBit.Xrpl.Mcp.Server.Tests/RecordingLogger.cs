using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

internal sealed record RecordedLogEntry(LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// Simple in-memory ILogger that records all entries for unit-test assertions.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<RecordedLogEntry> Entries { get; } = new List<RecordedLogEntry>();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new RecordedLogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new NoopScope();
        public void Dispose() { }
    }
}
