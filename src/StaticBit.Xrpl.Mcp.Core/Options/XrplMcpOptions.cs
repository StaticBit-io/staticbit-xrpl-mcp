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
    /// StaticBit's on-ledger platform <c>SourceTag</c> is <c>100010010</c>; this MCP server uses
    /// the sibling tag <c>100010011</c> so volume initiated through it is attributable separately
    /// from the main platform. Default value of <see cref="DefaultSourceTag"/>.
    /// </summary>
    public const uint StaticBitMcpSourceTag = 100010011;

    /// <summary>
    /// <c>SourceTag</c> stamped onto every <c>*_prepare</c>d transaction that does not already
    /// carry one — attributing on-ledger volume initiated through this server. A SourceTag the
    /// caller supplied explicitly (including <c>0</c>) is always preserved; for an XLS-56 Batch
    /// only the outer transaction is tagged, never the caller-signed inner transactions. Defaults
    /// to <see cref="StaticBitMcpSourceTag"/>; set to <c>null</c> to disable stamping entirely.
    /// </summary>
    public uint? DefaultSourceTag { get; set; } = StaticBitMcpSourceTag;

    /// <summary>
    /// Hard timeout for any single rippled WebSocket request, in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// If &gt; 0, evict and re-establish a pooled XRPL WebSocket connection after
    /// it has been alive this many minutes. Reactive — checked on the next
    /// <c>GetAsync</c> call, no background timer.
    /// 0 (default) disables TTL eviction; connections are reused indefinitely
    /// until the socket drops or the pool is disposed.
    /// </summary>
    public int ConnectionTtlMinutes { get; set; }

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
