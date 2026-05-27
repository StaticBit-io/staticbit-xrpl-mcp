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
/// XLS-66 Loan write-flow MCP tools.
///
/// Loan flow: lender (LoanBroker owner) submits <c>LoanSet</c> to originate a
/// loan to a Counterparty (borrower); the borrower's CounterpartySignature is
/// required unless the LoanSet is wrapped inside a Batch. Borrower submits
/// <c>LoanPay</c> for each scheduled (or over / late / full) payment. Either
/// side can submit <c>LoanManage</c> to mark default / impair / unimpair.
/// <c>LoanDelete</c> closes a fully-paid loan.
/// </summary>
[McpServerToolType]
public sealed class LoanTools
{
    private const uint TfLoanOverpaymentSet = 0x00010000u;

    private const uint TfLoanDefault = 0x00010000u;
    private const uint TfLoanImpair = 0x00020000u;
    private const uint TfLoanUnimpair = 0x00040000u;

    private const uint TfLoanOverpaymentPay = 0x00010000u;
    private const uint TfLoanFullPayment = 0x00020000u;
    private const uint TfLoanLatePayment = 0x00040000u;

    private readonly TransactionPreparer _preparer;

    public LoanTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_loan_set_prepare")]
    [Description("Prepares an UNSIGNED LoanSet (XLS-66). Originates a new loan against a LoanBroker. The borrower (counterparty) must co-sign — submit this inside a Batch with both signatures, or expect the chain to require an out-of-band CounterpartySignature. Rate fields are in 1/100th bp (0..100000 = 0..100%). Fee fields are STNumber decimal strings in the vault-asset units. Set 'allowOverpayment=true' to mark the loan with tfLoanOverpayment.")]
    public async Task<PreparedTransaction> LoanSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Loan originator (typically the LoanBroker owner).")] string account,
        [Description("64-hex LoanBrokerID under which to originate.")] string loanBrokerId,
        [Description("Borrower account (counterparty).")] string counterparty,
        [Description("Principal amount requested (STNumber decimal string, in vault-asset units).")] string principalRequested,
        [Description("Optional interest rate (1/100th bp, 0..100000).")] uint? interestRate = null,
        [Description("Optional late-payment interest rate (1/100th bp).")] uint? lateInterestRate = null,
        [Description("Optional early-close interest rate (1/100th bp).")] uint? closeInterestRate = null,
        [Description("Optional overpayment interest rate (1/100th bp).")] uint? overpaymentInterestRate = null,
        [Description("Optional overpayment fee (1/100th bp).")] uint? overpaymentFee = null,
        [Description("Optional loan origination fee (STNumber decimal string).")] string? loanOriginationFee = null,
        [Description("Optional per-payment service fee (STNumber decimal string).")] string? loanServiceFee = null,
        [Description("Optional late payment fee (STNumber decimal string).")] string? latePaymentFee = null,
        [Description("Optional close payment fee (STNumber decimal string).")] string? closePaymentFee = null,
        [Description("Optional total number of payments. Default 1.")] uint? paymentTotal = null,
        [Description("Optional interval between payments in seconds. Default 60.")] uint? paymentInterval = null,
        [Description("Optional grace period in seconds. Default 60.")] uint? gracePeriod = null,
        [Description("Optional hex blob (≤512 hex chars = 256 bytes).")] string? dataHex = null,
        [Description("If true, sets tfLoanOverpayment — permits the borrower to overpay scheduled installments.")] bool allowOverpayment = false,
        CancellationToken cancellationToken = default)
    {
        LoanBrokerTools.ValidateHash256(loanBrokerId, nameof(loanBrokerId));
        if (string.IsNullOrWhiteSpace(counterparty))
        {
            throw new ArgumentException("counterparty is required.", nameof(counterparty));
        }
        if (string.Equals(counterparty, account, StringComparison.Ordinal))
        {
            throw new ArgumentException("counterparty must differ from account (lender ≠ borrower).", nameof(counterparty));
        }
        if (string.IsNullOrWhiteSpace(principalRequested))
        {
            throw new ArgumentException("principalRequested is required.", nameof(principalRequested));
        }
        foreach ((string name, uint? val) in new[]
        {
            (nameof(interestRate), interestRate),
            (nameof(lateInterestRate), lateInterestRate),
            (nameof(closeInterestRate), closeInterestRate),
            (nameof(overpaymentInterestRate), overpaymentInterestRate),
            (nameof(overpaymentFee), overpaymentFee),
        })
        {
            if (val.HasValue && val.Value > 100000)
            {
                throw new ArgumentException($"{name} must be 0..100000 (1/100th bp).", name);
            }
        }
        if (!string.IsNullOrEmpty(dataHex))
        {
            LoanBrokerTools.ValidateHex(dataHex, nameof(dataHex));
            if (dataHex.Length > 512 || (dataHex.Length & 1) != 0)
            {
                throw new ArgumentException("dataHex must be even-length ≤512 hex chars.", nameof(dataHex));
            }
        }

        LoanSet tx = new LoanSet
        {
            Account = account,
            LoanBrokerID = loanBrokerId.ToUpperInvariant(),
            Counterparty = counterparty,
            PrincipalRequested = principalRequested,
            InterestRate = interestRate,
            LateInterestRate = lateInterestRate,
            CloseInterestRate = closeInterestRate,
            OverpaymentInterestRate = overpaymentInterestRate,
            OverpaymentFee = overpaymentFee,
            LoanOriginationFee = loanOriginationFee,
            LoanServiceFee = loanServiceFee,
            LatePaymentFee = latePaymentFee,
            ClosePaymentFee = closePaymentFee,
            PaymentTotal = paymentTotal,
            PaymentInterval = paymentInterval,
            GracePeriod = gracePeriod,
            Data = string.IsNullOrEmpty(dataHex) ? null : dataHex.ToUpperInvariant(),
            Flags = allowOverpayment ? LoanSetFlags.tfLoanOverpayment : null,
        };

        string opt = allowOverpayment ? ", overpayment-allowed" : "";
        string summary = $"LoanSet by lender {ToolDisplay.Truncate(account)} → borrower {ToolDisplay.Truncate(counterparty)} "
            + $"on broker {LoanBrokerTools.ShortHex(loanBrokerId)}: principal={principalRequested}"
            + (paymentTotal.HasValue ? $", {paymentTotal.Value} payment(s)" : "")
            + opt + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_manage_prepare")]
    [Description("Prepares an UNSIGNED LoanManage (XLS-66). Submitter must own the LoanBroker (or be authorized). Pass action='default' (tfLoanDefault), 'impair' (tfLoanImpair), or 'unimpair' (tfLoanUnimpair) — mutually exclusive.")]
    public async Task<PreparedTransaction> LoanManagePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("LoanBroker owner (or authorized manager).")] string account,
        [Description("64-hex LoanID.")] string loanId,
        [Description("'default' | 'impair' | 'unimpair' (mutually exclusive).")] string action,
        CancellationToken cancellationToken = default)
    {
        LoanBrokerTools.ValidateHash256(loanId, nameof(loanId));

        LoanManageFlags flag = action?.Trim().ToLowerInvariant() switch
        {
            "default" => LoanManageFlags.tfLoanDefault,
            "impair" => LoanManageFlags.tfLoanImpair,
            "unimpair" => LoanManageFlags.tfLoanUnimpair,
            _ => throw new ArgumentException("action must be 'default', 'impair' or 'unimpair'.", nameof(action)),
        };

        LoanManage tx = new LoanManage
        {
            Account = account,
            LoanID = loanId.ToUpperInvariant(),
            Flags = flag,
        };

        string summary = $"LoanManage by {ToolDisplay.Truncate(account)}: {action.ToUpperInvariant()} loan {LoanBrokerTools.ShortHex(loanId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_pay_prepare")]
    [Description("Prepares an UNSIGNED LoanPay (XLS-66). Borrower makes a payment on a loan. 'amountValue' is the payment amount in the vault-asset units. Optional 'paymentKind' ∈ {'scheduled','overpayment','full','late'} — at most one of overpayment/full/late may be set; 'scheduled' (default) sets no kind-flag. Overpayment requires the loan was originated with tfLoanOverpayment.")]
    public async Task<PreparedTransaction> LoanPayPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Borrower account (the loan's Counterparty).")] string account,
        [Description("64-hex LoanID.")] string loanId,
        [Description("Vault asset currency.")] string assetCurrency,
        [Description("Vault asset issuer (empty for XRP).")] string? assetIssuer,
        [Description("Payment amount (decimal string).")] string amountValue,
        [Description("Optional payment kind: 'scheduled' (default), 'overpayment', 'full', or 'late'.")] string? paymentKind = null,
        CancellationToken cancellationToken = default)
    {
        LoanBrokerTools.ValidateHash256(loanId, nameof(loanId));
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        Currency amount = VaultTools.BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);

        LoanPayFlags? flags = null;
        switch (paymentKind?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "scheduled":
                flags = null;
                break;
            case "overpayment":
                flags = LoanPayFlags.tfLoanOverpayment;
                break;
            case "full":
                flags = LoanPayFlags.tfLoanFullPayment;
                break;
            case "late":
                flags = LoanPayFlags.tfLoanLatePayment;
                break;
            default:
                throw new ArgumentException("paymentKind must be one of: scheduled, overpayment, full, late.", nameof(paymentKind));
        }

        LoanPay tx = new LoanPay
        {
            Account = account,
            LoanID = loanId.ToUpperInvariant(),
            Amount = amount,
            Flags = flags,
        };

        string kindStr = flags is null ? "scheduled" : flags.Value.ToString().Replace("tfLoan", "").Replace("Payment", "").ToLowerInvariant();
        string summary = $"LoanPay by borrower {ToolDisplay.Truncate(account)} on loan {LoanBrokerTools.ShortHex(loanId)}: "
            + ToolDisplay.DescribeAmount(amount) + " (" + kindStr + ").";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_loan_delete_prepare")]
    [Description("Prepares an UNSIGNED LoanDelete (XLS-66). Removes a loan ledger entry — allowed only when the loan is fully repaid (or otherwise closed by the amendment rules).")]
    public async Task<PreparedTransaction> LoanDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Submitter account (typically LoanBroker owner).")] string account,
        [Description("64-hex LoanID to delete.")] string loanId,
        CancellationToken cancellationToken = default)
    {
        LoanBrokerTools.ValidateHash256(loanId, nameof(loanId));

        LoanDelete tx = new LoanDelete
        {
            Account = account,
            LoanID = loanId.ToUpperInvariant(),
        };

        string summary = $"LoanDelete by {ToolDisplay.Truncate(account)}: {LoanBrokerTools.ShortHex(loanId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }
}
