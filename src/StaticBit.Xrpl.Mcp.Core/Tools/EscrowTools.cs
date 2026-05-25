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
/// Escrow write-flow MCP tools — Create / Finish / Cancel.
/// Time-locked transfers with optional PREIMAGE-SHA-256 crypto-conditions.
/// </summary>
[McpServerToolType]
public sealed class EscrowTools
{
    private readonly TransactionPreparer _preparer;

    public EscrowTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_escrow_create_prepare")]
    [Description("Prepares an UNSIGNED EscrowCreate. amount is XRP drops (string), an issued-token JSON object, or an MPT amount (requires TokenEscrow amendment). Must specify FinishAfter or CancelAfter (or both). Condition (PREIMAGE-SHA-256, hex) makes it a conditional escrow that requires a matching Fulfillment to finish. All times are UTC; they are converted to Ripple-epoch by the SDK.")]
    public async Task<PreparedTransaction> EscrowCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender account (escrow funder).")] string account,
        [Description("Destination account that receives the escrowed funds on finish.")] string destination,
        [Description("Amount: drops string for XRP, JSON {value,currency,issuer} for tokens.")] string amount,
        [Description("UTC time after which the destination can claim funds (recipient-side).")] DateTime? finishAfterUtc = null,
        [Description("UTC time after which the escrow can be cancelled (refund to sender).")] DateTime? cancelAfterUtc = null,
        [Description("Hex PREIMAGE-SHA-256 crypto-condition. If set, EscrowFinish requires a matching Fulfillment.")] string? conditionHex = null,
        [Description("Optional destination tag.")] uint? destinationTag = null,
        CancellationToken cancellationToken = default)
    {
        if (!finishAfterUtc.HasValue && !cancelAfterUtc.HasValue)
        {
            throw new ArgumentException("At least one of finishAfterUtc or cancelAfterUtc is required.");
        }
        if (!finishAfterUtc.HasValue && string.IsNullOrEmpty(conditionHex))
        {
            throw new ArgumentException("Either finishAfterUtc or conditionHex must be specified.");
        }

        Currency parsed = CurrencyParser.Parse(amount);

        EscrowCreate tx = new EscrowCreate
        {
            Account = account,
            Destination = destination,
            Amount = parsed,
            FinishAfter = finishAfterUtc,
            CancelAfter = cancelAfterUtc,
            Condition = conditionHex,
            DestinationTag = destinationTag,
        };

        string conditional = string.IsNullOrEmpty(conditionHex) ? "time-only" : "conditional";
        string summary = $"EscrowCreate ({conditional}) from {ToolDisplay.Truncate(account)} to {ToolDisplay.Truncate(destination)}: {ToolDisplay.DescribeAmount(parsed)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_escrow_finish_prepare")]
    [Description("Prepares an UNSIGNED EscrowFinish. owner is the original escrow funder; offerSequence is the Sequence of the EscrowCreate transaction. For conditional escrows, provide both conditionHex and fulfillmentHex.")]
    public async Task<PreparedTransaction> EscrowFinishPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender of the finish (often the destination).")] string account,
        [Description("Owner = original funder address.")] string owner,
        [Description("Sequence number of the original EscrowCreate transaction.")] uint offerSequence,
        [Description("Hex PREIMAGE-SHA-256 condition (must match the escrow). Required for conditional escrows.")] string? conditionHex = null,
        [Description("Hex PREIMAGE-SHA-256 fulfillment matching the condition. Required for conditional escrows.")] string? fulfillmentHex = null,
        CancellationToken cancellationToken = default)
    {
        bool hasCond = !string.IsNullOrEmpty(conditionHex);
        bool hasFulf = !string.IsNullOrEmpty(fulfillmentHex);
        if (hasCond != hasFulf)
        {
            throw new ArgumentException("conditionHex and fulfillmentHex must be provided together (or both omitted).");
        }

        EscrowFinish tx = new EscrowFinish
        {
            Account = account,
            Owner = owner,
            OfferSequence = offerSequence,
            Condition = conditionHex,
            Fulfillment = fulfillmentHex,
        };

        string mode = hasCond ? "conditional" : "time-only";
        string summary = $"EscrowFinish ({mode}) by {ToolDisplay.Truncate(account)}: owner={ToolDisplay.Truncate(owner)}, offerSequence={offerSequence}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_escrow_cancel_prepare")]
    [Description("Prepares an UNSIGNED EscrowCancel. Only valid after the escrow's CancelAfter time has passed; refunds the funds to the original owner.")]
    public async Task<PreparedTransaction> EscrowCancelPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender of the cancel (typically the original owner, but anyone can cancel after CancelAfter).")] string account,
        [Description("Owner = original funder address.")] string owner,
        [Description("Sequence number of the original EscrowCreate transaction.")] uint offerSequence,
        CancellationToken cancellationToken = default)
    {
        EscrowCancel tx = new EscrowCancel
        {
            Account = account,
            Owner = owner,
            OfferSequence = offerSequence,
        };

        string summary = $"EscrowCancel by {ToolDisplay.Truncate(account)}: owner={ToolDisplay.Truncate(owner)}, offerSequence={offerSequence}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }
}
