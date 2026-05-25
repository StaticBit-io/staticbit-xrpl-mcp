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
/// Write-flow MCP tools that produce an UNSIGNED, autofilled transaction.
/// The caller signs the returned blob locally and submits via <c>xrpl_tx_submit_signed</c>.
/// The server never sees a seed or private key.
/// </summary>
[McpServerToolType]
public sealed class PaymentTools
{
    private readonly TransactionPreparer _preparer;

    public PaymentTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_payment_prepare")]
    [Description("Prepares an UNSIGNED Payment transaction. Returns autofilled tx JSON + unsigned blob + signing data. Caller signs locally and then calls xrpl_tx_submit_signed. Amount: numeric drops string for XRP (1 XRP = 1000000 drops), or {\"value\":\"...\",\"currency\":\"...\",\"issuer\":\"...\"} JSON for tokens.")]
    public async Task<PreparedTransaction> PaymentPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender address (classic r-address). Server does NOT need its seed.")] string account,
        [Description("Destination XRP address.")] string destination,
        [Description("Amount. XRP drops as a numeric string (e.g. '10000000' = 10 XRP) OR token amount as JSON {\"value\":\"...\",\"currency\":\"...\",\"issuer\":\"...\"}.")] string amount,
        [Description("Optional destination tag (uint32, e.g. for exchange deposits).")] uint? destinationTag = null,
        [Description("Optional source tag.")] uint? sourceTag = null,
        [Description("Optional invoice ID (32-byte hex).")] string? invoiceId = null,
        CancellationToken cancellationToken = default)
    {
        Currency parsedAmount = CurrencyParser.Parse(amount);

        Payment payment = new Payment
        {
            Account = account,
            Destination = destination,
            Amount = parsedAmount,
            DestinationTag = destinationTag,
            SourceTag = sourceTag,
            InvoiceID = invoiceId,
        };

        string summary = BuildSummary(account, destination, parsedAmount, destinationTag);

        return await _preparer
            .PrepareAsync(new NetworkRef(network), payment, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_trustset_prepare")]
    [Description("Prepares an UNSIGNED TrustSet transaction to create or modify a trust line. Set limitValue to '0' to remove a trust line (only succeeds when balance and flags are at defaults).")]
    public async Task<PreparedTransaction> TrustSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account that holds the trust line (sender).")] string account,
        [Description("Token currency code (3-char or 40-hex).")] string currency,
        [Description("Token issuer address.")] string issuer,
        [Description("Trust limit value as a decimal string (e.g. '1000000'). Use '0' to attempt removal.")] string limitValue,
        [Description("Quality in (rate at which received tokens are valued), 1_000_000_000 = 1.0. Optional.")] uint? qualityIn = null,
        [Description("Quality out (rate at which sent tokens are valued). Optional.")] uint? qualityOut = null,
        CancellationToken cancellationToken = default)
    {
        Currency limit = new Currency
        {
            CurrencyCode = currency,
            Issuer = issuer,
            Value = limitValue,
        };

        TrustSet trustSet = new TrustSet
        {
            Account = account,
            LimitAmount = limit,
            QualityIn = qualityIn,
            QualityOut = qualityOut,
        };

        string summary = $"TrustSet from {Truncate(account)}: limit {limitValue} {currency} issued by {Truncate(issuer)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), trustSet, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildSummary(string sender, string destination, Currency amount, uint? destinationTag)
    {
        string amountDescription = string.Equals(amount.CurrencyCode, "XRP", System.StringComparison.OrdinalIgnoreCase)
            ? $"{amount.Value} drops XRP"
            : $"{amount.Value} {amount.CurrencyCode} (issuer {Truncate(amount.Issuer)})";

        string tagSuffix = destinationTag.HasValue ? $" (DestTag {destinationTag.Value})" : string.Empty;

        return $"Payment from {Truncate(sender)} to {Truncate(destination)}{tagSuffix}: {amountDescription}.";
    }

    private static string Truncate(string? address)
    {
        if (string.IsNullOrEmpty(address)) return "<null>";
        return address.Length <= 12 ? address : $"{address.AsSpan(0, 6)}...{address.AsSpan(address.Length - 4, 4)}";
    }
}
