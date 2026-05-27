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
        XrplMcpOptions options = BuildOptions();
        IOptionsMonitor<XrplMcpOptions> monitor = new StaticOptionsMonitor(options);
        NetworkResolver resolver = new NetworkResolver(monitor);
        XrplMcpMetrics metrics = new XrplMcpMetrics();
        return new XrplClientPool(resolver, NullLogger<XrplClientPool>.Instance, new OptionsWrapper<XrplMcpOptions>(options), metrics);
    }

    /// <summary>
    /// Build a TransactionPreparer wired to the testnet pool — used by prepare-smoke tests
    /// that round-trip Autofill + binary encoding but do NOT sign or submit. The Account
    /// passed to each prepare-tool must be a real funded testnet account (Autofill calls
    /// account_info); other addresses (Destination, Holder, Counterparty, etc.) can be
    /// synthetic since rippled only validates them at submit time.
    /// </summary>
    public static (XrplClientPool pool, TransactionPreparer preparer) BuildPreparer()
    {
        XrplMcpOptions options = BuildOptions();
        IOptionsMonitor<XrplMcpOptions> monitor = new StaticOptionsMonitor(options);
        NetworkResolver resolver = new NetworkResolver(monitor);
        XrplMcpMetrics metrics = new XrplMcpMetrics();
        XrplClientPool pool = new XrplClientPool(resolver, NullLogger<XrplClientPool>.Instance, new OptionsWrapper<XrplMcpOptions>(options), metrics);
        TransactionPreparer preparer = new TransactionPreparer(pool, new OptionsWrapper<XrplMcpOptions>(options));
        return (pool, preparer);
    }

    private static XrplMcpOptions BuildOptions()
    {
        string url = Environment.GetEnvironmentVariable("XRPL_TESTNET_WS") ?? DefaultTestnetWs;
        return new XrplMcpOptions
        {
            DefaultNetwork = "testnet",
            Networks = new System.Collections.Generic.Dictionary<string, string>
            {
                ["testnet"] = url,
            },
        };
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
