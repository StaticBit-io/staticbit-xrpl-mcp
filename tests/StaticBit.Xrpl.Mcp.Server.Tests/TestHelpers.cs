using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Server.Services;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

/// <summary>
/// Minimal in-memory IOptionsMonitor for unit tests. Holds a single immutable snapshot;
/// no change notifications. Sufficient for middleware/service constructors that only
/// read <see cref="IOptionsMonitor{T}.CurrentValue"/>.
/// </summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>
/// In-memory IAdminAlerter used by middleware tests. Records every Alert() call
/// so the test can assert on kind / summary / tags.
/// </summary>
internal sealed class RecordingAdminAlerter : IAdminAlerter
{
    public List<RecordedAlert> Alerts { get; } = new List<RecordedAlert>();

    public void Alert(AlertKind kind, string summary, IReadOnlyDictionary<string, string>? tags = null)
    {
        Alerts.Add(new RecordedAlert(kind, summary, tags));
    }
}

internal sealed record RecordedAlert(AlertKind Kind, string Summary, IReadOnlyDictionary<string, string>? Tags);

/// <summary>
/// Hand-rolled TimeProvider that returns a fixed UTC instant, advanced manually
/// by tests via <see cref="Advance"/>. Used by AdminAlerter dedup/rate tests.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset initial)
    {
        _now = initial;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}

/// <summary>
/// Quick logger factory for tests — emits to NullLogger so test output isn't polluted.
/// </summary>
internal static class TestLoggers
{
    public static Microsoft.Extensions.Logging.ILogger<T> Null<T>() => NullLogger<T>.Instance;
}
