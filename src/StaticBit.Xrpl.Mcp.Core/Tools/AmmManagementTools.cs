using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using static Xrpl.Models.Common.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// AMM administrative write-flow MCP tools — AMMCreate / AMMVote / AMMBid / AMMDelete.
/// Liquidity Deposit/Withdraw live in <see cref="AmmTransactionTools"/>.
/// </summary>
[McpServerToolType]
public sealed class AmmManagementTools
{
    private readonly TransactionPreparer _preparer;

    public AmmManagementTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_amm_create_prepare")]
    [Description("Prepares an UNSIGNED AMMCreate. Both amounts must be supplied (this defines the initial pool composition). tradingFeeBasisPoints is in 1/10 bps (0..1000 = 0%..1%). Caller pays the full amounts when this lands.")]
    public async Task<PreparedTransaction> AmmCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender account that funds the AMM and becomes initial LP.")] string account,
        [Description("First asset amount. Drops string for XRP or JSON {value,currency,issuer}.")] string amount,
        [Description("Second asset amount. Same format as 'amount'.")] string amount2,
        [Description("Trading fee in 1/10 bps (0..1000). 1 = 0.001%, 1000 = 1%.")] uint tradingFeeBasisPoints,
        CancellationToken cancellationToken = default)
    {
        if (tradingFeeBasisPoints > 1000)
        {
            throw new ArgumentException("tradingFeeBasisPoints must be between 0 and 1000.", nameof(tradingFeeBasisPoints));
        }

        Currency a1 = CurrencyParser.Parse(amount);
        Currency a2 = CurrencyParser.Parse(amount2);

        AMMCreate tx = new AMMCreate
        {
            Account = account,
            Amount = a1,
            Amount2 = a2,
            TradingFee = tradingFeeBasisPoints,
        };

        string summary = $"AMMCreate by {ToolDisplay.Truncate(account)}: pool {ToolDisplay.DescribeAmount(a1)} / {ToolDisplay.DescribeAmount(a2)}, tradingFee={tradingFeeBasisPoints} (1/10 bps).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_amm_vote_prepare")]
    [Description("Prepares an UNSIGNED AMMVote — LP votes on the desired trading fee of the pool. Vote weight is proportional to held LP tokens. tradingFeeBasisPoints in 1/10 bps (0..1000).")]
    public async Task<PreparedTransaction> AmmVotePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Voter (must hold LP tokens of this pool).")] string account,
        [Description("First pool asset currency code ('XRP' or 3-char/40-hex).")] string asset1Currency,
        [Description("First pool asset issuer (empty for XRP).")] string? asset1Issuer,
        [Description("Second pool asset currency code.")] string asset2Currency,
        [Description("Second pool asset issuer (empty for XRP).")] string? asset2Issuer,
        [Description("Vote: trading fee in 1/10 bps (0..1000).")] uint tradingFeeBasisPoints,
        CancellationToken cancellationToken = default)
    {
        if (tradingFeeBasisPoints > 1000)
        {
            throw new ArgumentException("tradingFeeBasisPoints must be between 0 and 1000.", nameof(tradingFeeBasisPoints));
        }

        IssuedCurrency a1 = ToolDisplay.BuildAsset(asset1Currency, asset1Issuer);
        IssuedCurrency a2 = ToolDisplay.BuildAsset(asset2Currency, asset2Issuer);

        AMMVote tx = new AMMVote
        {
            Account = account,
            Asset = a1,
            Asset2 = a2,
            TradingFee = tradingFeeBasisPoints,
        };

        string summary = $"AMMVote by {ToolDisplay.Truncate(account)} on pool {ToolDisplay.DescribeAsset(a1)} / {ToolDisplay.DescribeAsset(a2)}: vote={tradingFeeBasisPoints}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_amm_bid_prepare")]
    [Description("Prepares an UNSIGNED AMMBid for the AMM auction slot (discounted trading fee for a period). bidMin/bidMax are LP-token amounts as JSON {value,currency,issuer} where issuer = AMM account. authAccountsJson is an optional JSON array of up to 4 r-addresses: [\"r...\",\"r...\"].")]
    public async Task<PreparedTransaction> AmmBidPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Bidder account (must hold LP tokens of this pool).")] string account,
        [Description("First pool asset currency code.")] string asset1Currency,
        [Description("First pool asset issuer (empty for XRP).")] string? asset1Issuer,
        [Description("Second pool asset currency code.")] string asset2Currency,
        [Description("Second pool asset issuer (empty for XRP).")] string? asset2Issuer,
        [Description("Optional minimum bid (LP tokens, JSON {value,currency,issuer}).")] string? bidMin = null,
        [Description("Optional maximum bid (LP tokens, JSON {value,currency,issuer}).")] string? bidMax = null,
        [Description("Optional JSON array of up to 4 r-addresses authorized to trade at the discounted fee.")] string? authAccountsJson = null,
        CancellationToken cancellationToken = default)
    {
        IssuedCurrency a1 = ToolDisplay.BuildAsset(asset1Currency, asset1Issuer);
        IssuedCurrency a2 = ToolDisplay.BuildAsset(asset2Currency, asset2Issuer);

        AMMBid tx = new AMMBid
        {
            Account = account,
            Asset = a1,
            Asset2 = a2,
            BidMin = bidMin is null ? null : CurrencyParser.Parse(bidMin),
            BidMax = bidMax is null ? null : CurrencyParser.Parse(bidMax),
            AuthAccounts = ParseAuthAccounts(authAccountsJson, account)!,
        };

        string summary = $"AMMBid by {ToolDisplay.Truncate(account)} on pool {ToolDisplay.DescribeAsset(a1)} / {ToolDisplay.DescribeAsset(a2)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_amm_delete_prepare")]
    [Description("Prepares an UNSIGNED AMMDelete to fully delete an empty AMM (after AMMWithdraw left residual trust lines). May need to be run multiple times to fully clean up.")]
    public async Task<PreparedTransaction> AmmDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender account.")] string account,
        [Description("First pool asset currency code.")] string asset1Currency,
        [Description("First pool asset issuer (empty for XRP).")] string? asset1Issuer,
        [Description("Second pool asset currency code.")] string asset2Currency,
        [Description("Second pool asset issuer (empty for XRP).")] string? asset2Issuer,
        CancellationToken cancellationToken = default)
    {
        IssuedCurrency a1 = ToolDisplay.BuildAsset(asset1Currency, asset1Issuer);
        IssuedCurrency a2 = ToolDisplay.BuildAsset(asset2Currency, asset2Issuer);

        AMMDelete tx = new AMMDelete
        {
            Account = account,
            Asset = a1,
            Asset2 = a2,
        };

        string summary = $"AMMDelete by {ToolDisplay.Truncate(account)} on pool {ToolDisplay.DescribeAsset(a1)} / {ToolDisplay.DescribeAsset(a2)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static List<AuthAccount>? ParseAuthAccounts(string? json, string senderAccount)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("authAccountsJson must be a JSON array of r-addresses.");
        }

        List<AuthAccount> result = new List<AuthAccount>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("authAccountsJson entries must be r-address strings.");
            }
            string? addr = el.GetString();
            if (string.IsNullOrWhiteSpace(addr))
            {
                throw new ArgumentException("authAccountsJson contains an empty address.");
            }
            if (string.Equals(addr, senderAccount, StringComparison.Ordinal))
            {
                throw new ArgumentException("authAccounts must not include the sender's own address.");
            }
            result.Add(new AuthAccount { Account = addr });
        }

        if (result.Count > 4)
        {
            throw new ArgumentException("authAccounts cannot contain more than 4 entries.");
        }

        return result;
    }
}
