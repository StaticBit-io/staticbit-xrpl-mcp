using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// NFT (XLS-20) MCP tools. Write-flow (Mint/Burn/CreateOffer/CancelOffer/AcceptOffer)
/// returns UNSIGNED autofilled transactions; read-flow queries the rippled node directly.
/// </summary>
[McpServerToolType]
public sealed class NftTools
{
    private readonly XrplClientPool _pool;
    private readonly TransactionPreparer _preparer;

    public NftTools(XrplClientPool pool, TransactionPreparer preparer)
    {
        _pool = pool;
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_nft_mint_prepare")]
    [Description("Prepares an UNSIGNED NFTokenMint. uriHex must be hex-encoded (use uriPlain if you have a plain-string URI and want auto-encoding). Flags are a sum of: tfBurnable=1, tfOnlyXRP=2, tfTrustLine=4, tfTransferable=8, tfMutable=16. transferFee 0..50000 (0.000%..50.000%); requires tfTransferable.")]
    public async Task<PreparedTransaction> NftMintPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Minter account (and owner of the freshly minted NFT, unless issuer is set).")] string account,
        [Description("NFTokenTaxon (uint32). Group identifier; set 0 if you don't use it.")] uint nfTokenTaxon,
        [Description("Optional issuer account, only if account is an authorized minter on behalf of someone else (sets the Issuer field).")] string? issuer = null,
        [Description("Optional transfer fee (0..50000) for secondary sales. Requires tfTransferable.")] uint? transferFee = null,
        [Description("Optional NFT URI as a hex string. Mutually exclusive with uriPlain.")] string? uriHex = null,
        [Description("Optional NFT URI as a plain string; will be UTF-8-hex-encoded. Mutually exclusive with uriHex.")] string? uriPlain = null,
        [Description("Sum of NFTokenMint flags (tfBurnable=1, tfOnlyXRP=2, tfTrustLine=4, tfTransferable=8, tfMutable=16).")] uint flags = 0,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(uriHex) && !string.IsNullOrEmpty(uriPlain))
        {
            throw new ArgumentException("Provide only one of uriHex or uriPlain.");
        }

        string? finalUri = uriHex;
        if (string.IsNullOrEmpty(finalUri) && !string.IsNullOrEmpty(uriPlain))
        {
            finalUri = Convert.ToHexString(Encoding.UTF8.GetBytes(uriPlain));
        }

        NFTokenMint tx = new NFTokenMint
        {
            Account = account,
            NFTokenTaxon = nfTokenTaxon,
            Issuer = issuer,
            TransferFee = transferFee,
            URI = finalUri,
            Flags = flags == 0 ? null : (NFTokenMintFlags?)flags,
        };

