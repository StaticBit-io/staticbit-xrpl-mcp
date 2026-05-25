using System.Collections.Generic;

namespace StaticBit.Xrpl.Mcp.Core.Options;

/// <summary>
/// Configuration for the StaticBit XRPL MCP server.
/// Bound from the <c>StaticBitXrplMcp</c> configuration section.
/// </summary>
public sealed class XrplMcpOptions
{
    public const string SectionName = "StaticBitXrplMcp";

    /// <summary>
    /// Friendly network name → WebSocket URL. The well-known names
    /// <c>mainnet</c>, <c>testnet</c>, <c>devnet</c> are merged on top of defaults
    /// from <see cref="DefaultNetworks"/> if not present here.
    /// </summary>
    public Dictionary<string, string> Networks { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Default network used when the caller did not specify one.
    /// </summary>
    public string DefaultNetwork { get; set; } = "mainnet";

    /// <summary>
    /// Offset added to the current ledger sequence to compute LastLedgerSequence
    /// during <c>*_prepare</c>. Defaults to 20 — the value recommended by ripple docs.
    /// </summary>
    public uint LastLedgerSequenceOffset { get; set; } = 20;

    /// <summary>
    /// Multiplier applied to the auto-filled <c>Fee</c> after Autofill. Use values > 1.0 to
    /// proactively over-pay during open-ledger fee escalation. Set to <c>1.0</c> (default) to
    /// leave the SDK's autofilled fee untouched. Resulting fee is rounded up to the next drop.
    /// </summary>
    public decimal FeeBumpMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// Hard timeout for any single rippled WebSocket request, in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Built-in defaults used when configuration does not override a well-known network.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DefaultNetworks { get; } =
        new Dictionary<string, string>
        {
            ["mainnet"] = "wss://xrplcluster.com",
            ["testnet"] = "wss://s.altnet.rippletest.net:51233",
            ["devnet"] = "wss://s.devnet.rippletest.net:51233",
        };
}
