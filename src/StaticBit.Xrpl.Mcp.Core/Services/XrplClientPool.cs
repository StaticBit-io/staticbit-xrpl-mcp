using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StaticBit.Xrpl.Mcp.Abstractions;
using Xrpl.Client;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Thread-safe pool of <see cref="XrplClient"/> instances keyed by the WebSocket URL
/// resolved from a <see cref="NetworkRef"/>.
/// Connects lazily on first request and transparently reconnects when the socket dropped.
/// </summary>
public sealed class XrplClientPool : IXrplClientPool<IXrplClient>
{
    private readonly NetworkResolver _resolver;
    private readonly ILogger<XrplClientPool> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<XrplClient>>> _clients =
        new ConcurrentDictionary<string, Lazy<Task<XrplClient>>>(StringComparer.Ordinal);
    private int _disposed;

    public XrplClientPool(NetworkResolver resolver, ILogger<XrplClientPool> logger)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IXrplClient> GetAsync(NetworkRef network, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        string url = _resolver.Resolve(network);

        while (true)
        {
            Lazy<Task<XrplClient>> lazy = _clients.GetOrAdd(
                url,
                static key => new Lazy<Task<XrplClient>>(() => CreateAndConnectAsync(key), LazyThreadSafetyMode.ExecutionAndPublication));

            XrplClient client;
            try
            {
                client = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to XRPL node {Url}; evicting from pool.", url);
                _clients.TryRemove(new System.Collections.Generic.KeyValuePair<string, Lazy<Task<XrplClient>>>(url, lazy));
                throw;
            }

            if (client.IsConnected())
            {
                return client;
            }

            _logger.LogInformation("XRPL connection to {Url} is no longer alive — reconnecting.", url);
            _clients.TryRemove(new System.Collections.Generic.KeyValuePair<string, Lazy<Task<XrplClient>>>(url, lazy));
            try
            {
                await client.DisconnectAndWaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Disconnect of stale client to {Url} threw — ignoring.", url);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (System.Collections.Generic.KeyValuePair<string, Lazy<Task<XrplClient>>> kvp in _clients)
        {
            try
            {
                XrplClient client = await kvp.Value.Value.ConfigureAwait(false);
                await client.DisconnectAndWaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while disconnecting client to {Url} on dispose.", kvp.Key);
            }
        }

        _clients.Clear();
    }

    private static async Task<XrplClient> CreateAndConnectAsync(string url)
    {
        XrplClient client = new XrplClient(url);
        await client.Connect(CancellationToken.None).ConfigureAwait(false);
        return client;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(XrplClientPool));
        }
    }
}