        string summary = $"NFTokenMint by {ToolDisplay.Truncate(account)}: taxon={nfTokenTaxon}" +
                         (issuer is null ? string.Empty : $", on behalf of {ToolDisplay.Truncate(issuer)}") +
                         (transferFee.HasValue ? $", transferFee={transferFee}" : string.Empty) +
                         (flags == 0 ? string.Empty : $", flags={(NFTokenMintFlags)flags}") +
                         ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_nft_burn_prepare")]
    [Description("Prepares an UNSIGNED NFTokenBurn. owner is only needed when burning a token you don't currently hold but are authorized to burn (e.g. issuer burning a tfBurnable NFT).")]
    public async Task<PreparedTransaction> NftBurnPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Account submitting the burn (usually the current holder).")] string account,
        [Description("NFTokenID (64-char hex) of the NFT to burn.")] string nfTokenId,
        [Description("Optional current owner if different from account (issuer burning a tfBurnable NFT).")] string? owner = null,
        CancellationToken cancellationToken = default)
    {
        NFTokenBurn tx = new NFTokenBurn
        {
            Account = account,
            NFTokenID = nfTokenId,
            Owner = owner,
        };

        string summary = $"NFTokenBurn by {ToolDisplay.Truncate(account)}: NFT {Short(nfTokenId)}" +
                         (owner is null ? string.Empty : $" (held by {ToolDisplay.Truncate(owner)})") +
                         ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_nft_create_offer_prepare")]
    [Description("Prepares an UNSIGNED NFTokenCreateOffer. SELL offer: set isSellOffer=true and DO NOT pass owner. BUY offer: set isSellOffer=false and pass owner (the current NFT holder). amount uses same format as xrpl_payment_prepare (drops string for XRP, JSON token object otherwise).")]
    public async Task<PreparedTransaction> NftCreateOfferPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender account.")] string account,
        [Description("NFTokenID (64-char hex).")] string nfTokenId,
        [Description("Price the offer GIVES (sell offer) or OFFERS (buy offer). Drops string for XRP or JSON {value,currency,issuer}.")] string amount,
        [Description("True for sell offer, false for buy offer.")] bool isSellOffer,
        [Description("Current owner of the NFT. Required for BUY offers; must be omitted for SELL offers.")] string? owner = null,
        [Description("Optional destination — only this account will be able to accept the offer.")] string? destination = null,
        [Description("Optional UTC expiration time (offer becomes invalid after).")] DateTime? expirationUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (isSellOffer && !string.IsNullOrEmpty(owner))
        {
            throw new ArgumentException("owner must be omitted for sell offers.", nameof(owner));
        }
        if (!isSellOffer && string.IsNullOrEmpty(owner))
        {
            throw new ArgumentException("owner is required for buy offers.", nameof(owner));
        }

        Currency price = CurrencyParser.Parse(amount);

        NFTokenCreateOffer tx = new NFTokenCreateOffer
        {
            Account = account,
            NFTokenID = nfTokenId,
            Amount = price,
            Owner = owner,
            Destination = destination,
            Expiration = expirationUtc,
            Flags = isSellOffer ? NFTokenCreateOfferFlags.tfSellNFToken : null,
        };

        string kind = isSellOffer ? "SELL" : "BUY";
        string ownerSuffix = owner is null ? string.Empty : $" from {ToolDisplay.Truncate(owner)}";
        string destSuffix = destination is null ? string.Empty : $" (only {ToolDisplay.Truncate(destination)} may accept)";
        string summary = $"NFTokenCreateOffer ({kind}) by {ToolDisplay.Truncate(account)} on {Short(nfTokenId)}{ownerSuffix}: {ToolDisplay.DescribeAmount(price)}{destSuffix}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_nft_cancel_offer_prepare")]
    [Description("Prepares an UNSIGNED NFTokenCancelOffer. nftOfferIds is the comma-separated list of NFTokenOffer object IDs (64-char hex each) to cancel.")]
    public async Task<PreparedTransaction> NftCancelOfferPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Sender account.")] string account,
        [Description("Comma-separated list of NFTokenOffer object IDs (each 64-char hex).")] string nftOfferIds,
        CancellationToken cancellationToken = default)
    {
        string[] offers = SplitOffers(nftOfferIds);

        NFTokenCancelOffer tx = new NFTokenCancelOffer
        {
            Account = account,
            NFTokenOffers = offers,
        };

        string summary = $"NFTokenCancelOffer by {ToolDisplay.Truncate(account)}: cancel {offers.Length} offer(s).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_nft_accept_offer_prepare")]
    [Description("Prepares an UNSIGNED NFTokenAcceptOffer. DIRECT mode: pass either sellOfferId OR buyOfferId. BROKERED mode: pass both AND optionally a brokerFee (the broker's cut, same format as xrpl_payment_prepare).")]
    public async Task<PreparedTransaction> NftAcceptOfferPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Account accepting the offer (the broker in brokered mode).")] string account,
        [Description("Sell-offer NFTokenOffer ID (direct sell-accept, or brokered mode).")] string? sellOfferId = null,
        [Description("Buy-offer NFTokenOffer ID (direct buy-accept, or brokered mode).")] string? buyOfferId = null,
        [Description("Optional broker fee (brokered mode only). Same format as xrpl_payment_prepare amount.")] string? brokerFee = null,
        CancellationToken cancellationToken = default)
    {
        bool hasSell = !string.IsNullOrEmpty(sellOfferId);
        bool hasBuy = !string.IsNullOrEmpty(buyOfferId);
        if (!hasSell && !hasBuy)
        {
            throw new ArgumentException("At least one of sellOfferId or buyOfferId is required.");
        }
        if (!hasSell && !string.IsNullOrEmpty(brokerFee))
        {
            throw new ArgumentException("brokerFee is only valid in brokered mode (both sellOfferId and buyOfferId).");
        }
        if (hasSell && hasBuy == false && !string.IsNullOrEmpty(brokerFee))
        {
            throw new ArgumentException("brokerFee is only valid in brokered mode (both sellOfferId and buyOfferId).");
        }

        Currency? fee = string.IsNullOrEmpty(brokerFee) ? null : CurrencyParser.Parse(brokerFee);

        NFTokenAcceptOffer tx = new NFTokenAcceptOffer
        {
            Account = account,
            NFTokenSellOffer = sellOfferId,
            NFTokenBuyOffer = buyOfferId,
            NFTokenBrokerFee = fee!,
        };

        string mode = hasSell && hasBuy ? "BROKERED" : hasSell ? "DIRECT-SELL" : "DIRECT-BUY";
        string feeSuffix = fee is null ? string.Empty : $", brokerFee={ToolDisplay.DescribeAmount(fee)}";
        string summary = $"NFTokenAcceptOffer ({mode}) by {ToolDisplay.Truncate(account)}{feeSuffix}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_account_nfts")]
    [Description("Read-only: lists NFTs currently owned by an account. Pageable via limit/marker.")]
    public async Task<string> AccountNftsAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Classic XRP address.")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description("Ledger selector: 'validated' (default), 'current', 'closed', or a numeric sequence.")] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountNFTsRequest request = new AccountNFTsRequest(account)
        {
            Limit = limit,
            Marker = marker,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
        };

        AccountNFTs response = await client.AccountNFTs(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_nft_buy_offers")]
    [Description("Read-only: lists outstanding BUY offers for the given NFTokenID.")]
    public async Task<string> NftBuyOffersAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("NFTokenID (64-char hex).")] string nfTokenId,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        NFTBuyOffersRequest request = new NFTBuyOffersRequest(nfTokenId);
        NFTBuyOffers response = await client.NFTBuyOffers(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_nft_sell_offers")]
    [Description("Read-only: lists outstanding SELL offers for the given NFTokenID.")]
    public async Task<string> NftSellOffersAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("NFTokenID (64-char hex).")] string nfTokenId,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        NFTSellOffersRequest request = new NFTSellOffersRequest(nfTokenId);
        NFTSellOffers response = await client.NFTSellOffers(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    internal static string[] SplitOffers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("nftOfferIds is required.");
        }

        string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new ArgumentException("nftOfferIds must contain at least one offer id.");
        }
        return parts;
    }

    private static string Short(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "<null>";
        return id.Length <= 16 ? id : $"{id.AsSpan(0, 8)}...{id.AsSpan(id.Length - 6, 6)}";
    }
}
