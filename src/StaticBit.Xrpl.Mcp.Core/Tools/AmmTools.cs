using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using static Xrpl.Models.Common.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// AMM (Automated Market Maker) read-only MCP tools.
/// </summary>
[McpServerToolType]
public sealed class AmmTools
{
    private readonly XrplClientPool _pool;

    public AmmTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_amm_info")]
    [Description("Returns state of an AMM instance — pool assets, LP token supply, trading fee, auction slot. Identify by asset pair OR by AMM account.")]
    public async Task<string> AmmInfoAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("First asset of the pool — currency code ('XRP' or 3-char/40-hex token).")] string? asset1Currency = null,
        [Description("Issuer of the first asset. Leave empty for XRP.")] string? asset1Issuer = null,
        [Description("Second asset of the pool — currency code.")] string? asset2Currency = null,
        [Description("Issuer of the second asset. Leave empty for XRP.")] string? asset2Issuer = null,
        [Description("Optional: AMM account address. If set, asset1/asset2 are ignored.")] string? ammAccount = null,
        [Description("Optional: liquidity provider account — when set, returns LP token amount this account holds.")] string? lpAccount = null,
        [Description("Ledger selector: 'validated' (default), 'current', 'closed', or a numeric sequence.")] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AMMInfoRequest request = new AMMInfoRequest
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Account = lpAccount,
            AmmAccount = ammAccount,
        };

        if (ammAccount is null)
        {
            if (string.IsNullOrWhiteSpace(asset1Currency) || string.IsNullOrWhiteSpace(asset2Currency))
            {
                throw new ArgumentException("When ammAccount is not provided, both asset1Currency and asset2Currency are required.");
            }

            request.Asset = BuildAsset(asset1Currency!, asset1Issuer);
            request.Asset2 = BuildAsset(asset2Currency!, asset2Issuer);
        }

        AMMInfoResponse response = await client.AmmInfo(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    private static IssuedCurrency BuildAsset(string currency, string? issuer)
    {
        string normalized = currency.Trim();
        bool isXrp = string.Equals(normalized, "XRP", StringComparison.OrdinalIgnoreCase);

        return new IssuedCurrency
        {
            Currency = isXrp ? "XRP" : normalized,
            Issuer = isXrp ? null! : (issuer ?? throw new ArgumentException("Issuer is required for non-XRP currencies.", nameof(issuer))),
        };
    }
}
