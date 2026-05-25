using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Payment Channel write-flow MCP tools — Create / Fund / Claim.
/// Off-chain authorization (channel_authorize / channel_verify) is handled by the signer plugin.
/// </summary>
[McpServerToolType]
public sealed class PaymentChannelTools
{
    private readonly TransactionPreparer _preparer;

    public PaymentChannelTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_payment_channel_create_prepare")]
    [Description("Prepares an UNSIGNED PaymentChannelCreate. amountDrops is XRP drops (string). settleDelaySeconds is the source-side close grace period. publicKeyHex is the secp256k1/ed25519 public key the source will sign claims with — get it from xrpl_wallet_address (publicKey).")]
    public async Task<PreparedTransaction> PaymentChannelCreatePrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Source account funding the channel.")] string account,
        [Description("Destination account that can claim from this channel.")] string destination,
        [Description("Amount of XRP in drops (string) to fund the channel.")] string amountDrops,
        [Description("Settle delay in seconds — minimum wait before source can close with unclaimed XRP.")] uint settleDelaySeconds,
        [Description("Hex public key the source will sign claims with (33-byte secp256k1 or 32-byte ed25519, hex).")] string publicKeyHex,
        [Description("Optional UTC time after which the channel auto-closes (immutable).")] DateTime? cancelAfterUtc = null,
        [Description("Optional destination tag.")] uint? destinationTag = null,
        [Description("Optional source tag.")] uint? sourceTag = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(amountDrops))
        {
            throw new ArgumentException("amountDrops is required.", nameof(amountDrops));
        }
        if (string.IsNullOrWhiteSpace(publicKeyHex))
        {
            throw new ArgumentException("publicKeyHex is required.", nameof(publicKeyHex));
        }

        PaymentChannelCreate tx = new PaymentChannelCreate
        {
            Account = account,
            Destination = destination,
            Amount = amountDrops,
            SettleDelay = settleDelaySeconds,
            PublicKey = publicKeyHex,
            CancelAfter = cancelAfterUtc,
            DestinationTag = destinationTag,
            SourceTag = sourceTag,
        };

        string summary = $"PaymentChannelCreate from {ToolDisplay.Truncate(account)} to {ToolDisplay.Truncate(destination)}: {amountDrops} drops, settleDelay={settleDelaySeconds}s.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_payment_channel_fund_prepare")]
    [Description("Prepares an UNSIGNED PaymentChannelFund. Adds XRP to an open channel and optionally bumps its Expiration. Only the source account can fund.")]
    public async Task<PreparedTransaction> PaymentChannelFundPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Source account (channel owner).")] string account,
        [Description("Channel ID (64-char hex).")] string channelId,
        [Description("Amount of XRP in drops to add.")] string amountDrops,
        [Description("Optional new UTC Expiration time (must be later than current expiration + settleDelay).")] DateTime? expirationUtc = null,
        CancellationToken cancellationToken = default)
    {
        PaymentChannelFund tx = new PaymentChannelFund
        {
            Account = account,
            Channel = channelId,
            Amount = amountDrops,
            Expiration = expirationUtc,
        };

        string summary = $"PaymentChannelFund by {ToolDisplay.Truncate(account)} on {Short(channelId)}: +{amountDrops} drops.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_payment_channel_claim_prepare")]
    [Description("Prepares an UNSIGNED PaymentChannelClaim. Used by either side to claim XRP and/or renew/close the channel. signatureHex + publicKeyHex are required when the destination is claiming OR when the source is redeeming a signed claim (sign offline via the signer plugin). Pass renew=true to clear the channel's Expiration; close=true to schedule closure.")]
    public async Task<PreparedTransaction> PaymentChannelClaimPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender of the claim (source or destination of the channel).")] string account,
        [Description("Channel ID (64-char hex).")] string channelId,
        [Description("Cumulative XRP drops authorized by the signature.")] string? amountDrops = null,
        [Description("Cumulative XRP drops delivered after this claim (required unless closing).")] string? balanceDrops = null,
        [Description("Hex signature over (channelId, amount). Required when destination claims or when redeeming a third-party signed claim.")] string? signatureHex = null,
        [Description("Hex public key matching the channel's PublicKey. Required when signatureHex is set.")] string? publicKeyHex = null,
        [Description("If true, set tfRenew to clear the channel's Expiration (source only).")] bool renew = false,
        [Description("If true, set tfClose to schedule channel closure (source or destination).")] bool close = false,
        CancellationToken cancellationToken = default)
    {
        if (renew && close)
        {
            throw new ArgumentException("renew and close are mutually exclusive.");
        }

        bool hasSig = !string.IsNullOrEmpty(signatureHex);
        bool hasPub = !string.IsNullOrEmpty(publicKeyHex);
        if (hasSig != hasPub)
        {
            throw new ArgumentException("signatureHex and publicKeyHex must be provided together.");
        }

        PaymentChannelClaimFlags? flags = null;
        if (renew) flags = PaymentChannelClaimFlags.tfRenew;
        else if (close) flags = PaymentChannelClaimFlags.tfClose;

        PaymentChannelClaim tx = new PaymentChannelClaim
        {
            Account = account,
            Channel = channelId,
            Amount = amountDrops,
            Balance = balanceDrops,
            Signature = signatureHex,
            PublicKey = publicKeyHex,
            Flags = flags,
        };

        string action = close ? "CLOSE" : renew ? "RENEW" : "CLAIM";
        string deliveredSuffix = string.IsNullOrEmpty(balanceDrops) ? string.Empty : $" → delivered {balanceDrops} drops";
        string summary = $"PaymentChannelClaim ({action}) by {ToolDisplay.Truncate(account)} on {Short(channelId)}{deliveredSuffix}.";

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
