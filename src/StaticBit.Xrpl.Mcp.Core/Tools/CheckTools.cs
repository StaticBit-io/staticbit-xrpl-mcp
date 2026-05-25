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
/// Check write-flow MCP tools — CheckCreate / CheckCash / CheckCancel.
/// A Check is a deferred payment that the destination must explicitly cash.
/// </summary>
[McpServerToolType]
public sealed class CheckTools
{
    private readonly TransactionPreparer _preparer;

    public CheckTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_check_create_prepare")]
    [Description("Prepares an UNSIGNED CheckCreate. sendMax = upper bound the sender allows to be debited (includes transfer fees). Same format as xrpl_payment_prepare amounts.")]
    public async Task<PreparedTransaction> CheckCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender (issuer of the Check).")] string account,
        [Description("Destination — the only account that can cash the Check.")] string destination,
        [Description("SendMax: max amount the Check can debit. Drops string for XRP, JSON {value,currency,issuer} for tokens.")] string sendMax,
        [Description("Optional destination tag.")] uint? destinationTag = null,
        [Description("Optional UTC expiration; the Check is invalid after this time.")] DateTime? expirationUtc = null,
        [Description("Optional InvoiceID (uint32).")] uint? invoiceId = null,
        CancellationToken cancellationToken = default)
    {
        Currency parsed = CurrencyParser.Parse(sendMax);

        CheckCreate tx = new CheckCreate
        {
            Account = account,
            Destination = destination,
            SendMax = parsed,
            DestinationTag = destinationTag,
            Expiration = expirationUtc,
            InvoiceID = invoiceId,
        };

        string tagSuffix = destinationTag.HasValue ? $" (DestTag {destinationTag.Value})" : string.Empty;
        string summary = $"CheckCreate from {ToolDisplay.Truncate(account)} to {ToolDisplay.Truncate(destination)}{tagSuffix}: sendMax {ToolDisplay.DescribeAmount(parsed)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_check_cash_prepare")]
    [Description("Prepares an UNSIGNED CheckCash. Submitted by the Check's destination. Provide EITHER amount (cash for exactly this amount) OR deliverMin (cash for at least this amount, up to the Check's sendMax). Currency must match the Check's SendMax.")]
    public async Task<PreparedTransaction> CheckCashPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender — must be the Check's destination.")] string account,
        [Description("Check ledger object ID (64-char hex).")] string checkId,
        [Description("Cash for EXACTLY this amount. Drops string for XRP or JSON token object. Mutually exclusive with deliverMin.")] string? amount = null,
        [Description("Cash for AT LEAST this amount. Drops string for XRP or JSON token object. Mutually exclusive with amount.")] string? deliverMin = null,
        CancellationToken cancellationToken = default)
    {
        bool hasAmount = !string.IsNullOrEmpty(amount);
        bool hasDeliverMin = !string.IsNullOrEmpty(deliverMin);
        if (hasAmount == hasDeliverMin)
        {
            throw new ArgumentException("Provide exactly one of amount or deliverMin.");
        }

        Currency? parsedAmount = hasAmount ? CurrencyParser.Parse(amount!) : null;
        Currency? parsedDeliverMin = hasDeliverMin ? CurrencyParser.Parse(deliverMin!) : null;

        CheckCash tx = new CheckCash
        {
            Account = account,
            CheckID = checkId,
            Amount = parsedAmount,
            DeliverMin = parsedDeliverMin,
        };

        string descr = hasAmount
            ? $"Amount={ToolDisplay.DescribeAmount(parsedAmount!)}"
            : $"DeliverMin={ToolDisplay.DescribeAmount(parsedDeliverMin!)}";
        string summary = $"CheckCash by {ToolDisplay.Truncate(account)} on check {Short(checkId)}: {descr}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_check_cancel_prepare")]
    [Description("Prepares an UNSIGNED CheckCancel. Can be sent by the Check's source or destination at any time; by anyone after the Check has expired.")]
    public async Task<PreparedTransaction> CheckCancelPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender of the cancel.")] string account,
        [Description("Check ledger object ID (64-char hex).")] string checkId,
        CancellationToken cancellationToken = default)
    {
        CheckCancel tx = new CheckCancel
        {
            Account = account,
            CheckID = checkId,
        };

        string summary = $"CheckCancel by {ToolDisplay.Truncate(account)} on check {Short(checkId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Short(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "<null>";
        return id.Length <= 16 ? id : $"{id.AsSpan(0, 8)}...{id.AsSpan(id.Length - 6, 6)}";
    }
}
