using System;
using System.Threading;
using System.Threading.Tasks;

namespace StaticBit.Xrpl.Mcp.Abstractions;

/// <summary>
/// Pool of XRPL WebSocket clients keyed by <see cref="NetworkRef"/>.
/// Implementations are expected to be thread-safe and to reuse a single connection per network.
/// </summary>
/// <typeparam name="TClient">
/// The concrete client type. Generic to keep the Abstractions package free of the
/// <c>Xrpl</c> NuGet dependency — Core will close it over <c>IXrplClient</c>.
/// </typeparam>
public interface IXrplClientPool<TClient> : IAsyncDisposable
    where TClient : class
{
    /// <summary>
    /// Returns a connected client for the requested network. Connects lazily on first use
    /// and reconnects transparently if the underlying socket dropped.
    /// </summary>
    Task<TClient> GetAsync(NetworkRef network, CancellationToken cancellationToken = default);
}
