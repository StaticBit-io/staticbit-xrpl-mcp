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
/// DEX write-flow MCP tools — create / cancel offers. Two-phase prepare → sign locally → submit.
/// </summary>
[McpServerToolType]
public sealed class OfferTools
{
    private readonly TransactionPreparer _preparer;

    public OfferTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_offer_create_prepare")]
    [Description("Prepares an UNSIGNED OfferCreate (limit order on the XRPL DEX). Amounts use the same format as xrpl_payment_prepare (drops string for XRP, JSON {value,currency,issuer} for tokens).")]
    public async Task<PreparedTransaction> OfferCreatePrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender address (the offer owner).")] string account,
        [Description("TakerGets — what the OFFER GIVES UP. Drops string for XRP or JSON token object.")] string takerGets,
        [Description("TakerPays — what the OFFER WANTS. Drops string for XRP or JSON token object.")] string takerPays,
        [Description("Optional Ripple-epoch expiration (DateTime UTC). Use null for no expiration.")] DateTime? expirationUtc = null,
        [Description("OfferSequence of an existing offer to cancel atomically. Optional.")] uint? offerSequence = null,
        [Description("If true, set tfPassive (don't consume exact-match offers).")] bool passive = false,
        [Description("If true, set tfImmediateOrCancel.")] bool immediateOrCancel = false,
        [Description("If true, set tfFillOrKill.")] bool fillOrKill = false,
        [Description("If true, set tfSell (exchange entire TakerGets even if you get more than TakerPays).")] bool sell = false,
        CancellationToken cancellationToken = default)
    {
        Currency parsedGets = CurrencyParser.Parse(takerGets);
        Currency parsedPays = CurrencyParser.Parse(takerPays);

        OfferCreateFlags flags = 0;
        if (passive) flags |= OfferCreateFlags.tfPassive;
        if (immediateOrCancel) flags |= OfferCreateFlags.tfImmediateOrCancel;
        if (fillOrKill) flags |= OfferCreateFlags.tfFillOrKill;
        if (sell) flags |= OfferCreateFlags.tfSell;

        OfferCreate offer = new OfferCreate
        {
            Account = account,
            TakerGets = parsedGets,
            TakerPays = parsedPays,
            Expiration = expirationUtc,
            OfferSequence = offerSequence,
            Flags = flags == 0 ? null : flags,
        };

        string summary =
            $"OfferCreate from {Truncate(account)}: give {Describe(parsedGets)} for {Describe(parsedPays)}." +
            (flags == 0 ? string.Empty : $" Flags: {flags}.");

        return await _preparer
            .PrepareAsync(new NetworkRef(network), offer, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_offer_cancel_prepare")]
    [Description("Prepares an UNSIGNED OfferCancel transaction. offerSequence is the Sequence number of the OfferCreate to remove.")]
    public async Task<PreparedTransaction> OfferCancelPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender address (the offer owner).")] string account,
        [Description("Sequence number of the OfferCreate transaction to cancel.")] uint offerSequence,
        CancellationToken cancellationToken = default)
    {
        OfferCancel cancel = new OfferCancel
        {
            Account = account,
            OfferSequence = offerSequence,
        };

        string summary = $"OfferCancel from {Truncate(account)}: remove offer with sequence {offerSequence}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), cancel, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string Describe(Currency amount)
    {
        return string.Equals(amount.CurrencyCode, "XRP", StringComparison.OrdinalIgnoreCase)
            ? $"{amount.Value} drops XRP"
            : $"{amount.Value} {amount.CurrencyCode} ({Truncate(amount.Issuer)})";
    }

    private static string Truncate(string? address)
    {
        if (string.IsNullOrEmpty(address)) return "<null>";
        return address.Length <= 12 ? address : $"{address.AsSpan(0, 6)}...{address.AsSpan(address.Length - 4, 4)}";
    }
}
