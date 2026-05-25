namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Compile-time string constants for <see cref="System.ComponentModel.DescriptionAttribute"/>.
/// All MCP-tool descriptions that appear in more than one place live here so the
/// surface visible to LLM agents stays consistent.
/// </summary>
public static class ToolDescriptions
{
    /// <summary>
    /// Canonical phrasing of the <c>network</c> parameter — appears on every read/prepare tool.
    /// Keep in sync with <see cref="StaticBit.Xrpl.Mcp.Core.Options.XrplMcpOptions.DefaultNetworks"/>.
    /// </summary>
    public const string Network =
        "Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.";

    /// <summary>
    /// Canonical phrasing of the <c>ledgerIndex</c> parameter for read-side tools that accept
    /// a ledger selector.
    /// </summary>
    public const string LedgerIndex =
        "Ledger selector: 'validated' (default), 'current', 'closed', or a numeric ledger sequence.";
}
