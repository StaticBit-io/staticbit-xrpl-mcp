using System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Core.Options;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Shared infrastructure for integration tests — builds an <see cref="XrplClientPool"/>
/// against a real testnet WebSocket without standing up the full ASP.NET host.
///
/// The default endpoint is <c>wss://s.altnet.rippletest.net:51233</c>. Override
/// via the <c>XRPL_TESTNET_WS</c> environment variable to point at a different
/// testnet node (useful for self-hosted rippled or devnet).
/// </summary>
internal static class TestnetFixture
{
    public const string DefaultTestnetWs = "wss://s.altnet.rippletest.net:51233";

    /// <summary>
    /// Well-known funded testnet faucet — used as the read target for smoke tests.
    /// This is a static, publicly documented account that always exists on testnet.
    /// </summary>
    public const string KnownFundedTestnetAccount = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";

    public static XrplClientPool BuildPool()
    {
        string url = Environment.GetEnvironmentVariable("XRPL_TESTNET_WS") ?? DefaultTestnetWs;

        XrplMcpOptions options = new XrplMcpOptions
        {
            DefaultNetwork = "testnet",
            Networks = new System.Collections.Generic.Dictionary<string, string>
            {
                ["testnet"] = url,
            },
        };

        IOptionsMonitor<XrplMcpOptions> monitor = new StaticOptionsMonitor(options);
        NetworkResolver resolver = new NetworkResolver(monitor);
        XrplMcpMetrics metrics = new XrplMcpMetrics();
        return new XrplClientPool(resolver, NullLogger<XrplClientPool>.Instance, new OptionsWrapper<XrplMcpOptions>(options), metrics);
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<XrplMcpOptions>
    {
        public StaticOptionsMonitor(XrplMcpOptions value) { CurrentValue = value; }
        public XrplMcpOptions CurrentValue { get; }
        public XrplMcpOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<XrplMcpOptions, string?> listener) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
