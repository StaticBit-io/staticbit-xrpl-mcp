using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using static Xrpl.Models.Common.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// AMM write-flow MCP tools — deposit / withdraw liquidity.
///
/// The pool is always identified by the asset pair (<c>asset1Currency/issuer</c> and
/// <c>asset2Currency/issuer</c>). The deposit/withdraw <em>mode</em> is determined by which
/// amount fields the caller provides — the server sets the appropriate flag automatically.
/// </summary>
[McpServerToolType]
public sealed class AmmTransactionTools
{
    private readonly TransactionPreparer _preparer;

    public AmmTransactionTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_amm_deposit_prepare")]
    [Description("Prepares an UNSIGNED AMMDeposit. Provide the pool's two assets and ONE of: (a) amount only — single-asset deposit; (b) amount + amount2 — two-asset deposit; (c) lpTokenOut only — double-asset deposit by LP amount; (d) amount + lpTokenOut — single-asset deposit by LP amount.")]
    public async Task<PreparedTransaction> AmmDepositPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender address (liquidity provider).")] string account,
        [Description("First pool asset — currency code ('XRP' or 3-char / 40-hex).")] string asset1Currency,
        [Description("First pool asset — issuer. Empty for XRP.")] string? asset1Issuer,
        [Description("Second pool asset — currency code.")] string asset2Currency,
        [Description("Second pool asset — issuer. Empty for XRP.")] string? asset2Issuer,
        [Description("Optional amount to deposit (drops string for XRP, JSON token object for issued currencies).")] string? amount = null,
        [Description("Optional second amount (only with 'amount' for tfTwoAsset mode).")] string? amount2 = null,
        [Description("Optional LP token amount to receive (JSON {value,currency,issuer} where issuer is the AMM account).")] string? lpTokenOut = null,
        [Description("Optional effective price (JSON token object). Used with single-asset deposit.")] string? ePrice = null,
        CancellationToken cancellationToken = default)
    {
        IssuedCurrency asset1 = BuildAsset(asset1Currency, asset1Issuer);
        IssuedCurrency asset2 = BuildAsset(asset2Currency, asset2Issuer);

        AMMDeposit deposit = new AMMDeposit
        {
            Account = account,
            Asset = asset1,
            Asset2 = asset2,
            Amount = amount is null ? null : CurrencyParser.Parse(amount),
            Amount2 = amount2 is null ? null : CurrencyParser.Parse(amount2),
            LPTokenOut = lpTokenOut is null ? null : CurrencyParser.Parse(lpTokenOut),
            EPrice = ePrice is null ? null : CurrencyParser.Parse(ePrice),
            Flags = ChooseDepositFlag(amount, amount2, lpTokenOut, ePrice),
        };

        string summary = $"AMMDeposit from {Truncate(account)} into pool {DescribeAsset(asset1)} / {DescribeAsset(asset2)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), deposit, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_amm_withdraw_prepare")]
    [Description("Prepares an UNSIGNED AMMWithdraw. Provide the pool's two assets and either lpTokenIn, amount(s), or set withdrawAll=true to redeem all LP tokens.")]
    public async Task<PreparedTransaction> AmmWithdrawPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender address (liquidity provider).")] string account,
        [Description("First pool asset — currency code ('XRP' or 3-char / 40-hex).")] string asset1Currency,
        [Description("First pool asset — issuer. Empty for XRP.")] string? asset1Issuer,
        [Description("Second pool asset — currency code.")] string asset2Currency,
        [Description("Second pool asset — issuer. Empty for XRP.")] string? asset2Issuer,
        [Description("Optional amount to withdraw (drops string for XRP, JSON token object for issued currencies).")] string? amount = null,
        [Description("Optional second amount for two-asset withdrawals.")] string? amount2 = null,
        [Description("Optional LP token amount to redeem (JSON token object, issuer = AMM account).")] string? lpTokenIn = null,
        [Description("If true, set tfWithdrawAll to redeem ALL held LP tokens (mutually exclusive with explicit amounts).")] bool withdrawAll = false,
        [Description("If true and an amount is provided, redeem all LP tokens via tfOneAssetWithdrawAll for a single asset.")] bool oneAssetWithdrawAll = false,
        CancellationToken cancellationToken = default)
    {
        IssuedCurrency asset1 = BuildAsset(asset1Currency, asset1Issuer);
        IssuedCurrency asset2 = BuildAsset(asset2Currency, asset2Issuer);

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = account,
            Asset = asset1,
            Asset2 = asset2,
            Amount = amount is null ? null! : CurrencyParser.Parse(amount),
            Amount2 = amount2 is null ? null! : CurrencyParser.Parse(amount2),
            LPTokenIn = lpTokenIn is null ? null! : CurrencyParser.Parse(lpTokenIn),
            Flags = ChooseWithdrawFlag(amount, amount2, lpTokenIn, withdrawAll, oneAssetWithdrawAll),
        };

        string summary = $"AMMWithdraw from {Truncate(account)} of pool {DescribeAsset(asset1)} / {DescribeAsset(asset2)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), withdraw, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    private static AMMDepositFlags? ChooseDepositFlag(string? amount, string? amount2, string? lpTokenOut, string? ePrice)
    {
        if (lpTokenOut is not null && amount is null) return AMMDepositFlags.tfLPToken;
        if (amount is not null && amount2 is not null) return AMMDepositFlags.tfTwoAsset;
        if (amount is not null && lpTokenOut is not null) return AMMDepositFlags.tfOneAssetLPToken;
        if (amount is not null && ePrice is not null) return AMMDepositFlags.tfLimitLPToken;
        if (amount is not null) return AMMDepositFlags.tfSingleAsset;
        return null;
    }

    private static AMMWithdrawFlags? ChooseWithdrawFlag(string? amount, string? amount2, string? lpTokenIn, bool withdrawAll, bool oneAssetWithdrawAll)
    {
        if (withdrawAll) return AMMWithdrawFlags.tfWithdrawAll;
        if (oneAssetWithdrawAll && amount is not null) return AMMWithdrawFlags.tfOneAssetWithdrawAll;
        if (lpTokenIn is not null && amount is null) return AMMWithdrawFlags.tfLPToken;
        if (amount is not null && amount2 is not null) return AMMWithdrawFlags.tfTwoAsset;
        if (amount is not null && lpTokenIn is not null) return AMMWithdrawFlags.tfOneAssetLPToken;
        if (amount is not null) return AMMWithdrawFlags.tfSingleAsset;
        return null;
    }

    private static IssuedCurrency BuildAsset(string currency, string? issuer)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Asset currency is required.", nameof(currency));
        }

        string normalized = currency.Trim();
        bool isXrp = string.Equals(normalized, "XRP", StringComparison.OrdinalIgnoreCase);

        return new IssuedCurrency
        {
            Currency = isXrp ? "XRP" : normalized,
            Issuer = isXrp ? null! : (issuer ?? throw new ArgumentException("Issuer is required for non-XRP assets.", nameof(issuer))),
        };
    }

    private static string DescribeAsset(IssuedCurrency asset)
    {
        return string.Equals(asset.Currency, "XRP", StringComparison.OrdinalIgnoreCase)
            ? "XRP"
            : $"{asset.Currency} ({Truncate(asset.Issuer)})";
    }

    private static string Truncate(string? address)
    {
        if (string.IsNullOrEmpty(address)) return "<null>";
        return address.Length <= 12 ? address : $"{address.AsSpan(0, 6)}...{address.AsSpan(address.Length - 4, 4)}";
    }
}
