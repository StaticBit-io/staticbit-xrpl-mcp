using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Ledger-scoped read-only MCP tools: server status, current fee, ledger snapshot, single-tx lookup.
/// All tools accept a <c>network</c> parameter on every call — the server is stateless.
/// </summary>
[McpServerToolType]
public sealed class LedgerTools
{
    private readonly XrplClientPool _pool;

    public LedgerTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_server_info")]
    [Description("Returns rippled node status: version, validated ledger range, build info, peers. Read-only.")]
    public async Task<string> ServerInfoAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        ServerInfo response = await client.ServerInfo(new ServerInfoRequest(), cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_fee")]
    [Description("Returns current open-ledger transaction cost (drops). Use this to size Fee before submitting.")]
    public async Task<string> FeeAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        Fee response = await client.Fee(cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_ledger")]
    [Description("Returns a ledger header (and optionally its transactions) for the specified ledger.")]
    public async Task<string> LedgerAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Ledger selector: 'validated' (default), 'current', 'closed', or a numeric ledger sequence.")] string? ledgerIndex = null,
        [Description("If true, include the transaction list of the ledger.")] bool transactions = false,
        [Description("If true, expand the transactions to full JSON instead of hashes only.")] bool expand = false,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        LedgerRequest request = new LedgerRequest
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Transactions = transactions,
            Expand = expand,
        };

        LOLedger response = await client.Ledger(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_tx_lookup")]
    [Description("Looks up a single transaction by hash. Returns engine result, metadata and validated flag.")]
    public async Task<string> TxLookupAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("64-char hex transaction hash.")] string txHash,
        [Description("If true, return the binary blob instead of expanded JSON.")] bool binary = false,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        TxRequest request = new TxRequest(txHash)
        {
            Binary = binary,
        };

        TransactionResponse response = await client.Tx(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }
}
