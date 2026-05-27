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
/// XLS-66 LoanBroker write-flow MCP tools.
///
/// A LoanBroker sits on top of a Vault and manages a pool of loans. It collects
/// cover (first-loss) capital, defines cover-ratio thresholds and a management
/// fee, and acts as the counterparty for Loan ledger objects. The submitting
/// account must own the underlying vault. Cover is held in the broker's
/// pseudo-account and is liquidated when a borrower defaults.
/// </summary>
[McpServerToolType]
public sealed class LoanBrokerTools
{
    private readonly TransactionPreparer _preparer;

    public LoanBrokerTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_loan_broker_set_prepare")]
    [Description("Prepares an UNSIGNED LoanBrokerSet (XLS-66). Creates a new LoanBroker (omit loanBrokerId) or modifies an existing one (provide its 64-hex LoanBrokerID). The submitting account must own the vault identified by vaultId. CoverRateMinimum / CoverRateLiquidation are in 1/100th bp (0..100000 = 0..100%). ManagementFeeRate is in 1/10th bp (0..10000 = 0..100%). DebtMaximum is an STNumber decimal string (omit or '0' for no cap). 'dataHex' is ≤512 hex chars (= 256 bytes).")]
    public async Task<PreparedTransaction> LoanBrokerSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Vault owner account (also becomes LoanBroker owner).")] string account,
        [Description("64-hex VaultID of the underlying Vault. Required even when modifying — must match the broker's vault.")] string vaultId,
        [Description("Optional 64-hex LoanBrokerID — omit to create a new broker.")] string? loanBrokerId = null,
        [Description("Minimum cover rate (1/100th bp, 0..100000). Defines the lower-bound coverage required from cover capital.")] uint? coverRateMinimum = null,
        [Description("Liquidation cover rate (1/100th bp, 0..100000). When coverage drops to this threshold, defaults trigger liquidation.")] uint? coverRateLiquidation = null,
        [Description("Management fee rate (1/10th bp, 0..10000). Charged on each loan payment to the broker.")] uint? managementFeeRate = null,
        [Description("Optional debt ceiling (STNumber decimal string). Omit or '0' = no limit.")] string? debtMaximum = null,
        [Description("Optional hex blob (≤512 hex chars = 256 bytes).")] string? dataHex = null,
        CancellationToken cancellationToken = default)
    {
        VaultTools.ValidateVaultId(vaultId);
        if (!string.IsNullOrEmpty(loanBrokerId))
        {
            ValidateHash256(loanBrokerId, nameof(loanBrokerId));
        }
        if (coverRateMinimum.HasValue && coverRateMinimum.Value > 100000)
        {
            throw new ArgumentException("coverRateMinimum must be 0..100000 (1/100th bp).", nameof(coverRateMinimum));
        }
        if (coverRateLiquidation.HasValue && coverRateLiquidation.Value > 100000)
        {
            throw new ArgumentException("coverRateLiquidation must be 0..100000 (1/100th bp).", nameof(coverRateLiquidation));
        }
        if (coverRateMinimum.HasValue && coverRateLiquidation.HasValue
            && coverRateLiquidation.Value > coverRateMinimum.Value)
        {
            throw new ArgumentException("coverRateLiquidation must be ≤ coverRateMinimum (liquidation triggers BELOW the minimum).");
        }
        if (managementFeeRate.HasValue && managementFeeRate.Value > 10000)
        {
            throw new ArgumentException("managementFeeRate must be 0..10000 (1/10th bp).", nameof(managementFeeRate));
        }
        if (!string.IsNullOrEmpty(dataHex))
        {
            ValidateHex(dataHex, nameof(dataHex));
            if (dataHex.Length > 512 || (dataHex.Length & 1) != 0)
            {
                throw new ArgumentException("dataHex must be even-length ≤512 hex chars (= 256 bytes).", nameof(dataHex));
            }
        }

        LoanBrokerSet tx = new LoanBrokerSet
        {
            Account = account,
            VaultID = vaultId.ToUpperInvariant(),
            LoanBrokerID = string.IsNullOrEmpty(loanBrokerId) ? null : loanBrokerId.ToUpperInvariant(),
            CoverRateMinimum = coverRateMinimum,
            CoverRateLiquidation = coverRateLiquidation,
            ManagementFeeRate = managementFeeRate.HasValue ? (ushort?)managementFeeRate.Value : null,
            DebtMaximum = string.IsNullOrEmpty(debtMaximum) ? null : debtMaximum,
            Data = string.IsNullOrEmpty(dataHex) ? null : dataHex.ToUpperInvariant(),
        };

        string mode = string.IsNullOrEmpty(loanBrokerId) ? "CREATE" : "MODIFY " + ShortHex(loanBrokerId);
        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        if (coverRateMinimum.HasValue) parts.Add($"coverMin={coverRateMinimum.Value}");
        if (coverRateLiquidation.HasValue) parts.Add($"coverLiq={coverRateLiquidation.Value}");
        if (managementFeeRate.HasValue) parts.Add($"feeRate={managementFeeRate.Value}");
        if (!string.IsNullOrEmpty(debtMaximum)) parts.Add($"debtMax={debtMaximum}");
        string detail = parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
        string summary = $"LoanBrokerSet by {ToolDisplay.Truncate(account)}: {mode} on vault {ShortHex(vaultId)}{detail}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_broker_delete_prepare")]
    [Description("Prepares an UNSIGNED LoanBrokerDelete (XLS-66). Removes a LoanBroker — allowed only when no active loans remain and cover is fully withdrawn.")]
    public async Task<PreparedTransaction> LoanBrokerDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("LoanBroker owner account.")] string account,
        [Description("64-hex LoanBrokerID to delete.")] string loanBrokerId,
        CancellationToken cancellationToken = default)
    {
        ValidateHash256(loanBrokerId, nameof(loanBrokerId));

        LoanBrokerDelete tx = new LoanBrokerDelete
        {
            Account = account,
            LoanBrokerID = loanBrokerId.ToUpperInvariant(),
        };

        string summary = $"LoanBrokerDelete by {ToolDisplay.Truncate(account)}: {ShortHex(loanBrokerId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_broker_cover_deposit_prepare")]
    [Description("Prepares an UNSIGNED LoanBrokerCoverDeposit (XLS-66). Deposits cover (first-loss) capital into the LoanBroker's pseudo-account. The amount currency must match the underlying vault's asset.")]
    public async Task<PreparedTransaction> LoanBrokerCoverDepositPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account depositing cover capital (typically the broker owner or a backstop LP).")] string account,
        [Description("64-hex LoanBrokerID.")] string loanBrokerId,
        [Description("Vault asset currency ('XRP', 3-char, or 40-hex).")] string assetCurrency,
        [Description("Vault asset issuer (empty for XRP).")] string? assetIssuer,
        [Description("Decimal amount to deposit (drops for XRP, decimal value for IOU/MPT).")] string amountValue,
        CancellationToken cancellationToken = default)
    {
        ValidateHash256(loanBrokerId, nameof(loanBrokerId));
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        Currency amount = VaultTools.BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);

        LoanBrokerCoverDeposit tx = new LoanBrokerCoverDeposit
        {
            Account = account,
            LoanBrokerID = loanBrokerId.ToUpperInvariant(),
            Amount = amount,
        };

        string summary = $"LoanBrokerCoverDeposit by {ToolDisplay.Truncate(account)} into {ShortHex(loanBrokerId)}: "
            + ToolDisplay.DescribeAmount(amount) + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_broker_cover_withdraw_prepare")]
    [Description("Prepares an UNSIGNED LoanBrokerCoverWithdraw (XLS-66). Withdraws cover capital from a LoanBroker. Allowed only up to the amount that keeps CoverRate above CoverRateMinimum (the rippled check is post-tx). Optional destination defaults to account.")]
    public async Task<PreparedTransaction> LoanBrokerCoverWithdrawPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Owner/depositor account submitting the withdrawal.")] string account,
        [Description("64-hex LoanBrokerID.")] string loanBrokerId,
        [Description("Vault asset currency.")] string assetCurrency,
        [Description("Vault asset issuer (empty for XRP).")] string? assetIssuer,
        [Description("Decimal amount to withdraw.")] string amountValue,
        [Description("Optional destination address (defaults to account).")] string? destination = null,
        [Description("Optional destination tag.")] uint? destinationTag = null,
        CancellationToken cancellationToken = default)
    {
        ValidateHash256(loanBrokerId, nameof(loanBrokerId));
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        Currency amount = VaultTools.BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);

        LoanBrokerCoverWithdraw tx = new LoanBrokerCoverWithdraw
        {
            Account = account,
            LoanBrokerID = loanBrokerId.ToUpperInvariant(),
            Amount = amount,
            Destination = string.IsNullOrEmpty(destination) ? account : destination,
            DestinationTag = destinationTag,
        };

        string destPart = string.IsNullOrEmpty(destination) || string.Equals(destination, account, StringComparison.Ordinal)
            ? "self"
            : ToolDisplay.Truncate(destination);
        string summary = $"LoanBrokerCoverWithdraw by {ToolDisplay.Truncate(account)} from {ShortHex(loanBrokerId)}: "
            + ToolDisplay.DescribeAmount(amount) + " → " + destPart + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_broker_cover_clawback_prepare")]
    [Description("Prepares an UNSIGNED LoanBrokerCoverClawback (XLS-66). Asset issuer claws back cover capital from a LoanBroker. At least ONE of loanBrokerId or amount (currency+value) must be specified. With only loanBrokerId: max available from that broker. With only amount: amount-only clawback (broker chosen by amendment rules). With both: amount-from-broker.")]
    public async Task<PreparedTransaction> LoanBrokerCoverClawbackPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer account submitting the clawback.")] string account,
        [Description("Optional 64-hex LoanBrokerID. Omit to let the network select the broker by amount.")] string? loanBrokerId = null,
        [Description("Optional asset currency (with amount).")] string? assetCurrency = null,
        [Description("Optional asset issuer.")] string? assetIssuer = null,
        [Description("Optional decimal amount. At least one of loanBrokerId or amount must be present.")] string? amountValue = null,
        CancellationToken cancellationToken = default)
    {
        bool hasBroker = !string.IsNullOrEmpty(loanBrokerId);
        bool hasAmount = !string.IsNullOrEmpty(amountValue);
        if (!hasBroker && !hasAmount)
        {
            throw new ArgumentException("At least one of loanBrokerId or amountValue must be specified.");
        }
        if (hasBroker)
        {
            ValidateHash256(loanBrokerId!, nameof(loanBrokerId));
        }

        Currency? amount = null;
        if (hasAmount)
        {
            if (string.IsNullOrWhiteSpace(assetCurrency))
            {
                throw new ArgumentException("assetCurrency is required when amountValue is provided.", nameof(assetCurrency));
            }
            if (string.Equals(assetCurrency, "XRP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("LoanBrokerCoverClawback only applies to issued tokens — XRP cannot be clawed back.", nameof(assetCurrency));
            }
            amount = VaultTools.BuildInitialAmount(assetCurrency, assetIssuer, amountValue!, account);
        }

        LoanBrokerCoverClawback tx = new LoanBrokerCoverClawback
        {
            Account = account,
            LoanBrokerID = hasBroker ? loanBrokerId!.ToUpperInvariant() : null,
            Amount = amount!,
        };

        string brokerPart = hasBroker ? ShortHex(loanBrokerId!) : "any-broker";
        string amtPart = amount is null ? "max available" : ToolDisplay.DescribeAmount(amount);
        string summary = $"LoanBrokerCoverClawback by issuer {ToolDisplay.Truncate(account)} on {brokerPart}: amount={amtPart}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void ValidateHash256(string id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }
        if (id.Length != 64)
        {
            throw new ArgumentException($"{paramName} must be a 64-char hex string (Hash256).", paramName);
        }
        ValidateHex(id, paramName);
    }

    internal static void ValidateHex(string hex, string paramName)
    {
        for (int i = 0; i < hex.Length; i++)
        {
            char c = hex[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
            if (!ok)
            {
                throw new ArgumentException($"{paramName} contains non-hex character at position {i}.", paramName);
            }
        }
    }

    internal static string ShortHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return "<null>";
        return hex.Length <= 16 ? hex : $"{hex.AsSpan(0, 8)}...{hex.AsSpan(hex.Length - 6, 6)}";
    }
}
