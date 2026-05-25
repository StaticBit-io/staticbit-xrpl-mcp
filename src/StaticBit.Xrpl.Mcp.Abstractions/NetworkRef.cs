using System;

namespace StaticBit.Xrpl.Mcp.Abstractions;

/// <summary>
/// Identifies the XRPL network for an MCP tool call.
/// </summary>
/// <remarks>
/// The MCP server is stateless and accepts a network reference on every call.
/// Either a well-known network name (<c>mainnet</c>, <c>testnet</c>, <c>devnet</c>)
/// or an explicit WebSocket URL (<c>wss://...</c>) is accepted.
/// </remarks>
public sealed class NetworkRef
{
    public const string Mainnet = "mainnet";
    public const string Testnet = "testnet";
    public const string Devnet = "devnet";

    public NetworkRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Network must be provided.", nameof(value));
        }

        Value = value.Trim();
    }

    /// <summary>
    /// Normalized network identifier — either a known short name or a full URL.
    /// </summary>
    public string Value { get; }

    public bool IsUrl =>
        Value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
        || Value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
        || Value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || Value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Value;

    public static NetworkRef Parse(string value) => new NetworkRef(value);
}
