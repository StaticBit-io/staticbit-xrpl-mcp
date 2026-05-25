using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Issuer-side MCP tools — Clawback and TrustLine freeze/deep-freeze.
/// Freeze helpers wrap a TrustSet with the appropriate Freeze flags.
/// </summary>
[McpServerToolType]
public sealed class IssuerTools
{
    private readonly TransactionPreparer _preparer;

    public IssuerTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_clawback_prepare")]
    [Description("Prepares an UNSIGNED Clawback. The issuer claws back its own tokens from a holder. amount.issuer must be the HOLDER (the account to claw FROM), NOT the issuer (the sender). amount = JSON {value,currency,issuer}. value=0 is rejected; if value > holder's balance, the entire balance is clawed. Requires asfAllowTrustLineClawback enabled on the issuer (set BEFORE issuing any tokens).")]
    public async Task<PreparedTransaction> ClawbackPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender = issuer (the account that originally issued these tokens).")] string account,
        [Description("Amount JSON: {value,currency,issuer} where 'issuer' is the HOLDER to claw FROM.")] string amount,
        [Description("Optional Holder (r-address). Required only when clawing back MPT tokens; must be omitted for trust-line tokens.")] string? holder = null,
        CancellationToken cancellationToken = default)
    {
        Currency parsed = CurrencyParser.Parse(amount);
        if (string.IsNullOrEmpty(parsed.Issuer) && string.IsNullOrEmpty(holder))
        {
            throw new ArgumentException("Clawback amount must include 'issuer' = holder address (for trust-line tokens), or specify 'holder' (for MPT).");
        }
        if (!string.IsNullOrEmpty(parsed.Issuer) && string.Equals(parsed.Issuer, account, StringComparison.Ordinal))
        {
            throw new ArgumentException("amount.issuer must be the HOLDER, not the issuer (sender).");
        }

        ClawBack tx = new ClawBack
        {
            Account = account,
            Amount = parsed,
            Holder = holder,
        };

        string from = !string.IsNullOrEmpty(holder) ? ToolDisplay.Truncate(holder) : ToolDisplay.Truncate(parsed.Issuer);
        string summary = $"Clawback by issuer {ToolDisplay.Truncate(account)} from {from}: {ToolDisplay.DescribeAmount(parsed)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_trustline_freeze_prepare")]
    [Description("Prepares an UNSIGNED TrustSet that freezes or unfreezes a specific trust line — wrapper over TrustSet with tfSetFreeze/tfClearFreeze (or tfSetDeepFreeze/tfClearDeepFreeze when deep=true). Only meaningful when sender is the token issuer. limitValue defaults to '0' (do not change the trust limit) — pass a positive value only if you also want to adjust the limit.")]
    public async Task<PreparedTransaction> TrustlineFreezePrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender (issuer) freezing the trust line.")] string account,
        [Description("Token currency code (3-char or 40-hex).")] string currency,
        [Description("Counterparty (holder) address — the side being frozen.")] string holder,
        [Description("True = freeze; false = unfreeze.")] bool freeze,
        [Description("If true, use DeepFreeze flags (tfSetDeepFreeze / tfClearDeepFreeze) instead of regular Freeze.")] bool deep = false,
        [Description("Limit value to set on the trust line. Default '0' — keep current behavior (just toggle the flag).")] string limitValue = "0",
        CancellationToken cancellationToken = default)
    {
        TrustSetFlags flags = (freeze, deep) switch
        {
            (true, false) => TrustSetFlags.tfSetFreeze,
            (false, false) => TrustSetFlags.tfClearFreeze,
            (true, true) => TrustSetFlags.tfSetDeepFreeze,
            (false, true) => TrustSetFlags.tfClearDeepFreeze,
        };

        Currency limit = new Currency
        {
            CurrencyCode = currency,
            Issuer = holder,
            Value = limitValue,
        };

        TrustSet tx = new TrustSet
        {
            Account = account,
            LimitAmount = limit,
            Flags = flags,
        };

        string action = (freeze, deep) switch
        {
            (true, false) => "FREEZE",
            (false, false) => "UNFREEZE",
            (true, true) => "DEEP-FREEZE",
            (false, true) => "CLEAR-DEEP-FREEZE",
        };
        string summary = $"TrustSet/{action} by issuer {ToolDisplay.Truncate(account)}: {currency} held by {ToolDisplay.Truncate(holder)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }
}
