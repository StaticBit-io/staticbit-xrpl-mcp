using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Options;
using Xrpl.Client;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Thread-safe pool of <see cref="XrplClient"/> instances keyed by the WebSocket URL
/// resolved from a <see cref="NetworkRef"/>. Connects lazily on first request,
/// transparently reconnects when the socket dropped, and (if
/// <see cref="XrplMcpOptions.ConnectionTtlMinutes"/> &gt; 0) evicts connections
/// older than TTL on the next <c>GetAsync</c>. Emits metrics through
/// <see cref="XrplMcpMetrics"/>.
/// </summary>
public sealed class XrplClientPool : IXrplClientPool<IXrplClient>
{
    private readonly NetworkResolver _resolver;
    private readonly ILogger<XrplClientPool> _logger;
    private readonly XrplMcpOptions _options;
    private readonly XrplMcpMetrics _metrics;
    private readonly ConcurrentDictionary<string, PoolEntry> _entries =
        new ConcurrentDictionary<string, PoolEntry>(StringComparer.Ordinal);
    private int _disposed;

    public XrplClientPool(
        NetworkResolver resolver,
        ILogger<XrplClientPool> logger,
        IOptions<XrplMcpOptions> options,
        XrplMcpMetrics metrics)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

        // ObservableGauge captured lazily — emits whatever the dictionary count is at the moment
        // the metrics collector polls. Cheap, no background timer.
        _metrics.Meter.CreateObservableGauge<long>(
            name: "xrpl_mcp_pool_connections",
            observeValue: () => _entries.Count,
            unit: "1",
            description: "Open XRPL WebSocket connections currently held by the pool (total across networks).");
    }

    public async Task<IXrplClient> GetAsync(NetworkRef network, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        string url = _resolver.Resolve(network);
        TimeSpan? ttl = _options.ConnectionTtlMinutes > 0
            ? TimeSpan.FromMinutes(_options.ConnectionTtlMinutes)
            : null;

        while (true)
        {
            PoolEntry entry = _entries.GetOrAdd(url, key =>
            {
                _metrics.PoolReconnects.Add(1,
                    new KeyValuePair<string, object?>("network", key),
                    new KeyValuePair<string, object?>("reason", "cold"));
                return new PoolEntry(this, key);
            });

            XrplClient client;
            try
            {
                client = await entry.Connection.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to XRPL node {Url}; evicting from pool.", url);
                _metrics.PoolReconnects.Add(1,
                    new KeyValuePair<string, object?>("network", url),
                    new KeyValuePair<string, object?>("reason", "error"));
                EvictEntry(url, entry);
                throw;
            }

            // TTL eviction is reactive — a stale entry is replaced on its next use,
            // no background timer needed.
            if (ttl.HasValue && DateTimeOffset.UtcNow - entry.CreatedAt > ttl.Value)
            {
                _logger.LogInformation(
                    "Evicting pooled XRPL connection to {Url} (age {Age}, TTL {Ttl}).",
                    url, DateTimeOffset.UtcNow - entry.CreatedAt, ttl.Value);
                _metrics.PoolReconnects.Add(1,
                    new KeyValuePair<string, object?>("network", url),
                    new KeyValuePair<string, object?>("reason", "ttl"));
                await DisconnectAndEvict(url, entry, client, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (client.IsConnected())
            {
                return client;
            }

            _logger.LogInformation("XRPL connection to {Url} is no longer alive — reconnecting.", url);
            _metrics.PoolReconnects.Add(1,
                new KeyValuePair<string, object?>("network", url),
                new KeyValuePair<string, object?>("reason", "dropped"));
            await DisconnectAndEvict(url, entry, client, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (KeyValuePair<string, PoolEntry> kvp in _entries)
        {
            try
            {
                XrplClient client = await kvp.Value.Connection.ConfigureAwait(false);
                await client.DisconnectAndWaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting client to {Url} on dispose.", kvp.Key);
            }
        }

        _entries.Clear();
        _metrics.Meter.Dispose();
    }

    private async Task DisconnectAndEvict(string url, PoolEntry entry, XrplClient client, CancellationToken cancellationToken)
    {
        EvictEntry(url, entry);
        try
        {
            await client.DisconnectAndWaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Disconnect of evicted client to {Url} threw — ignoring.", url);
        }
    }

    private void EvictEntry(string url, PoolEntry entry)
    {
        // Only evict if the exact same entry is still in the dictionary — protects
        // against racing with a fresh entry some other thread just inserted.
        _entries.TryRemove(new KeyValuePair<string, PoolEntry>(url, entry));
    }

    private async Task<XrplClient> CreateAndConnectAsync(string url)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        XrplClient client = new XrplClient(url);
        await client.Connect(CancellationToken.None).ConfigureAwait(false);
        double seconds = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        _metrics.PoolConnectDurationSeconds.Record(seconds,
            new KeyValuePair<string, object?>("network", url));
        return client;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(XrplClientPool));
        }
    }

    /// <summary>
    /// One pooled connection — wraps the lazy task with its creation timestamp so
    /// the TTL check is O(1) without an extra dictionary.
    /// </summary>
    private sealed class PoolEntry
    {
        public DateTimeOffset CreatedAt { get; }
        public Task<XrplClient> Connection { get; }

        public PoolEntry(XrplClientPool owner, string url)
        {
            CreatedAt = DateTimeOffset.UtcNow;
            Connection = Task.Run(() => owner.CreateAndConnectAsync(url));
        }
    }
}
